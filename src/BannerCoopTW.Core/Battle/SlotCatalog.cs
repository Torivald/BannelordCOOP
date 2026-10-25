using System;
using System.Collections.Generic;
using System.Linq;

namespace BannerCoopTW.Core.Battle
{
    /// <summary>Immutable description of one claimable slot (a lord, an officer, a troop class or a siege engine).</summary>
    public sealed class SlotDefinition
    {
        public SlotDefinition(string id, BattleSide side, SlotKind kind, string label, int capacity, int order)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Slot id is required.", nameof(id));
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            if ((kind == SlotKind.Lord || kind == SlotKind.Officer) && capacity != 1)
                throw new ArgumentException("Lords and officers are single-seat slots.", nameof(capacity));
            Id = id;
            Side = side;
            Kind = kind;
            Label = label ?? id;
            Capacity = capacity;
            Order = order;
        }

        public string Id { get; }

        public BattleSide Side { get; }

        public SlotKind Kind { get; }

        public string Label { get; }

        /// <summary>Initial seat count (troop classes: how many agents of this class exist).</summary>
        public int Capacity { get; }

        /// <summary>Sort key inside the kind group.</summary>
        public int Order { get; }
    }

    /// <summary>
    /// The ordered set of slots of one battle: lords → officers → troop classes → siege engines (spectator is implicit and unlimited).
    /// Built once from the battle roster; counts change later through <see cref="BattleSession"/>.
    /// </summary>
    public sealed class SlotCatalog
    {
        // Copy-on-write: both fields point at objects that are never mutated after publication, so readers on other threads
        // (UI, serialisation) always see a consistent catalog even while the session adds a slot.
        private volatile State _state;

        private sealed class State
        {
            public State(List<SlotDefinition> ordered)
            {
                Ordered = ordered;
                ById = ordered.ToDictionary(s => s.Id, StringComparer.Ordinal);
            }

            public List<SlotDefinition> Ordered { get; }

            public Dictionary<string, SlotDefinition> ById { get; }
        }

        private SlotCatalog(List<SlotDefinition> ordered) => _state = new State(ordered);

        /// <summary>Independent copy (sessions own their catalog).</summary>
        public SlotCatalog Clone() => new SlotCatalog(new List<SlotDefinition>(_state.Ordered));

        public IReadOnlyList<SlotDefinition> All => _state.Ordered;

        public bool TryGet(string id, out SlotDefinition slot) => _state.ById.TryGetValue(id, out slot!);

        /// <summary>Slots of one side in presentation order.</summary>
        public IEnumerable<SlotDefinition> ForSide(BattleSide side) => _state.Ordered.Where(s => s.Side == side);

        /// <summary>Adds a slot mid-battle (reinforcements). Writers must be serialised by the caller (the session lock); readers need no lock.</summary>
        internal void Extend(SlotDefinition slot)
        {
            var cur = _state;
            if (cur.ById.ContainsKey(slot.Id))
                throw new ArgumentException("Duplicate slot id: " + slot.Id, nameof(slot));
            // Stable order: kind, then Order, then insertion (a new slot goes after existing ones with an equal key).
            var sorted = cur.Ordered.Concat(new[] { slot }).Select((x, i) => new { x, i }).OrderBy(t => (int)t.x.Kind).ThenBy(t => t.x.Order).ThenBy(t => t.i).Select(t => t.x).ToList();
            _state = new State(sorted);
        }

        public static Builder Create() => new Builder();

        public sealed class Builder
        {
            private readonly List<SlotDefinition> _slots = new List<SlotDefinition>();
            private int _seq;

            public Builder AddLord(BattleSide side, string id, string label) => Add(side, SlotKind.Lord, id, label, 1);

            public Builder AddOfficer(BattleSide side, string id, string label) => Add(side, SlotKind.Officer, id, label, 1);

            public Builder AddTroopClass(BattleSide side, string id, string label, int count) => Add(side, SlotKind.TroopClass, id, label, count);

            /// <summary>Siege roles (ram, tower, ballista …): classes of the engine crew, listed after troop classes.</summary>
            public Builder AddSiegeEngine(BattleSide side, string id, string label, int count) => Add(side, SlotKind.SiegeEngine, id, label, count);

            private Builder Add(BattleSide side, SlotKind kind, string id, string label, int capacity)
            {
                if (_slots.Any(s => s.Id == id))
                    throw new ArgumentException("Duplicate slot id: " + id, nameof(id));
                _slots.Add(new SlotDefinition(id, side, kind, label, capacity, _seq++));
                return this;
            }

            public SlotCatalog Build()
            {
                var ordered = _slots.OrderBy(s => (int)s.Kind).ThenBy(s => s.Order).ToList();
                return new SlotCatalog(ordered);
            }
        }
    }
}
