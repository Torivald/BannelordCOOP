using System.Linq;
using BannerCoopTW.Core.Battle;
using BannerCoopTW.Core.Protocol;
using Xunit;

namespace BannerCoopTW.Networking.Tests
{
    /// <summary>
    /// Integration: fake clients (no game) on localhost over real LiteNetLib UDP run the Phase 3 slot-reservation scenarios
    /// against the authoritative BattleSession behind BattleServerHost. 5 places: the initiator is local, 4 fake clients are remote.
    /// </summary>
    public class SlotReservationOverWireTests
    {
        private const ulong A = 1, B = 2, C = 3, D = 4;

        private static Harness Start(int maxPlayers = 5) => new Harness(new ManualClock(), maxPlayers);

        private static void ChooseSide(Harness h, FakeClient c, BattleSide side)
        {
            c.Send(new ChooseSide { Side = side });
            h.Pump(() => c.Last<SlotList>() != null && c.Last<SlotList>()!.Slots.Count > 0, "slot list for " + c.PlayerId);
        }

        [Fact]
        public void Four_clients_join_see_ordered_slots_and_the_initiators_hero_is_not_offered()
        {
            using var h = Start();
            var clients = new[] { A, B, C, D }.Select(h.Join).ToArray();
            Assert.All(clients, c => Assert.NotNull(c.Last<HelloAck>()));
            Assert.All(clients, c => Assert.Equal(BattleSide.Attacker, c.Last<BattleInvite>()!.InitiatorSide));

            ChooseSide(h, clients[0], BattleSide.Attacker);
            var ids = clients[0].Last<SlotList>()!.Slots.Select(s => s.Id).ToArray();
            Assert.Equal(new[] { "a-lord2", "a-off", "a-inf", "a-arch" }, ids); // lords, officers, classes; a-lord1 belongs to the initiator

            ChooseSide(h, clients[1], BattleSide.Defender); // PvP
            Assert.Equal(new[] { "d-lord", "d-inf" }, clients[1].Last<SlotList>()!.Slots.Select(s => s.Id).ToArray());
        }

        [Fact]
        public void Simultaneous_claims_for_the_last_archer_have_exactly_one_winner_and_the_slot_disappears_for_everyone()
        {
            using var h = Start();
            var x = h.Join(A);
            var y = h.Join(B);
            var z = h.Join(C);
            ChooseSide(h, x, BattleSide.Attacker);
            ChooseSide(h, y, BattleSide.Attacker);
            ChooseSide(h, z, BattleSide.Attacker);

            // Both requests are on the wire before the server runs its next loop iteration.
            x.Send(new ReserveRequest { RequestId = 1, SlotId = "a-arch" });
            y.Send(new ReserveRequest { RequestId = 2, SlotId = "a-arch" });
            h.Pump(() => x.Last<ReserveResponse>() != null && y.Last<ReserveResponse>() != null, "both replies");

            var replies = new[] { x.Last<ReserveResponse>()!, y.Last<ReserveResponse>()! };
            Assert.Equal(1, replies.Count(r => r.Error == SessionError.None));
            Assert.Equal(1, replies.Count(r => r.Error == SessionError.SlotUnavailable));
            Assert.Equal(1, replies.Single(r => r.Error == SessionError.None).ReservationId > 0 ? 1 : 0);

            h.Pump(() => z.Last<SlotList>()!.Slots.All(s => s.Id != "a-arch"), "archer gone from the bystander's list");
            Assert.All(new[] { x, y, z }, c => Assert.DoesNotContain(c.Last<SlotList>()!.Slots, s => s.Id == "a-arch"));
        }

        [Fact]
        public void Reservation_then_confirm_makes_the_player_control_the_slot_and_others_are_told()
        {
            using var h = Start();
            var x = h.Join(A);
            var y = h.Join(B);
            ChooseSide(h, x, BattleSide.Attacker);
            ChooseSide(h, y, BattleSide.Attacker);

            x.Send(new ReserveRequest { RequestId = 7, SlotId = "a-lord2" });
            h.Pump(() => x.Last<ReserveResponse>() != null, "reserve reply");
            var r = x.Last<ReserveResponse>()!;
            Assert.Equal((7u, SessionError.None), (r.RequestId, r.Error));
            Assert.InRange(r.ExpiresInMs, 10_000, 15_000);

            x.Send(new ConfirmRequest { ReservationId = r.ReservationId });
            h.Pump(() => x.Last<ConfirmResponse>() != null, "confirm reply");
            Assert.Equal(SessionError.None, x.Last<ConfirmResponse>()!.Error);
            h.Pump(() => y.All<PlayerPhaseChanged>().Any(p => p.PlayerId == A && p.Phase == PlayerPhase.Controlling), "phase broadcast to the other player");
            Assert.Equal("a-lord2", y.All<PlayerPhaseChanged>().Last(p => p.PlayerId == A).SlotId);
        }

        [Fact]
        public void Disconnect_during_reservation_frees_the_slot_for_another_client()
        {
            using var h = Start();
            var x = h.Join(A);
            var y = h.Join(B);
            ChooseSide(h, x, BattleSide.Attacker);
            ChooseSide(h, y, BattleSide.Attacker);

            x.Send(new ReserveRequest { RequestId = 1, SlotId = "a-lord2" });
            h.Pump(() => x.Last<ReserveResponse>() != null, "x reserved");
            y.Send(new ReserveRequest { RequestId = 2, SlotId = "a-lord2" });
            h.Pump(() => y.Last<ReserveResponse>() != null, "y refused");
            Assert.Equal(SessionError.SlotUnavailable, y.Last<ReserveResponse>()!.Error);

            x.Kill(); // no goodbye: the server must notice by itself
            h.Pump(() => h.Session.GetPhase(new PlayerId(A)) == PlayerPhase.Left, "server noticed the disconnect", 20_000);
            y.Send(new ReserveRequest { RequestId = 3, SlotId = "a-lord2" });
            h.Pump(() => y.All<ReserveResponse>().Any(r => r.RequestId == 3), "y retry");
            Assert.Equal(SessionError.None, y.All<ReserveResponse>().Single(r => r.RequestId == 3).Error);
        }

        [Fact]
        public void Graceful_disconnect_while_controlling_releases_the_seat()
        {
            using var h = Start();
            var x = h.Join(A);
            ChooseSide(h, x, BattleSide.Attacker);
            x.Send(new ReserveRequest { RequestId = 1, SlotId = "a-off" });
            h.Pump(() => x.Last<ReserveResponse>() != null, "reserved");
            x.Send(new ConfirmRequest { ReservationId = x.Last<ReserveResponse>()!.ReservationId });
            h.Pump(() => x.Last<ConfirmResponse>() != null, "confirmed");

            x.Dispose();
            h.Pump(() => h.Session.GetPhase(new PlayerId(A)) == PlayerPhase.Left, "left");
            var y = h.Join(B);
            ChooseSide(h, y, BattleSide.Attacker);
            Assert.Contains(y.Last<SlotList>()!.Slots, s => s.Id == "a-off");
        }

        [Fact]
        public void Reservation_expires_after_the_timeout_and_the_owner_is_told()
        {
            using var h = Start();
            var x = h.Join(A);
            ChooseSide(h, x, BattleSide.Attacker);
            x.Send(new ReserveRequest { RequestId = 1, SlotId = "a-lord2" });
            h.Pump(() => x.Last<ReserveResponse>() != null, "reserved");
            var id = x.Last<ReserveResponse>()!.ReservationId;

            h.Clock.Advance(12_001);
            h.Pump(() => x.All<PlayerPhaseChanged>().Any(p => p.PlayerId == A && p.Phase == PlayerPhase.SideChosen), "expiry broadcast");
            h.Pump(() => x.Last<SlotList>()!.Slots.Any(s => s.Id == "a-lord2"), "slot listed again");

            x.Send(new ConfirmRequest { ReservationId = id });
            h.Pump(() => x.Last<ConfirmResponse>() != null, "late confirm reply");
            Assert.Equal(SessionError.ReservationExpired, x.Last<ConfirmResponse>()!.Error);
        }

        [Fact]
        public void Release_returns_to_selection_and_spectate_is_accepted()
        {
            using var h = Start();
            var x = h.Join(A);
            ChooseSide(h, x, BattleSide.Attacker);
            x.Send(new ReserveRequest { RequestId = 1, SlotId = "a-lord2" });
            h.Pump(() => x.Last<ReserveResponse>() != null, "reserved");
            h.Pump(() => h.Session.GetPhase(new PlayerId(A)) == PlayerPhase.Reserved, "reservation applied");
            x.Send(new ReleaseRequest());
            h.Pump(() => h.Session.GetPhase(new PlayerId(A)) == PlayerPhase.SideChosen, "released");

            x.Send(new ChooseSide { Spectate = true });
            h.Pump(() => h.Session.GetPhase(new PlayerId(A)) == PlayerPhase.Spectating, "spectating");
        }

        [Fact]
        public void Battle_end_while_a_reservation_is_open_rejects_the_late_confirm()
        {
            using var h = Start();
            var x = h.Join(A);
            ChooseSide(h, x, BattleSide.Attacker);
            x.Send(new ReserveRequest { RequestId = 1, SlotId = "a-lord2" });
            h.Pump(() => x.Last<ReserveResponse>() != null, "reserved");

            h.Host.EndBattle();
            h.Pump(() => x.Last<BattleEnded>() != null, "battle ended broadcast");
            x.Send(new ConfirmRequest { ReservationId = x.Last<ReserveResponse>()!.ReservationId });
            h.Pump(() => x.Last<ConfirmResponse>() != null, "confirm reply");
            Assert.Equal(SessionError.BattleClosed, x.Last<ConfirmResponse>()!.Error);
        }

        [Fact]
        public void Player_cap_rejects_the_extra_client_with_session_full()
        {
            using var h = Start(maxPlayers: 4); // initiator + 3 remote
            var clients = new[] { A, B, C, D }.Select(h.Join).ToArray();
            Assert.NotNull(clients[0].Last<BattleInvite>());
            Assert.NotNull(clients[2].Last<BattleInvite>());
            Assert.Equal(RejectReason.SessionFull, clients[3].Last<HelloReject>()!.Reason);
        }

        [Fact]
        public void Client_with_another_protocol_version_is_rejected_before_it_can_join()
        {
            using var h = Start();
            var c = h.AddClient(A);
            h.Pump(() => c.IsConnected, "connect");
            c.SendHello(version: 99);
            h.Pump(() => c.Last<HelloReject>() != null, "reject");
            Assert.Equal(RejectReason.ProtocolMismatch, c.Last<HelloReject>()!.Reason);
            Assert.Null(h.Session.GetPhase(new PlayerId(A)));
        }

        [Fact]
        public void Client_with_another_game_build_is_rejected()
        {
            using var h = Start();
            var c = h.AddClient(A);
            h.Pump(() => c.IsConnected, "connect");
            c.SendHello(build: "1.3.15.110062");
            h.Pump(() => c.Last<HelloReject>() != null, "reject");
            Assert.Equal(RejectReason.GameBuildMismatch, c.Last<HelloReject>()!.Reason);
        }

        [Fact]
        public void Messages_before_hello_get_the_client_dropped()
        {
            using var h = Start();
            var c = h.AddClient(A);
            h.Pump(() => c.IsConnected, "connect");
            c.Send(new ReserveRequest { RequestId = 1, SlotId = "a-lord2" });
            h.Pump(() => c.DisconnectReason.HasValue, "dropped");
        }

        [Fact]
        public void Duplicate_player_id_is_rejected()
        {
            using var h = Start();
            var first = h.Join(A);
            var second = h.Join(A);
            Assert.NotNull(first.Last<BattleInvite>());
            Assert.Equal(RejectReason.SessionFull, second.Last<HelloReject>()!.Reason);
        }
    }
}
