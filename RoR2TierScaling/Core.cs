using RoR2;
using R2API;
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
using static RoR2TierScaling.Main;
using static UnityEngine.UIElements.UIR.GradientSettingsAtlas;

#pragma warning disable Publicizer001 // Accessing a member that was not originally public
namespace RoR2TierScaling
{
    public static class Core
    {
        public static HashSet<ItemDef> alternate = new HashSet<ItemDef>();
        public static Dictionary<ItemTier, Color> tierColors = new Dictionary<ItemTier, Color>();
        public static Dictionary<string, ItemDef> alternateTokens = new Dictionary<string, ItemDef>();
        public static Dictionary<ItemTier, ItemTierDef> tiers = new Dictionary<ItemTier, ItemTierDef>();
        public static Dictionary<ItemDef, List<CustomItem>> alternates = new Dictionary<ItemDef, List<CustomItem>>();

        public static Dictionary<ItemTier,double> scaling = new Dictionary<ItemTier, double>()
        {
            { ItemTier.Tier1, 1 }, { ItemTier.VoidTier1, 2 },
            { ItemTier.Tier2, 3 }, { ItemTier.VoidTier2, 4 },
            { ItemTier.Boss, 5 }, { ItemTier.VoidBoss, 6 },
            { ItemTier.Lunar, 7 }, { ItemTier.NoTier, 8 },
            { ItemTier.Tier3, 15 }, { ItemTier.VoidTier3, 16 }
        };

        public static double GetScaling(ItemTier tier, ItemTier atier)
        {
            return scaling[atier] / scaling[tier];
        }

        public static ItemTier ItemTierIndex(ItemTierDef tier)
        {
            if (tier is null) return ItemTier.NoTier;
            ItemTier i;
#pragma warning disable CS0642 // Possible mistaken empty statement
            if (Enum.TryParse(tier.name.Substring(0,tier.name.Length-3), out i));
            else if (Enum.TryParse(tier.name.Substring(0,tier.name.Length-7), out i));
            else i = tier._tier;
#pragma warning restore CS0642 // Possible mistaken empty statement
            if (i == ItemTier.AssignedAtRuntime)
                i = ItemTier.NoTier;
            return i;
        }

        public static ItemDef[] OnItemCatalogSetItemDefs(ItemDef[] items)
        {
            var litems = items.ToList();
            foreach (var item in items)
            {
                var aitems = MakeAlternateItems(item)?.Where(i => ItemAPI.Add(i)).ToList();
                if (aitems != null)
                {
                    alternates.Add(item, aitems);
                    aitems.ForEach(i => alternate.Add(i.ItemDef));
                    litems.AddRange(aitems.Select(i => i.ItemDef));
                }
            }
            return litems.ToArray();
        }

        public static void OnGenerateRuntimeValues(ItemDisplayRuleSet rules)
        {
            var lassets = new List<ItemDisplayRuleSet.KeyAssetRuleGroup>();
            foreach (var g in rules.keyAssetRuleGroups)
                if (g.keyAsset is ItemDef item && alternates.TryGetValue(item, out var aitems))
                    foreach(var aitem in aitems)
                    {
                        lassets.Add(new ItemDisplayRuleSet.KeyAssetRuleGroup()
                            { keyAsset = aitem.ItemDef, displayRuleGroup = g.displayRuleGroup });
                    }
            rules.keyAssetRuleGroups = rules.keyAssetRuleGroups.AddRangeToArray(lassets.ToArray());
        }

        public static int OnGetItemCount(Inventory inv, ItemDef item)
        {
            double subcount = 0;   
            if (alternates.TryGetValue(item, out var aitems))
            {
                doOriginalItemCount = true;
                foreach (var aitem in aitems.Select(i => i.ItemDef))
                    subcount += inv.GetItemCount(aitem) * GetScaling(item.tier,aitem.tier);
                doOriginalItemCount = false;
            }    
            return (int)subcount + ((subcount % 1) > random.NextDouble() ? 1 : 0);    
        }

        public static bool doOriginalItemCount = false;

        public static string suffixA = "Alternate";
        public static string suffixB = "_ALTERNATE_";

        public static Color Border(Color color)
        {
            Color.RGBToHSV(color.NoAlpha(), out var h, out var s, out var v);
            return Color.HSVToRGB(h,s*1.2f,v*0.9f);
        }

        public static Texture2D Stain(Texture texture, Color stain)
        {
            Color? aura = null;
            return texture.Duplicate((x,y,c) => {
                if (aura is null) aura = Border(c);
                var a = aura.Value;
                var d = c-a;
                var m = d.r*d.r + d.g*d.g + d.b*d.b;
                var s = (float)Math.Abs(Math.Tanh(4*m));
                return Color.Lerp(stain,c,s).AlphaMultiplied(c.a);
            });
        }

        public static Dictionary<string,Action> delayedLanguage = new Dictionary<string,Action>();
        public static string extraDescription = " <style=cIsUtility>Scaled by {0}%</style>.";

        public static CustomItem MakeAlternateItem(ItemTierDef tier, ItemTier itier, ItemDef item, ItemDisplayRule[] rules = null)
        {
            var _suffixA = suffixA + itier.ToString();
            var _suffixB = suffixB + itier.ToString().ToUpper();
            var token = item.nameToken + _suffixB;

            var sprite = item.pickupIconSprite;
            var texture = sprite.texture.ToReadable();

            if (!tierColors.ContainsKey(item.tier))
                tierColors[item.tier] = Border(texture.GetPixel(0,0));

            var aitem = new CustomItem(
                item.name + _suffixA, token, item.descriptionToken + _suffixB,
                item.loreToken, item.pickupToken + _suffixB, item.pickupIconSprite, 
                item.pickupModelPrefab, item.tags, itier, item.hidden, 
                item.canRemove, item.unlockableDef, rules, tier);

            alternateTokens.Add(token,aitem.ItemDef);

            ItemCatalog.availability.CallWhenAvailable(() => {
                aitem.ItemDef.requiredExpansion = item.requiredExpansion;

                var color = tierColors[itier];
            texture = Stain(texture,color);
            sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.zero);
            aitem.ItemDef.pickupIconSprite = sprite;
            aitem.ItemDef.pickupModelPrefab = item.pickupModelPrefab;
            });

            delayedLanguage.Add(token,() => {
                delayedLanguage.Remove(aitem.ItemDef.nameToken);
                LanguageAPI.AddOverlay(aitem.ItemDef.nameToken, Language.GetString(item.nameToken) + $" {itier}");
                var extra = string.Format(extraDescription, (int)(10000*scaling[itier]/scaling[item.tier])/100f);
                LanguageAPI.AddOverlay(aitem.ItemDef.pickupToken, Language.GetString(item.pickupToken) + extra);
                LanguageAPI.AddOverlay(aitem.ItemDef.descriptionToken, Language.GetString(item.descriptionToken) + extra);
            });

            return aitem;
        }

        public static List<CustomItem> MakeAlternateItems(ItemDef item, ItemDisplayRule[] rules = null)
        {
            if (item.hidden) return null;
            if (item.tier == ItemTier.NoTier) return null;
            if (item.tier == ItemTier.AssignedAtRuntime) return null;
            if (!tiers.TryGetValue(item.tier, out var tier)) return null;

            var aitems = new List<CustomItem>();

            foreach(var t in tiers.Where(t => t.Value != tier))
            {
                var aitem = MakeAlternateItem(t.Value, t.Key, item, rules);
                if (aitem != null) aitems.Add(aitem);
            }

            return aitems;
        }

    }
}
#pragma warning restore Publicizer001 // Accessing a member that was not originally public