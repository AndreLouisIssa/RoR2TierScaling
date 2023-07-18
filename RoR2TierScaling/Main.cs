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
using BepInEx.Configuration;

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
        // TODO:
        // * fix ItemBehavior not correctly applying the stack count from GetItemCount
        // * fix ItemDisplayRules not applying
        // * test multiplayer
        //  - properly network the random values for item stacks / keep the iterator state consistent
        // * fix custom ColorCatalog entries
        // * fix tier assigment of alternates for mod added tiers
        // * fix custom colouring of mod added tiers
        // * add artifacts for lobby / run-level config overrides
        // * test with more diverse configs and mods
        // * refactor to distinguish aspects other mods should be able to tweak / depend on

        public static Random random;
        public static ConfigEntry<bool> configIconColours;
        public static ConfigEntry<bool> configVoidColours;
        public static ConfigEntry<bool> configShowLogbook;
        public static ConfigEntry<bool> configMakeAlternates;
        public static ConfigEntry<bool> configItemScaling;
        public static ConfigEntry<bool> configConsumableScaling;
        public static ConfigEntry<bool> configTieredContagion;
        public static ConfigEntry<bool> configInvertBlacklist;
        public static ConfigEntry<string> configItemBlacklist;
        public static ConfigEntry<bool> configLoadReduction;
        public static ConfigEntry<double> configLoadProportion;
        public static ConfigEntry<int> configLoadSeed;
        public static ConfigEntry<bool> configLoadRandomSeed;
        public static ConfigEntry<string> configLanguage;
        public static ConfigEntry<double> configDefaultTierScaling;
        public static Dictionary<ItemTier, ConfigEntry<double>> configTierScaling = new Dictionary<ItemTier, ConfigEntry<double>>();
        public static Dictionary<ItemTier, ConfigEntry<string>> configTierAliases = new Dictionary<ItemTier, ConfigEntry<string>>();

        public void Awake()
        {
            configShowLogbook = Config.Bind(
                "1. Feature Selection",
                "Show In Logbook",
                false,
                "Allows the scaled variants of the items to be shown in the logbook."
            );

            configTieredContagion = Config.Bind(
                "1. Feature Selection",
                "Tiered Void Corruption",
                true,
                "Makes void corruption transfer items within tiers instead of between them."
            );

            configIconColours = Config.Bind(
                "1. Feature Selection",
                "Reshaded Icon Colours",
                true,
                "Changes the scaled item's icon colours to match their new tier colour."
            );

            configVoidColours = Config.Bind(
                "1. Feature Selection",
                "Custom Void Colours",
                true,
                "Changes the void tiers' colours so you can tell them apart."
            );

            configMakeAlternates = Config.Bind(
                "1. Feature Selection",
                "Make Alternate Tiered Items",
                true,
                "Required for most of the mod to function, generates variants of each item for every other item tier."
            );

            configItemScaling = Config.Bind(
                "1. Feature Selection",
                "Item Scaling",
                true,
                "Based on tier, scales items so they have a chance to count as extra stacks of their original form."
            );

            configConsumableScaling = Config.Bind(
                "1. Feature Selection",
                "Consumable Scaling",
                true,
                "Based on tier, scales consumables so they have a chance to not be consumed, and scales how much is consumed at a time."
            );

            configInvertBlacklist = Config.Bind(
                "2. Item Exceptions",
                "Item Blacklist Is Whitelist",
                false,
                "Makes the item blacklist act as a whitelist instead, so only those items are included rather than excluded."
            );

            configItemBlacklist = Config.Bind(
                "2. Item Exceptions",
                "Item Blacklist",
                "",
                "Semicolon (';') separated tokens / codenames / names of items that should be excluded from tier scaling."
            );

            configLoadReduction = Config.Bind(
                "3. Load Reduction",
                "Load Reduction",
                false,
                "Reduce the number of items that get tier scaled to reduce the strain on the game"
            );

            configLoadProportion = Config.Bind(
                "3. Load Reduction",
                "Load Proportion",
                1d,
                "Proportion of items that get scaled to other tiers (between 0 and 1)."
            );

            configLoadSeed = Config.Bind(
                "3. Load Reduction",
                "Load Seed",
                100000,
                "Seed to use for randomly deciding which items to skip tier scaling for"
            );

            random = new Random(configLoadSeed.Value);

            configDefaultTierScaling = Config.Bind(
                "4. Scaling",
                "Default Tier Weight",
                0d,
                "Tier weight assigned to new modded tiers"
            );

            On.RoR2.ItemTierCatalog.Init += (orig) => {
                orig.Invoke();
                OnItemTierCatalogInit();
            };

            On.RoR2.ItemCatalog.SetItemDefs += (orig, items) => {

                var languages = string.Join(", ",Language.GetAllLanguages().Select(s=>s.name));
                configLanguage = Config.Bind(
                    "5. Language",
                    "Language For Names",
                    "en",
                    "The language used to check the names of items (must be the same for all users!). Valid language options: " + languages
                );

                foreach (var (i,t) in tiers.OrderBy(p => p.Key).Select(p => (p.Key, p.Value)))
                {
                    if (configTierScaling.ContainsKey(i)) continue;
                    if (!initialTierScaling.TryGetValue(i, out var w))
                        w = configDefaultTierScaling.Value;
                    if (!initialTierAliases.TryGetValue(i, out var a))
                        a = t.name;
                    var c = configTierAliases[i] = Config.Bind(
                        "5. Language",
                        "Tier Alias: " + t.name,
                        a,
                        "Custom name (must be the same for all users!) of the tier internally called " + t.name
                    );
                    configTierScaling[i] = Config.Bind(
                        "4. Scaling",
                        "Tier Weight: " + t.name,
                        w,
                        "Tier weight of the tier " + c.Value + ", set to 0 to disable scaling this tier"
                    );
                }

                if (configItemBlacklist.Value.Length > 0)
                    foreach (var p in configItemBlacklist.Value.Split(';').Select(s => s.Trim().ToLower()))
                        excludedItems.Add(p);

                items = OnItemCatalogSetItemDefs(items);
                orig.Invoke(items);
            };

            On.RoR2.Items.ContagiousItemManager.Init += (orig) => {
                if (configTieredContagion.Value)
                    OnContagiousItemManagerInit();
                orig.Invoke();
            };

            On.RoR2.ItemDisplayRuleSet.GenerateRuntimeValues += (orig,rules) => {
                if (configMakeAlternates.Value)
                    OnGenerateRuntimeValues(rules);
                orig.Invoke(rules);
            };

            On.RoR2.UI.LogBook.LogBookController.BuildPickupEntries += (orig,exps) =>
            {
                foreach (var action in delayedLanguage) action.Invoke();
                if (configShowLogbook.Value) return orig.Invoke(exps);
                return orig.Invoke(exps).Where(e => !alternateTokens.ContainsKey(e.nameToken)).ToArray();
            };

            On.RoR2.Inventory.GetItemCount_ItemDef += (orig, inv, item) => {
                if (!configItemScaling.Value || doOriginalItemCount || item == null)
                    return orig.Invoke(inv,item);
                if (alternate.Contains(item)) return 0;
                return orig.Invoke(inv,item) + OnGetItemCount(inv, item);
            };

            On.RoR2.ColorCatalog.GetColor += (orig, i) => {
                if (altColorCatalog.TryGetValue(i,out var color)) return color;
                return orig.Invoke(i);
            };

            On.RoR2.ColorCatalog.GetColorHexString += (orig, i) => {
                if (altColorCatalogHex.TryGetValue(i,out var color)) return color;
                return orig.Invoke(i);
            };

            On.RoR2.Run.Start += (orig,self) => {
                random = new Random((int)self.seed);
                orig.Invoke(self);
            };

            On.RoR2.Inventory.RemoveItem_ItemDef_int += (orig, inv, item, amount) => {
                if (configConsumableScaling.Value && !doOriginalItemCount && item != null 
                    && alternates.TryGetValue(item, out var aitems))
                {
                    doOriginalItemCount = true;
                    var items = aitems.AddItem(item).Select(a => 
                        (a,GetScaling(item.tier,a.tier))).OrderByDescending(t => t.Item2).ToList();
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
