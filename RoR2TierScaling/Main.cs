using BepInEx;
using RoR2;
using R2API;
using R2API.Utils;
using System.Collections.Generic;
using System.Security.Permissions;
using static RoR2TierScaling.Core;
using System;
using System.Linq;
using HarmonyLib;

#pragma warning disable CS0618 // Type or member is obsolete
[assembly: SecurityPermission( SecurityAction.RequestMinimum, SkipVerification = true )]
#pragma warning restore CS0618 // Type or member is obsolete

namespace RoR2TierScaling
{
    [BepInPlugin("com.MagicGonads.RoR2TierScaling", "Tier Scaling", "1.0.0")]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod)]
    [BepInDependency(ItemAPI.PluginGUID)]
    [BepInDependency(LanguageAPI.PluginGUID)]
    public class Main : BaseUnityPlugin
    {

        public static Random random;
        public static int seed = 5042891;

        public void Awake()
        {
            random = new Random(seed);

            On.RoR2.ItemTierCatalog.Init += (orig) =>
            {
                foreach (var t in RoR2.ContentManagement.ContentManager._itemTierDefs)
                {
                    var i = ItemTierIndex(t);
                    if (!scaling.ContainsKey(i))
                        scaling[i] = scaling[ItemTier.NoTier];
                    if (i != ItemTier.NoTier)
                        tiers[i] = t;
                }
                orig.Invoke();
            };

            On.RoR2.ItemCatalog.SetItemDefs += (orig, items) =>
            {
                items = OnItemCatalogSetItemDefs(items);
                orig.Invoke(items);
            };

            On.RoR2.Items.ContagiousItemManager.Init += (orig) => 
            {
                OnContagiousItemManagerInit();
                orig.Invoke();
            };

            On.RoR2.ItemDisplayRuleSet.GenerateRuntimeValues += (orig,rules) =>
            {
                orig.Invoke(rules);
                OnGenerateRuntimeValues(rules);
            };

            On.RoR2.UI.LogBook.LogBookController.BuildPickupEntries += (orig,exps) =>
            {
                var entries = orig.Invoke(exps); 
                foreach (var entry in entries)
                    if (alternateTokens.ContainsKey(entry.nameToken) 
                        && delayedLanguage.TryGetValue(entry.nameToken, out var action))
                        action.Invoke();
                return entries;
            };

            On.RoR2.Inventory.GetItemCount_ItemDef += (orig, inv, item) =>
            {
                if (item == null) return orig.Invoke(inv,item);
                if (doOriginalItemCount) return orig.Invoke(inv,item);
                if (alternate.Contains(item)) return 0;
                return orig.Invoke(inv,item) + OnGetItemCount(inv, item);
            };

            On.RoR2.Inventory.RemoveItem_ItemDef_int += (orig, inv, item, amount) =>
            {
                if (!doOriginalItemCount && item != null 
                    && alternates.TryGetValue(item, out var aitems))
                {
                    doOriginalItemCount = true;
                    var items = aitems.Select(a => a.ItemDef).AddItem(item).Select(a => (a,GetScaling(item.tier,a.tier)))
                        .OrderByDescending(t => t.Item2).ToList();
                    double count = inv.GetItemCount(item);
                    double target = count - amount;
                    foreach (var (i,s) in items) {
                        double p = Math.Min(1,1/s);
                        double a = Math.Max(1,1/s);
                        double c;
                        while (count > target && (c=inv.GetItemCount(i)) > 0)
                        {
                            if (p <= random.NextDouble())
                            {
                                doOriginalItemCount = false;
                                return;
                            }
                            var d = (int)Math.Min(c,a);
                            orig.Invoke(inv,i,d);
                            count -= d*s;
                        }
                    }
                    doOriginalItemCount = false;
                }
                else orig.Invoke(inv, item, amount);
            };

        }

    }
}
