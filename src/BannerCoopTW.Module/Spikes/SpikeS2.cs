using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BannerCoopTW.Core.Logging;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;
using TaleWorlds.PlayerServices;

namespace BannerCoopTW.Spikes
{
    /// <summary>Server → client probe carried by the engine's own UDP channel (proves the link without lobby or mission).</summary>
    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class SpikePing : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer Range = new CompressionInfo.Integer(0, 1_000_000, true);

        public int Value { get; set; }

        protected override void OnWrite() => WriteIntToPacket(Value, Range);

        protected override bool OnRead()
        {
            var ok = true;
            Value = ReadIntFromPacket(Range, ref ok);
            return ok;
        }

        protected override MultiplayerMessageFilter OnGetLogFilter() => MultiplayerMessageFilter.General;

        protected override string OnGetLogFormat() => "SpikePing " + Value;
    }

    /// <summary>Client → server answer.</summary>
    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromClient)]
    public sealed class SpikePong : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer Range = new CompressionInfo.Integer(0, 1_000_000, true);

        public int Value { get; set; }

        protected override void OnWrite() => WriteIntToPacket(Value, Range);

        protected override bool OnRead()
        {
            var ok = true;
            Value = ReadIntFromPacket(Range, ref ok);
            return ok;
        }

        protected override MultiplayerMessageFilter OnGetLogFilter() => MultiplayerMessageFilter.General;

        protected override string OnGetLogFormat() => "SpikePong " + Value;
    }

    /// <summary>
    /// Phase 2b spike S2 (ADR-0006 "B-listen"): can a campaign process act as a native multiplayer listen server, and can a second
    /// instance join it without the TaleWorlds lobby? Every step is logged and guarded; a failure is the result, not a bug to patch around.
    /// Handshake data (session key, peer index) is passed through a file in the spikes folder, standing in for our own channel.
    /// </summary>
    public static class SpikeS2
    {
        public const int Port = 9999;

        private static readonly List<string> Callbacks = new List<string>();

        public static string HandshakeFile => Path.Combine(SpikeS1.OutDir, "s2-handshake.txt");

        private static void Note(string text)
        {
            SubModule.Log?.Info("S2: " + text);
            lock (Callbacks)
                Callbacks.Add(text);
        }

        private sealed class Handler : IGameNetworkHandler
        {
            public void OnNewPlayerConnect(PlayerConnectionInfo info, NetworkCommunicator peer) => Note("handler.OnNewPlayerConnect " + info?.Name);

            public void OnInitialize() => Note("handler.OnInitialize");

            public void OnPlayerConnectedToServer(NetworkCommunicator peer) => Note("handler.OnPlayerConnectedToServer " + peer?.UserName + " idx=" + peer?.Index);

            public void OnPlayerDisconnectedFromServer(NetworkCommunicator peer) => Note("handler.OnPlayerDisconnectedFromServer " + peer?.UserName);

            private int _disconnects;

            public void OnDisconnectedFromServer()
            {
                // The engine repeats this callback every frame after a failed/lost connection; log the first few and tear down once.
                if (++_disconnects <= 3)
                    Note("handler.OnDisconnectedFromServer #" + _disconnects);
                if (_disconnects == 1)
                    Note("(teardown deferred to s2_stop; callback keeps firing until then)");
            }

            public void OnStartMultiplayer() => Note("handler.OnStartMultiplayer");

            public void OnStartReplay() => Note("handler.OnStartReplay");

            public void OnEndMultiplayer() => Note("handler.OnEndMultiplayer");

            public void OnEndReplay() => Note("handler.OnEndReplay");

            public void OnHandleConsoleCommand(string command) => Note("handler.OnHandleConsoleCommand " + command);
        }

        private static bool HandlerInstalled()
        {
            var f = typeof(GameNetwork).GetField("_handler", BindingFlags.NonPublic | BindingFlags.Static);
            return f != null && f.GetValue(null) != null;
        }

        private static string Step(string name, Action a)
        {
            try
            {
                a();
                Note("OK   " + name);
                return "OK";
            }
            catch (Exception e)
            {
                Note("FAIL " + name + ": " + e.GetType().Name + ": " + e.Message);
                SubModule.Log?.Error("S2 step failed: " + name, e);
                return "FAIL";
            }
        }

        private static PlayerConnectionInfo MakeInfo(ulong id, string name) =>
            new PlayerConnectionInfo(new PlayerId(id, 0UL, 0UL, 0UL)) { Name = name };

        private static void EnsureHandler()
        {
            if (HandlerInstalled())
                Note("GameNetwork handler already installed (Multiplayer module?)");
            else
                Step("GameNetwork.Initialize(handler)", () => GameNetwork.Initialize(new Handler()));
            Step("register spike messages", RegisterMessages);
        }

        public static string State()
        {
            var sb = new StringBuilder();
            sb.Append($"S2 state: handler={HandlerInstalled()} IsServer={GameNetwork.IsServer} IsClient={GameNetwork.IsClient} IsSessionActive={GameNetwork.IsSessionActive} gameType={MBCommon.CurrentGameType}");
            try
            {
                sb.Append($" peers={GameNetwork.NetworkPeers?.Count} myPeer={(GameNetwork.MyPeer != null ? GameNetwork.MyPeer.UserName : "null")}");
                if (GameNetwork.NetworkPeers != null)
                    foreach (var p in GameNetwork.NetworkPeers)
                        sb.Append($" [{p.Index}:{p.UserName} key={p.SessionKey} server={p.IsServerPeer} active={p.IsConnectionActive}]");
            }
            catch (Exception e)
            {
                sb.Append(" (peer list failed: " + e.Message + ")");
            }

            try
            {
                if (GameNetwork.IsSessionActive)
                    sb.Append($" sinceLastUdpPacket={GameNetwork.ElapsedTimeSinceLastUdpPacketArrived():F1}s");
            }
            catch (Exception e)
            {
                sb.Append(" (udp age failed: " + e.Message + ")");
            }

            lock (Callbacks)
                sb.Append(" callbacks=" + Callbacks.Count);
            var text = sb.ToString();
            SubModule.Log?.Info(text);
            return text;
        }

        private static bool _messagesRegistered;

        /// <summary>Registers ping (client side) and pong (server side) handlers; call after the network was started.</summary>
        public static void RegisterMessages()
        {
            if (_messagesRegistered)
                return;
            _messagesRegistered = true;
            var reg = new GameNetwork.NetworkMessageHandlerRegisterer(GameNetwork.NetworkMessageHandlerRegisterer.RegisterMode.Add);
            reg.Register<SpikePing>(OnPing);
            reg.Register<SpikePong>(OnPong);
        }

        private static void OnPing(SpikePing m)
        {
            Note("RECEIVED SpikePing " + m.Value + " on client; answering");
            GameNetwork.BeginModuleEventAsClient();
            GameNetwork.WriteMessage(new SpikePong { Value = m.Value + 1 });
            GameNetwork.EndModuleEventAsClient();
        }

        private static bool OnPong(NetworkCommunicator peer, SpikePong m)
        {
            Note("RECEIVED SpikePong " + m.Value + " from peer " + peer?.UserName);
            return true;
        }

        /// <summary>Host: send a ping to every remote peer over the engine channel.</summary>
        public static string Ping(int value)
        {
            var sent = 0;
            foreach (var p in new List<NetworkCommunicator>(GameNetwork.NetworkPeers))
            {
                if (p.IsServerPeer)
                    continue;
                Step("send SpikePing " + value + " to " + p.UserName, () =>
                {
                    GameNetwork.BeginModuleEventAsServer(p);
                    GameNetwork.WriteMessage(new SpikePing { Value = value });
                    GameNetwork.EndModuleEventAsServer();
                });
                sent++;
            }

            return "pings sent: " + sent;
        }

        public static string ReadyFile => Path.Combine(SpikeS1.OutDir, "s2-client-ready.txt");

        /// <summary>Host step 1: start the native server from the campaign and register the host's own peer.</summary>
        public static string HostStart()
        {
            State();
            EnsureHandler();
            var ok = Step($"GameNetwork.StartMultiplayerOnServer({Port})", () => GameNetwork.StartMultiplayerOnServer(Port));
            if (ok == "OK")
                Step("AddNewPlayerOnServer(host, serverPeer:true)", () => GameNetwork.AddNewPlayerOnServer(MakeInfo(1, "spike-host"), true, true));
            return State();
        }

        /// <summary>Host step 2 (once the client is ready; the engine drops an unclaimed UDP session after ~20 s): create the remote peer and publish key + index.</summary>
        public static string HostAddRemote()
        {
            ICommunicator? remote = null;
            Step("AddNewPlayerOnServer(client, serverPeer:false)", () => remote = GameNetwork.AddNewPlayerOnServer(MakeInfo(2, "spike-client"), false, false));
            if (remote is NetworkCommunicator nc)
            {
                File.WriteAllText(HandshakeFile, $"{nc.SessionKey}	{nc.Index}");
                Note($"handshake published: sessionKey={nc.SessionKey} playerIndex={nc.Index}");
            }
            else
            {
                Note("no NetworkCommunicator returned for the remote peer: " + (remote == null ? "null" : remote.GetType().Name));
            }

            return State();
        }

        public static void SignalReady() => File.WriteAllText(ReadyFile, "ready");

        public static bool ClientReady => File.Exists(ReadyFile);

        public static bool HandshakeAvailable => File.Exists(HandshakeFile);

        /// <summary>Client side: read the handshake and connect with the native client.</summary>
        public static string Join(string address)
        {
            State();
            if (!File.Exists(HandshakeFile))
                return "S2 join: no handshake file";
            var parts = File.ReadAllText(HandshakeFile).Split('\t');
            var key = int.Parse(parts[0]);
            var index = int.Parse(parts[1]);
            EnsureHandler();
            Step($"GameNetwork.StartMultiplayerOnClient({address}, {Port}, key={key}, index={index})", () => GameNetwork.StartMultiplayerOnClient(address, Port, key, index));
            return State();
        }

        public static string Stop()
        {
            Step("GameNetwork.EndMultiplayer()", GameNetwork.EndMultiplayer);
            return State();
        }

        public static void ClearHandshake()
        {
            try
            {
                if (File.Exists(HandshakeFile))
                    File.Delete(HandshakeFile);
                if (File.Exists(ReadyFile))
                    File.Delete(ReadyFile);
            }
            catch
            {
                // best effort
            }
        }
    }
}
