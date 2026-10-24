using System;

namespace BannerCoopTW.Core.Battle
{
    public enum BattleSide : byte
    {
        Attacker = 0,
        Defender = 1,
    }

    /// <summary>Slot groups in the order they are listed to a player (BATTLE_PARTICIPATION.md: lords, officers, classes; spectator is implicit).</summary>
    public enum SlotKind : byte
    {
        Lord = 0,
        Officer = 1,
        TroopClass = 2,
        SiegeEngine = 3,
    }

    /// <summary>Player lifecycle inside one battle session.</summary>
    public enum PlayerPhase : byte
    {
        Invited = 0,
        SideChosen = 1,
        Reserved = 2,
        Controlling = 3,
        Spectating = 4,
        /// <summary>Disconnected or left; terminal.</summary>
        Left = 5,
        /// <summary>Battle finished; terminal.</summary>
        Ended = 6,
    }

    public readonly struct PlayerId : IEquatable<PlayerId>, IComparable<PlayerId>
    {
        public PlayerId(ulong value) => Value = value;

        public ulong Value { get; }

        public bool Equals(PlayerId other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is PlayerId p && Equals(p);

        public override int GetHashCode() => Value.GetHashCode();

        public int CompareTo(PlayerId other) => Value.CompareTo(other.Value);

        public override string ToString() => "P" + Value;

        public static bool operator ==(PlayerId a, PlayerId b) => a.Equals(b);

        public static bool operator !=(PlayerId a, PlayerId b) => !a.Equals(b);
    }

    public interface IClock
    {
        /// <summary>Monotonic milliseconds.</summary>
        long NowMs { get; }
    }

    public sealed class SystemClock : IClock
    {
        private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();

        public long NowMs => _sw.ElapsedMilliseconds;
    }

    public sealed class SessionOptions
    {
        public const int MinReservationTimeoutMs = 10_000;
        public const int MaxReservationTimeoutMs = 15_000;

        private int _timeoutMs = 12_000;

        /// <summary>Reservation lifetime; must be within 10–15 s (BATTLE_PARTICIPATION.md).</summary>
        public int ReservationTimeoutMs
        {
            get => _timeoutMs;
            set
            {
                if (value < MinReservationTimeoutMs || value > MaxReservationTimeoutMs)
                    throw new ArgumentOutOfRangeException(nameof(value), "Reservation timeout must be 10000–15000 ms.");
                _timeoutMs = value;
            }
        }

        /// <summary>PvP (ADR-0007): players may join the opposing side. When false only the initiator's side is allowed.</summary>
        public bool AllowPvp { get; set; } = true;

        /// <summary>A player who lost their slot may take another one (default behaviour, ADR-0007).</summary>
        public bool AllowReallocation { get; set; } = true;
    }
}
