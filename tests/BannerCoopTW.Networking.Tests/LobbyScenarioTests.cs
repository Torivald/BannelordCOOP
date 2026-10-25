using System;
using System.Collections.Generic;
using System.Linq;
using BannerCoopTW.Core.Battle;
using BannerCoopTW.Core.Protocol;
using BannerCoopTW.Networking;
using Xunit;

namespace BannerCoopTW.Networking.Tests
{
    /// <summary>
    /// Phase 4 scenario: four fake clients (no game) go lobby → ready → battle created → side → slot reservation over the wire,
    /// including the race for one lord, the reservation timeout, a disconnect that frees a seat, a reconnect with the same id and the "no slot left → spectator" case.
    /// Run twice: on a clean localhost link and behind the network-condition decorator (5 % loss, 100 ms each way).
    /// </summary>
    public class LobbyScenarioTests
    {
        public static IEnumerable<object[]> Links()
        {
            yield return new object[] { "clean", 0, 0 };
            yield return new object[] { "loss5-lat100", 5, 100 };
        }

        private static SlotCatalog Catalog() => SlotCatalog.Create()
            .AddLord(BattleSide.Attacker, "a-lord1", "Lord A")   // the initiator's own hero
            .AddLord(BattleSide.Attacker, "a-lord2", "Lord C")
            .AddOfficer(BattleSide.Attacker, "a-off", "Captain B")
            .AddTroopClass(BattleSide.Attacker, "a-inf", "Infantry", 2)
            .AddTroopClass(BattleSide.Attacker, "a-arch", "Archers", 1)
            .AddLord(BattleSide.Defender, "d-lord", "Lord D")
            .Build();

        private static SlotInfo? Slot(CoopClient c, string id) => c.Slots.FirstOrDefault(s => s.Id == id);

        [Theory]
        [MemberData(nameof(Links))]
        public void Four_fake_clients_run_the_lobby_and_slot_reservation_scenario(string link, int lossPercent, int latencyMs)
        {
            var conditions = lossPercent > 0 || latencyMs > 0 ? new NetworkConditions { LossPercent = lossPercent, MinLatencyMs = latencyMs, MaxLatencyMs = latencyMs } : null;
            using var rig = new LobbyRig(conditions, new HostOptions { RttBroadcastIntervalMs = 200 });
            const ulong A = 11, B = 12, C = 13, D = 14;

            // 1. lobby: four join, a fifth with the wrong mod version is refused
            var ca = rig.Join(A);
            var cb = rig.Join(B);
            var cc = rig.Join(C);
            var cd = rig.Join(D);
            var wrong = rig.AddClient(99, modVersion: "9.9.9");
            rig.Pump(() => wrong.State == CoopClientState.Rejected, "wrong mod refused");
            Assert.Equal(RejectReason.ModuleMismatch, wrong.RejectReason);
            rig.Pump(() => new[] { ca, cb, cc, cd }.All(x => x.Lobby.Count == 4), "everyone sees 4 players");

            // 2. ping / RTT
            rig.Pump(() => new[] { ca, cb, cc, cd }.All(x => x.PongsReceived >= 2 && x.RttMs >= 0), "pongs");
            if (rig.Lossy)
                Assert.All(new[] { ca, cb, cc, cd }, x => Assert.True(x.RttMs >= 150, "emulated 100 ms each way, got " + x.RttMs));
            rig.Pump(() => ca.Lobby.All(p => p.RttMs >= 0), "host-published RTT");

            // 3. ready
            foreach (var c in new[] { ca, cb, cc, cd })
                c.SetReady(true);
            rig.Pump(() => rig.Host.AllReady, "all ready on the host");
            rig.Pump(() => ca.Lobby.Count == 4 && ca.Lobby.All(p => p.Ready), "ready visible to clients");

            // 4. battle created: everybody is invited
            var session = new BattleSession("battle-1", Catalog(), rig.Clock, new SessionOptions { MaxParticipants = 5, Epoch = 7 }, new PlayerId(1000), BattleSide.Attacker, "a-lord1");
            rig.Host.StartBattle(session);
            rig.Pump(() => new[] { ca, cb, cc, cd }.All(x => x.State == CoopClientState.InBattle), "invites");
            Assert.All(new[] { ca, cb, cc, cd }, x => Assert.Equal(7u, x.Epoch));

            // 5. sides: three attackers, one defender; the initiator's hero is not offered
            ca.ChooseSide(BattleSide.Attacker);
            cb.ChooseSide(BattleSide.Attacker);
            cc.ChooseSide(BattleSide.Attacker);
            cd.ChooseSide(BattleSide.Defender);
            rig.Pump(() => new[] { ca, cb, cc, cd }.All(x => x.Slots.Count > 0), "slot lists");
            Assert.Null(Slot(ca, "a-lord1"));
            Assert.Equal(new[] { "a-lord2", "a-off", "a-inf", "a-arch" }, ca.Slots.Select(s => s.Id).ToArray());
            Assert.Equal(new[] { "d-lord" }, cd.Slots.Select(s => s.Id).ToArray());

            // 6. race: A and B ask for the same lord at the same moment — exactly one wins
            var ra = ca.Reserve("a-lord2");
            var rb = cb.Reserve("a-lord2");
            rig.Pump(() => ca.ResponseFor(ra) != null && cb.ResponseFor(rb) != null, "race answers");
            var results = new[] { ca.ResponseFor(ra)!.Error, cb.ResponseFor(rb)!.Error };
            Assert.Equal(1, results.Count(e => e == SessionError.None));
            Assert.Equal(1, results.Count(e => e == SessionError.SlotUnavailable));
            var winner = ca.ResponseFor(ra)!.Error == SessionError.None ? ca : cb;
            var loser = winner == ca ? cb : ca;
            rig.Pump(() => Slot(cc, "a-lord2") == null && Slot(loser, "a-lord2") == null, "the taken lord disappears for everyone");

            // 7. reservation timeout: the winner never confirms; the lord is offered again
            rig.Clock.Advance(12_001);
            rig.Pump(() => Slot(cc, "a-lord2")?.Available == 1, "lord back after the timeout");
            rig.Pump(() => winner.All<PlayerPhaseChanged>().Any(p => p.PlayerId == winner.PlayerId && p.Phase == PlayerPhase.SideChosen), "owner told about the expiry");

            // 8. C takes the single archer slot and confirms; a second claimant is refused; C then disconnects and the seat frees
            var rc = cc.Reserve("a-arch");
            rig.Pump(() => cc.ResponseFor(rc) != null, "C reserved the archers");
            Assert.Equal(SessionError.None, cc.ResponseFor(rc)!.Error);
            cc.Confirm(cc.ResponseFor(rc)!.ReservationId);
            rig.Pump(() => session.GetPhase(new PlayerId(C)) == PlayerPhase.Controlling, "C controls the archers");
            var rl = loser.Reserve("a-arch");
            rig.Pump(() => loser.ResponseFor(rl) != null, "loser's archer claim answered");
            Assert.Equal(SessionError.SlotUnavailable, loser.ResponseFor(rl)!.Error);

            cc.Dispose();
            rig.Pump(() => session.GetPhase(new PlayerId(C)) == PlayerPhase.Left, "host noticed C left");
            rig.Pump(() => Slot(loser, "a-arch")?.Available == 1, "the archers are free again for the others");

            // 9. D takes the only defender slot; C reconnects with the same id (resumed) and finds no defender slot → spectator
            var rd = cd.Reserve("d-lord");
            rig.Pump(() => cd.ResponseFor(rd) != null, "D reserved the defender lord");
            Assert.Equal(SessionError.None, cd.ResponseFor(rd)!.Error);

            var c2 = rig.Join(C);
            Assert.True(c2.Resumed);
            rig.Pump(() => c2.State == CoopClientState.InBattle, "C invited again");
            c2.ChooseSide(BattleSide.Defender);
            rig.Pump(() => session.GetPhase(new PlayerId(C)) == PlayerPhase.SideChosen, "C chose the defenders");
            rig.Pump(() => c2.Last<SlotList>() != null && c2.Last<SlotList>()!.Revision >= session.Revision, "current slot list for C");
            Assert.True(c2.Slots.All(s => s.Available == 0), "no defender slot is available");
            c2.Spectate();
            rig.Pump(() => session.GetPhase(new PlayerId(C)) == PlayerPhase.Spectating, "C is a spectator");

            // 10. the end of the battle reaches everyone
            rig.Host.EndBattle();
            rig.Pump(() => new[] { ca, cb, cd, c2 }.All(x => x.Last<BattleEnded>() != null), "battle ended for all");
            Assert.True(link.Length > 0);
        }
    }
}
