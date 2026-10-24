using System.Collections.Generic;

namespace BannerCoopTW.Core.Battle
{
    /// <summary>Life of one seat of a slot (BATTLE_PARTICIPATION.md: FREE → RESERVED → CONTROLLED → DEAD/RELEASED).</summary>
    public enum SeatState : byte
    {
        Free = 0,
        Reserved = 1,
        Controlled = 2,
        /// <summary>Dead, or an immortal hero knocked out: the seat is gone for this battle.</summary>
        Down = 3,
    }

    public static class SeatStateMachine
    {
        public static bool CanTransition(SeatState from, SeatState to)
        {
            switch (from)
            {
                case SeatState.Free:
                    return to == SeatState.Reserved;
                case SeatState.Reserved:
                    return to == SeatState.Free || to == SeatState.Controlled || to == SeatState.Down; // cancel / expiry / disconnect, confirm, or the agent died before confirm
                case SeatState.Controlled:
                    return to == SeatState.Free || to == SeatState.Down;       // released (disconnect, leave) or killed/knocked out
                default:
                    return false;                                              // Down is terminal
            }
        }
    }

    public static class PlayerStateMachine
    {
        private static readonly Dictionary<PlayerPhase, PlayerPhase[]> Allowed = new Dictionary<PlayerPhase, PlayerPhase[]>
        {
            [PlayerPhase.Invited] = new[] { PlayerPhase.SideChosen, PlayerPhase.Spectating, PlayerPhase.Left, PlayerPhase.Ended },
            [PlayerPhase.SideChosen] = new[] { PlayerPhase.SideChosen, PlayerPhase.Reserved, PlayerPhase.Spectating, PlayerPhase.Left, PlayerPhase.Ended },
            [PlayerPhase.Reserved] = new[] { PlayerPhase.Controlling, PlayerPhase.SideChosen, PlayerPhase.Spectating, PlayerPhase.Left, PlayerPhase.Ended },
            [PlayerPhase.Controlling] = new[] { PlayerPhase.SideChosen, PlayerPhase.Spectating, PlayerPhase.Left, PlayerPhase.Ended },
            [PlayerPhase.Spectating] = new[] { PlayerPhase.SideChosen, PlayerPhase.Left, PlayerPhase.Ended },
            [PlayerPhase.Left] = new PlayerPhase[0],
            [PlayerPhase.Ended] = new PlayerPhase[0],
        };

        public static bool CanTransition(PlayerPhase from, PlayerPhase to)
        {
            foreach (var p in Allowed[from])
                if (p == to)
                    return true;
            return false;
        }
    }
}
