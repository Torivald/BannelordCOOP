using System;
using BannerCoopTW.Core;
using BannerCoopTW.Core.Battle;
using BannerCoopTW.Core.Logging;
using BannerCoopTW.Networking;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;

namespace BannerCoopTW.Spikes.L1
{
    /// <summary>
    /// Spike L1 (Phase 4, "2 game instances see each other"): at the main menu one instance becomes the lobby host (<see cref="BattleServerHost"/> without a battle),
    /// the other connects with <see cref="CoopClient"/>, sends Hello (protocol + game build + mod version), goes ready and pings. PASS lines are logged on both sides.
    /// Driven from <c>SubModule.OnApplicationTick</c>; no mission or campaign is needed.
    /// </summary>
    public static class L1Driver
    {
        public const int Port = 9997;
        private const int PlayerIdOfClient = 4242;

        private static bool _started;
        private static bool _host;
        private static BattleServerHost? _server;
        private static CoopClient? _client;
        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
        private static float _sinceLog;
        private static float _sinceRetry;
        private static bool _readySent, _passed;
        private static float _passedFor;

        public static void Tick(float dt, float menuTime, Func<bool> electHost)
        {
            if (!_started)
            {
                if (menuTime < 10f)
                    return;
                _started = true;
                _host = electHost();
                BannerlordConfig.StopGameOnFocusLost = false; // a lobby host must keep running when its window is not in front
                var build = Utilities.GetApplicationVersionWithBuildNumber().ToString();
                if (_host)
                {
                    var transport = new LiteNetLibServerTransport();
                    transport.Listen(Port);
                    _server = new BattleServerHost(transport, new SystemClock(), build, new HostOptions { RequiredModVersion = ModInfo.Version, RttBroadcastIntervalMs = 500 });
                    Log($"L1: role host, lobby listening on {Port}, mod {ModInfo.Version}, build {build}");
                }
                else
                {
                    _client = new CoopClient(new LiteNetLibClientTransport(), () => Clock.ElapsedMilliseconds, PlayerIdOfClient, "L1 client", build, ModInfo.Version) { PingIntervalMs = 250 };
                    Log($"L1: role client, connecting to 127.0.0.1:{Port}, mod {ModInfo.Version}, build {build}");
                    _client.Connect("127.0.0.1", Port);
                }

                return;
            }

            _sinceLog += dt;
            if (_host)
                HostTick();
            else
                ClientTick(dt);

            if (_passed)
            {
                _passedFor += dt;
                if (_passedFor > 8f && _passedFor - dt <= 8f)
                    Log("L1: done, staying up so the other side can finish");
            }
        }

        private static void HostTick()
        {
            _server!.Poll();
            if (_sinceLog < 2f)
                return;
            _sinceLog = 0f;
            var players = _server.LobbyPlayers;
            var text = string.Join(", ", System.Linq.Enumerable.Select(players, p => $"{p.PlayerId}:{p.Name} connected={p.Connected} ready={p.Ready} rtt={p.RttMs}ms"));
            Log($"L1: host lobby [{text}] allReady={_server.AllReady}");
            if (!_passed && players.Count == 1 && players[0].Connected && players[0].Ready && players[0].RttMs >= 0)
            {
                _passed = true;
                Log($"L1: PASS host: client {players[0].PlayerId} is in the lobby, ready, rtt {players[0].RttMs} ms");
            }
        }

        private static void ClientTick(float dt)
        {
            var c = _client!;
            c.Poll();
            if (c.State == CoopClientState.Rejected || c.State == CoopClientState.Disconnected || c.State == CoopClientState.Idle)
            {
                _sinceRetry += dt;
                if (_sinceRetry > 3f && !_passed)
                {
                    _sinceRetry = 0f;
                    Log($"L1: client state {c.State} ({c.RejectReason}, {c.DisconnectReason}); retrying connect");
                    _client = new CoopClient(new LiteNetLibClientTransport(), () => Clock.ElapsedMilliseconds, PlayerIdOfClient, "L1 client", Utilities.GetApplicationVersionWithBuildNumber().ToString(), ModInfo.Version) { PingIntervalMs = 250 };
                    _readySent = false;
                    _client.Connect("127.0.0.1", Port);
                }

                return;
            }

            if (c.State == CoopClientState.InLobby && !_readySent)
            {
                _readySent = true;
                c.SetReady(true);
                Log($"L1: client connected (session '{c.SessionId}', resumed={c.Resumed}), ready sent");
            }

            if (_sinceLog < 2f)
                return;
            _sinceLog = 0f;
            Log($"L1: client state {c.State}, lobby players {c.Lobby.Count}, pongs {c.PongsReceived}, rtt {c.RttMs} ms");
            if (!_passed && c.State == CoopClientState.InLobby && c.PongsReceived >= 5 && c.RttMs >= 0 && c.Lobby.Count == 1 && c.Lobby[0].Ready)
            {
                _passed = true;
                Log($"L1: PASS client: host answered Hello ('{c.SessionId}'), lobby shows me ready, {c.PongsReceived} pongs, rtt {c.RttMs} ms");
            }
        }

        private static void Log(string text) => SubModule.Log?.Info(text);
    }
}
