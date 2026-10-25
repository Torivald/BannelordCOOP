using System;
using System.Collections.Generic;
using System.Linq;
using BannerCoopTW.Core.Battle;
using BannerCoopTW.Core.Protocol;

namespace BannerCoopTW.Networking
{
    public enum CoopClientState : byte
    {
        Idle = 0,
        Connecting,
        /// <summary>Connected, Hello sent, waiting for the host's answer.</summary>
        Handshaking,
        InLobby,
        /// <summary>A BattleInvite arrived.</summary>
        InBattle,
        /// <summary>The host refused the Hello (see <see cref="CoopClient.RejectReason"/>).</summary>
        Rejected,
        Disconnected,
    }

    /// <summary>
    /// Client side of the lobby and battle-participation protocol over any <see cref="IClientTransport"/>. It has no game dependencies: the fake clients of the
    /// integration tests and the real game client use the same class. Everything happens inside <see cref="Poll"/> on the caller's thread.
    /// </summary>
    public sealed class CoopClient : IDisposable
    {
        private readonly IClientTransport _transport;
        private readonly Func<long> _nowMs;
        private readonly string _name;
        private readonly string _gameBuild;
        private readonly string _modVersion;
        private readonly List<Message> _received = new List<Message>();
        private readonly Dictionary<uint, long> _pingSentAt = new Dictionary<uint, long>();
        private readonly SequenceCounter _pingSeq = new SequenceCounter();
        private ConnectionId _connection;
        private long _nextPingAt;
        private uint _requestId;
        private IReadOnlyList<LobbyPlayer> _lobby = new List<LobbyPlayer>();

        public CoopClient(IClientTransport transport, Func<long> nowMs, ulong playerId, string name, string gameBuild, string modVersion)
        {
            _transport = transport;
            _nowMs = nowMs;
            PlayerId = playerId;
            _name = name;
            _gameBuild = gameBuild;
            _modVersion = modVersion;
            _transport.Connected += c =>
            {
                _connection = c;
                State = CoopClientState.Handshaking;
                SendHello();
            };
            _transport.Disconnected += (c, r) =>
            {
                DisconnectReason = r;
                if (State != CoopClientState.Rejected)
                    State = CoopClientState.Disconnected;
            };
            _transport.DataReceived += OnData;
        }

        public ulong PlayerId { get; }

        public CoopClientState State { get; private set; }

        public DisconnectReason? DisconnectReason { get; private set; }

        public RejectReason? RejectReason { get; private set; }

        public string RejectDetail { get; private set; } = "";

        /// <summary>The host recognised this player id as a recent disconnect (reconnect stub).</summary>
        public bool Resumed { get; private set; }

        public string SessionId { get; private set; } = "";

        public IReadOnlyList<LobbyPlayer> Lobby => _lobby;

        public uint LobbyRevision { get; private set; }

        /// <summary>Last measured round trip in ms, -1 before the first pong.</summary>
        public int RttMs { get; private set; } = -1;

        public int PongsReceived { get; private set; }

        public int PingIntervalMs { get; set; } = 500;

        public BattleInvite? Invite { get; private set; }

        public uint Epoch { get; private set; }

        public IReadOnlyList<SlotInfo> Slots { get; private set; } = new List<SlotInfo>();

        public IReadOnlyList<Message> Received => _received;

        public bool IsConnected => State == CoopClientState.InLobby || State == CoopClientState.InBattle;

        public void Connect(string host, int port)
        {
            State = CoopClientState.Connecting;
            _transport.Connect(host, port);
        }

        /// <summary>Pumps the transport and sends the periodic ping.</summary>
        public void Poll()
        {
            _transport.Poll();
            if (IsConnected && _nowMs() >= _nextPingAt)
            {
                _nextPingAt = _nowMs() + PingIntervalMs;
                var seq = _pingSeq.Next();
                _pingSentAt[seq] = _nowMs();
                _pingSentAt.Remove(unchecked(seq - 64));
                SendUnreliable(new Ping { Seq = seq, ClientTimeMs = _nowMs(), LastRttMs = RttMs });
            }
        }

        public void SetReady(bool ready) => Send(new ReadyRequest { Ready = ready });

        public void ChooseSide(BattleSide side) => Send(new ChooseSide { Epoch = Epoch, Side = side });

        public void Spectate() => Send(new ChooseSide { Epoch = Epoch, Spectate = true });

        /// <summary>Returns the request id the matching <see cref="ReserveResponse"/> carries.</summary>
        public uint Reserve(string slotId)
        {
            var id = ++_requestId;
            Send(new ReserveRequest { Epoch = Epoch, RequestId = id, SlotId = slotId });
            return id;
        }

        public void Confirm(long reservationId) => Send(new ConfirmRequest { Epoch = Epoch, ReservationId = reservationId });

        public void Release() => Send(new ReleaseRequest { Epoch = Epoch });

        public T? Last<T>() where T : Message => _received.OfType<T>().LastOrDefault();

        public IEnumerable<T> All<T>() where T : Message => _received.OfType<T>();

        public ReserveResponse? ResponseFor(uint requestId) => _received.OfType<ReserveResponse>().FirstOrDefault(r => r.RequestId == requestId);

        /// <summary>Cuts the connection without a goodbye (a crashed game).</summary>
        public void Kill() => _transport.Dispose();

        public void Dispose() => _transport.Dispose();

        private void SendHello() =>
            Send(new Hello { PlayerId = PlayerId, PlayerName = _name, GameBuild = _gameBuild, ModVersion = _modVersion });

        private void Send(Message m) => _transport.Send(_connection, MessageCodec.Encode(m), DeliveryMode.ReliableOrdered);

        private void SendUnreliable(Message m) => _transport.Send(_connection, MessageCodec.Encode(m), DeliveryMode.Unreliable);

        private void OnData(ConnectionId c, byte[] data, DeliveryMode mode)
        {
            var d = MessageCodec.Decode(data);
            if (!d.Ok)
                return;
            var message = d.Message!;
            switch (message)
            {
                case HelloAck ack:
                    SessionId = ack.SessionId;
                    Resumed = ack.Resumed;
                    State = CoopClientState.InLobby;
                    break;
                case HelloReject reject:
                    RejectReason = reject.Reason;
                    RejectDetail = reject.Detail;
                    State = CoopClientState.Rejected;
                    break;
                case LobbyUpdate lobby:
                    if (lobby.Revision > LobbyRevision || LobbyRevision == 0)
                    {
                        LobbyRevision = lobby.Revision;
                        _lobby = lobby.Players;
                    }

                    break;
                case BattleInvite invite:
                    Invite = invite;
                    Epoch = invite.Epoch;
                    State = CoopClientState.InBattle;
                    break;
                case SlotList list:
                    Slots = list.Slots;
                    break;
                case SlotsChanged changed:
                    Slots = changed.Slots;
                    break;
                case Pong pong:
                    if (_pingSentAt.TryGetValue(pong.Seq, out var sent))
                    {
                        _pingSentAt.Remove(pong.Seq);
                        RttMs = (int)Math.Max(0, _nowMs() - sent);
                        PongsReceived++;
                    }

                    break;
            }

            _received.Add(message);
            if (_received.Count > 4000)
                _received.RemoveRange(0, 1000); // long-lived clients (pings, lobby updates) must not grow without bound
        }
    }
}
