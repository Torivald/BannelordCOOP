using System;
using BannerCoopTW.Core.Logging;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace BannerCoopTW.Spikes
{
    /// <summary>Research 0012 (crafted item that disappears after save → load): where is every crafted_item_* referenced in the live session?</summary>
    public static class SpikeCrafted
    {
        public static string Report(string tag)
        {
            var sb = new StringBuilder();
            sb.AppendLine("crafted report [" + tag + "] pid " + System.Diagnostics.Process.GetCurrentProcess().Id);
            var crafted = MBObjectManager.Instance.GetObjectTypeList<ItemObject>().Where(i => i.StringId.StartsWith("crafted_item_")).ToList();
            sb.AppendLine("registered crafted items: " + string.Join(", ", crafted.Select(i => i.StringId + "('" + i.Name + "', crafted=" + i.IsCraftedByPlayer + ")")));

            var behavior = Campaign.Current.GetCampaignBehavior<CraftingCampaignBehavior>();
            var field = typeof(CraftingCampaignBehavior).GetField("_craftedItemDictionary", BindingFlags.NonPublic | BindingFlags.Instance);
            var dict = field?.GetValue(behavior) as IDictionary;
            sb.AppendLine("CraftingCampaignBehavior._craftedItemDictionary entries: " + (dict == null ? "n/a" : dict.Count.ToString()));
            var count = typeof(CraftingCampaignBehavior).GetField("_craftedItemCount", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(behavior);
            sb.AppendLine("_craftedItemCount: " + count);
            var history = typeof(CraftingCampaignBehavior).GetField("_cratingItemsHistory", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(behavior) as IEnumerable;
            if (history != null)
            {
                var ids = new List<string>();
                foreach (var h in history)
                    ids.Add((h as ItemObject)?.StringId ?? "?");
                sb.AppendLine("_cratingItemsHistory: " + string.Join(", ", ids));
            }

            foreach (var item in crafted)
            {
                var where = new List<string>();
                foreach (var p in MobileParty.All)
                    Scan(p.ItemRoster, item, "party:" + p.StringId, where);
                foreach (var s in Settlement.All)
                {
                    if (s.Stash != null)
                        Scan(s.Stash, item, "stash:" + s.StringId, where);
                }

                foreach (var h in Hero.AllAliveHeroes.Concat(Hero.DeadOrDisabledHeroes))
                {
                    foreach (var eq in new[] { h.BattleEquipment, h.CivilianEquipment })
                    {
                        if (eq == null)
                            continue;
                        for (var i = 0; i < 12; i++)
                            if (eq[i].Item == item)
                                where.Add("hero:" + h.StringId + "#" + i);
                    }
                }

                sb.AppendLine(item.StringId + " referenced by: " + (where.Count == 0 ? "NOTHING" : string.Join("; ", where)));
            }

            var text = sb.ToString();
            SubModule.Log?.Info(text);
            File.AppendAllText(Path.Combine(SpikeS1.OutDir, "crafted-report.txt"), text + Environment.NewLine);
            return text;
        }

        private static void Scan(ItemRoster roster, ItemObject item, string label, List<string> where)
        {
            for (var i = 0; i < roster.Count; i++)
                if (roster[i].EquipmentElement.Item == item)
                    where.Add(label + " x" + roster[i].Amount);
        }
    }
}
