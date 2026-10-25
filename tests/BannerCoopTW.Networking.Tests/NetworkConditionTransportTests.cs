using System;
using System.Collections.Generic;
using BannerCoopTW.Networking;
using Xunit;

namespace BannerCoopTW.Networking.Tests
{
    public class NetworkConditionTransportTests
    {
        /// <summary>Raises DataReceived on demand; everything else is a stub.</summary>
        private sealed class FakeInner : IClientTransport
        {
            public event Action<ConnectionId>? Connected { add { } remove { } }

            public event Action<ConnectionId, DisconnectReason>? Disconnected { add { } remove { } }

            public event Action<ConnectionId, byte[], DeliveryMode>? DataReceived;

            public int LocalPort => 0;

            public bool IsConnected => true;

            public void Push(byte b, DeliveryMode mode) => DataReceived?.Invoke(new ConnectionId(1), new[] { b }, mode);

            public void Send(ConnectionId connection, byte[] data, DeliveryMode mode) { }

            public void Disconnect(ConnectionId connection) { }

            public void Poll() { }

            public void Connect(string host, int port) { }

            public void Dispose() { }
        }

        private static (NetworkConditionClientTransport t, FakeInner inner, List<(long at, byte b)> got, long[] clock) Make(NetworkConditions c, int seed = 1)
        {
            var inner = new FakeInner();
            var clock = new long[1];
            var t = new NetworkConditionClientTransport(inner, c, seed, () => clock[0]);
            var got = new List<(long at, byte b)>();
            t.DataReceived += (cid, d, m) => got.Add((clock[0], d[0]));
            return (t, inner, got, clock);
        }

        [Fact]
        public void Inactive_conditions_pass_through_immediately()
        {
            var (t, inner, got, _) = Make(new NetworkConditions());
            inner.Push(7, DeliveryMode.Unreliable);
            Assert.Single(got);
        }

        [Fact]
        public void Latency_holds_packets_until_due_within_bounds()
        {
            var (t, inner, got, clock) = Make(new NetworkConditions { MinLatencyMs = 50, MaxLatencyMs = 100 });
            for (byte i = 0; i < 20; i++)
                inner.Push(i, DeliveryMode.Unreliable);
            t.Poll();
            Assert.Empty(got);
            for (clock[0] = 1; clock[0] <= 120; clock[0]++)
                t.Poll();
            Assert.Equal(20, got.Count);
            Assert.All(got, g => Assert.InRange(g.at, 50, 100));
        }

        [Fact]
        public void Unreliable_loss_drops_about_the_configured_share()
        {
            var (t, inner, got, clock) = Make(new NetworkConditions { LossPercent = 20, MinLatencyMs = 1, MaxLatencyMs = 1 });
            for (var i = 0; i < 5000; i++)
                inner.Push(1, DeliveryMode.Unreliable);
            clock[0] = 10;
            t.Poll();
            Assert.InRange(got.Count, 3800, 4200);
        }

        [Fact]
        public void Reliable_packets_are_never_lost_and_keep_their_order()
        {
            var (t, inner, got, clock) = Make(new NetworkConditions { LossPercent = 30, MinLatencyMs = 10, MaxLatencyMs = 40, RetransmitDelayMs = 300 });
            for (byte i = 0; i < 100; i++)
                inner.Push(i, DeliveryMode.ReliableOrdered);
            for (clock[0] = 0; clock[0] < 5000; clock[0] += 5)
                t.Poll();
            Assert.Equal(100, got.Count);
            for (var i = 0; i < got.Count; i++)
                Assert.Equal((byte)i, got[i].b);
        }
    }
}
