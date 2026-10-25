using BannerCoopTW.Core.Battle;
using BannerCoopTW.Core.Protocol;
using Xunit;

namespace BannerCoopTW.Core.Tests
{
    public class SequenceGateTests
    {
        [Fact]
        public void Accepts_increasing_and_drops_old_or_duplicate()
        {
            var g = new SequenceGate();
            Assert.True(g.Accept(1));
            Assert.True(g.Accept(2));
            Assert.True(g.Accept(5));
            Assert.False(g.Accept(4));   // late
            Assert.False(g.Accept(5));   // duplicate
            Assert.True(g.Accept(6));
            Assert.Equal(4, g.Accepted);
            Assert.Equal(2, g.Dropped);
            Assert.Equal(6u, g.Newest);
        }

        [Fact]
        public void Survives_wrap_around()
        {
            var g = new SequenceGate();
            Assert.True(g.Accept(uint.MaxValue - 1));
            Assert.True(g.Accept(uint.MaxValue));
            Assert.True(g.Accept(0));
            Assert.True(g.Accept(1));
            Assert.False(g.Accept(uint.MaxValue)); // older than 1 after the wrap
        }

        [Fact]
        public void Counter_and_gate_work_together_over_a_shuffled_stream()
        {
            var c = new SequenceCounter();
            var sent = new uint[10];
            for (var i = 0; i < sent.Length; i++)
                sent[i] = c.Next();
            // network order: 1 2 4 3 5 7 6 8 10 9
            var arrival = new[] { 0, 1, 3, 2, 4, 6, 5, 7, 9, 8 };
            var g = new SequenceGate();
            var accepted = new System.Collections.Generic.List<uint>();
            foreach (var i in arrival)
                if (g.Accept(sent[i]))
                    accepted.Add(sent[i]);
            Assert.Equal(new uint[] { 1, 2, 4, 5, 7, 8, 10 }, accepted);
            Assert.Equal(3, g.Dropped);
        }

        [Fact]
        public void Reset_forgets_the_stream()
        {
            var g = new SequenceGate();
            g.Accept(100);
            g.Reset();
            Assert.True(g.Accept(1));
        }
    }

    public class RejoinTests
    {
        private static readonly PlayerId Host = new PlayerId(1);
        private static readonly PlayerId P2 = new PlayerId(2);

        private static (BattleSession s, ManualClock c) Open(int max = 4)
        {
            var clock = new ManualClock();
            var catalog = SlotCatalog.Create()
                .AddLord(BattleSide.Attacker, "a-lord1", "Lord A")
                .AddLord(BattleSide.Attacker, "a-lord2", "Lord C")
                .AddTroopClass(BattleSide.Defender, "d-inf", "Infantry", 2)
                .Build();
            return (new BattleSession("b1", catalog, clock, new SessionOptions { MaxParticipants = max }, Host, BattleSide.Attacker, "a-lord1"), clock);
        }

        [Fact]
        public void A_player_who_left_can_rejoin_as_invited_and_has_no_seat()
        {
            var (s, _) = Open();
            s.Invite(P2);
            s.ChooseSide(P2, BattleSide.Attacker);
            Assert.True(s.Reserve(P2, "a-lord2").Ok);
            s.Disconnect(P2);
            Assert.Equal(PlayerPhase.Left, s.GetPhase(P2));

            Assert.Equal(SessionError.None, s.Rejoin(P2));
            Assert.Equal(PlayerPhase.Invited, s.GetPhase(P2));
            Assert.Null(s.GetHeldSlot(P2));
            Assert.Equal(SessionError.None, s.ChooseSide(P2, BattleSide.Attacker));
            Assert.True(s.Reserve(P2, "a-lord2").Ok); // the lord was freed when the player left
        }

        [Fact]
        public void Rejoin_is_refused_for_unknown_present_or_after_the_end()
        {
            var (s, _) = Open();
            Assert.Equal(SessionError.UnknownPlayer, s.Rejoin(P2));
            s.Invite(P2);
            Assert.Equal(SessionError.AlreadyInSession, s.Rejoin(P2));
            s.Disconnect(P2);
            s.End();
            Assert.Equal(SessionError.BattleClosed, s.Rejoin(P2));
        }

        [Fact]
        public void Rejoin_respects_the_participant_cap()
        {
            var (s, _) = Open(max: 2);                 // initiator + one more
            s.Invite(P2);
            s.Disconnect(P2);
            Assert.Equal(SessionError.None, s.Invite(new PlayerId(3)));
            Assert.Equal(SessionError.SessionFull, s.Rejoin(P2));
        }
    }
}
