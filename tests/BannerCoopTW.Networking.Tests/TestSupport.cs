using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using BannerCoopTW.Core.Battle;
using BannerCoopTW.Core.Protocol;
using BannerCoopTW.Networking;

namespace BannerCoopTW.Networking.Tests
{
    internal sealed class ManualClock : IClock
    {
        private long _now;

        public long NowMs => Interlocked.Read(ref _now);

        public void Advance(long ms) => Interlocked.Add(ref _now, ms);
    }

    /// <summary>A game-less client: real LiteNetLib UDP socket on localhost, speaking the real wire protocol.</summary>
    internal sealed class FakeClient : IDisposable
    {
        public const string Build = "1.4.8.119303";

        private readonly LiteNetLibClientTransport _transport = new LiteNetLibClientTransport();
        private readonly List<Message> _received = new List<Message>();
        private ConnectionId _connection;

        public FakeClient(ulong playerId)
        {
            PlayerId = playerId;
            _transport.Connected += c =>
            {
                _connection = c;
                HasConnected = true;
            };
            _transport.Disconnected += (c, r) => DisconnectReason = r;
            _transport.DataReceived += (c, data, mode) =>
            {
                var d = MessageCodec.Decode(data);
                if (!d.Ok)
                    return;
                _received.Add(d.Message!);
                if (d.Message is BattleInvite invite)
                    Epoch = invite.Epoch;
            };
        }

        public ulong PlayerId { get; }

        /// <summary>Battle epoch learnt from the invite; stamped on every request unless overridden.</summary>
        public uint Epoch { get; set; }

        public DisconnectReason? DisconnectReason { get; private set; }

        /// <summary>True once the Connected event was raised (i.e. after Poll), not merely when the library socket is connected.</summary>
        public bool HasConnected { get; private set; }

        public bool IsConnected => HasConnected && _transport.IsConnected;

        public IReadOnlyList<Message> Received => _received;

        public void Connect(int port) => _transport.Connect("127.0.0.1", port);

        public void Poll() => _transport.Poll();

        public void Send(Message m, ushort? version = null)
        {
            switch (m)
            {
                case ChooseSide x when x.Epoch == 0: x.Epoch = Epoch; break;
                case ReserveRequest x when x.Epoch == 0: x.Epoch = Epoch; break;
                case ConfirmRequest x when x.Epoch == 0: x.Epoch = Epoch; break;
                case ReleaseRequest x when x.Epoch == 0: x.Epoch = Epoch; break;
            }

            SendRaw(m, version);
        }

        private void SendRaw(Message m, ushort? version) =>
            _transport.Send(_connection, version.HasValue ? MessageCodec.Encode(m, version.Value) : MessageCodec.Encode(m), DeliveryMode.ReliableOrdered);

        public void SendHello(string build = Build, ushort? version = null) =>
            Send(new Hello { PlayerId = PlayerId, PlayerName = "fake" + PlayerId, GameBuild = build, ProtocolVersion = version ?? ProtocolInfo.Version }, version);

        public T? Last<T>() where T : Message => _received.OfType<T>().LastOrDefault();

        public IEnumerable<T> All<T>() where T : Message => _received.OfType<T>();

        /// <summary>Closes the socket without a goodbye (a crash / network loss as far as the server can tell, after LiteNetLib's disconnect packet is skipped).</summary>
        public void Kill() => _transport.Dispose();

        public void Dispose() => _transport.Dispose();
    }

    /// <summary>Single-threaded pump: the "game loop" of the test. Polls the host and every client until a condition holds.</summary>
    internal sealed class Harness : IDisposable
    {
        public Harness(ManualClock clock, int maxParticipants = 5)
        {
            Clock = clock;
            Transport = new LiteNetLibServerTransport();
            Transport.Listen(0);
            Catalog = SlotCatalog.Create()
                .AddLord(BattleSide.Attacker, "a-lord1", "Lord A")
                .AddLord(BattleSide.Attacker, "a-lord2", "Lord C")
                .AddOfficer(BattleSide.Attacker, "a-off", "Captain B")
                .AddTroopClass(BattleSide.Attacker, "a-inf", "Infantry", 5)
                .AddTroopClass(BattleSide.Attacker, "a-arch", "Archers", 1)
                .AddLord(BattleSide.Defender, "d-lord", "Lord D")
                .AddTroopClass(BattleSide.Defender, "d-inf", "Infantry", 2)
                .Build();
            Session = new BattleSession("battle-1", Catalog, clock, new SessionOptions { MaxParticipants = maxParticipants, Epoch = 7 }, new PlayerId(1000), BattleSide.Attacker, "a-lord1");
            Host = new BattleServerHost(Transport, Session, clock, FakeClient.Build) { AllowClientConfirm = true };
        }

        public ManualClock Clock { get; }

        public LiteNetLibServerTransport Transport { get; }

        public SlotCatalog Catalog { get; }

        public BattleSession Session { get; }

        public BattleServerHost Host { get; }

        public List<FakeClient> Clients { get; } = new List<FakeClient>();

        public FakeClient AddClient(ulong playerId)
        {
            var c = new FakeClient(playerId);
            Clients.Add(c);
            c.Connect(Transport.LocalPort);
            return c;
        }

        /// <summary>Joins: connect, Hello, wait for the invite.</summary>
        public FakeClient Join(ulong playerId)
        {
            var c = AddClient(playerId);
            Pump(() => c.IsConnected, "connect " + playerId);
            c.SendHello();
            Pump(() => c.Last<BattleInvite>() != null || c.Last<HelloReject>() != null, "invite " + playerId);
            return c;
        }

        public void Pump(Func<bool> until, string what, int timeoutMs = 8000)
        {
            var sw = Stopwatch.StartNew();
            while (!until())
            {
                if (sw.ElapsedMilliseconds > timeoutMs)
                    throw new TimeoutException("Timed out waiting for: " + what);
                Step();
                Thread.Sleep(1);
            }
        }

        /// <summary>Runs the loop for a short settle time (to let in-flight packets arrive).</summary>
        public void Settle(int ms = 150)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                Step();
                Thread.Sleep(1);
            }
        }

        public void Step()
        {
            Host.Poll();
            foreach (var c in Clients.ToList())
                c.Poll();
        }

        public void Dispose()
        {
            foreach (var c in Clients)
                c.Dispose();
            Host.Dispose();
        }
    }
}
