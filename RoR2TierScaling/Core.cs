using HarmonyLib;
using R2API;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static RoR2TierScaling.Main;
using static RoR2.ColorCatalog;
using UnityEngine.UIElements;

#pragma warning disable Publicizer001 // Accessing a member that was not originally public
namespace RoR2TierScaling
{
    public static class Core
    {
        public static HashSet<ItemDef> alternate = new HashSet<ItemDef>();
        public static Dictionary<ItemTier, Color> tierColors = new Dictionary<ItemTier, Color>();
        public static Dictionary<ItemTier, Color> tierAltColors = new Dictionary<ItemTier, Color>();
        public static Dictionary<string, ItemDef> alternateTokens = new Dictionary<string, ItemDef>();
        public static Dictionary<ItemTier, ItemTierDef> tiers = new Dictionary<ItemTier, ItemTierDef>();
        public static Dictionary<ColorIndex, Color32> altColorCatalog = new Dictionary<ColorIndex, Color32>();
        public static Dictionary<ColorIndex, string> altColorCatalogHex = new Dictionary<ColorIndex, string>();
        public static Dictionary<ItemDef, List<CustomItem>> alternates = new Dictionary<ItemDef, List<CustomItem>>();

        public static double defaultTierScaling = 1/4;//0;
        public static Dictionary<ItemTier,double> tierScaling = new Dictionary<ItemTier, double>()//;
        {
            { ItemTier.Tier1, 1/1 }, { ItemTier.VoidTier1, 1/2 },
            { ItemTier.Tier2, 1/3 }, { ItemTier.VoidTier2, 1/4 },
            { ItemTier.Boss, 1/5 }, { ItemTier.VoidBoss, 1/6 },
            { ItemTier.Lunar, 1/7 }, { ItemTier.NoTier, 1/8 },
            { ItemTier.Tier3, 1/15 }, { ItemTier.VoidTier3, 1/16 }
        };

        public static double GetScaling(ItemTier tier)
        {
            if (!tierScaling.TryGetValue(tier,out var scaling))
                return tierScaling[tier] = defaultTierScaling;
            return scaling;
        }

        public static double? GetScaling(ItemTier tier, ItemTier atier)
        {
            var a = GetScaling(tier); var b = GetScaling(atier);
            if (a == 0 || b == 0) return null;
            return a / b;
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
                        lassets.Add(new ItemDisplayRuleSet.KeyAssetRuleGroup()
                            { keyAsset = aitem.ItemDef, displayRuleGroup = g.displayRuleGroup });
            rules.keyAssetRuleGroups = rules.keyAssetRuleGroups.AddRangeToArray(lassets.ToArray());
        }

        public static void OnContagiousItemManagerInit()
        {
            var key = DLC1Content.ItemRelationshipTypes.ContagiousItem;
            if (key is null || !ItemCatalog.itemRelationships.TryGetValue(key,out var _contagions)) return;
            var contagions = _contagions.Select(p => (p.itemDef1,p.itemDef2));
            var newContagions = new HashSet<ItemDef.Pair>{};
            var applicableItems = new HashSet<ItemDef>{};
            var oldContagees = new Dictionary<ItemTier, Dictionary<ItemDef, HashSet<ItemDef>>>{ };
            var oldContagers = new Dictionary<ItemTier, Dictionary<ItemDef, HashSet<ItemDef>>>{ };

            foreach (var (a,b) in contagions)
            {
                if (!oldContagees.TryGetValue(a.tier, out var contagees))
                    oldContagees[a.tier] = contagees = new Dictionary<ItemDef, HashSet<ItemDef>>{ };
                if (!contagees.TryGetValue(b, out var contagee))
                    contagees[b] = contagee = new HashSet<ItemDef>{ };
                contagee.Add(a);

                if (!oldContagers.TryGetValue(b.tier, out var contagers))
                    oldContagers[b.tier] = contagers = new Dictionary<ItemDef, HashSet<ItemDef>>{ };
                if (!contagers.TryGetValue(a, out var contager))
                    contagers[a] = contager = new HashSet<ItemDef>{ };
                contager.Add(b);

                applicableItems.Add(a);
                applicableItems.Add(b);
            }

            var alternatesByTier = new Dictionary<ItemTier, Dictionary<ItemDef, ItemDef>>();

            foreach (var i in applicableItems)
            {
                if (!alternates.TryGetValue(i, out var aitems)) break;
                var items = aitems.Select(a => a.ItemDef).AddItem(i);
                foreach (var a in items) foreach (var b in items)
                { 
                    if (!alternatesByTier.TryGetValue(a.tier,out var bitems))
                        alternatesByTier[a.tier] = bitems = new Dictionary<ItemDef, ItemDef>();
                    bitems[b] = a;
                }
                   
            }
            void addContagion(ItemDef a, ItemDef b)
            {
                newContagions.Add(new ItemDef.Pair(){itemDef1 = a, itemDef2 = b});
            }

            foreach (var c in oldContagees) foreach (var cc in c.Value)
            {
                var contager = cc.Key; var contagees = cc.Value;
                var alts = alternatesByTier[c.Key];
                foreach (var contagee in contagees)
                    addContagion(contagee,alts[contager]);
            }

            foreach (var c in oldContagers) foreach (var cc in c.Value)
            {
                var contagee = cc.Key; var contagers = cc.Value;
                var alts = alternatesByTier[c.Key];
                foreach (var contager in contagers)
                    addContagion(alts[contagee],contager);
            }

            ItemCatalog.itemRelationships[key] = newContagions.ToArray();
        }

        public static int OnGetItemCount(Inventory inv, ItemDef item)
        {
            double subcount = 0;   
            if (alternates.TryGetValue(item, out var aitems))
            {
                doOriginalItemCount = true;
                int count; double? scaling;
                foreach (var aitem in aitems.Select(i => i.ItemDef)) {
                    if ((count = inv.GetItemCount(aitem)) != 0 && (scaling = GetScaling(item.tier, aitem.tier)).HasValue)
                        subcount += count * scaling.Value;
                }
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

        public static void GenerateTierScaling(BasicPickupDropTable table)
        {
            //tierScaling[ItemTier.Tier1] = table.tier1Weight;
            //tierScaling[ItemTier.VoidTier1] = table.voidTier1Weight;
            //tierScaling[ItemTier.Tier2] = table.tier2Weight;
            //tierScaling[ItemTier.VoidTier2] = table.voidTier2Weight;
            //tierScaling[ItemTier.Tier3] = table.tier3Weight;
            //tierScaling[ItemTier.VoidTier3] = table.voidTier3Weight;
            //tierScaling[ItemTier.Boss] = table.bossWeight;
            //tierScaling[ItemTier.VoidBoss] = table.voidBossWeight;
            //tierScaling[ItemTier.Lunar] = table.lunarItemWeight;
            Debug.LogWarning(string.Join(", ", tierScaling.Select(p => $"{p.Key}:{p.Value}")));
            //foreach (var tier in tierScaling.Where(p => p.Value == 0).Select(p => p.Key).ToList())
            //    tierScaling[tier] = 1;
        }

        public static Dictionary<string,Action> delayedLanguage = new Dictionary<string,Action>();
        public static Dictionary<string,(string token, Func<string,string> adjust)> dynamicLanguage = new Dictionary<string, (string token, Func<string, string> adjust)>();
        public static string extraDescriptionDynamic = " <style=cIsUtility>Scaled by <color=#{0}>{1}</color>%</style>.";
        public static string extraDescription = " <style=cIsUtility>Scaled from <color=#{0}>{1}</color> to <color=#{2}>{3}</color></style>.";

        public static CustomItem MakeAlternateItem(ItemTierDef tier, ItemTier itier, ItemDef item, ItemDisplayRule[] rules = null)
        {
            var _suffixA = suffixA + itier.ToString();
            var _suffixB = suffixB + itier.ToString().ToUpper();
            var token = item.nameToken + _suffixB;

            var aitem = new CustomItem(
                item.name + _suffixA, token, item.descriptionToken + _suffixB,
                item.loreToken, item.pickupToken + _suffixB, item.pickupIconSprite, 
                item.pickupModelPrefab, item.tags, itier, item.hidden, 
                item.canRemove, item.unlockableDef, rules, tier);

            alternateTokens.Add(token,aitem.ItemDef);

            ItemCatalog.availability.CallWhenAvailable(() => {
                aitem.ItemDef.requiredExpansion = item.requiredExpansion;

                var color = tierColors[itier];

                var sprite = item.pickupIconSprite;
                var texture = sprite.texture.ToReadable();

                texture = Stain(texture,color);
                sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.zero);
                aitem.ItemDef.pickupIconSprite = sprite;
                aitem.ItemDef.pickupModelPrefab = item.pickupModelPrefab;
            });

            var oldTier = tiers[item.tier];
            var oldColor = GetColorHexString(oldTier.colorIndex);
            var newColor = GetColorHexString(tier.colorIndex);
            string scaling(string s) => s + string.Format(extraDescriptionDynamic,newColor,(int)(10000*GetScaling(item.tier,itier))/100f);

            delayedLanguage.Add(token,() => {
                delayedLanguage.Remove(aitem.ItemDef.nameToken);
                LanguageAPI.AddOverlay(aitem.ItemDef.nameToken, Language.GetString(item.nameToken) + $" as {tier.name}");

                var extra = string.Format(extraDescription, oldColor, oldTier.name, newColor, tier.name);
                LanguageAPI.AddOverlay(aitem.ItemDef.pickupToken, Language.GetString(item.pickupToken) + extra);
                LanguageAPI.AddOverlay(aitem.ItemDef.descriptionToken, Language.GetString(item.descriptionToken) + extra);
            });

            //dynamicLanguage.Add(aitem.ItemDef.nameToken,(item.nameToken,null));
            dynamicLanguage.Add(aitem.ItemDef.pickupToken,(item.pickupToken,scaling));
            dynamicLanguage.Add(aitem.ItemDef.descriptionToken,(item.descriptionToken,scaling));

            return aitem;
        }

        public static Color stain = Color.Lerp(new Color(0.4f,0.1f,0.7f),Color.white,0.5f).gamma;
        public static Color lastStain = Color.white;

        public static List<CustomItem> MakeAlternateItems(ItemDef item, ItemDisplayRule[] rules = null)
        {
            if (item.hidden) return null;
            if (item.tier == ItemTier.NoTier) return null;
            if (item.tier == ItemTier.AssignedAtRuntime) return null;
            if (!tiers.TryGetValue(item.tier, out var tier)) return null;

            var sprite = item.pickupIconSprite;
            var texture = sprite.texture.ToReadable();

            if (!tierColors.ContainsKey(item.tier))
                tierColors[item.tier] = Border(texture.GetPixel(0,0));

            if (tier.colorIndex == ColorIndex.VoidItem)
            {
                if (!tierAltColors.TryGetValue(item.tier, out var color))
                {
                    var bname = tier.name.ToLower().Replace("void","");
                    var btier = tiers.Select(t=>new Tuple<ItemTier,ItemTierDef>(t.Key,t.Value))
                        .FirstOrDefault(t => t.Item2.name.ToLower() == bname);

                    if (btier is null)
                    {
                        color = tierColors[item.tier];
                        color = color.RGBMultiplied(lastStain);
                        lastStain = lastStain.RGBMultiplied(stain);
                    }
                    else
                    {
                        color = tierColors[btier.Item1];
                        color = color.gamma.RGBMultiplied(stain).linear;
                    }

                    tierAltColors[item.tier] = tierColors[item.tier] = color;

                    tier.bgIconTexture = tier.bgIconTexture.Duplicate(
                        (x,y,c) => c.gamma.Grayscale().RGBMultiplied(color));

                    Color icolor = GetColor(tier.colorIndex);
                    icolor = icolor.RGBMultiplied(color);
                    var hex = Util.RGBToHex( icolor );

                    tier.colorIndex = (ColorIndex)indexToColor32.Length;
                    indexToColor32 = indexToColor32.AddItem(icolor).ToArray();
                    indexToHexString = indexToHexString.AddItem(hex).ToArray();
                    altColorCatalog[tier.colorIndex] = icolor;
                    altColorCatalogHex[tier.colorIndex] = hex;
                    //indexToColor32[(int)tier.colorIndex] = icolor;
                    //indexToHexString[(int)tier.colorIndex] = hex;

                    icolor = GetColor(tier.darkColorIndex);
                    icolor = icolor.RGBMultiplied(color);
                    hex = Util.RGBToHex( icolor );

                    tier.darkColorIndex = (ColorIndex)indexToColor32.Length;
                    indexToColor32 = indexToColor32.AddItem(icolor).ToArray();
                    indexToHexString = indexToHexString.AddItem(hex).ToArray();
                    altColorCatalog[tier.darkColorIndex] = icolor;
                    altColorCatalogHex[tier.darkColorIndex] = hex;
                    //indexToColor32[(int)tier.darkColorIndex] = icolor;
                    //indexToHexString[(int)tier.darkColorIndex] = hex;
                }
                  
                texture = Stain(texture,color);
                sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.zero);
                item.pickupIconSprite = sprite;
            }

            var aitems = new List<CustomItem>();

            foreach(var t in tiers.Where(t => t.Value != tier))
            {
                item.tags = item.tags.Where(i => i != ItemTag.WorldUnique).ToArray();
                var aitem = MakeAlternateItem(t.Value, t.Key, item, rules);
                if (aitem != null) aitems.Add(aitem);
            }

            return aitems;
        }

    }
}
#pragma warning restore Publicizer001 // Accessing a member that was not originally public