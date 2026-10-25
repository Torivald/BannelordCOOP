using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using BannerCoopTW.Core.Battle;
using BannerCoopTW.Networking;

namespace BannerCoopTW.Networking.Tests
{
    /// <summary>Lobby host + CoopClient fakes over real UDP on localhost, optionally behind <see cref="NetworkConditionTransportBase"/> decorators on every endpoint.</summary>
    internal sealed class LobbyRig : IDisposable
    {
        public const string Build = "1.4.8.119303";
        public const string ModVersion = "0.1.0";

        private readonly Stopwatch _real = Stopwatch.StartNew();
        private readonly NetworkConditions? _conditions;

        public LobbyRig(NetworkConditions? conditions = null, HostOptions? options = null)
        {
            _conditions = conditions != null && conditions.IsActive ? conditions : null;
            Clock = new ManualClock();
            var inner = new LiteNetLibServerTransport();
            IServerTransport server = inner;
            if (_conditions != null)
                server = new NetworkConditionServerTransport(inner, _conditions, seed: 77);
            server.Listen(0);
            Port = server.LocalPort;
            Options = options ?? new HostOptions();
            Options.RequiredModVersion ??= ModVersion;
            Host = new BattleServerHost(server, Clock, Build, Options) { AllowClientConfirm = true };
        }

        public ManualClock Clock { get; }

        public int Port { get; }

        public HostOptions Options { get; }

        public BattleServerHost Host { get; }

        public List<CoopClient> Clients { get; } = new List<CoopClient>();

        public bool Lossy => _conditions != null;

        public long RealMs => _real.ElapsedMilliseconds;

        public CoopClient AddClient(ulong playerId, string? modVersion = null, string? build = null, bool connect = true)
        {
            var inner = new LiteNetLibClientTransport();
            IClientTransport t = inner;
            if (_conditions != null)
                t = new NetworkConditionClientTransport(inner, _conditions, seed: 1000 + (int)playerId);
            var c = new CoopClient(t, () => _real.ElapsedMilliseconds, playerId, "fake" + playerId, build ?? Build, modVersion ?? ModVersion) { PingIntervalMs = 200 };
            Clients.Add(c);
            if (connect)
                c.Connect("127.0.0.1", Port);
            return c;
        }

        public CoopClient Join(ulong playerId)
        {
            var c = AddClient(playerId);
            Pump(() => c.State == CoopClientState.InLobby || c.State == CoopClientState.InBattle || c.State == CoopClientState.Rejected, "hello answer " + playerId);
            return c;
        }

        public void Pump(Func<bool> until, string what, int timeoutMs = 20_000)
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

        public void Settle(int ms = 200)
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
