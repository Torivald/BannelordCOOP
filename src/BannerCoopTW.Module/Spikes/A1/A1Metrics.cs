using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using BannerCoopTW.Networking;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using BinaryReader = System.IO.BinaryReader;
using BinaryWriter = System.IO.BinaryWriter;

namespace BannerCoopTW.Spikes.A1
{
    /// <summary>
    /// A1b measurements (research 0013): host-client divergence counters, input to visible-effect latency samples, local-damage attempts on the client,
    /// stall diagnostics (poll time, packets per frame, GC) and LiteNetLib network emulation (modes <c>loss3</c>, <c>lat50-100</c>).
    /// </summary>
    public sealed partial class A1Behavior
    {
        // ---------- network emulation ----------

        private NetworkConditions ParseNetConditions()
        {
            var c = new NetworkConditions();
            var m = Regex.Match(_mode, @"loss(\d+)");
            if (m.Success)
                c.LossPercent = int.Parse(m.Groups[1].Value);
            m = Regex.Match(_mode, @"lat(\d+)-(\d+)");
            if (m.Success)
            {
                c.MinLatencyMs = int.Parse(m.Groups[1].Value);
                c.MaxLatencyMs = int.Parse(m.Groups[2].Value);
            }

            if (c.IsActive)
                Log($"network emulation ON ({(_host ? "host" : "client")} receive side, own decorator): packet loss {c.LossPercent}%, latency {c.MinLatencyMs}-{c.MaxLatencyMs} ms per direction");
            return c;
        }

        // ---------- divergence (host stats once per second, compared on the client) ----------

        private float _sinceStats;
        private bool _immortalSet;
        private bool _applyingHostDeath;
        private int _statSamples, _aliveMismatchSamples;
        private int _lastHostAlive, _lastClientAlive;
        private float _lastHostHp, _lastClientHp;
        private int _aliveDeltaMax;
        private double _hpDeltaMax, _hpDeltaSum;
        private readonly List<double> _posErr = new List<double>();
        private int _localBlows, _localBlowsTotal, _localDeaths, _localDeathsTotal;
        private double _localBlowDamage;

        private void HostStatsTick(float dt)
        {
            if (!_immortalSet && _controlSwitched && _controlled != null && _controlled.IsActive())
            {
                _immortalSet = true;
                _controlled.SetMortalityState(Agent.MortalityState.Immortal); // player heroes are immortal (ADR-0007); keeps the latency script alive
            }

            _sinceStats += dt;
            if (_sinceStats < 1f)
                return;
            _sinceStats = 0f;
            var alive = 0;
            var hp = 0f;
            foreach (var a in _hostById.Values)
            {
                if (!a.IsActive())
                    continue;
                alive++;
                hp += a.Health;
            }

            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms))
            {
                w.Write(MsgStats);
                w.Write(alive);
                w.Write(hp);
            }

            Send(ms.ToArray(), DeliveryMode.ReliableOrdered);
        }

        private void ApplyHostStats(BinaryReader r)
        {
            var hostAlive = r.ReadInt32();
            var hostHp = r.ReadSingle();
            var alive = 0;
            var hp = 0f;
            foreach (var a in _table.Values)
            {
                if (!a.IsActive())
                    continue;
                alive++;
                hp += a.Health;
            }

            _lastHostAlive = hostAlive;
            _lastClientAlive = alive;
            _lastHostHp = hostHp;
            _lastClientHp = hp;
            _statSamples++;
            var da = Math.Abs(hostAlive - alive);
            if (da != 0)
                _aliveMismatchSamples++;
            _aliveDeltaMax = Math.Max(_aliveDeltaMax, da);
            var dh = Math.Abs(hostHp - hp);
            _hpDeltaSum += dh;
            _hpDeltaMax = Math.Max(_hpDeltaMax, dh);
        }

        private void NotePositionError(double meters)
        {
            if (_posErr.Count < 20000)
                _posErr.Add(meters);
        }

        public override void OnRegisterBlow(Agent attacker, Agent victim, WeakGameEntity realHitEntity, Blow b, ref AttackCollisionData collisionData, in MissionWeapon attackerWeapon)
        {
            if ((_host && !_mode.Contains("countblows")) || (!_running && !_mode.Contains("solo")) || victim == null || !victim.IsHuman || b.InflictedDamage <= 0)
                return;
            _localBlows++;
            _localBlowsTotal++;
            _localBlowDamage += b.InflictedDamage;
        }

        private void NoteClientRemoval(Agent agent, AgentState state)
        {
            if (!_running || _applyingHostDeath || !agent.IsHuman)
                return;
            _localDeaths++;
            _localDeathsTotal++;
            var hostIdx = -1;
            foreach (var kv in _table)
                if (kv.Value == agent)
                    hostIdx = kv.Key;
            Log($"client: LOCAL removal (state {state}) of agent local#{agent.Index} (host#{hostIdx}, {agent.Character?.StringId}) health={agent.Health:F0} pos={agent.Position} team={agent.Team?.Side} mount={agent.MountAgent != null} stack={Environment.StackTrace.Replace(Environment.NewLine, " | ").Substring(0, 700)}");
        }

        private void DivergenceWindowLog()
        {
            if (_mode.Contains("fightdiag") || _mode.Contains("brawl"))
                FightDiagLog();
            if (_mode.Contains("countblows"))
                Log($"blows: counted by OnRegisterBlow so far {_localBlowsTotal} (damage {_localBlowDamage:F0})");
            if (!_running)
                return;
            if (_host)
            {
                Log($"gc: gen0/1/2 total={GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)} managed={GC.GetTotalMemory(false) / 1048576}MB");
                return;
            }

            var pe = Percentiles(_posErr);
            var meanHp = _statSamples > 0 ? _hpDeltaSum / _statSamples : 0.0;
            Log($"div: alive host/client={_lastHostAlive}/{_lastClientAlive} (|d| max {_aliveDeltaMax}, mismatching samples {_aliveMismatchSamples}/{_statSamples}) hp host/client={_lastHostHp:F0}/{_lastClientHp:F0} (|d| mean {meanHp:F1} max {_hpDeltaMax:F1}) " +
                $"posErr p50/p95/max={pe.Item1:F2}/{pe.Item2:F2}/{pe.Item3:F2}m n={_posErr.Count} localBlows={_localBlows} (total {_localBlowsTotal}, dmg {_localBlowDamage:F0}) localDeaths={_localDeaths} (total {_localDeathsTotal}) staleSnapshotChunksDropped={_snapshotGate.Dropped}");
            EffectSummaryLog("run");
            Log($"gc: gen0/1/2 total={GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)} managed={GC.GetTotalMemory(false) / 1048576}MB pollMax={_pollMaxMs:F1}ms rxPerFrameMax={_rxPerFrameMax} applyMax={_applyMaxMs:F2}ms");
            _statSamples = _aliveMismatchSamples = 0;
            _aliveDeltaMax = 0;
            _hpDeltaMax = _hpDeltaSum = 0;
            _posErr.Clear();
            _localBlows = _localDeaths = 0;
            _localBlowDamage = 0;
            _pollMaxMs = 0;
            _rxPerFrameMax = 0;
            _applyMaxMs = 0;
        }

        // ---------- keeping the fight going (mode 'brawl') ----------

        private float _sinceBrawl;
        private int _brawlOrders;

        /// <summary>
        /// Without it the Custom Battle goes quiet after ~90 s (about 25 hits, 1 death in 13 min). In mode 'brawl' every team gets a Charge order again every 10 s
        /// so that the death path keeps being exercised during a long run.
        /// </summary>
        private void BrawlTick(float dt, bool force = false)
        {
            if (!_mode.Contains("brawl") || (!force && !_chargeOrdered))
                return;
            _sinceBrawl += dt;
            if (_sinceBrawl < 10f)
                return;
            _sinceBrawl = 0f;
            try
            {
                foreach (var team in Mission.Teams)
                {
                    team.MasterOrderController.SelectAllFormations();
                    team.MasterOrderController.SetOrder(OrderType.Charge);
                }

                if (++_brawlOrders % 6 == 1)
                    Log($"brawl: Charge re-ordered for {Mission.Teams.Count} teams (#{_brawlOrders}), alive humans {Mission.Agents.Count}");
            }
            catch (Exception e)
            {
                Log("brawl order failed: " + e.Message);
                _brawlOrders = int.MaxValue / 2;
            }
        }

        private void FightDiagLog()
        {
            try
            {
                var perSide = new int[2];
                var minD = float.MaxValue;
                var meleeContacts = 0;
                var humans = new List<Agent>();
                foreach (var a in Mission.Agents)
                    if (a.IsHuman && a.IsActive() && a.Team != null)
                        humans.Add(a);
                foreach (var a in humans)
                {
                    perSide[(int)a.Team.Side == 0 ? 0 : 1]++;
                    if (a.Team.Side != BattleSideEnum.Attacker)
                        continue;
                    foreach (var b in humans)
                    {
                        if (b.Team == null || b.Team.Side == a.Team.Side)
                            continue;
                        var d = a.Position.Distance(b.Position);
                        if (d < minD)
                            minD = d;
                        if (d < 3f)
                            meleeContacts++;
                    }
                }

                Log($"fight: ended={Mission.MissionEnded} ending={Mission.IsMissionEnding} alive def/att={perSide[0]}/{perSide[1]} minEnemyDist={minD:F1}m pairsWithin3m={meleeContacts}");
            }
            catch (Exception e)
            {
                Log("fightdiag failed: " + e.Message);
            }
        }

        // ---------- poll / stall diagnostics ----------

        private double _pollMaxMs, _applyMaxMs;
        private double _lastPollMs;
        private int _rxPerFrameMax, _rxAtFrameStart;
        private long _gcAtLastFrame;

        private void NotePoll(double ms)
        {
            _lastPollMs = ms;
            if (ms > _pollMaxMs)
                _pollMaxMs = ms;
            var rx = _rxPackets - _rxAtFrameStart;
            if (rx > _rxPerFrameMax)
                _rxPerFrameMax = rx;
            _rxAtFrameStart = _rxPackets;
            if (_applyMs > 0 && _applyCalls > 0)
            {
                var avg = _applyMs / _applyCalls;
                if (avg > _applyMaxMs)
                    _applyMaxMs = avg;
            }
        }

        private void StallLog(double frameWallSeconds)
        {
            if (!_running)
                return;
            var gc = GC.CollectionCount(0) + GC.CollectionCount(1) * 1000L + GC.CollectionCount(2) * 1000000L;
            Log($"STALL frame={frameWallSeconds * 1000:F0}ms role={(_host ? "host" : "client")} lastPoll={_lastPollMs:F1}ms ownTickMax={_tickMsMax:F1}ms rxPerFrameMax={_rxPerFrameMax} applyAvgSoFar={(_applyCalls > 0 ? _applyMs / _applyCalls : 0):F2}ms gcGen0/1/2={GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)} gcChanged={gc != _gcAtLastFrame} managed={GC.GetTotalMemory(false) / 1048576}MB");
            _gcAtLastFrame = gc;
        }

        // ---------- input -> visible effect latency ----------

        // The scripted client alternates 2.0 s of Forward and 1.5 s of idle (after 3 s of running).
        private const float ForwardSeconds = 2.0f, IdleSeconds = 1.5f;
        private bool _lastForward;
        private long _startSentAt = -1, _stopSentAt = -1;
        private Vec3 _lastControlledPos;
        private bool _haveControlledPos;
        private readonly List<double> _stopLatencies = new List<double>();
        private readonly List<double> _effectFree = new List<double>(), _effectNear = new List<double>();

        /// <summary>Script: forward 2 s, idle 1.5 s, backward 2 s, idle 1.5 s (net displacement ~0, so the controlled agent stays out of the melee longer).</summary>
        private static bool InputScriptForward(float runningTime)
        {
            var phase = (runningTime - 3f) % (2f * (ForwardSeconds + IdleSeconds));
            return phase < ForwardSeconds;
        }

        private static bool InputScriptBackward(float runningTime)
        {
            var phase = (runningTime - 3f) % (2f * (ForwardSeconds + IdleSeconds)) - (ForwardSeconds + IdleSeconds);
            return phase >= 0f && phase < ForwardSeconds;
        }

        /// <summary>Called when an input packet is built: remembers when a start or stop edge was first sent.</summary>
        private void NoteInputEdge(bool forward)
        {
            // 'forward' means "any movement input" here (forward or backward)
            if (forward && !_lastForward)
            {
                _startSentAt = NowMs;
                _stopSentAt = -1;
            }
            else if (!forward && _lastForward)
            {
                _stopSentAt = NowMs;
                _startSentAt = -1;
            }

            _lastForward = forward;
        }

        /// <summary>Each applied snapshot: the first position change (> 2 cm) after a start edge is the visible effect; the first snapshot with no change (&lt; 1 cm) after a stop edge ends the stop latency.</summary>
        private void ObserveControlled(Vec3 pos, long now)
        {
            if (_haveControlledPos)
            {
                var moved = pos.Distance(_lastControlledPos);
                if (_startSentAt >= 0 && moved > 0.02f)
                {
                    var lat = now - _startSentAt;
                    _effectLatencies.Add(lat);
                    var enemy = NearestEnemy();
                    var far = enemy == null || _controlled == null || enemy.Position.Distance(_controlled.Position) > 15f;
                    (far ? _effectFree : _effectNear).Add(lat);
                    _startSentAt = -1;
                }
                else if (_stopSentAt >= 0 && moved < 0.01f)
                {
                    _stopLatencies.Add(now - _stopSentAt);
                    _stopSentAt = -1;
                }
            }

            _lastControlledPos = pos;
            _haveControlledPos = true;
        }

        private void EffectSummaryLog(string tag)
        {
            var e = Percentiles(_effectLatencies);
            var s = Percentiles(_stopLatencies);
            var f = Percentiles(_effectFree);
            var n = Percentiles(_effectNear);
            Log($"effect[{tag}]: start n={_effectLatencies.Count} p50/p95/max={e.Item1:F0}/{e.Item2:F0}/{e.Item3:F0}ms; stop n={_stopLatencies.Count} p50/p95/max={s.Item1:F0}/{s.Item2:F0}/{s.Item3:F0}ms; " +
                $"start-free(enemy>15m) n={_effectFree.Count} p50/p95/max={f.Item1:F0}/{f.Item2:F0}/{f.Item3:F0}ms; start-near n={_effectNear.Count} p50/p95/max={n.Item1:F0}/{n.Item2:F0}/{n.Item3:F0}ms");
        }
    }
}
