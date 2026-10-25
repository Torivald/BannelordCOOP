using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BannerCoopTW.Core.Logging;
using BannerCoopTW.Networking;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using BinaryReader = System.IO.BinaryReader;
using BinaryWriter = System.IO.BinaryWriter;

namespace BannerCoopTW.Spikes.A1
{
    /// <summary>
    /// A1 siege part: ladders and ranged siege weapons are addressed by (script type name, entity name), the same pair on both instances of one scene.
    /// The host sends state changes and weapon use; the client applies them through the public State setters and Agent.UseGameObject.
    /// </summary>
    public sealed partial class A1Behavior
    {
        private const byte MsgSiege = 9;

        private sealed class SiegeEntry
        {
            public string Key = string.Empty;
            public SiegeWeapon Weapon = null!;
            public int LastState = int.MinValue;
            public int LastUser = -2;
        }

        private readonly Dictionary<string, SiegeEntry> _siegeByKey = new Dictionary<string, SiegeEntry>();
        private int _siegeSent, _siegeApplied, _siegeDiverged, _siegeUses, _siegeWrites;
        private bool _siegeUseDone;

        private static string SiegeKey(SiegeWeapon w) => w.GetType().Name + "|" + (w.GameEntity != null ? w.GameEntity.Name : "?");

        private static int SiegeState(SiegeWeapon w)
        {
            switch (w)
            {
                case SiegeLadder l:
                    return (int)l.State;
                case RangedSiegeWeapon r:
                    return (int)r.State;
                default:
                    return -1;
            }
        }

        private void SiegeCollect()
        {
            _siegeByKey.Clear();
            foreach (var mo in Mission.MissionObjects)
            {
                var w = mo as SiegeWeapon;
                if (w == null || !(w is SiegeLadder || w is RangedSiegeWeapon))
                    continue;
                var key = SiegeKey(w);
                if (_siegeByKey.ContainsKey(key))
                    key += "#" + _siegeByKey.Count; // duplicate entity names: disambiguate by scene order (same on both sides, unverified)
                _siegeByKey[key] = new SiegeEntry { Key = key, Weapon = w };
            }
        }

        private void SiegeWriteTable(BinaryWriter w)
        {
            SiegeCollect();
            w.Write(_siegeByKey.Count);
            foreach (var e in _siegeByKey.Values)
            {
                w.Write(e.Key);
                w.Write(SiegeState(e.Weapon));
            }

            Log($"siege objects on host: {_siegeByKey.Count} ({string.Join(", ", _siegeByKey.Keys.Take(6))}{(_siegeByKey.Count > 6 ? ", ..." : string.Empty)})");
        }

        private void SiegeReadTable(BinaryReader r)
        {
            var count = r.ReadInt32();
            var hostKeys = new List<string>();
            for (var i = 0; i < count; i++)
            {
                hostKeys.Add(r.ReadString());
                r.ReadInt32();
            }

            SiegeCollect();
            var matched = hostKeys.Count(k => _siegeByKey.ContainsKey(k));
            Log($"siege objects: host {count}, client {_siegeByKey.Count}, matched by (type, entity name) {matched}");
            SiegePuppetize();
        }

        /// <summary>
        /// Client puppet mode: the object's own OnTick/OnTickParallel (state machine, AI usage, animation) is switched off through the public
        /// <c>SetScriptComponentToTick(None)</c>; only host events move the state afterwards. Mode <c>nopuppetsiege</c> leaves the local logic running (old behaviour).
        /// </summary>
        private void SiegePuppetize()
        {
            if (_mode.Contains("nopuppetsiege"))
            {
                Log("siege puppet mode OFF (nopuppetsiege): local siege logic keeps running");
                return;
            }

            var off = 0;
            foreach (var e in _siegeByKey.Values)
            {
                try
                {
                    e.Weapon.SetScriptComponentToTick(TaleWorlds.Engine.ScriptComponentBehavior.TickRequirement.None);
                    off++;
                }
                catch (Exception ex)
                {
                    Log("siege puppetize failed for " + e.Key + ": " + ex.Message);
                }
            }

            Log($"siege puppet mode ON: local tick disabled on {off}/{_siegeByKey.Count} objects");
        }

        private static readonly System.Reflection.FieldInfo RangedStateField = typeof(RangedSiegeWeapon).GetField("_state", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        private void SiegeHostTick()
        {
            if (_siegeByKey.Count == 0 || !_linked)
                return;
            foreach (var e in _siegeByKey.Values)
            {
                var state = SiegeState(e.Weapon);
                var user = -1;
                var sp = e.Weapon.StandingPoints != null ? e.Weapon.StandingPoints.FirstOrDefault(p => p.HasUser) : null;
                if (sp != null && sp.UserAgent != null)
                    user = sp.UserAgent.Index;
                if (state == e.LastState && user == e.LastUser)
                    continue;
                e.LastState = state;
                e.LastUser = user;
                var ms = new MemoryStream();
                using (var w = new BinaryWriter(ms))
                {
                    w.Write(MsgSiege);
                    w.Write(e.Key);
                    w.Write(state);
                    w.Write(user);
                    w.Write(sp != null ? e.Weapon.StandingPoints.IndexOf(sp) : -1);
                }

                Send(ms.ToArray(), DeliveryMode.ReliableOrdered);
                _siegeSent++;
            }

            if (!_siegeUseDone && _runningTime > 6f && _controlled != null && _controlled.IsActive())
            {
                _siegeUseDone = true;
                SiegeUseDemo();
            }
        }

        /// <summary>Host: the client-controlled agent is placed at a ranged siege weapon and uses it; the use travels as a siege event.</summary>
        private void SiegeUseDemo()
        {
            var weapon = _siegeByKey.Values.Select(e => e.Weapon).OfType<RangedSiegeWeapon>().FirstOrDefault(w => !w.IsDeactivated && w.StandingPoints != null && w.StandingPoints.Count > 0);
            if (weapon == null)
            {
                Log("siege use demo: no usable ranged siege weapon in the scene");
                return;
            }

            var sp = weapon.StandingPoints[0];
            _controlled!.TeleportToPosition(sp.GameEntity.GlobalPosition);
            _controlled.UseGameObject(sp);
            _siegeUses++;
            Log($"siege use demo: controlled agent sent to {SiegeKey(weapon)}, HasUser={sp.HasUser}, weapon state {SiegeState(weapon)}");
        }

        private void SiegeApply(BinaryReader r)
        {
            var key = r.ReadString();
            var state = r.ReadInt32();
            var user = r.ReadInt32();
            var spIndex = r.ReadInt32();
            if (!_siegeByKey.TryGetValue(key, out var e))
            {
                Log("siege event for unknown object " + key);
                return;
            }

            var local = SiegeState(e.Weapon);
            if (e.LastState != int.MinValue && local != e.LastState)
                _siegeDiverged++; // the client's own machine logic moved the object away from the last host state
            if (local != state && !_mode.Contains("nostate"))
            {
                // logged before the write: a native crash leaves this line as the last one
                Log($"client: siege write {key} state {local} -> {state}{(!_mode.Contains("setter") ? " (raw)" : string.Empty)}");
                if (e.Weapon is SiegeLadder ladder)
                {
                    ladder.State = (SiegeLadder.LadderState)state;
                }
                else if (e.Weapon is RangedSiegeWeapon ranged && !_mode.Contains("noranged"))
                {
                    if (!_mode.Contains("setter") && RangedStateField != null)
                        RangedStateField.SetValue(ranged, (RangedSiegeWeapon.WeaponState)state); // only the value: in single player the setter runs the server branch and fires a projectile on the client (research 0012); mode 'setter' = old behaviour
                    else
                        ranged.State = (RangedSiegeWeapon.WeaponState)state;
                }

                _siegeWrites++;
            }

            e.LastState = state;
            if (_mode.Contains("nouse"))
            {
                _siegeApplied++;
                return;
            }

            if (user >= 0 && spIndex >= 0 && _table.TryGetValue(user, out var puppet) && puppet.IsActive() && e.Weapon.StandingPoints != null && spIndex < e.Weapon.StandingPoints.Count)
            {
                var sp = e.Weapon.StandingPoints[spIndex];
                puppet.TeleportToPosition(sp.GameEntity.GlobalPosition);
                puppet.UseGameObject(sp);
                _siegeUses++;
                Log($"client: puppet of host agent {user} now uses {key} (HasUser={sp.HasUser})");
            }

            _siegeApplied++;
        }
    }
}
