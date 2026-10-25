using System;
using System.Linq;
using BannerCoopTW.Core.Battle;
using BannerCoopTW.Core.Protocol;
using BannerCoopTW.Networking;
using Xunit;

namespace BannerCoopTW.Networking.Tests
{
    /// <summary>Defects found in the Phase 4 architect review (H1, H2, H3, M1, M2), each pinned by a test.</summary>
    public class LobbyRegressionTests
    {
        private static SlotCatalog Catalog() => SlotCatalog.Create()
            .AddLord(BattleSide.Attacker, "a-lord1", "Lord A")
            .AddLord(BattleSide.Attacker, "a-lord2", "Lord C")
            .AddLord(BattleSide.Defender, "d-lord", "Lord D")
            .Build();

        private static BattleSession NewSession(ManualClock clock, string id = "b1", int max = 5) =>
            new BattleSession(id, Catalog(), clock, new SessionOptions { MaxParticipants = max, Epoch = 3 }, new PlayerId(1000), BattleSide.Attacker, "a-lord1");

        [Fact]
        public void H1_after_the_battle_ends_the_lobby_works_again_and_a_new_battle_can_start()
        {
            using var rig = new LobbyRig();
            var a = rig.Join(1);
            a.SetReady(true);
            rig.Host.StartBattle(NewSession(rig.Clock));
            rig.Pump(() => a.State == CoopClientState.InBattle, "invited");
            rig.Host.EndBattle();
            Assert.Null(rig.Host.Session);
            rig.Pump(() => a.Last<BattleEnded>() != null, "ended");
            rig.Pump(() => rig.Host.LobbyPlayers.All(p => !p.Ready), "ready flags reset");

            var late = rig.Join(2);                                   // a new Hello is accepted again
            Assert.Equal(CoopClientState.InLobby, late.State);
            var second = NewSession(rig.Clock, "b2");
            rig.Host.StartBattle(second);                              // and a second battle starts
            rig.Pump(() => late.Invite?.BattleId == "b2", "late player invited to the second battle");
        }

        [Fact]
        public void H2_a_player_inside_the_reconnect_grace_is_not_invited_and_holds_no_place()
        {
            using var rig = new LobbyRig(options: new HostOptions { ReconnectGraceMs = 30_000 });
            var away = rig.Join(11);
            var stays = rig.Join(12);
            away.Dispose();
            rig.Pump(() => rig.Host.LobbyPlayers.Any(p => p.PlayerId == 11 && !p.Connected), "11 away");

            var session = NewSession(rig.Clock);
            rig.Host.StartBattle(session);
            Assert.Null(session.GetPhase(new PlayerId(11)));            // never invited
            Assert.Equal(PlayerPhase.Invited, session.GetPhase(new PlayerId(12)));
            rig.Pump(() => stays.State == CoopClientState.InBattle, "12 invited");

            rig.Clock.Advance(30_001);
            rig.Pump(() => rig.Host.LobbyPlayers.Count == 1, "11's lobby place expired");
            Assert.Null(session.GetPhase(new PlayerId(11)));
        }

        [Fact]
        public void H2_a_place_released_by_grace_expiry_during_a_battle_frees_the_participant_slot()
        {
            using var rig = new LobbyRig(options: new HostOptions { ReconnectGraceMs = 30_000 });
            var session = NewSession(rig.Clock, max: 2);                // initiator + one more
            rig.Host.StartBattle(session);
            var a = rig.Join(21);
            rig.Pump(() => a.State == CoopClientState.InBattle, "21 in battle");
            a.Dispose();
            rig.Pump(() => session.GetPhase(new PlayerId(21)) == PlayerPhase.Left, "21 left");
            rig.Clock.Advance(30_001);
            rig.Pump(() => rig.Host.LobbyPlayers.Count == 0, "lobby place expired");
            var b = rig.Join(22);
            Assert.NotEqual(CoopClientState.Rejected, b.State);         // the participant cap is not blocked by a ghost
            rig.Pump(() => b.State == CoopClientState.InBattle, "22 invited");
        }

        [Fact]
        public void H3_the_same_id_can_come_back_during_a_battle_after_the_grace_has_run_out()
        {
            using var rig = new LobbyRig(options: new HostOptions { ReconnectGraceMs = 30_000 });
            var session = NewSession(rig.Clock);
            rig.Host.StartBattle(session);
            var a = rig.Join(31);
            rig.Pump(() => a.State == CoopClientState.InBattle, "invited");
            a.Dispose();
            rig.Pump(() => session.GetPhase(new PlayerId(31)) == PlayerPhase.Left, "left");
            rig.Clock.Advance(30_001);
            rig.Pump(() => rig.Host.LobbyPlayers.Count == 0, "grace over");

            var again = rig.Join(31);
            Assert.NotEqual(CoopClientState.Rejected, again.State);
            Assert.False(again.Resumed);
            rig.Pump(() => again.State == CoopClientState.InBattle, "invited again");
            Assert.Equal(PlayerPhase.Invited, session.GetPhase(new PlayerId(31)));
        }

        [Fact]
        public void M1_a_refused_peer_cannot_slip_in_with_a_second_hello_during_the_linger()
        {
            using var rig = new LobbyRig();
            var raw = new LiteNetLibClientTransport();
            ConnectionId conn = default;
            var connected = false;
            raw.Connected += c => { conn = c; connected = true; };
            raw.Connect("127.0.0.1", rig.Port);
            rig.Pump(() => { raw.Poll(); return connected; }, "raw connected");
            rig.Settle(100);

            byte[] Hello(string build) => MessageCodec.Encode(new Hello { PlayerId = 41, PlayerName = "x", GameBuild = build, ModVersion = LobbyRig.ModVersion });
            raw.Send(conn, Hello("0.0.0"), DeliveryMode.ReliableOrdered);          // refused: wrong build
            raw.Send(conn, Hello(LobbyRig.Build), DeliveryMode.ReliableOrdered);   // a valid one right behind it
            rig.Settle(600);
            raw.Dispose();
            Assert.Empty(rig.Host.LobbyPlayers);
            Assert.Equal(0, rig.Host.ConnectedPlayers);
        }

        [Fact]
        public void M2_a_client_that_does_not_fit_into_the_battle_is_refused_and_cut_not_left_half_connected()
        {
            using var rig = new LobbyRig();
            var a = rig.Join(51);
            var b = rig.Join(52);
            rig.Host.StartBattle(NewSession(rig.Clock, max: 2));          // initiator + one
            rig.Pump(() => a.State == CoopClientState.InBattle, "51 fits");
            rig.Pump(() => b.State == CoopClientState.Rejected, "52 refused");
            Assert.Equal(RejectReason.SessionFull, b.RejectReason);
            rig.Pump(() => rig.Host.ConnectedPlayers == 1, "52 cut on the host");
            Assert.Single(rig.Host.LobbyPlayers);
        }
    }
}
