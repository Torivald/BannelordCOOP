using System;
using System.Collections.Generic;
using System.Linq;
using BannerCoopTW.Core.Battle;
using BannerCoopTW.Core.Protocol;

namespace BannerCoopTW.Networking
{
    /// <summary>
    /// Server-side glue between the transport and the authoritative <see cref="BattleSession"/> (ADR-0003): decodes client messages,
    /// applies them to the session and fans the resulting state changes out. Clients never decide anything; every reply is derived from the session.
    /// All work happens inside <see cref="Poll"/> on the caller's thread.
    /// </summary>
    public sealed class BattleServerHost : IDisposable
    {
        private readonly IServerTransport _transport;
        private readonly BattleSession _session;
        private readonly string _gameBuild;
        private readonly IClock _clock;
        private readonly Dictionary<ConnectionId, PlayerId> _playerOf = new Dictionary<ConnectionId, PlayerId>();
        private readonly Dictionary<PlayerId, ConnectionId> _connectionOf = new Dictionary<PlayerId, ConnectionId>();
        private readonly List<KeyValuePair<ConnectionId, long>> _closing = new List<KeyValuePair<ConnectionId, long>>();
        private readonly System.Diagnostics.Stopwatch _wall = System.Diagnostics.Stopwatch.StartNew();

        /// <summary>Grace before a refused peer is cut, so the reliable HelloReject (the reason) still reaches it.</summary>
        private const int RejectLingerMs = 200;

        public BattleServerHost(IServerTransport transport, BattleSession session, IClock clock, string gameBuild)
        {
            _transport = transport;
            _session = session;
            _clock = clock;
            _gameBuild = gameBuild;
            // The initiator is the host's own local player and is not connected through the transport.
            _transport.Disconnected += OnDisconnected;
            _transport.DataReceived += OnData;
        }

        public BattleSession Session => _session;

        /// <summary>
        /// Whether a client's ConfirmRequest may bind the reservation. In the game the engine side confirms (<see cref="ConfirmFromEngine"/>) once the
        /// agent is attached; clients must not self-assign, so this is off by default (tests switch it on as a stand-in for the engine).
        /// </summary>
        public bool AllowClientConfirm { get; set; }

        /// <summary>Engine callback: the agent of the player's reservation is attached; Reserved → Controlling.</summary>
        public SessionError ConfirmFromEngine(PlayerId player)
        {
            var held = _session.GetReservationId(player);
            if (held == 0)
                return SessionError.ReservationNotFound;
            var result = _session.Confirm(player, held);
            if (_connectionOf.TryGetValue(player, out var c))
                Send(c, new ConfirmResponse { ReservationId = held, Error = result });
            Flush();
            return result;
        }

        public int ConnectedPlayers => _playerOf.Count;

        /// <summary>Pumps the transport, expires reservations and flushes session events to clients.</summary>
        public void Poll()
        {
            _transport.Poll();
            _session.Tick();
            Flush();
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
            _session.End();
            Flush();
            Broadcast(new BattleEnded { BattleId = _session.BattleId });
        }

        public void Dispose() => _transport.Dispose();

        // ---------------------------------------------------------------- inbound

        private void OnDisconnected(ConnectionId c, DisconnectReason reason)
        {
            if (_playerOf.TryGetValue(c, out var player))
            {
                _playerOf.Remove(c);
                _connectionOf.Remove(player);
                _session.Disconnect(player);
            }
        }

        private void OnData(ConnectionId c, byte[] data, DeliveryMode mode)
        {
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

            var epoch = EpochOf(message);
            if (epoch.HasValue && epoch.Value != _session.Epoch)
            {
                RefuseStale(c, message);
                return;
            }

            switch (message)
            {
                case ChooseSide choose:
                    if (choose.Spectate)
                        _session.Spectate(player);
                    else
                        _session.ChooseSide(player, choose.Side);
                    SendSlotList(player);
                    break;
                case ReserveRequest reserve:
                    var r = _session.Reserve(player, reserve.SlotId);
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
                        Error = AllowClientConfirm ? _session.Confirm(player, confirm.ReservationId) : SessionError.WrongPhase,
                    });
                    break;
                case ReleaseRequest _:
                    _session.Release(player);
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

            var player = new PlayerId(hello.PlayerId);
            if (_connectionOf.ContainsKey(player))
            {
                Reject(c, RejectReason.SessionFull, "no free player place");
                return;
            }

            var invite = _session.Invite(player);
            if (invite != SessionError.None)
            {
                Reject(c, RejectReason.SessionFull, invite == SessionError.SessionFull ? "no free player place" : "battle not accepting players");
                return;
            }

            _playerOf[c] = player;
            _connectionOf[player] = c;
            Send(c, new HelloAck { SessionId = _session.BattleId });
            Send(c, new BattleInvite { BattleId = _session.BattleId, Epoch = _session.Epoch, InitiatorSide = _session.InitiatorSide, AllowPvp = true });
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
            Send(c, new HelloReject { Reason = reason, Detail = detail });
            DisconnectSoon(c);
        }

        // ---------------------------------------------------------------- outbound

        private void Flush()
        {
            var events = _session.DrainEvents();
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
                        Phase = _session.GetPhase(e.Player.Value) ?? PlayerPhase.Left,
                        SlotId = _session.GetHeldSlot(e.Player.Value) ?? "",
                    });
                }
            }

            if (slotsChanged)
                foreach (var p in _connectionOf.Keys.ToList())
                    SendSlotList(p);
        }

        private void SendSlotList(PlayerId player)
        {
            if (!_connectionOf.TryGetValue(player, out var c))
                return;
            Send(c, new SlotList { Revision = _session.Revision, Slots = _session.GetSlots(player).Select(SlotInfo.From).ToList() });
        }

        private void Broadcast(Message m)
        {
            foreach (var c in _playerOf.Keys.ToList())
                Send(c, m);
        }

        private void Send(ConnectionId c, Message m) => _transport.Send(c, MessageCodec.Encode(m), DeliveryMode.ReliableOrdered);
    }
}
