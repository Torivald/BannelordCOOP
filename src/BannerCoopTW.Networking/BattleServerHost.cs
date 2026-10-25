using System;
using System.Collections.Generic;
using System.Linq;
using BannerCoopTW.Core.Battle;
using BannerCoopTW.Core.Protocol;

namespace BannerCoopTW.Networking
{
    /// <summary>Host-side lobby settings.</summary>
    public sealed class HostOptions
    {
        /// <summary>Exact mod version a client must report in Hello; null = not checked.</summary>
        public string? RequiredModVersion { get; set; }

        /// <summary>How long a disconnected player's lobby place (and id) is kept so the same id can come back (reconnect stub).</summary>
        public int ReconnectGraceMs { get; set; } = 30_000;

        /// <summary>Remote players that fit in the lobby (the host's own player is not connected through the transport).</summary>
        public int MaxLobbyPlayers { get; set; } = 4;

        /// <summary>Pings update the shown RTT; the lobby is re-broadcast for RTT changes at most this often.</summary>
        public int RttBroadcastIntervalMs { get; set; } = 1000;
    }

    /// <summary>
    /// Server-side glue between the transport, the lobby and the authoritative <see cref="BattleSession"/> (ADR-0003): decodes client messages,
    /// applies them and fans the resulting state changes out. Clients never decide anything; every reply is derived from host state.
    /// A host starts as a lobby (connect, ready, ping, disconnect, reconnect stub) and gets a battle later through <see cref="StartBattle"/>;
    /// the constructor that takes a session starts with the battle already open (the original Phase 3/4 shape).
    /// All work happens inside <see cref="Poll"/> on the caller's thread.
    /// </summary>
    public sealed class BattleServerHost : IDisposable
    {
        private sealed class LobbyEntry
        {
            public PlayerId Id;
            public string Name = "";
            public bool Ready;
            public bool Connected;
            public int RttMs = -1;
            public long DisconnectedAtMs;
        }

        private readonly IServerTransport _transport;
        private BattleSession? _session;
        private readonly string _gameBuild;
        private readonly IClock _clock;
        private readonly Dictionary<ConnectionId, PlayerId> _playerOf = new Dictionary<ConnectionId, PlayerId>();
        private readonly Dictionary<PlayerId, ConnectionId> _connectionOf = new Dictionary<PlayerId, ConnectionId>();
        private readonly Dictionary<PlayerId, LobbyEntry> _lobby = new Dictionary<PlayerId, LobbyEntry>();
        private readonly HashSet<ConnectionId> _refused = new HashSet<ConnectionId>();
        private readonly List<KeyValuePair<ConnectionId, long>> _closing = new List<KeyValuePair<ConnectionId, long>>();
        private readonly System.Diagnostics.Stopwatch _wall = System.Diagnostics.Stopwatch.StartNew();
        private bool _hadBattle;
        private uint _lobbyRevision;
        private bool _rttDirty;
        private long _lastLobbySendWall = -100_000;

        /// <summary>Grace before a refused peer is cut, so the reliable HelloReject (the reason) still reaches it.</summary>
        private const int RejectLingerMs = 200;

        /// <summary>Lobby host without a battle yet.</summary>
        public BattleServerHost(IServerTransport transport, IClock clock, string gameBuild, HostOptions? options = null)
        {
            _transport = transport;
            _clock = clock;
            _gameBuild = gameBuild;
            Options = options ?? new HostOptions();
            _transport.Disconnected += OnDisconnected;
            _transport.DataReceived += OnData;
        }

        /// <summary>Host with the battle already open. The initiator is the host's own local player and is not connected through the transport.</summary>
        public BattleServerHost(IServerTransport transport, BattleSession session, IClock clock, string gameBuild, HostOptions? options = null)
            : this(transport, clock, gameBuild, options)
        {
            _session = session;
            _hadBattle = true;
        }

        public HostOptions Options { get; }

        /// <summary>The open battle, or null while the host is only a lobby.</summary>
        public BattleSession? Session => _session;

        /// <summary>
        /// Whether a client's ConfirmRequest may bind the reservation. In the game the engine side confirms (<see cref="ConfirmFromEngine"/>) once the
        /// agent is attached; clients must not self-assign, so this is off by default (tests switch it on as a stand-in for the engine).
        /// </summary>
        public bool AllowClientConfirm { get; set; }

        /// <summary>Connected + recently disconnected (grace) lobby members as clients see them.</summary>
        public IReadOnlyList<LobbyPlayer> LobbyPlayers => _lobby.Values.OrderBy(e => e.Id.Value).Select(ToWire).ToList();

        /// <summary>True when at least one player is connected and every connected player is ready.</summary>
        public bool AllReady
        {
            get
            {
                var connected = _lobby.Values.Where(e => e.Connected).ToList();
                return connected.Count > 0 && connected.All(e => e.Ready);
            }
        }

        public int ConnectedPlayers => _playerOf.Count;

        /// <summary>Opens the battle for everyone already in the lobby: each connected player is invited and receives the BattleInvite.</summary>
        public void StartBattle(BattleSession session)
        {
            if (_session != null)
                throw new InvalidOperationException("A battle is already open.");
            _session = session;
            _hadBattle = true;
            foreach (var entry in _lobby.Values.OrderBy(e => e.Id.Value).ToList())
            {
                // players inside their reconnect grace are not invited: they come back through Hello (Rejoin/Invite) and must not hold a place meanwhile
                if (!entry.Connected || !_connectionOf.TryGetValue(entry.Id, out var c))
                    continue;
                if (session.Invite(entry.Id) == SessionError.None)
                {
                    SendInvite(c);
                    continue;
                }

                // no place in this battle: tell the client properly and cut it, instead of leaving it half-connected
                _lobby.Remove(entry.Id);
                Reject(c, RejectReason.SessionFull, "battle full");
            }

            BroadcastLobby();

            Flush();
        }

        /// <summary>Engine callback: the agent of the player's reservation is attached; Reserved → Controlling.</summary>
        public SessionError ConfirmFromEngine(PlayerId player)
        {
            var session = _session;
            if (session == null)
                return SessionError.BattleClosed;
            var held = session.GetReservationId(player);
            if (held == 0)
                return SessionError.ReservationNotFound;
            var result = session.Confirm(player, held);
            if (_connectionOf.TryGetValue(player, out var c))
                Send(c, new ConfirmResponse { ReservationId = held, Error = result });
            Flush();
            return result;
        }

        /// <summary>Pumps the transport, expires reservations and lobby places, and flushes state changes to clients.</summary>
        public void Poll()
        {
            _transport.Poll();
            _session?.Tick();
            ExpireLobby();
            Flush();
            FlushRtt();
            CloseLingering();
        }

        private void CloseLingering()
        {
            for (var i = _closing.Count - 1; i >= 0; i--)
            {
                if (_wall.ElapsedMilliseconds < _closing[i].Value)
                    continue;
                _transport.Disconnect(_closing[i].Key);
                _closing.RemoveAt(i);
            }
        }

        private void DisconnectSoon(ConnectionId c) => _closing.Add(new KeyValuePair<ConnectionId, long>(c, _wall.ElapsedMilliseconds + RejectLingerMs));

        public void EndBattle()
        {
            var session = _session;
            if (session == null)
                return;
            session.End();
            Flush();
            Broadcast(new BattleEnded { BattleId = session.BattleId });
            // back to a plain lobby: new Hellos are accepted again and a new battle can be started
            _session = null;
            foreach (var e in _lobby.Values)
                e.Ready = false;
            BroadcastLobby();
        }

        public void Dispose() => _transport.Dispose();

        // ---------------------------------------------------------------- lobby

        private static LobbyPlayer ToWire(LobbyEntry e) => new LobbyPlayer { PlayerId = e.Id.Value, Name = e.Name, Ready = e.Ready, Connected = e.Connected, RttMs = e.RttMs };

        private void BroadcastLobby()
        {
            _lobbyRevision++;
            _rttDirty = false;
            _lastLobbySendWall = _wall.ElapsedMilliseconds;
            Broadcast(new LobbyUpdate { Revision = _lobbyRevision, Players = LobbyPlayers.ToList() });
        }

        private void FlushRtt()
        {
            if (_rttDirty && _wall.ElapsedMilliseconds - _lastLobbySendWall >= Options.RttBroadcastIntervalMs)
                BroadcastLobby();
        }

        private void ExpireLobby()
        {
            List<PlayerId>? gone = null;
            foreach (var e in _lobby.Values)
                if (!e.Connected && _clock.NowMs - e.DisconnectedAtMs > Options.ReconnectGraceMs)
                    (gone ??= new List<PlayerId>()).Add(e.Id);
            if (gone == null)
                return;
            foreach (var id in gone)
            {
                _session?.Disconnect(id);
                _lobby.Remove(id);
            }

            BroadcastLobby();
        }

        // ---------------------------------------------------------------- inbound

        private void OnDisconnected(ConnectionId c, DisconnectReason reason)
        {
            _refused.Remove(c);
            if (_playerOf.TryGetValue(c, out var player))
            {
                _playerOf.Remove(c);
                _connectionOf.Remove(player);
                _session?.Disconnect(player);
                if (_lobby.TryGetValue(player, out var entry))
                {
                    entry.Connected = false;
                    entry.Ready = false;
                    entry.DisconnectedAtMs = _clock.NowMs;
                }

                BroadcastLobby();
            }
        }

        private void OnData(ConnectionId c, byte[] data, DeliveryMode mode)
        {
            if (_refused.Contains(c))
                return; // refused and about to be cut: nothing it sends counts any more
            var decoded = MessageCodec.Decode(data);
            if (!decoded.Ok)
            {
                if (decoded.Error == DecodeError.UnsupportedVersion)
                    Reject(c, RejectReason.ProtocolMismatch, "server speaks protocol " + ProtocolInfo.Version + ", client " + decoded.PeerVersion);
                else
                    _transport.Disconnect(c); // garbage on a reliable channel: drop the peer
                return;
            }

            var message = decoded.Message!;
            if (message is Hello hello)
            {
                OnHello(c, hello);
                return;
            }

            if (!_playerOf.TryGetValue(c, out var player))
            {
                _transport.Disconnect(c); // anything before Hello is a protocol violation
                return;
            }

            // lobby messages work with or without a battle
            switch (message)
            {
                case ReadyRequest ready:
                    if (_lobby.TryGetValue(player, out var entry) && entry.Ready != ready.Ready)
                    {
                        entry.Ready = ready.Ready;
                        BroadcastLobby();
                    }

                    return;
                case Ping ping:
                    if (ping.LastRttMs >= 0 && _lobby.TryGetValue(player, out var e2) && e2.RttMs != ping.LastRttMs)
                    {
                        e2.RttMs = ping.LastRttMs;
                        _rttDirty = true;
                    }

                    _transport.Send(c, MessageCodec.Encode(new Pong { Seq = ping.Seq, ClientTimeMs = ping.ClientTimeMs, ServerTimeMs = _clock.NowMs }), DeliveryMode.Unreliable);
                    return;
            }

            var session = _session;
            if (session == null)
            {
                if (!_hadBattle)
                {
                    _transport.Disconnect(c); // battle messages before any battle was ever opened: protocol violation
                    return;
                }

                // a battle ended a moment ago: late requests get an answer instead of a dropped connection
                switch (message)
                {
                    case ReserveRequest late:
                        Send(c, new ReserveResponse { RequestId = late.RequestId, Error = SessionError.BattleClosed });
                        break;
                    case ConfirmRequest late:
                        Send(c, new ConfirmResponse { ReservationId = late.ReservationId, Error = SessionError.BattleClosed });
                        break;
                }

                return;
            }

            var epoch = EpochOf(message);
            if (epoch.HasValue && epoch.Value != session.Epoch)
            {
                RefuseStale(c, message);
                return;
            }

            switch (message)
            {
                case ChooseSide choose:
                    if (choose.Spectate)
                        session.Spectate(player);
                    else
                        session.ChooseSide(player, choose.Side);
                    SendSlotList(player);
                    break;
                case ReserveRequest reserve:
                    var r = session.Reserve(player, reserve.SlotId);
                    Send(c, new ReserveResponse
                    {
                        RequestId = reserve.RequestId,
                        Error = r.Error,
                        ReservationId = r.ReservationId,
                        ExpiresInMs = r.Ok ? (int)Math.Max(0, r.ExpiresAtMs - _clock.NowMs) : 0,
                    });
                    break;
                case ConfirmRequest confirm:
                    Send(c, new ConfirmResponse
                    {
                        ReservationId = confirm.ReservationId,
                        Error = AllowClientConfirm ? session.Confirm(player, confirm.ReservationId) : SessionError.WrongPhase,
                    });
                    break;
                case ReleaseRequest _:
                    session.Release(player);
                    SendSlotList(player);
                    break;
                default:
                    _transport.Disconnect(c); // server-to-client message sent by a client
                    break;
            }
        }

        private void OnHello(ConnectionId c, Hello hello)
        {
            if (_playerOf.ContainsKey(c))
            {
                _transport.Disconnect(c);
                return;
            }

            if (hello.ProtocolVersion != ProtocolInfo.Version)
            {
                Reject(c, RejectReason.ProtocolMismatch, "protocol " + hello.ProtocolVersion);
                return;
            }

            if (hello.GameBuild != _gameBuild)
            {
                Reject(c, RejectReason.GameBuildMismatch, "server build " + _gameBuild);
                return;
            }

            if (Options.RequiredModVersion != null && hello.ModVersion != Options.RequiredModVersion)
            {
                Reject(c, RejectReason.ModuleMismatch, "server mod " + Options.RequiredModVersion + ", client " + hello.ModVersion);
                return;
            }

            var player = new PlayerId(hello.PlayerId);
            var resumed = false;
            if (_lobby.TryGetValue(player, out var entry))
            {
                if (entry.Connected || _connectionOf.ContainsKey(player))
                {
                    Reject(c, RejectReason.SessionFull, "no free player place");
                    return;
                }

                if (_clock.NowMs - entry.DisconnectedAtMs > Options.ReconnectGraceMs)
                    _lobby.Remove(player); // grace is over: treated as a new player
                else
                    resumed = true;
            }

            if (!resumed && _lobby.Values.Count >= Options.MaxLobbyPlayers)
            {
                Reject(c, RejectReason.SessionFull, "no free player place");
                return;
            }

            var session = _session;
            if (session != null)
            {
                var join = resumed ? session.Rejoin(player) : session.Invite(player);
                if (join == SessionError.UnknownPlayer)
                    join = session.Invite(player);   // joined the lobby before this battle existed
                if (join == SessionError.AlreadyInSession)
                {
                    if (session.GetPhase(player) == PlayerPhase.Left)
                        join = session.Rejoin(player);   // left earlier and the lobby grace ran out: same id comes back as a fresh participant
                    else if (resumed)
                        join = SessionError.None;        // the session never saw the disconnect: nothing to repair
                }

                if (join != SessionError.None)
                {
                    Reject(c, RejectReason.SessionFull, join == SessionError.SessionFull ? "no free player place" : "battle not accepting players");
                    return;
                }
            }

            if (!_lobby.TryGetValue(player, out entry))
            {
                entry = new LobbyEntry { Id = player };
                _lobby[player] = entry;
            }

            entry.Name = hello.PlayerName;
            entry.Connected = true;
            entry.Ready = false;
            _playerOf[c] = player;
            _connectionOf[player] = c;
            Send(c, new HelloAck { SessionId = session?.BattleId ?? "lobby", Resumed = resumed });
            BroadcastLobby();
            if (session != null)
                SendInvite(c);
        }

        private void SendInvite(ConnectionId c)
        {
            var session = _session!;
            Send(c, new BattleInvite { BattleId = session.BattleId, Epoch = session.Epoch, InitiatorSide = session.InitiatorSide, AllowPvp = true });
        }

        private static uint? EpochOf(Message m)
        {
            switch (m)
            {
                case ChooseSide x: return x.Epoch;
                case ReserveRequest x: return x.Epoch;
                case ConfirmRequest x: return x.Epoch;
                case ReleaseRequest x: return x.Epoch;
                default: return null;
            }
        }

        private void RefuseStale(ConnectionId c, Message m)
        {
            switch (m)
            {
                case ReserveRequest r:
                    Send(c, new ReserveResponse { RequestId = r.RequestId, Error = SessionError.StaleBattle });
                    break;
                case ConfirmRequest k:
                    Send(c, new ConfirmResponse { ReservationId = k.ReservationId, Error = SessionError.StaleBattle });
                    break;
                default:
                    break; // side/release for another battle: ignored, the next SlotList carries the right epoch implicitly
            }
        }

        private void Reject(ConnectionId c, RejectReason reason, string detail)
        {
            _refused.Add(c);
            Send(c, new HelloReject { Reason = reason, Detail = detail });
            DisconnectSoon(c);
        }

        // ---------------------------------------------------------------- outbound

        private void Flush()
        {
            var session = _session;
            if (session == null)
                return;
            var events = session.DrainEvents();
            if (events.Count == 0)
                return;

            var slotsChanged = false;
            foreach (var e in events)
            {
                if (e.Kind == SessionEventKind.SlotChanged)
                    slotsChanged = true;
                else if (e.Kind == SessionEventKind.PlayerPhaseChanged && e.Player.HasValue)
                {
                    Broadcast(new PlayerPhaseChanged
                    {
                        Revision = e.Revision,
                        PlayerId = e.Player.Value.Value,
                        Phase = session.GetPhase(e.Player.Value) ?? PlayerPhase.Left,
                        SlotId = session.GetHeldSlot(e.Player.Value) ?? "",
                    });
                }
            }

            if (slotsChanged)
                foreach (var p in _connectionOf.Keys.ToList())
                    SendSlotList(p);
        }

        private void SendSlotList(PlayerId player)
        {
            var session = _session;
            if (session == null || !_connectionOf.TryGetValue(player, out var c))
                return;
            Send(c, new SlotList { Revision = session.Revision, Slots = session.GetSlots(player).Select(SlotInfo.From).ToList() });
        }

        private void Broadcast(Message m)
        {
            foreach (var c in _playerOf.Keys.ToList())
                Send(c, m);
        }

        private void Send(ConnectionId c, Message m) => _transport.Send(c, MessageCodec.Encode(m), DeliveryMode.ReliableOrdered);
    }
}
