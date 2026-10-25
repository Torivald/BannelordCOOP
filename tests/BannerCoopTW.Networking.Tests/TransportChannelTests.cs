using System;
using System.Collections.Generic;
using System.Linq;
using BannerCoopTW.Core.Protocol;
using BannerCoopTW.Networking;
using Xunit;

namespace BannerCoopTW.Networking.Tests
{
    /// <summary>The unreliable-sequenced channel (snapshots) next to the reliable-ordered one (events), and its behaviour on a jittery link.</summary>
    public class TransportChannelTests
    {
        private sealed class FakeInner : IClientTransport
        {
            public event Action<ConnectionId>? Connected { add { } remove { } }

            public event Action<ConnectionId, DisconnectReason>? Disconnected { add { } remove { } }

            public event Action<ConnectionId, byte[], DeliveryMode>? DataReceived;

            public int LocalPort => 0;

            public bool IsConnected => true;

            public void Push(uint seq, DeliveryMode mode) => DataReceived?.Invoke(new ConnectionId(1), BitConverter.GetBytes(seq), mode);

            public void Send(ConnectionId connection, byte[] data, DeliveryMode mode) { }

            public void Disconnect(ConnectionId connection) { }

            public void Poll() { }

            public void Connect(string host, int port) { }

            public void Dispose() { }
        }

        [Fact]
        public void Mode_survives_the_trip_over_liteNetLib_for_all_three_channels()
        {
            var received = new List<DeliveryMode>();
            var raw = new LiteNetLibServerTransport();
            raw.Listen(0);
            var serverConnected = 0;
            raw.Connected += c => serverConnected++;
            raw.DataReceived += (c, d, m) => received.Add(m);
            var client = new LiteNetLibClientTransport();
            ConnectionId conn = default;
            client.Connected += c => conn = c;
            client.Connect("127.0.0.1", raw.LocalPort);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!client.IsConnected && sw.ElapsedMilliseconds < 5000)
            {
                raw.Poll();
                client.Poll();
                System.Threading.Thread.Sleep(1);
            }

            Assert.True(client.IsConnected);
            sw.Restart();
            while (sw.ElapsedMilliseconds < 200)
            {
                raw.Poll();
                client.Poll();
                System.Threading.Thread.Sleep(1);
            }

            client.Send(conn, new byte[] { 1 }, DeliveryMode.ReliableOrdered);
            client.Send(conn, new byte[] { 2 }, DeliveryMode.UnreliableSequenced);
            client.Send(conn, new byte[] { 3 }, DeliveryMode.Unreliable);
            sw.Restart();
            while (received.Count < 3 && sw.ElapsedMilliseconds < 5000)
            {
                raw.Poll();
                client.Poll();
                System.Threading.Thread.Sleep(1);
            }

            Assert.True(received.Count == 3, "received=" + received.Count + " clientConnected=" + client.IsConnected + " conn=" + conn + " serverConnectedEvents=" + serverConnected);
            Assert.Contains(DeliveryMode.ReliableOrdered, received);
            Assert.Contains(DeliveryMode.UnreliableSequenced, received);
            Assert.Contains(DeliveryMode.Unreliable, received);
            client.Dispose();
            raw.Dispose();
        }

        private static List<uint> Run(DeliveryMode mode, out NetworkConditionClientTransport transport)
        {
            var inner = new FakeInner();
            var clock = new long[1];
            transport = new NetworkConditionClientTransport(inner, new NetworkConditions { MinLatencyMs = 0, MaxLatencyMs = 100 }, seed: 5, nowMs: () => clock[0]);
            var got = new List<uint>();
            transport.DataReceived += (c, d, m) => got.Add(BitConverter.ToUInt32(d, 0));
            // 200 snapshots, one per millisecond, each delayed by 0..100 ms: heavy reordering
            for (uint i = 1; i <= 200; i++)
            {
                inner.Push(i, mode);
                clock[0]++;
                transport.Poll();
            }

            for (var t = 0; t < 300; t++)
            {
                clock[0]++;
                transport.Poll();
            }

            return got;
        }

        [Fact]
        public void Sequenced_channel_never_delivers_an_older_snapshot_after_a_newer_one()
        {
            var got = Run(DeliveryMode.UnreliableSequenced, out var t);
            Assert.NotEmpty(got);
            for (var i = 1; i < got.Count; i++)
                Assert.True(got[i] > got[i - 1], "order violated at " + i);
            Assert.True(t.StaleSequencedDropped > 0, "the jitter was supposed to reorder packets");
            Assert.Equal(200, got.Count + t.StaleSequencedDropped);
        }

        [Fact]
        public void Plain_unreliable_does_reorder_and_the_sequence_gate_drops_the_late_ones()
        {
            var got = Run(DeliveryMode.Unreliable, out _);
            Assert.Equal(200, got.Count);
            Assert.False(got.SequenceEqual(got.OrderBy(x => x)), "control: jitter must reorder plain unreliable packets");
            var gate = new SequenceGate();
            var accepted = got.Where(gate.Accept).ToList();
            Assert.Equal(accepted.OrderBy(x => x), accepted);
            Assert.True(gate.Dropped > 0);
            Assert.Equal(200, gate.Accepted + gate.Dropped);
        }

        [Fact]
        public void Reliable_packets_are_not_reordered_by_the_same_jitter()
        {
            var got = Run(DeliveryMode.ReliableOrdered, out _);
            Assert.Equal(200, got.Count);
            Assert.Equal(got.OrderBy(x => x), got);
        }

        [Fact]
        public void Loss_applies_to_sequenced_packets_too()
        {
            var inner = new FakeInner();
            var clock = new long[1];
            var t = new NetworkConditionClientTransport(inner, new NetworkConditions { LossPercent = 25, MinLatencyMs = 1, MaxLatencyMs = 1 }, seed: 9, nowMs: () => clock[0]);
            var n = 0;
            t.DataReceived += (c, d, m) => n++;
            for (uint i = 1; i <= 4000; i++)
                inner.Push(i, DeliveryMode.UnreliableSequenced);
            clock[0] = 10;
            t.Poll();
            Assert.InRange(n, 2800, 3200);
        }
    }
}
