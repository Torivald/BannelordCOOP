using System;
using BannerCoopTW.Core.Logging;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using BannerCoopTW.Networking;
using TaleWorlds.Core;
using TaleWorlds.Library;
using BinaryReader = System.IO.BinaryReader;
using BinaryWriter = System.IO.BinaryWriter;
using TaleWorlds.MountAndBlade;

namespace BannerCoopTW.Spikes.A1
{
    /// <summary>
    /// Spike A1 mission side. Host: authoritative single-player mission, sends agent snapshots, applies one remote agent's input, forwards hits and deaths.
    /// Client: mirror mission, AI paused, puppets driven by snapshots through a translation table host agent index → local agent built from (character StringId, side, spawn position).
    /// All traffic goes through <see cref="ITransport"/> (LiteNetLib). Debug-only code (ADR-0006 option A, Gate A1).
    /// </summary>
    public sealed partial class A1Behavior : MissionBehavior
    {
        public const int Port = 9998;
        private const byte MsgHello = 1, MsgTable = 2, MsgTableAck = 3, MsgGo = 4, MsgSnapshot = 5, MsgInput = 6, MsgHit = 7, MsgDeath = 8, MsgStats = 10;
        private const float SnapshotInterval = 1f / 20f;
        private const float InputInterval = 1f / 30f;

        private readonly bool _host;
        private readonly string _mode;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private IServerTransport? _server;
        private IClientTransport? _client;
        private ConnectionId _peer;
        private bool _linked;
        private bool _initialised;
        private bool _running;
        private float _runningTime;
        private float _sinceSend;
        private float _sinceHello;
        private readonly BannerCoopTW.Core.Protocol.SequenceCounter _snapshotSeq = new BannerCoopTW.Core.Protocol.SequenceCounter();
        private readonly BannerCoopTW.Core.Protocol.SequenceGate _snapshotGate = new BannerCoopTW.Core.Protocol.SequenceGate();

        // translation table (client): host agent index -> local agent; host: the agents it announced
        private readonly Dictionary<int, Agent> _table = new Dictionary<int, Agent>();
        private Agent? _controlled;
        private int _controlledHostIndex = -1;

        // metrics
        private readonly StreamWriter _tsv;
        private float _winTime;
        private int _winFrames;
        private float _winMaxDt;
        private long _txBytes, _rxBytes;
        private int _txPackets, _rxPackets, _snapshotsApplied, _hitsApplied, _deathsApplied, _hitsSent, _deathsSent;
        private readonly List<double> _ackLatencies = new List<double>();
        private readonly List<double> _effectLatencies = new List<double>();
        private double _baselineFpsSum;
        private int _baselineWindows;

        // client input script
        private int _inputSeq;
        private readonly Dictionary<int, long> _inputSentAt = new Dictionary<int, long>();
        private int _lastAckSeq;

        // host: last input applied
        private uint _inFlags;
        private Vec2 _inLook = new Vec2(1f, 0f);
        private int _inSeq;

        public static A1Behavior? Instance { get; private set; }

        private Action? _pendingFinish;

        /// <summary>FinishDeployment removes the deployment controller from the mission's behaviour list, so it must not run inside Mission.OnTick (our OnMissionTick); the driver runs it from the application tick.</summary>
        private void RequestFinish(Action after) => _pendingFinish = after;

        public void RunPending()
        {
            var after = _pendingFinish;
            if (after == null)
                return;
            _pendingFinish = null;
            try
            {
                FinishDeploymentSafely();
                after();
            }
            catch (Exception e)
            {
                SubModule.Log?.Error("A1 finish deployment failed", e);
            }
        }

        public A1Behavior(bool host, string mode)
        {
            Instance = this;
            _host = host;
            _mode = mode ?? string.Empty;
            Directory.CreateDirectory(SpikeS1.OutDir);
            _tsv = new StreamWriter(Path.Combine(SpikeS1.OutDir, $"a1-{(host ? "host" : "client")}-{Process.GetCurrentProcess().Id}.tsv"), false) { AutoFlush = true };
            _tsv.WriteLine("t_s\tphase\tagents\tfps\tmaxDt_ms\ttxKBps\trxKBps\ttxPkt\trxPkt\tsnapApplied\thits\tdeaths\tackLat_ms_p50\tackLat_ms_p95\tackLat_ms_max\tmoved\tnote");
        }

        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        private static void Log(string text) => SubModule.Log?.Info("A1: " + text);

        private long NowMs => _clock.ElapsedMilliseconds;

        public override void OnMissionTick(float dt)
        {
            var tickStart = _wall.Elapsed.TotalMilliseconds;
            try
            {
                if (_mode.Contains("nodie") || (!_host && _running && !_mode.Contains("localdmg")))
                    Mission.DisableDying = true; // client: damage only comes from the host (A1b), local blows are counted in OnRegisterBlow
                if (_mode.Contains("solo"))
                {
                    SoloTick(dt);
                    return;
                }

                if (!_initialised)
                    Initialise();
                var pollStart = _wall.Elapsed.TotalMilliseconds;
                (_server as ITransport ?? _client)?.Poll();
                NotePoll(_wall.Elapsed.TotalMilliseconds - pollStart);
                Window(dt);
                if (_host)
                    HostTick(dt);
                else
                    ClientTick(dt);
            }
            catch (Exception e)
            {
                SubModule.Log?.Error("A1 behaviour tick failed", e);
            }
            finally
            {
                var ms = _wall.Elapsed.TotalMilliseconds - tickStart;
                _tickMsSum += ms;
                if (ms > _tickMsMax)
                    _tickMsMax = ms;
            }
        }

        private void Initialise()
        {
            _initialised = true;
            Log($"behaviour attached, role={(_host ? "host" : "client")}, agents={Mission.Agents.Count}, mode='{_mode}'");
            if (_host)
            {
                IServerTransport server = new LiteNetLibServerTransport();
                var cond = ParseNetConditions();
                if (cond.IsActive)
                    server = new NetworkConditionServerTransport(server, cond, 1);
                server.Listen(Port);
                server.Connected += c => { _peer = c; _linked = true; Log("host: client connected " + c); };
                server.Disconnected += (c, r) => { _linked = false; Log("host: client disconnected " + r); };
                server.DataReceived += (c, data, mode) => OnHostData(data);
                _server = server;
            }
            else
            {
                IClientTransport client = new LiteNetLibClientTransport();
                var cond = ParseNetConditions();
                if (cond.IsActive)
                    client = new NetworkConditionClientTransport(client, cond, 2);
                client.Connected += c => { _peer = c; _linked = true; Log("client: connected to host"); };
                client.Disconnected += (c, r) => { _linked = false; Log("client: disconnected " + r); };
                client.DataReceived += (c, data, mode) => OnClientData(data);
                client.Connect("127.0.0.1", Port);
                _client = client;
            }
        }

        /// <summary>The stock UI deploys the player's formations before finishing; without it the formations have no valid order position and the engine crashes on the next tick.</summary>
        private void FinishDeploymentSafely()
        {
            var handler = Mission.GetMissionBehavior<DeploymentHandler>();
            if (handler == null)
            {
                Log("no DeploymentHandler in this mission");
                return;
            }

            if (!_mode.Contains("nodeploy"))
            {
                handler.AutoDeployTeamUsingDeploymentPlan(Mission.PlayerTeam);
                handler.ForceUpdateAllUnits();
                Log("player team auto-deployed");
            }

            handler.FinishDeployment();
        }

        private float _soloTime;
        private bool _soloDone;

        /// <summary>Isolation experiment: no network, only DeploymentHandler.FinishDeployment after 10 s, then log the frame rate.</summary>
        private void SoloTick(float dt)
        {
            _soloTime += dt;
            if (!_soloDone && _soloTime > 10f)
            {
                _soloDone = true;
                Log($"solo({(_host ? "host" : "client")}): calling FinishDeployment now");
                RequestFinish(() => Log("solo: FinishDeployment returned"));
            }

            if (_soloCharged)
                BrawlTick(dt, true);
            if (_soloDone && !_soloCharged && _soloTime > 14f && Mission.PlayerTeam != null)
            {
                _soloCharged = true;
                Mission.PlayerTeam.MasterOrderController.SelectAllFormations();
                Mission.PlayerTeam.MasterOrderController.SetOrder(OrderType.Charge);
                Log("solo: Charge ordered");
            }

            Window(dt);
        }

        private bool _soloCharged;

        // ---------- metrics window ----------

        private readonly Stopwatch _wall = Stopwatch.StartNew();
        private double _lastFrameWall;
        private double _wallWindowStart;
        private double _tickMsSum;
        private double _tickMsMax;

        private void Window(float dt)
        {
            // dt is clamped by the engine (about 67 ms), so frame rate and stalls are measured on the wall clock.
            var now = _wall.Elapsed.TotalSeconds;
            var frameWall = _lastFrameWall > 0 ? now - _lastFrameWall : 0.0;
            _lastFrameWall = now;
            if (frameWall > _winMaxDt)
                _winMaxDt = (float)frameWall;
            if (frameWall > 0.2)
                StallLog(frameWall);
            _winFrames++;
            if (_running)
                _runningTime += dt;
            _winTime = (float)(now - _wallWindowStart);
            if (_winTime < 5f)
                return;
            _wallWindowStart = now;

            var fps = _winFrames / _winTime;
            var phase = _running ? "sync" : "baseline";
            if (!_running)
            {
                _baselineFpsSum += fps;
                _baselineWindows++;
            }

            var lat = Percentiles(_ackLatencies);
            var movingAgents = CountMovingAgents();
            var moved = _controlled != null && _controlled.IsActive() ? _controlled.Position.ToString() : "-";
            _tsv.WriteLine(string.Join("\t", new[]
            {
                (NowMs / 1000.0).ToString("F1"), phase, Mission.Agents.Count.ToString(), fps.ToString("F1"), (_winMaxDt * 1000f).ToString("F1"),
                (_txBytes / 1024.0 / _winTime).ToString("F2"), (_rxBytes / 1024.0 / _winTime).ToString("F2"), _txPackets.ToString(), _rxPackets.ToString(),
                _snapshotsApplied.ToString(), (_hitsSent + _hitsApplied).ToString(), (_deathsSent + _deathsApplied).ToString(),
                lat.Item1.ToString("F1"), lat.Item2.ToString("F1"), lat.Item3.ToString("F1"), moved, _linked ? "linked" : "unlinked",
            }));
            var ownAvg = _winFrames > 0 ? _tickMsSum / _winFrames : 0.0;
            var applyAvg = _applyCalls > 0 ? _applyMs / _applyCalls : 0.0;
            Log($"{phase}: apply={applyAvg:F2}ms/chunk x{_applyCalls} own={ownAvg:F2}ms/frame(max {_tickMsMax:F1}) moving={movingAgents} siege={_siegeSent}/{_siegeApplied}/{_siegeDiverged}/{_siegeUses} fps={fps:F1} maxDt={_winMaxDt * 1000f:F1}ms agents={Mission.Agents.Count} tx={_txBytes / 1024.0 / _winTime:F1}KB/s rx={_rxBytes / 1024.0 / _winTime:F1}KB/s " +
                $"snap={_snapshotsApplied} hits={_hitsSent + _hitsApplied} deaths={_deathsSent + _deathsApplied} ack p50/p95/max={lat.Item1:F1}/{lat.Item2:F1}/{lat.Item3:F1}ms");
            DivergenceWindowLog();
            _winFrames = 0;
            _tickMsSum = 0;
            _tickMsMax = 0;
            _winMaxDt = 0f;
            _txBytes = _rxBytes = 0;
            _txPackets = _rxPackets = 0;
            _snapshotsApplied = 0;
            _applyMs = 0;
            _applyCalls = 0;
            _ackLatencies.Clear();
        }

        private readonly Dictionary<int, Vec3> _lastWinPos = new Dictionary<int, Vec3>();

        /// <summary>Agents that moved more than 0.5 m since the previous metrics window (shows whether the battle AI is actually running).</summary>
        private int CountMovingAgents()
        {
            var moving = 0;
            foreach (var a in Mission.Agents)
            {
                if (!a.IsHuman || !a.IsActive())
                    continue;
                var p = a.Position;
                if (_lastWinPos.TryGetValue(a.Index, out var old) && old.Distance(p) > 0.5f)
                    moving++;
                _lastWinPos[a.Index] = p;
            }

            return moving;
        }

        private static Tuple<double, double, double> Percentiles(List<double> values)
        {
            if (values.Count == 0)
                return Tuple.Create(0.0, 0.0, 0.0);
            var copy = new List<double>(values);
            copy.Sort();
            return Tuple.Create(copy[copy.Count / 2], copy[Math.Min(copy.Count - 1, (int)(copy.Count * 0.95))], copy[copy.Count - 1]);
        }

        // ---------- sending ----------

        private void Send(byte[] data, DeliveryMode mode)
        {
            var t = (ITransport?)_server ?? _client;
            if (t == null || !_linked)
                return;
            Trace("send " + mode + " " + data.Length + " bytes");
            t.Send(_peer, data, mode);
            Trace("send done");
            _txBytes += data.Length;
            _txPackets++;
        }

        // ---------- host ----------

        private bool _tableSent;
        private bool _tableAcked;
        private float _sinceSnapshot;
        private int _traceFrames;
        private bool _controlSwitched;
        private bool _chargeOrdered;

        private void Trace(string what)
        {
            if (_traceFrames < 6)
                Log("trace: " + what);
        }

        private readonly Dictionary<int, Agent> _hostById = new Dictionary<int, Agent>();

        private void HostTick(float dt)
        {
            if (!_linked)
                return;
            if (!_tableSent)
            {
                _tableSent = true;
                SendTable();
                return;
            }

            if (!_running || _mode.Contains("idle"))
                return;

            Trace("host frame start");
            if (!_controlSwitched && _runningTime > 1f && _controlled != null && _controlled.IsActive())
            {
                _controlSwitched = true;
                if (_mode.Contains("pause"))
                    _controlled.SetIsAIPaused(true);
                else
                    _controlled.Controller = AgentControllerType.None;
                Log("host: controlled agent taken off AI (" + (_mode.Contains("pause") ? "SetIsAIPaused" : "Controller=None") + ") 1 s after deployment finished");
            }

            if (!_chargeOrdered && _runningTime > 3f && Mission.PlayerTeam != null)
            {
                _chargeOrdered = true;
                var oc = Mission.PlayerTeam.MasterOrderController;
                oc.SelectAllFormations();
                oc.SetOrder(OrderType.Charge);
                Log("host: Charge ordered for the player team");
            }

            if (_controlSwitched)
                ApplyInputToControlled();
            HostStatsTick(dt);
            BrawlTick(dt);
            Trace("host input applied");
            _sinceSnapshot += dt;
            if (_sinceSnapshot >= SnapshotInterval)
            {
                _sinceSnapshot = 0f;
                if (!_mode.Contains("nosnap"))
                    SendSnapshot();
                Trace("host snapshot sent");
            }

            SiegeHostTick();
            _traceFrames++;
        }

        private void SendTable()
        {
            _hostById.Clear();
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(MsgTable);
                var list = new List<Agent>();
                foreach (var a in Mission.Agents)
                    if (a.IsHuman && a.Team != null && a.Character != null)
                        list.Add(a);
                w.Write(list.Count);
                foreach (var a in list)
                {
                    _hostById[a.Index] = a;
                    w.Write(a.Index);
                    w.Write(a.Character.StringId);
                    w.Write((int)a.Team.Side);
                    var p = a.Position;
                    w.Write(p.x);
                    w.Write(p.y);
                    w.Write(p.z);
                }

                // the controlled agent: first non-player attacker foot soldier
                foreach (var a in list)
                {
                    if (!a.IsMainAgent && a.Team.Side == BattleSideEnum.Attacker && a.MountAgent == null)
                    {
                        _controlled = a;
                        _controlledHostIndex = a.Index;
                        break;
                    }
                }

                w.Write(_controlledHostIndex);
                SiegeWriteTable(w);
                Send(ms.ToArray(), DeliveryMode.ReliableOrdered);
                Log($"host: table sent, {list.Count} agents, controlled host index {_controlledHostIndex} ({_controlled?.Character?.StringId})");
            }
        }

        private void OnHostData(byte[] data)
        {
            _rxBytes += data.Length;
            _rxPackets++;
            using (var r = new BinaryReader(new MemoryStream(data)))
            {
                var type = r.ReadByte();
                switch (type)
                {
                    case MsgTableAck:
                        var matched = r.ReadInt32();
                        var total = r.ReadInt32();
                        Log($"host: table ack, client matched {matched}/{total}");
                        _tableAcked = true;
                        StartHostBattle();
                        break;
                    case MsgInput:
                        _inSeq = r.ReadInt32();
                        _inFlags = r.ReadUInt32();
                        _inLook = new Vec2(r.ReadSingle(), r.ReadSingle());
                        break;
                }
            }
        }

        private void StartHostBattle()
        {
            RequestFinish(() =>
            {
                _running = true;
                Log("host: deployment finished (from application tick), sync running");
                var go = new MemoryStream();
                go.WriteByte(MsgGo);
                Send(go.ToArray(), DeliveryMode.ReliableOrdered);
            });
        }

        private void ApplyInputToControlled()
        {
            var a = _controlled;
            if (a == null || !a.IsActive())
                return;
            a.MovementFlags = (Agent.MovementControlFlag)_inFlags;
            if (_inLook.LengthSquared > 0.0001f)
                a.LookDirection = new Vec3(_inLook.x, _inLook.y, 0f).NormalizedCopy();
        }

        private const int AgentsPerChunk = 25; // 25 agents x 36 bytes + header stays under LiteNetLib's 1023-byte unreliable limit

        private void SendSnapshot()
        {
            var agents = new List<Agent>();
            foreach (var a in Mission.Agents)
                if (a.IsHuman && a.Team != null && a.IsActive())
                    agents.Add(a);
            for (var start = 0; start < agents.Count; start += AgentsPerChunk)
            {
                var n = Math.Min(AgentsPerChunk, agents.Count - start);
                using (var ms = new MemoryStream(1024))
                using (var w = new BinaryWriter(ms))
                {
                    w.Write(MsgSnapshot);
                    w.Write(_snapshotSeq.Next()); // one number per chunk: a late chunk is dropped by the receiver's SequenceGate
                    w.Write(_inSeq);
                    w.Write((ushort)n);
                    for (var k = 0; k < n; k++)
                    {
                        var a = agents[start + k];
                        w.Write((ushort)a.Index);
                        var p = a.Position;
                        w.Write(p.x);
                        w.Write(p.y);
                        w.Write(p.z);
                        var l = a.LookDirection;
                        w.Write(l.x);
                        w.Write(l.y);
                        w.Write(a.Health);
                        w.Write((uint)a.MovementFlags);
                        w.Write(a.GetCurrentAction(0).Index);
                        w.Write(a.GetCurrentAction(1).Index);
                    }

                    Trace("snapshot chunk built, agents=" + n + ", bytes=" + ms.Length);
                    Send(ms.ToArray(), _mode.Contains("reliable") ? DeliveryMode.ReliableOrdered : DeliveryMode.UnreliableSequenced);
                }
            }
        }

        public override void OnAgentHit(Agent affectedAgent, Agent affectorAgent, in MissionWeapon affectorWeapon, in Blow blow, in AttackCollisionData attackCollisionData)
        {
            if (!_host || !_running || !_linked || !_hostById.ContainsKey(affectedAgent.Index))
                return;
            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms))
            {
                w.Write(MsgHit);
                w.Write(affectedAgent.Index);
                w.Write(affectorAgent != null ? affectorAgent.Index : -1);
                w.Write(blow.InflictedDamage);
                w.Write(affectedAgent.Health);
            }

            Send(ms.ToArray(), DeliveryMode.ReliableOrdered);
            _hitsSent++;
        }

        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, AgentState agentState, KillingBlow blow)
        {
            if (!_host)
            {
                NoteClientRemoval(affectedAgent, agentState);
                return;
            }

            if (!_running || !_linked || !_hostById.ContainsKey(affectedAgent.Index))
                return;
            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms))
            {
                w.Write(MsgDeath);
                w.Write(affectedAgent.Index);
                w.Write((int)agentState);
                w.Write(affectorAgent != null ? affectorAgent.Index : -1);
            }

            Send(ms.ToArray(), DeliveryMode.ReliableOrdered);
            _deathsSent++;
        }

        // ---------- client ----------

        private void ClientTick(float dt)
        {
            if (!_linked)
                return;
            if (!_tableReceived)
            {
                _sinceHello += dt;
                if (_sinceHello > 1f)
                {
                    _sinceHello = 0f;
                    var hello = new MemoryStream();
                    hello.WriteByte(MsgHello);
                    Send(hello.ToArray(), DeliveryMode.ReliableOrdered);
                }

                return;
            }

            if (!_running || _mode.Contains("idle"))
                return;

            Trace("client frame start");
            if (!_mode.Contains("nopuppetpause"))
            {
                // Pausing the AI of every agent on every frame stalls the engine for seconds (research 0012): pause each agent once.
                foreach (var a in Mission.Agents)
                    if (a.IsActive() && a.IsHuman && _paused.Add(a.Index))
                        a.SetIsAIPaused(true);
            }

            Trace("client puppets paused");
            _traceFrames++;

            _sinceSend += dt;
            if (_sinceSend >= InputInterval)
            {
                _sinceSend = 0f;
                SendInput();
            }
        }

        private bool _tableReceived;
        private readonly HashSet<int> _paused = new HashSet<int>();

        private void OnClientData(byte[] data)
        {
            _rxBytes += data.Length;
            _rxPackets++;
            using (var r = new BinaryReader(new MemoryStream(data)))
            {
                var type = r.ReadByte();
                switch (type)
                {
                    case MsgTable:
                        BuildTable(r);
                        break;
                    case MsgGo:
                        RequestFinish(() =>
                        {
                            _running = true;
                            Log("client: Go received, deployment finished (from application tick), puppet mode on");
                        });
                        break;
                    case MsgSnapshot:
                        ApplySnapshot(r);
                        break;
                    case MsgHit:
                        ApplyHit(r);
                        break;
                    case MsgDeath:
                        ApplyDeath(r);
                        break;
                    case MsgStats:
                        ApplyHostStats(r);
                        break;
                    case MsgSiege:
                        SiegeApply(r);
                        break;
                }
            }
        }

        private void BuildTable(BinaryReader r)
        {
            var count = r.ReadInt32();
            var used = new HashSet<int>();
            var matched = 0;
            var maxDist = 0f;
            var entries = new List<Tuple<int, string, int, Vec3>>();
            for (var i = 0; i < count; i++)
            {
                var idx = r.ReadInt32();
                var id = r.ReadString();
                var side = r.ReadInt32();
                var pos = new Vec3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                entries.Add(Tuple.Create(idx, id, side, pos));
            }

            _controlledHostIndex = r.ReadInt32();
            SiegeReadTable(r);
            foreach (var e in entries)
            {
                Agent? best = null;
                var bestD = float.MaxValue;
                foreach (var a in Mission.Agents)
                {
                    if (!a.IsHuman || a.Team == null || a.Character == null || used.Contains(a.Index))
                        continue;
                    if (a.Character.StringId != e.Item2 || (int)a.Team.Side != e.Item3)
                        continue;
                    var d = a.Position.Distance(e.Item4);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = a;
                    }
                }

                if (best != null)
                {
                    used.Add(best.Index);
                    _table[e.Item1] = best;
                    matched++;
                    if (bestD > maxDist)
                        maxDist = bestD;
                }
            }

            _controlled = _table.TryGetValue(_controlledHostIndex, out var c) ? c : null;
            _tableReceived = true;
            Log($"client: translation table built, matched {matched}/{count} agents (max spawn distance {maxDist:F2} m), controlled host index {_controlledHostIndex}");
            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms))
            {
                w.Write(MsgTableAck);
                w.Write(matched);
                w.Write(count);
            }

            Send(ms.ToArray(), DeliveryMode.ReliableOrdered);
        }

        private static readonly ConstructorInfo? ActionCtor = typeof(ActionIndexCache).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(int) }, null);

        private static ActionIndexCache MakeAction(int index) =>
            ActionCtor != null ? (ActionIndexCache)ActionCtor.Invoke(new object[] { index }) : ActionIndexCache.act_none;

        private readonly Stopwatch _applyWatch = new Stopwatch();
        private double _applyMs;
        private int _applyCalls;

        private void ApplySnapshot(BinaryReader r)
        {
            _applyWatch.Restart();
            ApplySnapshotCore(r);
            _applyMs += _applyWatch.Elapsed.TotalMilliseconds;
            _applyCalls++;
        }

        private void ApplySnapshotCore(BinaryReader r)
        {
            var seq = r.ReadUInt32();
            var ack = r.ReadInt32();
            var count = r.ReadUInt16();
            if (!_snapshotGate.Accept(seq))
                return; // an older snapshot chunk that overtook a newer one (G3 condition: out-of-order snapshots)
            var now = NowMs;
            if (ack > _lastAckSeq && _inputSentAt.TryGetValue(ack, out var sentAt))
            {
                _ackLatencies.Add(now - sentAt);
                _lastAckSeq = ack;
            }

            for (var i = 0; i < count; i++)
            {
                var idx = r.ReadUInt16();
                var pos = new Vec3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                var look = new Vec3(r.ReadSingle(), r.ReadSingle(), 0f);
                var health = r.ReadSingle();
                var flags = r.ReadUInt32();
                var act0 = r.ReadInt32();
                var act1 = r.ReadInt32();
                if (!_table.TryGetValue(idx, out var a) || !a.IsActive())
                    continue;
                if (a == _controlled)
                    ObserveControlled(pos, now);
                if (a != _controlled)
                    NotePositionError(a.Position.Distance(pos));
                if (a.Position.Distance(pos) > 0.25f)
                    a.TeleportToPosition(pos);
                if (look.LengthSquared > 0.0001f)
                    a.LookDirection = look.NormalizedCopy();
                a.MovementFlags = (Agent.MovementControlFlag)flags;
                a.Health = health;
                if (act0 >= 0 && a.GetCurrentAction(0).Index != act0)
                    a.SetActionChannel(0, MakeAction(act0), true);
                if (act1 >= 0 && a.GetCurrentAction(1).Index != act1)
                    a.SetActionChannel(1, MakeAction(act1), true);
            }

            _snapshotsApplied++;
        }

        private void ApplyHit(BinaryReader r)
        {
            var victim = r.ReadInt32();
            r.ReadInt32();
            r.ReadInt32();
            var health = r.ReadSingle();
            if (_table.TryGetValue(victim, out var a) && a.IsActive())
                a.Health = health;
            _hitsApplied++;
        }

        private void ApplyDeath(BinaryReader r)
        {
            var victim = r.ReadInt32();
            var state = (AgentState)r.ReadInt32();
            var attacker = r.ReadInt32();
            if (_table.TryGetValue(victim, out var a) && a.IsActive())
            {
                // Die() with a real blow so the client's own mission logic (morale, scoring) gets a valid killer instead of throwing.
                var killer = _table.TryGetValue(attacker, out var k) ? k : a;
                var blow = new Blow(killer.Index) { InflictedDamage = (int)a.Health + 1, DamageType = DamageTypes.Cut };
                _applyingHostDeath = true;
                try
                {
                    a.Die(blow, state == AgentState.Unconscious ? Agent.KillInfo.Gravity : Agent.KillInfo.Invalid);
                }
                finally
                {
                    _applyingHostDeath = false;
                }
            }

            _deathsApplied++;
        }

        private void SendInput()
        {
            uint flags = 0;
            var look = new Vec2(1f, 0f);
            var t = _runningTime;
            if (_controlled != null && _controlled.IsActive())
            {
                if (t > 3f && InputScriptBackward(t))
                    flags |= (uint)Agent.MovementControlFlag.Backward;
                if (t > 3f && InputScriptForward(t))
                {
                    flags |= (uint)Agent.MovementControlFlag.Forward;
                    var target = NearestEnemy();
                    if (target != null)
                    {
                        var d = target.Position.AsVec2 - _controlled.Position.AsVec2;
                        if (d.LengthSquared > 0.01f)
                            look = d.Normalized();
                    }

                    if ((int)(t * 2f) % 4 == 0)
                        flags |= (uint)Agent.MovementControlFlag.AttackUp;
                }
            }

            NoteInputEdge((flags & (uint)(Agent.MovementControlFlag.Forward | Agent.MovementControlFlag.Backward)) != 0);
            var seq = ++_inputSeq;
            _inputSentAt[seq] = NowMs;
            _inputSentAt.Remove(seq - 300);
            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms))
            {
                w.Write(MsgInput);
                w.Write(seq);
                w.Write(flags);
                w.Write(look.x);
                w.Write(look.y);
            }

            Trace("input built seq=" + seq);
            Send(ms.ToArray(), _mode.Contains("reliable") ? DeliveryMode.ReliableOrdered : DeliveryMode.Unreliable);
            Trace("input sent");
        }

        private Agent? NearestEnemy()
        {
            Agent? best = null;
            var bestD = float.MaxValue;
            foreach (var a in Mission.Agents)
            {
                if (!a.IsActive() || !a.IsHuman || a.Team == null || _controlled == null || a.Team == _controlled.Team)
                    continue;
                var d = a.Position.DistanceSquared(_controlled.Position);
                if (d < bestD)
                {
                    bestD = d;
                    best = a;
                }
            }

            return best;
        }

        protected override void OnEndMission()
        {
            try
            {
                var lat = Percentiles(_ackLatencies);
                Log($"summary role={(_host ? "host" : "client")} baselineFps={(_baselineWindows > 0 ? _baselineFpsSum / _baselineWindows : 0):F1} effectLatencies=[{string.Join(",", _effectLatencies)}] ms stopLatencies=[{string.Join(",", _stopLatencies)}] ms");
                EffectSummaryLog("final");
                _tsv.Dispose();
                (_server as IDisposable)?.Dispose();
                (_client as IDisposable)?.Dispose();
            }
            catch
            {
                // never throw from shutdown
            }
        }
    }
}
