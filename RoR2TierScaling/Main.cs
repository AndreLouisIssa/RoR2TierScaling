using BepInEx;
using RoR2;
using R2API;
using R2API.Utils;
using System.Collections.Generic;
using System.Security.Permissions;
using static RoR2TierScaling.Core;
using System;
using System.Linq;

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
                var count = orig.Invoke(inv,item);
                if (doOriginalItemCount || item == null) return count;
                return count + OnGetItemCount(inv, item);
            };

            On.RoR2.Inventory.RemoveItem_ItemDef_int += (orig, inv, item, amount) =>
            {
                if (!doOriginalItemCount && item != null 
                    && alternates.TryGetValue(item, out var aitems))
                {
                    doOriginalItemCount = true;
                    int i, d;
                    foreach (var aitem in aitems.Select(a => a.ItemDef))
                        if ((i = inv.GetItemCount(item)) < amount 
                            && inv.GetItemCount(aitem) >= (d = amount - i))
                        {
                            orig.Invoke(inv, item, i);
                            orig.Invoke(inv, aitem, d);
                        }
                    doOriginalItemCount = false;
                }
                else orig.Invoke(inv, item, amount);
            };

        }

    }
}
