using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BannerCoopTW.Core.Battle;
using Xunit;

namespace BannerCoopTW.Core.Tests
{
    internal sealed class ManualClock : IClock
    {
        private long _now;

        public long NowMs => Interlocked.Read(ref _now);

        public void Advance(long ms) => Interlocked.Add(ref _now, ms);
    }

    /// <summary>Cases from BATTLE_PARTICIPATION.md "Required tests" and TEST_MATRIX "Battle Participation".</summary>
    public class BattleSessionTests
    {
        private static readonly PlayerId Host = new PlayerId(1);
        private static readonly PlayerId P2 = new PlayerId(2);
        private static readonly PlayerId P3 = new PlayerId(3);
        private static readonly PlayerId P4 = new PlayerId(4);

        private static SlotCatalog Catalog() => SlotCatalog.Create()
            .AddTroopClass(BattleSide.Attacker, "a-inf", "Infantry", 5)
            .AddOfficer(BattleSide.Attacker, "a-off", "Captain B")
            .AddLord(BattleSide.Attacker, "a-lord1", "Lord A")
            .AddLord(BattleSide.Attacker, "a-lord2", "Lord C")
            .AddTroopClass(BattleSide.Attacker, "a-arch", "Archers", 1)
            .AddLord(BattleSide.Defender, "d-lord", "Lord D")
            .AddTroopClass(BattleSide.Defender, "d-inf", "Infantry", 2)
            .AddSiegeEngine(BattleSide.Attacker, "a-ram", "Ram crew", 2)
            .Build();

        private static (BattleSession s, ManualClock c) Open(SessionOptions? o = null)
        {
            var clock = new ManualClock();
            var s = new BattleSession("b1", Catalog(), clock, o, Host, BattleSide.Attacker, "a-lord1");
            return (s, clock);
        }

        private static void Join(BattleSession s, PlayerId p, BattleSide side)
        {
            Assert.Equal(SessionError.None, s.Invite(p));
            Assert.Equal(SessionError.None, s.ChooseSide(p, side));
        }

        // ---------------------------------------------------------------- catalog

        [Fact]
        public void Catalog_orders_lords_then_officers_then_classes_then_siege_per_side()
        {
            var kinds = Catalog().ForSide(BattleSide.Attacker).Select(s => s.Kind).ToArray();
            Assert.Equal(new[] { SlotKind.Lord, SlotKind.Lord, SlotKind.Officer, SlotKind.TroopClass, SlotKind.TroopClass, SlotKind.SiegeEngine }, kinds);
        }

        [Fact]
        public void Catalog_rejects_duplicate_ids_and_multi_seat_lords()
        {
            Assert.Throws<System.ArgumentException>(() => SlotCatalog.Create().AddLord(BattleSide.Attacker, "x", "X").AddLord(BattleSide.Defender, "x", "Y"));
            Assert.Throws<System.ArgumentException>(() => new SlotDefinition("l", BattleSide.Attacker, SlotKind.Lord, "L", 2, 0));
        }

        [Fact]
        public void Slot_list_shows_only_own_side_and_hides_initiators_hero()
        {
            var (s, _) = Open();
            Join(s, P2, BattleSide.Attacker);
            var ids = s.GetSlots(P2).Select(v => v.Id).ToArray();
            Assert.Equal(new[] { "a-lord2", "a-off", "a-inf", "a-arch", "a-ram" }, ids); // initiator's a-lord1 is not offered
            Assert.DoesNotContain("d-lord", ids);
        }

        // ---------------------------------------------------------------- side selection and PvP

        [Fact]
        public void Pvp_player_can_join_opposing_side_and_take_enemy_lord()
        {
            var (s, _) = Open();
            Join(s, P2, BattleSide.Defender);
            var r = s.Reserve(P2, "d-lord");
            Assert.True(r.Ok);
            Assert.Equal(SessionError.None, s.Confirm(P2, r.Reservation!.Id));
            Assert.Equal(PlayerPhase.Controlling, s.GetPhase(P2));
        }

        [Fact]
        public void Pvp_off_rejects_opposing_side()
        {
            var (s, _) = Open(new SessionOptions { AllowPvp = false });
            s.Invite(P2);
            Assert.Equal(SessionError.SideNotAllowed, s.ChooseSide(P2, BattleSide.Defender));
            Assert.Equal(SessionError.None, s.ChooseSide(P2, BattleSide.Attacker));
        }

        [Fact]
        public void Player_cannot_reserve_slot_of_other_side()
        {
            var (s, _) = Open();
            Join(s, P2, BattleSide.Attacker);
            Assert.Equal(SessionError.WrongSide, s.Reserve(P2, "d-lord").Error);
        }

        [Fact]
        public void Reserve_needs_a_side_first_and_a_known_slot()
        {
            var (s, _) = Open();
            s.Invite(P2);
            Assert.Equal(SessionError.WrongPhase, s.Reserve(P2, "a-lord2").Error);
            s.ChooseSide(P2, BattleSide.Attacker);
            Assert.Equal(SessionError.UnknownSlot, s.Reserve(P2, "nope").Error);
            Assert.Equal(SessionError.UnknownPlayer, s.Reserve(new PlayerId(99), "a-lord2").Error);
        }

        [Fact]
        public void Initiator_cannot_pick_a_slot_or_steal_their_own_hero_via_others()
        {
            var (s, _) = Open();
            Assert.Equal(PlayerPhase.Controlling, s.GetPhase(Host));
            Assert.Equal(SessionError.WrongPhase, s.Reserve(Host, "a-lord2").Error);
            Join(s, P2, BattleSide.Attacker);
            Assert.Equal(SessionError.SlotUnavailable, s.Reserve(P2, "a-lord1").Error);
        }

        // ---------------------------------------------------------------- conflicts

        [Fact]
        public void Two_players_claim_same_lord_sequentially_second_is_rejected_and_lord_disappears()
        {
            var (s, _) = Open();
            Join(s, P2, BattleSide.Attacker);
            Join(s, P3, BattleSide.Attacker);
            Assert.True(s.Reserve(P2, "a-lord2").Ok);
            Assert.Equal(SessionError.SlotUnavailable, s.Reserve(P3, "a-lord2").Error);
            Assert.DoesNotContain(s.GetSlots(P3), v => v.Id == "a-lord2");
            Assert.Equal(PlayerPhase.SideChosen, s.GetPhase(P3)); // loser can pick again
        }

        [Fact]
        public void Two_players_claim_last_archer_exactly_one_wins_under_contention()
        {
            for (var round = 0; round < 200; round++)
            {
                var (s, _) = Open();
                Join(s, P2, BattleSide.Attacker);
                Join(s, P3, BattleSide.Attacker);
                Join(s, P4, BattleSide.Attacker);
                var results = new ReserveResult[3];
                var gate = new ManualResetEventSlim();
                var tasks = new[] { P2, P3, P4 }.Select((p, i) => Task.Run(() =>
                {
                    gate.Wait();
                    results[i] = s.Reserve(p, "a-arch");
                })).ToArray();
                gate.Set();
                Task.WaitAll(tasks);
                Assert.Equal(1, results.Count(r => r.Ok));
                Assert.Equal(2, results.Count(r => r.Error == SessionError.SlotUnavailable));
            }
        }

        [Fact]
        public void Troop_class_with_two_seats_serves_two_players_not_three()
        {
            var res = SlotCatalog.Create().AddLord(BattleSide.Attacker, "l", "L").AddTroopClass(BattleSide.Attacker, "arch", "Archers", 2).Build();
            var s2 = new BattleSession("b2", res, new ManualClock(), null, Host, BattleSide.Attacker, "l");
            Join(s2, P2, BattleSide.Attacker);
            Join(s2, P3, BattleSide.Attacker);
            Join(s2, P4, BattleSide.Attacker);
            Assert.True(s2.Reserve(P2, "arch").Ok);
            Assert.True(s2.Reserve(P3, "arch").Ok);
            Assert.Equal(SessionError.SlotUnavailable, s2.Reserve(P4, "arch").Error);
            Assert.DoesNotContain(s2.GetSlots(P4), v => v.Id == "arch"); // class is full: hidden from the list
        }

        [Fact]
        public void Officer_and_troop_class_claimed_by_different_players_both_succeed()
        {
            var (s, _) = Open();
            Join(s, P2, BattleSide.Attacker);
            Join(s, P3, BattleSide.Attacker);
            Assert.True(s.Reserve(P2, "a-off").Ok);
            Assert.True(s.Reserve(P3, "a-inf").Ok);
            Assert.Equal(4, s.GetSlots(P4Join(s)).First(v => v.Id == "a-inf").Available);
        }

        private static PlayerId P4Join(BattleSession s)
        {
            Join(s, P4, BattleSide.Attacker);
            return P4;
        }

        [Fact]
        public void No_slots_remain_player_becomes_spectator_which_is_valid()
        {
            var cat = SlotCatalog.Create().AddLord(BattleSide.Attacker, "l", "L").Build();
            var s = new BattleSession("b", cat, new ManualClock(), null, Host, BattleSide.Attacker, "l");
            Join(s, P2, BattleSide.Attacker);
            Assert.Empty(s.GetSlots(P2));
            Assert.Equal(SessionError.None, s.Spectate(P2));
            Assert.Equal(PlayerPhase.Spectating, s.GetPhase(P2));
            Assert.Empty(s.GetSlots(P2));
        }

        [Fact]
        public void Player_holding_a_seat_cannot_reserve_a_second_one()
        {
            var (s, _) = Open();
            Join(s, P2, BattleSide.Attacker);
            Assert.True(s.Reserve(P2, "a-lord2").Ok);
            Assert.Equal(SessionError.WrongPhase, s.Reserve(P2, "a-off").Error);
        }

        // ---------------------------------------------------------------- reservation lifecycle

        [Fact]
        public void Confirm_moves_reserved_to_controlling()
        {
            var (s, _) = Open();
            Join(s, P2, BattleSide.Attacker);
            var r = s.Reserve(P2, "a-lord2");
            Assert.Equal(PlayerPhase.Reserved, s.GetPhase(P2));
            Assert.Equal(SessionError.None, s.Confirm(P2, r.Reservation!.Id));
            Assert.Equal(PlayerPhase.Controlling, s.GetPhase(P2));
            Assert.Equal("a-lord2", s.GetHeldSlot(P2));
        }

        [Fact]
        public void Reservation_expires_after_timeout_and_slot_returns_to_free()
        {
            var (s, clock) = Open(new SessionOptions { ReservationTimeoutMs = 12_000 });
            Join(s, P2, BattleSide.Attacker);
            Join(s, P3, BattleSide.Attacker);
            var r = s.Reserve(P2, "a-lord2");
            clock.Advance(11_999);
            Assert.Equal(PlayerPhase.Reserved, s.GetPhase(P2));
            Assert.Equal(SessionError.SlotUnavailable, s.Reserve(P3, "a-lord2").Error);
            clock.Advance(1);
            Assert.Equal(PlayerPhase.SideChosen, s.GetPhase(P2));
            Assert.Equal(SessionError.ReservationExpired, s.Confirm(P2, r.Reservation!.Id));
            Assert.True(s.Reserve(P3, "a-lord2").Ok);
            Assert.Contains(s.DrainEvents(), e => e.Kind == SessionEventKind.ReservationExpired && e.Player == P2);
        }

        [Fact]
        public void Controlled_seat_never_expires()
        {
            var (s, clock) = Open();
            Join(s, P2, BattleSide.Attacker);
            s.Confirm(P2, s.Reserve(P2, "a-lord2").Reservation!.Id);
            clock.Advance(10 * 60_000);
            Assert.Equal(PlayerPhase.Controlling, s.GetPhase(P2));
        }

        [Fact]
        public void Timeout_outside_ten_to_fifteen_seconds_is_refused()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new SessionOptions { ReservationTimeoutMs = 9_999 });
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new SessionOptions { ReservationTimeoutMs = 15_001 });
            Assert.Equal(10_000, new SessionOptions { ReservationTimeoutMs = 10_000 }.ReservationTimeoutMs);
        }

        [Fact]
        public void Release_cancels_reservation_and_returns_to_selection()
        {
            var (s, _) = Open();
            Join(s, P2, BattleSide.Attacker);
            s.Reserve(P2, "a-lord2");
            Assert.Equal(SessionError.None, s.Release(P2));
            Assert.Equal(PlayerPhase.SideChosen, s.GetPhase(P2));
            Assert.Contains(s.GetSlots(P2), v => v.Id == "a-lord2");
        }

        [Fact]
        public void Confirm_with_unknown_reservation_id_is_rejected()
        {
            var (s, _) = Open();
            Join(s, P2, BattleSide.Attacker);
            s.Reserve(P2, "a-lord2");
            Assert.Equal(SessionError.ReservationNotFound, s.Confirm(P2, 12345));
        }

        // ---------------------------------------------------------------- disconnect

        [Fact]
        public void Disconnect_during_reservation_frees_the_slot_immediately()
        {
            var (s, _) = Open();
            Join(s, P2, BattleSide.Attacker);
            Join(s, P3, BattleSide.Attacker);
            s.Reserve(P2, "a-lord2");
            s.Disconnect(P2);
            Assert.Equal(PlayerPhase.Left, s.GetPhase(P2));
            Assert.True(s.Reserve(P3, "a-lord2").Ok);
        }

        [Fact]
        public void Disconnect_while_controlling_releases_control_and_signals_ai_handover()
        {
            var (s, _) = Open();
            Join(s, P2, BattleSide.Attacker);
            s.Confirm(P2, s.Reserve(P2, "a-off").Reservation!.Id);
            s.DrainEvents();
            s.Disconnect(P2);
            var ev = s.DrainEvents();
            Assert.Contains(ev, e => e.Kind == SessionEventKind.ControlReleased && e.Player == P2 && e.SlotId == "a-off");
            Join(s, P3, BattleSide.Attacker);
            Assert.True(s.Reserve(P3, "a-off").Ok); // no inaccessible permanent slot
        }

        [Fact]
        public void Disconnect_is_idempotent_and_blocks_further_actions()
        {
            var (s, _) = Open();
            Join(s, P2, BattleSide.Attacker);
            s.Disconnect(P2);
            s.Disconnect(P2);
            Assert.Equal(SessionError.WrongPhase, s.Reserve(P2, "a-off").Error);
            Assert.Equal(SessionError.AlreadyInSession, s.Invite(P2)); // reconnect is a later milestone
        }

        // ---------------------------------------------------------------- death / knock-out

        [Fact]
        public void Controlled_death_with_free_slot_returns_player_to_selection_and_seat_is_lost()
        {
            var (s, _) = Open();
            Join(s, P2, BattleSide.Attacker);
            s.Confirm(P2, s.Reserve(P2, "a-lord2").Reservation!.Id);
            Assert.Equal(DownOutcome.Reallocate, s.ReportDown(P2));
            Assert.Equal(PlayerPhase.SideChosen, s.GetPhase(P2));
            Assert.DoesNotContain(s.GetSlots(P2), v => v.Id == "a-lord2"); // knocked-out hero is not reclaimable
            Assert.True(s.Reserve(P2, "a-off").Ok);
        }

        [Fact]
        public void Controlled_death_with_no_free_slot_makes_spectator()
        {
            var cat = SlotCatalog.Create().AddLord(BattleSide.Attacker, "l", "L").AddLord(BattleSide.Attacker, "l2", "L2").Build();
            var s = new BattleSession("b", cat, new ManualClock(), null, Host, BattleSide.Attacker, "l");
            Join(s, P2, BattleSide.Attacker);
            s.Confirm(P2, s.Reserve(P2, "l2").Reservation!.Id);
            Assert.Equal(DownOutcome.Spectate, s.ReportDown(P2));
            Assert.Equal(PlayerPhase.Spectating, s.GetPhase(P2));
        }

        [Fact]
        public void Initiator_knocked_out_follows_the_same_rule()
        {
            var (s, _) = Open();
            Assert.Equal(DownOutcome.Reallocate, s.ReportDown(Host));
            Assert.Equal(PlayerPhase.SideChosen, s.GetPhase(Host));
        }

        [Fact]
        public void Report_down_for_a_player_without_a_seat_is_ignored()
        {
            var (s, _) = Open();
            Join(s, P2, BattleSide.Attacker);
            Assert.Equal(DownOutcome.NotControlling, s.ReportDown(P2)); // chose a side but holds nothing
            Assert.Equal(DownOutcome.NotControlling, s.ReportDown(new PlayerId(99)));
            Assert.Equal(PlayerPhase.SideChosen, s.GetPhase(P2));
        }

        [Fact]
        public void Without_reallocation_a_downed_player_spectates_even_if_slots_exist()
        {
            var (s, _) = Open(new SessionOptions { AllowReallocation = false });
            Assert.Equal(DownOutcome.Spectate, s.ReportDown(Host));
            Assert.Equal(SessionError.WrongPhase, s.ChooseSide(Host, BattleSide.Attacker));
        }

        // ---------------------------------------------------------------- reinforcements

        [Fact]
        public void Reinforcements_add_seats_without_invalidating_existing_ownership()
        {
            var (s, _) = Open();
            Join(s, P2, BattleSide.Attacker);
            Join(s, P3, BattleSide.Attacker);
            Assert.True(s.Reserve(P2, "a-arch").Ok);
            Assert.Equal(SessionError.SlotUnavailable, s.Reserve(P3, "a-arch").Error);
            s.AddReinforcements("a-arch", 2);
            Assert.Equal(PlayerPhase.Reserved, s.GetPhase(P2));
            Assert.True(s.Reserve(P3, "a-arch").Ok);
            Assert.Equal(PlayerPhase.Reserved, s.GetPhase(P3));
        }

        [Fact]
        public void New_slot_mid_battle_is_listed_in_order_and_claimable_by_spectator_who_reselects()
        {
            var (s, _) = Open();
            s.Invite(P2);
            s.Spectate(P2);
            s.AddSlot(new SlotDefinition("a-lord3", BattleSide.Attacker, SlotKind.Lord, "Lord Late", 1, 99));
            Assert.Equal(SessionError.None, s.ChooseSide(P2, BattleSide.Attacker)); // spectator becomes eligible
            var ids = s.GetSlots(P2).Select(v => v.Id).ToList();
            Assert.True(ids.IndexOf("a-lord3") < ids.IndexOf("a-off")); // lords come before officers
            Assert.True(s.Reserve(P2, "a-lord3").Ok);
        }

        [Fact]
        public void Reinforcement_of_unknown_slot_is_an_error_code_not_an_exception()
        {
            var (s, _) = Open();
            Assert.Equal(SessionError.UnknownSlot, s.AddReinforcements("zzz", 1));
        }

        [Fact]
        public void Seat_count_beyond_the_wire_limit_is_refused()
        {
            var (s, _) = Open();
            Assert.Throws<System.ArgumentOutOfRangeException>(() => s.AddReinforcements("a-inf", 70_000));
        }

        // ---------------------------------------------------------------- battle end, closing

        [Fact]
        public void Battle_ends_while_allocation_is_open_pending_actions_fail_cleanly()
        {
            var (s, _) = Open();
            Join(s, P2, BattleSide.Attacker);
            var r = s.Reserve(P2, "a-lord2");
            Join(s, P3, BattleSide.Attacker);
            s.End();
            Assert.True(s.IsEnded);
            Assert.Equal(PlayerPhase.Ended, s.GetPhase(P2));
            Assert.Equal(PlayerPhase.Ended, s.GetPhase(P3));
            Assert.Equal(SessionError.BattleClosed, s.Confirm(P2, r.Reservation!.Id));
            Assert.Equal(SessionError.BattleClosed, s.Reserve(P3, "a-off").Error);
            Assert.Equal(SessionError.BattleClosed, s.Invite(P4));
            Assert.Empty(s.GetSlots(P3));
            Assert.Contains(s.DrainEvents(), e => e.Kind == SessionEventKind.BattleEnded);
            s.End(); // idempotent
        }

        [Fact]
        public void Close_joining_blocks_new_players_but_not_those_inside()
        {
            var (s, _) = Open();
            s.Invite(P2);
            s.CloseJoining();
            Assert.Equal(SessionError.BattleClosed, s.Invite(P3));
            Assert.Equal(SessionError.BattleClosed, s.ChooseSide(P2, BattleSide.Attacker)); // never entered: still invited
            Assert.Equal(SessionError.None, s.Spectate(P2)); // spectating stays possible
        }

        [Fact]
        public void Duplicate_invite_is_rejected()
        {
            var (s, _) = Open();
            Assert.Equal(SessionError.None, s.Invite(P2));
            Assert.Equal(SessionError.AlreadyInSession, s.Invite(P2));
            Assert.Equal(SessionError.AlreadyInSession, s.Invite(Host));
        }

        // ---------------------------------------------------------------- revision / events

        [Fact]
        public void Revision_grows_on_each_change_and_events_are_ordered()
        {
            var (s, _) = Open();
            var r0 = s.Revision;
            Join(s, P2, BattleSide.Attacker);
            s.Reserve(P2, "a-lord2");
            Assert.True(s.Revision > r0);
            var ev = s.DrainEvents();
            Assert.Equal(ev.Select(e => e.Revision).OrderBy(x => x), ev.Select(e => e.Revision));
            Assert.Empty(s.DrainEvents());
        }

        // ---------------------------------------------------------------- state machines

        [Theory]
        [InlineData(SeatState.Free, SeatState.Reserved, true)]
        [InlineData(SeatState.Free, SeatState.Controlled, false)]
        [InlineData(SeatState.Reserved, SeatState.Controlled, true)]
        [InlineData(SeatState.Reserved, SeatState.Free, true)]
        [InlineData(SeatState.Reserved, SeatState.Down, true)]
        [InlineData(SeatState.Controlled, SeatState.Down, true)]
        [InlineData(SeatState.Controlled, SeatState.Free, true)]
        [InlineData(SeatState.Down, SeatState.Free, false)]
        [InlineData(SeatState.Down, SeatState.Reserved, false)]
        public void Seat_state_machine(SeatState from, SeatState to, bool allowed) =>
            Assert.Equal(allowed, SeatStateMachine.CanTransition(from, to));

        [Theory]
        [InlineData(PlayerPhase.Invited, PlayerPhase.Reserved, false)]
        [InlineData(PlayerPhase.Invited, PlayerPhase.Controlling, false)]
        [InlineData(PlayerPhase.SideChosen, PlayerPhase.Controlling, false)]
        [InlineData(PlayerPhase.SideChosen, PlayerPhase.Reserved, true)]
        [InlineData(PlayerPhase.Reserved, PlayerPhase.Controlling, true)]
        [InlineData(PlayerPhase.Controlling, PlayerPhase.Reserved, false)]
        [InlineData(PlayerPhase.Spectating, PlayerPhase.SideChosen, true)]
        [InlineData(PlayerPhase.Left, PlayerPhase.SideChosen, false)]
        [InlineData(PlayerPhase.Ended, PlayerPhase.Spectating, false)]
        public void Player_state_machine(PlayerPhase from, PlayerPhase to, bool allowed) =>
            Assert.Equal(allowed, PlayerStateMachine.CanTransition(from, to));

        // ---------------------------------------------------------------- siege roles

        [Fact]
        public void Siege_engine_roles_are_claimable_classes_listed_after_troop_classes()
        {
            var (s, _) = Open();
            Join(s, P2, BattleSide.Attacker);
            Join(s, P3, BattleSide.Attacker);
            Join(s, P4, BattleSide.Attacker);
            Assert.Equal(SlotKind.SiegeEngine, s.GetSlots(P2).Last().Kind);
            Assert.True(s.Reserve(P2, "a-ram").Ok);
            Assert.True(s.Reserve(P3, "a-ram").Ok);
            Assert.Equal(SessionError.SlotUnavailable, s.Reserve(P4, "a-ram").Error);
        }

        [Fact]
        public void Four_players_fill_a_battle_and_session_state_stays_consistent()
        {
            var (s, _) = Open();
            Join(s, P2, BattleSide.Attacker);
            Join(s, P3, BattleSide.Defender);
            Join(s, P4, BattleSide.Attacker);
            s.Confirm(P2, s.Reserve(P2, "a-lord2").Reservation!.Id);
            s.Confirm(P3, s.Reserve(P3, "d-lord").Reservation!.Id);
            s.Confirm(P4, s.Reserve(P4, "a-inf").Reservation!.Id);
            Assert.All(new[] { Host, P2, P3, P4 }, p => Assert.Equal(PlayerPhase.Controlling, s.GetPhase(p)));
            Assert.Equal(new[] { "a-lord1", "a-lord2", "d-lord", "a-inf" }.OrderBy(x => x), new[] { Host, P2, P3, P4 }.Select(p => s.GetHeldSlot(p)!).OrderBy(x => x));
        }

        // ---------------------------------------------------------------- review findings (Phase 3 review)

        [Fact]
        public void Initiator_cannot_release_or_spectate_their_own_hero()
        {
            var (s, _) = Open();
            Join(s, P2, BattleSide.Attacker);
            Assert.Equal(SessionError.WrongPhase, s.Release(Host));
            Assert.Equal(SessionError.WrongPhase, s.Spectate(Host));
            Assert.Equal(PlayerPhase.Controlling, s.GetPhase(Host));
            Assert.Equal(SessionError.SlotUnavailable, s.Reserve(P2, "a-lord1").Error);
        }

        [Fact]
        public void Initiator_disconnect_never_hands_their_hero_to_someone_else_and_signals_ai_handover()
        {
            var (s, _) = Open();
            Join(s, P2, BattleSide.Attacker);
            s.DrainEvents();
            s.Disconnect(Host);
            Assert.Contains(s.DrainEvents(), e => e.Kind == SessionEventKind.ControlReleased && e.Player == Host && e.SlotId == "a-lord1");
            Assert.Equal(SessionError.SlotUnavailable, s.Reserve(P2, "a-lord1").Error);
            Assert.DoesNotContain(s.GetSlots(P2), v => v.Id == "a-lord1");
        }

        [Fact]
        public void Release_and_spectate_of_a_controlling_player_signal_control_released()
        {
            var (s, _) = Open();
            Join(s, P2, BattleSide.Attacker);
            Join(s, P3, BattleSide.Attacker);
            s.Confirm(P2, s.Reserve(P2, "a-lord2").Reservation!.Id);
            s.Confirm(P3, s.Reserve(P3, "a-off").Reservation!.Id);
            s.DrainEvents();
            s.Release(P2);
            s.Spectate(P3);
            var ev = s.DrainEvents().Where(e => e.Kind == SessionEventKind.ControlReleased).ToList();
            Assert.Contains(ev, e => e.Player == P2 && e.SlotId == "a-lord2");
            Assert.Contains(ev, e => e.Player == P3 && e.SlotId == "a-off");
        }

        [Fact]
        public void Cancelling_an_unconfirmed_reservation_is_not_a_control_release()
        {
            var (s, _) = Open();
            Join(s, P2, BattleSide.Attacker);
            s.Reserve(P2, "a-lord2");
            s.DrainEvents();
            s.Release(P2);
            Assert.DoesNotContain(s.DrainEvents(), e => e.Kind == SessionEventKind.ControlReleased);
        }

        [Fact]
        public void Ai_losses_shrink_a_class_until_it_disappears()
        {
            var (s, _) = Open();
            Join(s, P2, BattleSide.Attacker);
            Assert.True(s.ReportAiLoss("a-arch"));          // the only archer died
            Assert.DoesNotContain(s.GetSlots(P2), v => v.Id == "a-arch");
            Assert.Equal(SessionError.SlotUnavailable, s.Reserve(P2, "a-arch").Error);
            Assert.False(s.ReportAiLoss("a-arch"));         // nothing left to lose
            Assert.False(s.ReportAiLoss("nope"));
        }

        [Fact]
        public void Ai_loss_revokes_the_last_unconfirmed_reservation_and_reseats_the_player()
        {
            var (s, _) = Open();
            Join(s, P2, BattleSide.Attacker);
            Assert.True(s.Reserve(P2, "a-arch").Ok);
            Assert.True(s.ReportAiLoss("a-arch"));
            Assert.Equal(PlayerPhase.SideChosen, s.GetPhase(P2));
            Assert.True(s.Reserve(P2, "a-off").Ok);
        }

        [Fact]
        public void Ai_loss_never_takes_a_seat_away_from_a_controlling_player()
        {
            var (s, _) = Open();
            Join(s, P2, BattleSide.Attacker);
            s.Confirm(P2, s.Reserve(P2, "a-arch").Reservation!.Id);
            Assert.False(s.ReportAiLoss("a-arch"));
            Assert.Equal(PlayerPhase.Controlling, s.GetPhase(P2));
        }

        [Fact]
        public void Agent_dying_before_confirm_cancels_the_reservation_and_a_late_confirm_fails()
        {
            var (s, _) = Open();
            Join(s, P2, BattleSide.Attacker);
            var r = s.Reserve(P2, "a-lord2");
            Assert.Equal(DownOutcome.Reallocate, s.ReportDown(P2));
            Assert.NotEqual(SessionError.None, s.Confirm(P2, r.Reservation!.Id));
            Assert.DoesNotContain(s.GetSlots(P2), v => v.Id == "a-lord2");
        }

        [Fact]
        public void Participant_cap_includes_the_initiator_and_frees_up_when_someone_leaves()
        {
            var (s, _) = Open();                              // MaxParticipants = 4
            Assert.Equal(SessionError.None, s.Invite(P2));
            Assert.Equal(SessionError.None, s.Invite(P3));
            Assert.Equal(SessionError.None, s.Invite(P4));
            Assert.Equal(SessionError.SessionFull, s.Invite(new PlayerId(5)));
            s.Disconnect(P3);
            Assert.Equal(SessionError.None, s.Invite(new PlayerId(5)));
        }

        [Fact]
        public void Undefined_side_value_is_refused()
        {
            var (s, _) = Open();
            s.Invite(P2);
            Assert.Equal(SessionError.SideNotAllowed, s.ChooseSide(P2, (BattleSide)7));
            Assert.Equal(PlayerPhase.Invited, s.GetPhase(P2));
        }

        [Fact]
        public void Epoch_comes_from_the_options()
        {
            var s = new BattleSession("b", Catalog(), new ManualClock(), new SessionOptions { Epoch = 42 }, Host, BattleSide.Attacker, "a-lord1");
            Assert.Equal(42u, s.Epoch);
        }

        /// <summary>Clock that moves on every read: time passes between the session sweep and the reservation service call.</summary>
        private sealed class TickingClock : IClock
        {
            private long _now;

            public long NowMs => System.Threading.Interlocked.Add(ref _now, 4_000);
        }

        [Fact]
        public void Expiry_noticed_inside_another_call_never_loses_the_slot_change()
        {
            var clock = new TickingClock();
            var s = new BattleSession("b", Catalog(), clock, null, Host, BattleSide.Attacker, "a-lord1");
            Join(s, P2, BattleSide.Attacker);
            Join(s, P3, BattleSide.Attacker);
            Assert.True(s.Reserve(P2, "a-lord2").Ok);
            s.DrainEvents();
            // Each clock read advances 4 s, so P2's reservation expires inside this call, after the session's own sweep.
            s.Disconnect(P2);
            s.Disconnect(P3);
            var ev = s.DrainEvents();
            Assert.Contains(ev, e => e.Kind == SessionEventKind.SlotChanged && e.SlotId == "a-lord2");
        }

        [Fact]
        public void Two_players_claim_the_same_lord_concurrently_one_wins()
        {
            for (var round = 0; round < 200; round++)
            {
                var (s, _) = Open();
                Join(s, P2, BattleSide.Attacker);
                Join(s, P3, BattleSide.Attacker);
                var res = new ReserveResult[2];
                var gate = new ManualResetEventSlim();
                var tasks = new[] { P2, P3 }.Select((p, i) => Task.Run(() =>
                {
                    gate.Wait();
                    res[i] = s.Reserve(p, "a-lord2");
                })).ToArray();
                gate.Set();
                Task.WaitAll(tasks);
                Assert.Equal(1, res.Count(r => r.Ok));
            }
        }

        [Fact]
        public void Catalog_changes_while_other_threads_list_slots_do_not_corrupt_anything()
        {
            var (s, _) = Open();
            Join(s, P2, BattleSide.Attacker);
            var stop = false;
            var errors = new System.Collections.Concurrent.ConcurrentBag<System.Exception>();
            var readers = Enumerable.Range(0, 3).Select(_ => Task.Run(() =>
            {
                try
                {
                    while (!stop)
                        s.GetSlots(P2).ToList();
                }
                catch (System.Exception e)
                {
                    errors.Add(e);
                }
            })).ToArray();
            for (var i = 0; i < 300; i++)
                s.AddSlot(new SlotDefinition("x" + i, BattleSide.Attacker, SlotKind.TroopClass, "X", 1, 100 + i));
            stop = true;
            Task.WaitAll(readers);
            Assert.Empty(errors);
            Assert.Equal(300 + 5, s.GetSlots(P2).Count);
        }

        [Fact]
        public void End_racing_with_reserve_never_leaves_a_live_reservation()
        {
            for (var round = 0; round < 100; round++)
            {
                var (s, _) = Open();
                Join(s, P2, BattleSide.Attacker);
                var gate = new ManualResetEventSlim();
                var t1 = Task.Run(() =>
                {
                    gate.Wait();
                    s.Reserve(P2, "a-lord2");
                });
                var t2 = Task.Run(() =>
                {
                    gate.Wait();
                    s.End();
                });
                gate.Set();
                Task.WaitAll(t1, t2);
                Assert.Equal(PlayerPhase.Ended, s.GetPhase(P2));
                Assert.Equal(SessionError.BattleClosed, s.Reserve(P2, "a-off").Error);
            }
        }
    }
}
