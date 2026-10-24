using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using BannerCoopTW.Core;
using BannerCoopTW.Core.Logging;
using BannerCoopTW.Core.Spikes;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
using TaleWorlds.SaveSystem;

namespace BannerCoopTW.Spikes
{
    /// <summary>
    /// Phase 2b spike S1 (ADR-0006 / ADR-0010 §2): MBGUID parity dump, world-checksum candidates, save timings.
    /// Debug-only; enabled by <c>SpikesEnabled=true</c>. Output goes to the mod log folder.
    /// </summary>
    public static class SpikeS1
    {
        private static ILog? Log => SubModule.Log;

        public static string OutDir
        {
            get
            {
                var docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord");
                var dir = Path.Combine(docs, "Logs", ModInfo.Id, "spikes");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private static int Pid => Process.GetCurrentProcess().Id;

        /// <summary>One TSV row per registered object: type, index in registry order, StringId, MBGUID, SubId.</summary>
        public static string DumpGuids()
        {
            var path = Path.Combine(OutDir, $"guid-dump-{Pid}.tsv");
            var sw = Stopwatch.StartNew();
            var rows = 0;
            using (var w = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                w.WriteLine("type\tindex\tstringId\tguid\tsubId");
                rows += Write(w, "Hero", Hero.AllAliveHeroes);
                rows += Write(w, "HeroDead", Hero.DeadOrDisabledHeroes);
                rows += Write(w, "CharacterObject", CharacterObject.All);
                rows += Write(w, "ItemObject", MBObjectManager.Instance.GetObjectTypeList<ItemObject>());
                rows += Write(w, "ItemModifier", MBObjectManager.Instance.GetObjectTypeList<ItemModifier>());
                rows += Write(w, "MobileParty", MobileParty.All);
                rows += Write(w, "Settlement", Settlement.All);
                rows += Write(w, "Clan", Clan.All);
                rows += Write(w, "Kingdom", Kingdom.All);
            }

            var msg = $"dump_guids: {rows} rows in {sw.ElapsedMilliseconds} ms -> {path}";
            Log?.Info(msg);
            return msg;
        }

        private static int Write<T>(StreamWriter w, string type, IEnumerable<T> items) where T : MBObjectBase
        {
            var i = 0;
            foreach (var o in items)
            {
                w.Write(type); w.Write('\t');
                w.Write(i++); w.Write('\t');
                w.Write(o.StringId); w.Write('\t');
                w.Write(o.Id.InternalValue); w.Write('\t');
                w.WriteLine(o.Id.SubId);
            }

            return i;
        }

        /// <summary>
        /// Three checksum candidates. A: registry order + GUIDs. B: A + rosters of all parties + hero battle equipment.
        /// C: B + hero gold + party inventories. Prints value + compute time of each.
        /// </summary>
        public static string WorldHash()
        {
            var sb = new StringBuilder();
            var sw = Stopwatch.StartNew();
            var a = new WorldHasher();
            HashRegistry(a);
            var tA = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var b = new WorldHasher();
            HashRegistry(b);
            HashRosters(b);
            HashHeroEquipment(b);
            var tB = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var c = new WorldHasher();
            HashRegistry(c);
            HashRosters(c);
            HashHeroEquipment(c);
            HashEconomy(c);
            var tC = sw.Elapsed.TotalMilliseconds;

            sb.AppendLine($"world_hash pid={Pid} day={CampaignTime.Now.ToDays:F2}");
            sb.AppendLine($"  A registry : {a.ToHex()}  items={a.Count}  {tA:F1} ms");
            sb.AppendLine($"  B +rosters : {b.ToHex()}  items={b.Count}  {tB:F1} ms");
            sb.AppendLine($"  C +economy : {c.ToHex()}  items={c.Count}  {tC:F1} ms");
            var text = sb.ToString();
            Log?.Info(text);
            File.AppendAllText(Path.Combine(OutDir, $"world-hash-{Pid}.txt"), text);
            return text;
        }

        private static void HashRegistry(WorldHasher h)
        {
            Add(h, Hero.AllAliveHeroes);
            Add(h, Hero.DeadOrDisabledHeroes);
            Add(h, CharacterObject.All);
            Add(h, MBObjectManager.Instance.GetObjectTypeList<ItemObject>());
            Add(h, MobileParty.All);
            Add(h, Settlement.All);
            Add(h, Clan.All);
            Add(h, Kingdom.All);
        }

        private static void Add<T>(WorldHasher h, IEnumerable<T> items) where T : MBObjectBase
        {
            foreach (var o in items)
                h.Add(o.StringId).Add(o.Id.InternalValue);
        }

        private static void HashRosters(WorldHasher h)
        {
            foreach (var party in MobileParty.All)
            {
                h.Add(party.StringId);
                var roster = party.MemberRoster;
                for (var i = 0; i < roster.Count; i++)
                {
                    var e = roster.GetElementCopyAtIndex(i);
                    h.Add(e.Character.StringId).Add(e.Number).Add(e.WoundedNumber);
                }
            }
        }

        private static void HashHeroEquipment(WorldHasher h)
        {
            foreach (var hero in Hero.AllAliveHeroes)
            {
                h.Add(hero.StringId);
                var eq = hero.BattleEquipment;
                for (var slot = EquipmentIndex.WeaponItemBeginSlot; slot < EquipmentIndex.NumEquipmentSetSlots; slot++)
                {
                    var el = eq[slot];
                    h.Add(el.Item?.StringId).Add(el.ItemModifier?.StringId);
                }
            }
        }

        private static void HashEconomy(WorldHasher h)
        {
            foreach (var hero in Hero.AllAliveHeroes)
                h.Add(hero.StringId).Add(hero.Gold);
            foreach (var party in MobileParty.All)
            {
                var inv = party.ItemRoster;
                for (var i = 0; i < inv.Count; i++)
                {
                    var e = inv.GetElementCopyAtIndex(i);
                    h.Add(e.EquipmentElement.Item?.StringId).Add(e.Amount);
                }
            }
        }

        /// <summary>
        /// Captures the running campaign into an InMemDriver and measures it; then pushes the bytes through a
        /// loopback TCP socket (transfer lower bound; not a real network). Load time is measured separately by
        /// <see cref="LoadTimer"/> (cold load of a save by name).
        /// </summary>
        public static string SaveTiming()
        {
            if (Game.Current == null)
                return "save_timing: no running game";

            var driver = new InMemDriver();
            var meta = new MetaData();
            var done = false;
            var sw = Stopwatch.StartNew();
            Game.Current.Save(meta, "coop-spike", driver, _ => done = true);
            var captureMs = sw.Elapsed.TotalMilliseconds;

            var data = (byte[]?)typeof(InMemDriver).GetField("_data", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(driver);
            var size = data?.Length ?? -1;
            var transferMs = data == null ? -1 : LoopbackTransfer(data);

            var text = $"save_timing pid={Pid} capture={captureMs:F0} ms (callbackDone={done}) bytes={size} loopbackTransfer={transferMs:F0} ms";
            Log?.Info(text);
            File.AppendAllText(Path.Combine(OutDir, $"save-timing-{Pid}.txt"), text + Environment.NewLine);
            return text;
        }

        private static double LoopbackTransfer(byte[] data)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var received = 0;
            var sw = Stopwatch.StartNew();
            var reader = new System.Threading.Thread(() =>
            {
                using (var c = listener.AcceptTcpClient())
                {
                    var buf = new byte[81920];
                    int n;
                    var s = c.GetStream();
                    while ((n = s.Read(buf, 0, buf.Length)) > 0)
                        received += n;
                }
            });
            reader.Start();
            using (var client = new TcpClient())
            {
                client.Connect(IPAddress.Loopback, port);
                var s = client.GetStream();
                s.Write(data, 0, data.Length);
            }

            reader.Join();
            listener.Stop();
            return sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>Loads a save by name via the stock path and logs timing; call <see cref="LoadTimer.Finish"/> at campaign ready.</summary>
        public static string LoadSave(string name)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var result = MBSaveLoad.LoadSaveGameData(name);
                if (result == null)
                    return $"load_save: '{name}' failed to read";
                LoadTimer.Start(name, sw.Elapsed.TotalMilliseconds);
                MBGameManager.StartNewGame(new SandBox.SandBoxGameManager(result));
                return $"load_save: '{name}' started (read {sw.ElapsedMilliseconds} ms)";
            }
            catch (Exception e)
            {
                Log?.Error("load_save failed", e);
                return "load_save failed: " + e.Message;
            }
        }
    }

    public static class LoadTimer
    {
        private static Stopwatch? _sw;
        private static string _name = "";
        private static double _readMs;

        public static void Start(string name, double readMs)
        {
            _name = name;
            _readMs = readMs;
            _sw = Stopwatch.StartNew();
        }

        public static void Finish()
        {
            if (_sw == null)
                return;
            var text = $"load pid={Process.GetCurrentProcess().Id} save='{_name}' readFile={_readMs:F0} ms startToReady={_sw.Elapsed.TotalMilliseconds:F0} ms";
            SubModule.Log?.Info(text);
            File.AppendAllText(Path.Combine(SpikeS1.OutDir, $"load-timing-{Process.GetCurrentProcess().Id}.txt"), text + Environment.NewLine);
            _sw = null;
        }
    }

    public static class SpikeCommands
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("dump_guids", "coop")]
        public static string DumpGuids(List<string> args) => Guard(SpikeS1.DumpGuids);

        [CommandLineFunctionality.CommandLineArgumentFunction("world_hash", "coop")]
        public static string WorldHash(List<string> args) => Guard(SpikeS1.WorldHash);

        [CommandLineFunctionality.CommandLineArgumentFunction("save_timing", "coop")]
        public static string SaveTiming(List<string> args) => Guard(SpikeS1.SaveTiming);

        [CommandLineFunctionality.CommandLineArgumentFunction("load_save", "coop")]
        public static string LoadSave(List<string> args) =>
            args.Count == 0 ? "usage: coop.load_save <saveName>" : Guard(() => SpikeS1.LoadSave(args[0]));

        public static string Run(string name)
        {
            switch (name.Trim())
            {
                case "dump_guids": return Guard(SpikeS1.DumpGuids);
                case "world_hash": return Guard(SpikeS1.WorldHash);
                case "save_timing": return Guard(SpikeS1.SaveTiming);
                default: return "unknown spike command: " + name;
            }
        }

        private static string Guard(Func<string> f)
        {
            if (!SubModule.SpikesEnabled)
                return "spikes disabled (SpikesEnabled=false in config.ini)";
            try
            {
                return f();
            }
            catch (Exception e)
            {
                SubModule.Log?.Error("spike command failed", e);
                return "failed: " + e.Message;
            }
        }
    }
}
