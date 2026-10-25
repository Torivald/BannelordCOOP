using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BannerCoopTW.Networking
{
    /// <summary>Settings of the test-only network emulation (A1b). Applied to the receiving direction of the endpoint that is wrapped.</summary>
    public sealed class NetworkConditions
    {
        /// <summary>Percent (0-100) of packets that are lost. Unreliable ones vanish; reliable ones arrive late (<see cref="RetransmitDelayMs"/>) and block later reliable packets.</summary>
        public int LossPercent { get; set; }

        public int MinLatencyMs { get; set; }

        public int MaxLatencyMs { get; set; }

        /// <summary>Extra delay of a "lost" reliable packet (a retransmission after a timeout).</summary>
        public int RetransmitDelayMs { get; set; } = 300;

        public bool IsActive => LossPercent > 0 || MaxLatencyMs > 0;
    }

    /// <summary>
    /// Decorator that makes a real transport behave like a worse network. LiteNetLib's own Simulate* switches did not change anything in the release build we
    /// reference (A1b, research 0013), so the effect is implemented here: inbound data is held for a random time in [min, max] ms and, with the loss probability,
    /// dropped (unreliable) or delayed by a retransmit timeout (reliable, in order). Events are still raised from <see cref="Poll"/> on the caller's thread.
    /// </summary>
    public abstract class NetworkConditionTransportBase : ITransport
    {
        private struct Item
        {
            public long Due;
            public long Seq;
            public ConnectionId Connection;
            public byte[] Data;
            public DeliveryMode Mode;
        }

        private readonly ITransport _inner;
        private readonly NetworkConditions _conditions;
        private readonly Random _random;
        private readonly Func<long> _nowMs;
        private readonly List<Item> _queue = new List<Item>();
        private long _seq;
        private long _lastReliableDue;
        private long _dropped, _delayedReliable, _delivered, _staleDropped;
        private readonly Dictionary<int, long> _lastSequencedArrival = new Dictionary<int, long>();

        protected NetworkConditionTransportBase(ITransport inner, NetworkConditions conditions, int seed = 12345, Func<long>? nowMs = null)
        {
            _inner = inner;
            _conditions = conditions;
            _random = new Random(seed);
            if (nowMs == null)
            {
                var sw = Stopwatch.StartNew();
                nowMs = () => sw.ElapsedMilliseconds;
            }

            _nowMs = nowMs;
            _inner.Connected += c => Connected?.Invoke(c);
            _inner.Disconnected += (c, r) => Disconnected?.Invoke(c, r);
            _inner.DataReceived += OnInnerData;
        }

        public event Action<ConnectionId>? Connected;

        public event Action<ConnectionId, DisconnectReason>? Disconnected;

        public event Action<ConnectionId, byte[], DeliveryMode>? DataReceived;

        public int LocalPort => _inner.LocalPort;

        /// <summary>Packets dropped / reliable packets delayed / packets delivered so far.</summary>
        public string Counters => $"dropped={_dropped} staleSequenced={_staleDropped} reliableDelayed={_delayedReliable} delivered={_delivered}";

        /// <summary>Sequenced packets discarded because a newer one had already been delivered (what LiteNetLib's Sequenced channel does on a jittery link).</summary>
        public long StaleSequencedDropped => _staleDropped;

        protected ITransport Inner => _inner;

        public void Send(ConnectionId connection, byte[] data, DeliveryMode mode) => _inner.Send(connection, data, mode);

        public void Disconnect(ConnectionId connection) => _inner.Disconnect(connection);

        public void Dispose() => _inner.Dispose();

        public void Poll()
        {
            _inner.Poll();
            if (_queue.Count == 0)
                return;
            var now = _nowMs();
            _queue.Sort((a, b) => a.Due != b.Due ? a.Due.CompareTo(b.Due) : a.Seq.CompareTo(b.Seq));
            var n = 0;
            while (n < _queue.Count && _queue[n].Due <= now)
                n++;
            if (n == 0)
                return;
            var ready = _queue.GetRange(0, n);
            _queue.RemoveRange(0, n);
            foreach (var item in ready)
            {
                if (item.Mode == DeliveryMode.UnreliableSequenced)
                {
                    _lastSequencedArrival.TryGetValue(item.Connection.Value, out var last);
                    if (item.Seq < last)
                    {
                        _staleDropped++; // sent earlier than a sequenced packet that was already delivered
                        continue;
                    }

                    _lastSequencedArrival[item.Connection.Value] = item.Seq;
                }

                _delivered++;
                DataReceived?.Invoke(item.Connection, item.Data, item.Mode);
            }
        }

        private void OnInnerData(ConnectionId connection, byte[] data, DeliveryMode mode)
        {
            if (!_conditions.IsActive)
            {
                _delivered++;
                DataReceived?.Invoke(connection, data, mode);
                return;
            }

            var lost = _conditions.LossPercent > 0 && _random.Next(100) < _conditions.LossPercent;
            if (lost && mode != DeliveryMode.ReliableOrdered)
            {
                _dropped++;
                return;
            }

            var min = Math.Min(_conditions.MinLatencyMs, _conditions.MaxLatencyMs);
            var max = Math.Max(_conditions.MinLatencyMs, _conditions.MaxLatencyMs);
            var delay = min + (max > min ? _random.Next(max - min + 1) : 0);
            if (lost)
            {
                delay += _conditions.RetransmitDelayMs;
                _delayedReliable++;
            }

            var due = _nowMs() + delay;
            if (mode == DeliveryMode.ReliableOrdered)
            {
                // reliable-ordered keeps its order: nothing overtakes an earlier reliable packet
                if (due < _lastReliableDue)
                    due = _lastReliableDue;
                _lastReliableDue = due;
            }

            _queue.Add(new Item { Due = due, Seq = _seq++, Connection = connection, Data = data, Mode = mode });
        }
    }

    public sealed class NetworkConditionServerTransport : NetworkConditionTransportBase, IServerTransport
    {
        public NetworkConditionServerTransport(IServerTransport inner, NetworkConditions conditions, int seed = 12345, Func<long>? nowMs = null)
            : base(inner, conditions, seed, nowMs)
        {
        }

        public void Listen(int port) => ((IServerTransport)Inner).Listen(port);

        public void Stop() => ((IServerTransport)Inner).Stop();
    }

    public sealed class NetworkConditionClientTransport : NetworkConditionTransportBase, IClientTransport
    {
        public NetworkConditionClientTransport(IClientTransport inner, NetworkConditions conditions, int seed = 12345, Func<long>? nowMs = null)
            : base(inner, conditions, seed, nowMs)
        {
        }

        public bool IsConnected => ((IClientTransport)Inner).IsConnected;

        public void Connect(string host, int port) => ((IClientTransport)Inner).Connect(host, port);
    }
}
