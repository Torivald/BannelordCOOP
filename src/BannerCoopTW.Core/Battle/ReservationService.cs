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

    /// <summary>A seat held by a player. Instances handed out are snapshots-by-reference: read <see cref="State"/> only through the service or right after the call that returned it.</summary>
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

        /// <summary>Reserved until confirmed; then Controlled. Written only under the service lock.</summary>
        public SeatState State { get; internal set; } = SeatState.Reserved;

        /// <summary>The initiator's own hero: never goes back to the free pool.</summary>
        public bool Pinned { get; internal set; }
    }

    /// <summary>
    /// Atomic seat bookkeeping: a seat of a slot goes to exactly one player. Every public member takes one lock, so two
    /// simultaneous claims for the last seat have a single winner. A player holds at most one seat.
    /// Expiry is lazy: it runs inside every call; expired reservations are queued and reported exactly once through <see cref="ExpireDue"/>.
    /// </summary>
    public sealed class ReservationService
    {
        public const int MaxSeatsPerSlot = ushort.MaxValue;

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

        /// <summary>Hands a seat straight to a player as Controlled and pinned (the initiator's own hero: nobody else can ever claim it).</summary>
        public Reservation SeedControlled(PlayerId player, string slotId)
        {
            lock (_gate)
            {
                if (!_capacity.ContainsKey(slotId) || AvailableLocked(slotId) < 1 || _held.ContainsKey(player))
                    throw new InvalidOperationException("Cannot seed " + slotId + " for " + player);
                var r = new Reservation(_nextId++, player, slotId, long.MaxValue) { State = SeatState.Controlled, Pinned = true };
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
                    if (!SeatStateMachine.CanTransition(r.State, SeatState.Controlled))
                        return ConfirmError.WrongState;
                    r.State = SeatState.Controlled;
                    reservation = r;
                    return ConfirmError.None;
                }

                return _expired.Contains(reservationId) ? ConfirmError.Expired : ConfirmError.NotFound;
            }
        }

        /// <summary>
        /// Gives the seat back (cancel, leave, disconnect). A pinned seat is not returned to the pool: it is written off as lost.
        /// Returns the removed reservation (its <see cref="Reservation.State"/> tells whether control was released) or null.
        /// </summary>
        public Reservation? Release(PlayerId player)
        {
            lock (_gate)
            {
                ExpireDueLocked();
                if (!_held.TryGetValue(player, out var r))
                    return null;
                _held.Remove(player);
                if (r.Pinned)
                    _lost[r.SlotId]++;
                return r;
            }
        }

        /// <summary>The player's seat is gone for the battle (death / knock-out), also if the agent died before the reservation was confirmed.</summary>
        public Reservation? MarkDown(PlayerId player)
        {
            lock (_gate)
            {
                ExpireDueLocked();
                if (!_held.TryGetValue(player, out var r))
                    return null;
                _held.Remove(player);
                _lost[r.SlotId]++;
                return r;
            }
        }

        /// <summary>
        /// An AI-controlled agent of the slot died. The seat count shrinks: a free seat is written off; if none is free, the most
        /// recent unconfirmed reservation is revoked (its agent is dead) and returned. Controlled seats belong to players and are never touched.
        /// </summary>
        public bool LoseAiSeat(string slotId, out Reservation? revoked)
        {
            lock (_gate)
            {
                revoked = null;
                ExpireDueLocked();
                if (!_capacity.ContainsKey(slotId))
                    return false;
                if (_capacity[slotId] - _lost[slotId] <= 0)
                    return false;
                if (AvailableLocked(slotId) < 1)
                {
                    revoked = _held.Values.Where(r => r.SlotId == slotId && r.State == SeatState.Reserved).OrderByDescending(r => r.Id).FirstOrDefault();
                    if (revoked == null)
                        return false; // every remaining seat is player-controlled: the dead agent was not an AI one
                    _held.Remove(revoked.Player);
                }

                _lost[slotId]++;
                return true;
            }
        }

        /// <summary>Reinforcements: raises the seat count. Existing claims are never invalidated.</summary>
        public bool AddCapacity(string slotId, int extra)
        {
            if (extra < 1)
                throw new ArgumentOutOfRangeException(nameof(extra));
            lock (_gate)
            {
                if (!_capacity.TryGetValue(slotId, out var cap))
                    return false;
                if ((long)cap + extra > MaxSeatsPerSlot)
                    throw new ArgumentOutOfRangeException(nameof(extra), "A slot cannot exceed " + MaxSeatsPerSlot + " seats (wire format).");
                _capacity[slotId] = cap + extra;
                return true;
            }
        }

        /// <summary>Drains the reservations whose time ran out (each reported exactly once, whichever call noticed the expiry).</summary>
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
