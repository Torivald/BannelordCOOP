using System;
using System.Collections.Generic;
using BannerCoopTW.Core.Protocol;
using LiteNetLib;

namespace BannerCoopTW.Networking
{
    /// <summary>Shared LiteNetLib plumbing: one NetManager, events queued by the library and raised from <see cref="Poll"/>.</summary>
    public abstract class LiteNetLibTransportBase : ITransport
    {
        /// <summary>Connection key: peers with another protocol version are refused by LiteNetLib before any of our code runs.</summary>
        public static readonly string ConnectionKey = "bannercoop/" + ProtocolInfo.Version;

        private readonly Dictionary<int, NetPeer> _peers = new Dictionary<int, NetPeer>();
        private readonly object _gate = new object();
        private bool _disposed;

        protected LiteNetLibTransportBase()
        {
            Listener = new EventBasedNetListener();
            Manager = new NetManager(Listener)
            {
                // Events are only raised from PollEvents() (no UnsyncedEvents), so everything below runs on the Poll() caller.
                UnsyncedEvents = false,
                DisconnectTimeout = 10_000,
            };
            Listener.PeerConnectedEvent += peer =>
            {
                lock (_gate)
                    _peers[peer.Id] = peer;
                Connected?.Invoke(new ConnectionId(peer.Id));
            };
            Listener.PeerDisconnectedEvent += (peer, info) =>
            {
                lock (_gate)
                    _peers.Remove(peer.Id);
                Disconnected?.Invoke(new ConnectionId(peer.Id), Map(info.Reason));
            };
            Listener.NetworkReceiveEvent += (peer, reader, channel, method) =>
            {
                var data = reader.GetRemainingBytes();
                reader.Recycle();
                DataReceived?.Invoke(new ConnectionId(peer.Id), data, FromMethod(method));
            };
        }

        public event Action<ConnectionId>? Connected;

        public event Action<ConnectionId, DisconnectReason>? Disconnected;

        public event Action<ConnectionId, byte[], DeliveryMode>? DataReceived;

        protected EventBasedNetListener Listener { get; }

        protected NetManager Manager { get; }

        private static DeliveryMethod ToMethod(DeliveryMode mode)
        {
            switch (mode)
            {
                case DeliveryMode.Unreliable: return DeliveryMethod.Unreliable;
                case DeliveryMode.UnreliableSequenced: return DeliveryMethod.Sequenced;
                default: return DeliveryMethod.ReliableOrdered;
            }
        }

        private static DeliveryMode FromMethod(DeliveryMethod method)
        {
            switch (method)
            {
                case DeliveryMethod.Unreliable: return DeliveryMode.Unreliable;
                case DeliveryMethod.Sequenced: return DeliveryMode.UnreliableSequenced;
                default: return DeliveryMode.ReliableOrdered;
            }
        }

        public int LocalPort => Manager.LocalPort;

        public void Send(ConnectionId connection, byte[] data, DeliveryMode mode)
        {
            NetPeer? peer;
            lock (_gate)
                _peers.TryGetValue(connection.Value, out peer);
            if (peer == null || peer.ConnectionState != ConnectionState.Connected)
                return; // the peer is gone; callers learn it from Disconnected
            peer.Send(data, ToMethod(mode));
        }

        public void Disconnect(ConnectionId connection)
        {
            NetPeer? peer;
            lock (_gate)
                _peers.TryGetValue(connection.Value, out peer);
            peer?.Disconnect();
        }

        public void Poll()
        {
            if (!_disposed)
                Manager.PollEvents();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Manager.Stop();
        }

        private static DisconnectReason Map(LiteNetLib.DisconnectReason reason)
        {
            switch (reason)
            {
                case LiteNetLib.DisconnectReason.DisconnectPeerCalled:
                    return DisconnectReason.LocalClose;
                case LiteNetLib.DisconnectReason.RemoteConnectionClose:
                    return DisconnectReason.RemoteClose;
                case LiteNetLib.DisconnectReason.Timeout:
                    return DisconnectReason.Timeout;
                case LiteNetLib.DisconnectReason.ConnectionFailed:
                case LiteNetLib.DisconnectReason.ConnectionRejected:
                case LiteNetLib.DisconnectReason.InvalidProtocol:
                    return DisconnectReason.ConnectionFailed;
                default:
                    return DisconnectReason.Unknown;
            }
        }
    }

    public sealed class LiteNetLibServerTransport : LiteNetLibTransportBase, IServerTransport
    {
        public LiteNetLibServerTransport()
        {
            Listener.ConnectionRequestEvent += request => request.AcceptIfKey(ConnectionKey);
        }

        public void Listen(int port)
        {
            if (!Manager.Start(port))
                throw new InvalidOperationException("Could not bind UDP port " + port);
        }

        public void Stop() => Manager.Stop();
    }

    public sealed class LiteNetLibClientTransport : LiteNetLibTransportBase, IClientTransport
    {
        public bool IsConnected => Manager.FirstPeer != null && Manager.FirstPeer.ConnectionState == ConnectionState.Connected;

        public void Connect(string host, int port)
        {
            if (!Manager.Start())
                throw new InvalidOperationException("Could not open a UDP socket");
            Manager.Connect(host, port, ConnectionKey);
        }
    }
}
