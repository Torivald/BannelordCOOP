using System;
using System.Collections.Generic;
using System.Linq;

namespace BannerCoopTW.Core.Battle
{
    public enum SessionError : byte
    {
        None = 0,
        BattleClosed,
        UnknownPlayer,
        AlreadyInSession,
        WrongPhase,
        SideNotAllowed,
        WrongSide,
        UnknownSlot,
        SlotUnavailable,
        ReservationExpired,
        ReservationNotFound,
    }

    public enum SessionEventKind : byte
    {
        /// <summary>Availability of a slot changed (taken, freed, lost, grown).</summary>
        SlotChanged,
        PlayerPhaseChanged,
        ReservationExpired,
        /// <summary>A controlling player left or disconnected; the engine side may hand the agent back to AI.</summary>
        ControlReleased,
        BattleEnded,
    }

    public sealed class SessionEvent
    {
        public SessionEvent(SessionEventKind kind, int revision, PlayerId? player, string? slotId)
        {
            Kind = kind;
            Revision = revision;
            Player = player;
            SlotId = slotId;
        }

        public SessionEventKind Kind { get; }

        public int Revision { get; }

        public PlayerId? Player { get; }

        public string? SlotId { get; }
    }

    public sealed class SlotView
    {
        public SlotView(string id, BattleSide side, SlotKind kind, string label, int capacity, int available)
        {
            Id = id;
            Side = side;
            Kind = kind;
            Label = label;
            Capacity = capacity;
            Available = available;
        }

        public string Id { get; }

        public BattleSide Side { get; }

        public SlotKind Kind { get; }

        public string Label { get; }

        public int Capacity { get; }

        public int Available { get; }
    }

    public sealed class ReserveResult
    {
        internal ReserveResult(SessionError error, Reservation? reservation)
        {
            Error = error;
            Reservation = reservation;
        }

        public SessionError Error { get; }

        public Reservation? Reservation { get; }

        public bool Ok => Error == SessionError.None;
    }

    public enum DownOutcome : byte
    {
        NotControlling = 0,
        /// <summary>Another slot is free: the player is back at slot selection.</summary>
        Reallocate,
        Spectate,
    }

    /// <summary>
    /// Authoritative state of one battle (ADR-0003): who may join, which slots exist, who holds what. Clients only send requests;
    /// every decision happens here. All members are thread-safe (one lock). <see cref="Revision"/> grows on every observable change
    /// so snapshots/deltas can be versioned.
    /// </summary>
    public sealed class BattleSession
    {
        private readonly object _gate = new object();
        private readonly SlotCatalog _catalog;
        private readonly ReservationService _reservations;
        private readonly SessionOptions _options;
        private readonly Dictionary<PlayerId, PlayerInfo> _players = new Dictionary<PlayerId, PlayerInfo>();
        private readonly List<SessionEvent> _events = new List<SessionEvent>();
        private bool _accepting = true;
        private bool _ended;
        private int _revision;

        private sealed class PlayerInfo
        {
            public PlayerPhase Phase = PlayerPhase.Invited;
            public BattleSide? Side;
        }

        /// <param name="initiatorSlotId">The initiator's own hero; they control it from the start and nobody can claim it.</param>
        public BattleSession(string battleId, SlotCatalog catalog, IClock clock, SessionOptions? options, PlayerId initiator, BattleSide initiatorSide, string initiatorSlotId)
        {
            BattleId = battleId;
            _catalog = catalog;
            _options = options ?? new SessionOptions();
            _reservations = new ReservationService(catalog, clock, _options.ReservationTimeoutMs);
            Initiator = initiator;
            InitiatorSide = initiatorSide;

            if (!catalog.TryGet(initiatorSlotId, out var slot) || slot.Side != initiatorSide)
                throw new ArgumentException("Initiator slot must exist on the initiator's side.", nameof(initiatorSlotId));
            _reservations.SeedControlled(initiator, initiatorSlotId);
            _players[initiator] = new PlayerInfo { Phase = PlayerPhase.Controlling, Side = initiatorSide };
        }

        public string BattleId { get; }

        public PlayerId Initiator { get; }

        public BattleSide InitiatorSide { get; }

        public int Revision
        {
            get
            {
                lock (_gate)
                    return _revision;
            }
        }

        public bool IsEnded
        {
            get
            {
                lock (_gate)
                    return _ended;
            }
        }

        public PlayerPhase? GetPhase(PlayerId player)
        {
            lock (_gate)
            {
                Sync();
                return _players.TryGetValue(player, out var p) ? p.Phase : (PlayerPhase?)null;
            }
        }

        public BattleSide? GetSide(PlayerId player)
        {
            lock (_gate)
                return _players.TryGetValue(player, out var p) ? p.Side : null;
        }

        /// <summary>Slot the player reserves or controls, if any.</summary>
        public string? GetHeldSlot(PlayerId player)
        {
            lock (_gate)
            {
                Sync();
                return _reservations.GetHeld(player)?.SlotId;
            }
        }

        // ---------------------------------------------------------------- joining

        public SessionError Invite(PlayerId player)
        {
            lock (_gate)
            {
                Sync();
                if (_ended || !_accepting)
                    return SessionError.BattleClosed;
                if (_players.ContainsKey(player))
                    return SessionError.AlreadyInSession; // reconnect is a later milestone
                _players[player] = new PlayerInfo();
                Emit(SessionEventKind.PlayerPhaseChanged, player, null);
                return SessionError.None;
            }
        }

        /// <summary>Stops admitting new players; players already inside keep their rights.</summary>
        public void CloseJoining()
        {
            lock (_gate)
                _accepting = false;
        }

        public SessionError ChooseSide(PlayerId player, BattleSide side)
        {
            lock (_gate)
            {
                Sync();
                if (_ended)
                    return SessionError.BattleClosed;
                if (!_players.TryGetValue(player, out var p))
                    return SessionError.UnknownPlayer;
                if (p.Phase == PlayerPhase.Invited && !_accepting)
                    return SessionError.BattleClosed;
                if (p.Phase != PlayerPhase.Invited && p.Phase != PlayerPhase.SideChosen && !(p.Phase == PlayerPhase.Spectating && _options.AllowReallocation))
                    return SessionError.WrongPhase;
                if (!_options.AllowPvp && side != InitiatorSide)
                    return SessionError.SideNotAllowed;
                p.Side = side;
                SetPhase(player, p, PlayerPhase.SideChosen);
                return SessionError.None;
            }
        }

        public SessionError Spectate(PlayerId player)
        {
            lock (_gate)
            {
                Sync();
                if (_ended)
                    return SessionError.BattleClosed;
                if (!_players.TryGetValue(player, out var p))
                    return SessionError.UnknownPlayer;
                if (p.Phase == PlayerPhase.Left || p.Phase == PlayerPhase.Ended)
                    return SessionError.WrongPhase;
                if (p.Phase == PlayerPhase.Spectating)
                    return SessionError.None;
                FreeSeat(player);
                p.Side = null;
                SetPhase(player, p, PlayerPhase.Spectating);
                return SessionError.None;
            }
        }

        // ---------------------------------------------------------------- slots

        /// <summary>Claimable slots on the player's side in presentation order. Full slots are omitted ("a taken lord disappears for everyone").</summary>
        public IReadOnlyList<SlotView> GetSlots(PlayerId player)
        {
            lock (_gate)
            {
                Sync();
                if (_ended || !_players.TryGetValue(player, out var p) || p.Side == null)
                    return new SlotView[0];
                return SlotsFor(p.Side.Value);
            }
        }

        private List<SlotView> SlotsFor(BattleSide side) =>
            _catalog.ForSide(side)
                .Select(s => new SlotView(s.Id, s.Side, s.Kind, s.Label, _reservations.Capacity(s.Id), _reservations.Available(s.Id)))
                .Where(v => v.Available > 0)
                .ToList();

        public ReserveResult Reserve(PlayerId player, string slotId)
        {
            lock (_gate)
            {
                Sync();
                if (_ended)
                    return new ReserveResult(SessionError.BattleClosed, null);
                if (!_players.TryGetValue(player, out var p))
                    return new ReserveResult(SessionError.UnknownPlayer, null);
                if (p.Phase != PlayerPhase.SideChosen)
                    return new ReserveResult(SessionError.WrongPhase, null);
                if (!_catalog.TryGet(slotId, out var def))
                    return new ReserveResult(SessionError.UnknownSlot, null);
                if (def.Side != p.Side)
                    return new ReserveResult(SessionError.WrongSide, null);
                switch (_reservations.TryReserve(player, slotId, out var r))
                {
                    case ReserveError.None:
                        SetPhase(player, p, PlayerPhase.Reserved);
                        Emit(SessionEventKind.SlotChanged, player, slotId);
                        return new ReserveResult(SessionError.None, r);
                    case ReserveError.UnknownSlot:
                        return new ReserveResult(SessionError.UnknownSlot, null);
                    default:
                        return new ReserveResult(SessionError.SlotUnavailable, null);
                }
            }
        }

        /// <summary>The engine side bound the agent: Reserved → Controlling.</summary>
        public SessionError Confirm(PlayerId player, long reservationId)
        {
            lock (_gate)
            {
                Sync();
                if (_ended)
                    return SessionError.BattleClosed;
                if (!_players.TryGetValue(player, out var p))
                    return SessionError.UnknownPlayer;
                switch (_reservations.TryConfirm(player, reservationId, out _))
                {
                    case ConfirmError.None:
                        SetPhase(player, p, PlayerPhase.Controlling);
                        return SessionError.None;
                    case ConfirmError.Expired:
                        Sync();
                        return SessionError.ReservationExpired;
                    case ConfirmError.WrongState:
                        return SessionError.WrongPhase;
                    default:
                        return SessionError.ReservationNotFound;
                }
            }
        }

        /// <summary>Cancels a reservation or gives control back; the player returns to slot selection on the same side.</summary>
        public SessionError Release(PlayerId player)
        {
            lock (_gate)
            {
                Sync();
                if (_ended)
                    return SessionError.BattleClosed;
                if (!_players.TryGetValue(player, out var p))
                    return SessionError.UnknownPlayer;
                if (p.Phase != PlayerPhase.Reserved && p.Phase != PlayerPhase.Controlling)
                    return SessionError.WrongPhase;
                FreeSeat(player);
                SetPhase(player, p, PlayerPhase.SideChosen);
                return SessionError.None;
            }
        }

        /// <summary>
        /// A controlled character died, or an immortal hero was knocked out (ADR-0007). The seat is lost for this battle; the player goes
        /// back to selection if another slot is free on their side, otherwise spectates.
        /// </summary>
        public DownOutcome ReportDown(PlayerId player)
        {
            lock (_gate)
            {
                Sync();
                if (_ended || !_players.TryGetValue(player, out var p) || p.Phase != PlayerPhase.Controlling)
                    return DownOutcome.NotControlling;
                var r = _reservations.MarkDown(player);
                if (r == null)
                    return DownOutcome.NotControlling;
                Emit(SessionEventKind.SlotChanged, player, r.SlotId);
                if (_options.AllowReallocation && p.Side != null && SlotsFor(p.Side.Value).Count > 0)
                {
                    SetPhase(player, p, PlayerPhase.SideChosen);
                    return DownOutcome.Reallocate;
                }

                p.Side = null;
                SetPhase(player, p, PlayerPhase.Spectating);
                return DownOutcome.Spectate;
            }
        }

        public void Disconnect(PlayerId player)
        {
            lock (_gate)
            {
                Sync();
                if (!_players.TryGetValue(player, out var p) || p.Phase == PlayerPhase.Left || p.Phase == PlayerPhase.Ended)
                    return;
                var wasControlling = p.Phase == PlayerPhase.Controlling;
                var slot = FreeSeat(player);
                if (wasControlling)
                    Emit(SessionEventKind.ControlReleased, player, slot);
                SetPhase(player, p, PlayerPhase.Left);
            }
        }

        // ---------------------------------------------------------------- reinforcements, end, time

        /// <summary>More troops of an existing class arrived. Existing ownership is untouched.</summary>
        public void AddReinforcements(string slotId, int count)
        {
            lock (_gate)
            {
                if (_ended)
                    return;
                _reservations.AddCapacity(slotId, count);
                Emit(SessionEventKind.SlotChanged, null, slotId);
            }
        }

        /// <summary>A new slot appears mid-battle (a new troop class, a lord arriving with reinforcements).</summary>
        public void AddSlot(SlotDefinition slot)
        {
            lock (_gate)
            {
                if (_ended)
                    return;
                _catalog.Extend(slot);
                _reservations.Register(slot);
                Emit(SessionEventKind.SlotChanged, null, slot.Id);
            }
        }

        /// <summary>The battle is over (also while a player has the allocation UI open): everything is cancelled, later requests fail with BattleClosed.</summary>
        public void End()
        {
            lock (_gate)
            {
                if (_ended)
                    return;
                Sync();
                _reservations.ReleaseAll();
                _ended = true;
                foreach (var kv in _players.Where(x => x.Value.Phase != PlayerPhase.Left && x.Value.Phase != PlayerPhase.Ended).ToList())
                    SetPhase(kv.Key, kv.Value, PlayerPhase.Ended);
                Emit(SessionEventKind.BattleEnded, null, null);
            }
        }

        /// <summary>Expires due reservations (also happens lazily on every call). Returns the players who lost one.</summary>
        public IReadOnlyList<PlayerId> Tick()
        {
            lock (_gate)
                return Sync();
        }

        public IReadOnlyList<SessionEvent> DrainEvents()
        {
            lock (_gate)
            {
                Sync();
                var list = _events.ToList();
                _events.Clear();
                return list;
            }
        }

        // ---------------------------------------------------------------- internals (caller holds the lock)

        private IReadOnlyList<PlayerId> Sync()
        {
            var lost = new List<PlayerId>();
            foreach (var r in _reservations.ExpireDue())
            {
                if (_ended || !_players.TryGetValue(r.Player, out var p) || p.Phase != PlayerPhase.Reserved)
                    continue;
                SetPhase(r.Player, p, PlayerPhase.SideChosen);
                Emit(SessionEventKind.ReservationExpired, r.Player, r.SlotId);
                Emit(SessionEventKind.SlotChanged, r.Player, r.SlotId);
                lost.Add(r.Player);
            }

            return lost;
        }

        private string? FreeSeat(PlayerId player)
        {
            var r = _reservations.Release(player);
            if (r != null)
                Emit(SessionEventKind.SlotChanged, player, r.SlotId);
            return r?.SlotId;
        }

        private void SetPhase(PlayerId id, PlayerInfo p, PlayerPhase to)
        {
            if (!PlayerStateMachine.CanTransition(p.Phase, to))
                throw new InvalidOperationException("Illegal player transition " + p.Phase + " -> " + to);
            p.Phase = to;
            Emit(SessionEventKind.PlayerPhaseChanged, id, null);
        }

        private void Emit(SessionEventKind kind, PlayerId? player, string? slotId)
        {
            _revision++;
            _events.Add(new SessionEvent(kind, _revision, player, slotId));
        }
    }
}
