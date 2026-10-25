using System;

namespace BannerCoopTW.Networking
{
    public readonly struct ConnectionId : IEquatable<ConnectionId>
    {
        public ConnectionId(int value) => Value = value;

        public int Value { get; }

        public bool Equals(ConnectionId other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is ConnectionId c && Equals(c);

        public override int GetHashCode() => Value;

        public override string ToString() => "C" + Value;
    }

    public enum DeliveryMode : byte
    {
        /// <summary>Delivered, in order (commands, reservations, save chunks).</summary>
        ReliableOrdered = 0,

        /// <summary>May be lost or reordered (pings, anything where a late copy is still useful).</summary>
        Unreliable = 1,

        /// <summary>
        /// May be lost, never duplicated and never delivered after a newer packet of the same kind (state snapshots): a late packet is dropped by the transport.
        /// Receivers should still carry a sequence number in the payload (<see cref="BannerCoopTW.Core.Protocol.SequenceGate"/>) so the rule also holds
        /// across backends and test decorators.
        /// </summary>
        UnreliableSequenced = 2,
    }

    public enum DisconnectReason : byte
    {
        Unknown = 0,
        /// <summary>We closed the connection.</summary>
        LocalClose,
        /// <summary>The peer closed it.</summary>
        RemoteClose,
        Timeout,
        /// <summary>The connect attempt never succeeded (no answer or rejected by key).</summary>
        ConnectionFailed,
    }

    /// <summary>
    /// Our co-op channel (ADR-0012). Threading contract (research 0008): backends may use their own network threads but only
    /// <b>enqueue</b>; events are raised from <see cref="Poll"/>, which the owner calls on the game main thread (OnNetworkTick).
    /// </summary>
    public interface ITransport : IDisposable
    {
        event Action<ConnectionId> Connected;

        event Action<ConnectionId, DisconnectReason> Disconnected;

        event Action<ConnectionId, byte[], DeliveryMode> DataReceived;

        /// <summary>Local UDP port (valid after Listen/Connect).</summary>
        int LocalPort { get; }

        void Send(ConnectionId connection, byte[] data, DeliveryMode mode);

        void Disconnect(ConnectionId connection);

        /// <summary>Raises pending events on the calling thread.</summary>
        void Poll();
    }

    public interface IServerTransport : ITransport
    {
        /// <summary>Starts listening; port 0 picks a free port (see <see cref="ITransport.LocalPort"/>).</summary>
        void Listen(int port);

        void Stop();
    }

    public interface IClientTransport : ITransport
    {
        bool IsConnected { get; }

        /// <summary>Starts connecting; success arrives as <see cref="ITransport.Connected"/>, failure as <see cref="ITransport.Disconnected"/> with ConnectionFailed.</summary>
        void Connect(string host, int port);
    }
}
