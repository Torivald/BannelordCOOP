using System;
using System.Collections.Generic;
using System.Linq;

namespace BannerCoopTW.Core.Battle
{
    public enum ReserveError : byte
    {
        None = 0,
        UnknownSlot,
        SlotUnavailable,
        AlreadyHolding,
    }

    public enum ConfirmError : byte
    {
        None = 0,
        NotFound,
        Expired,
        WrongState,
    }

    public sealed class Reservation
    {
        internal Reservation(long id, PlayerId player, string slotId, long expiresAtMs)
        {
            Id = id;
            Player = player;
            SlotId = slotId;
            ExpiresAtMs = expiresAtMs;
        }

        public long Id { get; }

        public PlayerId Player { get; }

        public string SlotId { get; }

        public long ExpiresAtMs { get; }

        /// <summary>Reserved until confirmed; then Controlled.</summary>
        public SeatState State { get; internal set; } = SeatState.Reserved;
    }

    /// <summary>
    /// Atomic seat bookkeeping: a seat of a slot goes to exactly one player. Every public member takes one lock, so two
    /// simultaneous claims for the last seat have a single winner. A player holds at most one seat.
    /// Expiry is lazy and deterministic: it runs on every call and can also be driven by <see cref="ExpireDue"/>.
    /// </summary>
    public sealed class ReservationService
    {
        private readonly object _gate = new object();
        private readonly IClock _clock;
        private readonly int _timeoutMs;
        private readonly Dictionary<string, int> _capacity = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _lost = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<PlayerId, Reservation> _held = new Dictionary<PlayerId, Reservation>();
        private readonly HashSet<long> _expired = new HashSet<long>();
        private readonly List<Reservation> _expiredUnreported = new List<Reservation>();
        private long _nextId = 1;

        public ReservationService(SlotCatalog catalog, IClock clock, int timeoutMs)
        {
            if (timeoutMs < SessionOptions.MinReservationTimeoutMs || timeoutMs > SessionOptions.MaxReservationTimeoutMs)
                throw new ArgumentOutOfRangeException(nameof(timeoutMs), "Reservation timeout must be 10000–15000 ms.");
            _clock = clock;
            _timeoutMs = timeoutMs;
            foreach (var s in catalog.All)
                Register(s);
        }

        public void Register(SlotDefinition slot)
        {
            lock (_gate)
            {
                _capacity[slot.Id] = slot.Capacity;
                _lost[slot.Id] = 0;
            }
        }

        public int Capacity(string slotId)
        {
            lock (_gate)
                return _capacity.TryGetValue(slotId, out var c) ? c : 0;
        }

        public int Available(string slotId)
        {
            lock (_gate)
            {
                ExpireDueLocked();
                return AvailableLocked(slotId);
            }
        }

        private int AvailableLocked(string slotId)
        {
            if (!_capacity.TryGetValue(slotId, out var cap))
                return 0;
            var held = _held.Values.Count(r => r.SlotId == slotId);
            return Math.Max(0, cap - _lost[slotId] - held);
        }

        public Reservation? GetHeld(PlayerId player)
        {
            lock (_gate)
            {
                ExpireDueLocked();
                return _held.TryGetValue(player, out var r) ? r : null;
            }
        }

        /// <summary>Hands a seat straight to a player as Controlled (the initiator's own hero is seeded this way, so nobody else can claim it).</summary>
        public Reservation SeedControlled(PlayerId player, string slotId)
        {
            lock (_gate)
            {
                if (!_capacity.ContainsKey(slotId) || AvailableLocked(slotId) < 1 || _held.ContainsKey(player))
                    throw new InvalidOperationException("Cannot seed " + slotId + " for " + player);
                var r = new Reservation(_nextId++, player, slotId, long.MaxValue) { State = SeatState.Controlled };
                _held[player] = r;
                return r;
            }
        }

        public ReserveError TryReserve(PlayerId player, string slotId, out Reservation? reservation)
        {
            lock (_gate)
            {
                reservation = null;
                ExpireDueLocked();
                if (!_capacity.ContainsKey(slotId))
                    return ReserveError.UnknownSlot;
                if (_held.ContainsKey(player))
                    return ReserveError.AlreadyHolding;
                if (AvailableLocked(slotId) < 1)
                    return ReserveError.SlotUnavailable;
                reservation = new Reservation(_nextId++, player, slotId, _clock.NowMs + _timeoutMs);
                _held[player] = reservation;
                return ReserveError.None;
            }
        }

        public ConfirmError TryConfirm(PlayerId player, long reservationId, out Reservation? reservation)
        {
            lock (_gate)
            {
                reservation = null;
                ExpireDueLocked();
                if (_held.TryGetValue(player, out var r) && r.Id == reservationId)
                {
                    if (r.State != SeatState.Reserved)
                        return ConfirmError.WrongState;
                    r.State = SeatState.Controlled;
                    reservation = r;
                    return ConfirmError.None;
                }

                return _expired.Contains(reservationId) ? ConfirmError.Expired : ConfirmError.NotFound;
            }
        }

        /// <summary>Gives the seat back (cancel, leave, disconnect). Returns the freed reservation or null.</summary>
        public Reservation? Release(PlayerId player)
        {
            lock (_gate)
            {
                ExpireDueLocked();
                if (!_held.TryGetValue(player, out var r))
                    return null;
                _held.Remove(player);
                return r;
            }
        }

        /// <summary>The player's controlled seat is gone for the battle (death / knock-out). Returns the reservation or null.</summary>
        public Reservation? MarkDown(PlayerId player)
        {
            lock (_gate)
            {
                ExpireDueLocked();
                if (!_held.TryGetValue(player, out var r) || r.State != SeatState.Controlled)
                    return null;
                _held.Remove(player);
                _lost[r.SlotId]++;
                return r;
            }
        }

        /// <summary>Reinforcements: raises the seat count. Existing claims are never invalidated.</summary>
        public void AddCapacity(string slotId, int extra)
        {
            if (extra < 1)
                throw new ArgumentOutOfRangeException(nameof(extra));
            lock (_gate)
            {
                if (!_capacity.ContainsKey(slotId))
                    throw new KeyNotFoundException(slotId);
                _capacity[slotId] += extra;
            }
        }

        /// <summary>Drops reservations whose time is up. Each expired reservation is reported exactly once (here, whichever call noticed it first).</summary>
        public IReadOnlyList<Reservation> ExpireDue()
        {
            lock (_gate)
            {
                ExpireDueLocked();
                var list = _expiredUnreported.ToList();
                _expiredUnreported.Clear();
                return list;
            }
        }

        /// <summary>Cancels everything (battle end). Returns all seats that were held.</summary>
        public IReadOnlyList<Reservation> ReleaseAll()
        {
            lock (_gate)
            {
                var all = _held.Values.ToList();
                _held.Clear();
                return all;
            }
        }

        private void ExpireDueLocked()
        {
            var now = _clock.NowMs;
            foreach (var r in _held.Values.Where(x => x.State == SeatState.Reserved && x.ExpiresAtMs <= now).ToList())
            {
                _held.Remove(r.Player);
                _expired.Add(r.Id);
                _expiredUnreported.Add(r);
            }
        }
    }
}
