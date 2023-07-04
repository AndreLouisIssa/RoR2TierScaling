using HarmonyLib;
using R2API;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static RoR2TierScaling.Main;
using static RoR2.ColorCatalog;

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
        public static HashSet<string> excludedItems = new HashSet<string>();
        public static Dictionary<string,Action> delayedLanguage = new Dictionary<string,Action>();
        public static string extraDescriptionDynamic = " <style=cIsUtility>Scaled by <color=#{0}>{1}%</color></style>.";

        //public static double defaultTierScaling = 8;//0;
        public static Dictionary<ItemTier,double> initialTierScaling = new Dictionary<ItemTier, double>()//;
        {
            { ItemTier.Tier1, 1  }, { ItemTier.VoidTier1, 2  },
            { ItemTier.Tier2, 3  }, { ItemTier.VoidTier2, 4  },
            { ItemTier.Boss,  5  }, { ItemTier.VoidBoss,  6  },
            { ItemTier.Lunar, 7  }, { ItemTier.NoTier,    8  },
            { ItemTier.Tier3, 15 }, { ItemTier.VoidTier3, 16 }
        };
        public static Dictionary<ItemTier,string> initialTierAliases = new Dictionary<ItemTier, string>()//;
        {
            { ItemTier.Tier1, "White" }, { ItemTier.VoidTier1, "Void White" },
            { ItemTier.Tier2, "Green" }, { ItemTier.VoidTier2, "Void Green" },
            { ItemTier.Boss,  "Boss"  }, { ItemTier.VoidBoss,  "Void Boss"  },
            { ItemTier.Lunar, "Lunar" }, { ItemTier.NoTier,    "Default"   },
            { ItemTier.Tier3, "Red"   }, { ItemTier.VoidTier3, "Void Red"   }
        };

        public static double GetScaling(ItemTier tier)
        {
            if (configTierScaling.TryGetValue(tier,out var scaling))
                return scaling.Value;
            return 0;
        }

        public static double GetScaling(ItemTier tier, ItemTier atier)
        {
            var a = GetScaling(tier); var b = GetScaling(atier);
            if (a == 0 || b == 0) return default;
            return b / a;
        }

        public static ItemTier ItemTierIndex(ItemTierDef tier)
        {
            var i = ItemTier.NoTier;
            if (tier is null) return i;
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
            if (key is null || !ItemCatalog.itemRelationships.TryGetValue(key,out var contagions)) return;
            var newContagions = new HashSet<ItemDef.Pair>();
            var keepContagions = new HashSet<ItemDef.Pair>();
            var applicableItems = new HashSet<ItemDef>();
            var oldContagees = new Dictionary<ItemTier, Dictionary<ItemDef, HashSet<ItemDef>>>();
            var oldContagers = new Dictionary<ItemTier, Dictionary<ItemDef, HashSet<ItemDef>>>();

            foreach (var p in contagions)
            {
                var a = p.itemDef1; var b = p.itemDef2;
                if (GetScaling(a.tier) == 0 || GetScaling(b.tier) == 0 
                    || !alternates.ContainsKey(a) || !alternates.ContainsKey(b))
                {
                    keepContagions.Add(p);
                    continue;
                }

                if (!oldContagees.TryGetValue(a.tier, out var contagees))
                    oldContagees[a.tier] = contagees = new Dictionary<ItemDef, HashSet<ItemDef>>();
                if (!contagees.TryGetValue(b, out var contagee))
                    contagees[b] = contagee = new HashSet<ItemDef>();
                contagee.Add(a);

                if (!oldContagers.TryGetValue(b.tier, out var contagers))
                    oldContagers[b.tier] = contagers = new Dictionary<ItemDef, HashSet<ItemDef>>();
                if (!contagers.TryGetValue(a, out var contager))
                    contagers[a] = contager = new HashSet<ItemDef>();
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
                if (alternatesByTier.TryGetValue(c.Key,out var alts))
                    foreach (var contagee in contagees)
                        if (alts.TryGetValue(contager, out var acontager))
                            addContagion(contagee,acontager);
            }

            foreach (var c in oldContagers) foreach (var cc in c.Value)
            {
                var contagee = cc.Key; var contagers = cc.Value;
                if (alternatesByTier.TryGetValue(c.Key,out var alts))
                    foreach (var contager in contagers)
                        if (alts.TryGetValue(contagee, out var acontagee))
                            addContagion(acontagee,contager);
            }

            ItemCatalog.itemRelationships[key] = keepContagions.Concat(newContagions).ToArray();
        }

        public static int OnGetItemCount(Inventory inv, ItemDef item)
        {
            double subcount = 0;   
            if (alternates.TryGetValue(item, out var aitems))
            {
                doOriginalItemCount = true;
                int count;
                foreach (var aitem in aitems.Select(i => i.ItemDef)) {
                    if ((count = inv.GetItemCount(aitem)) != 0)
                        subcount += count * GetScaling(item.tier, aitem.tier);
                }
                doOriginalItemCount = false;
            }    
            return (int)subcount + ((subcount % 1) > random.NextDouble() ? 1 : 0);    
        }

        public static bool doOriginalItemCount = false;

        public static Color Border(Color color)
        {
            Color.RGBToHSV(color.NoAlpha(), out var h, out var s, out var v);
            return Color.HSVToRGB(h,s*1.2f,v*0.9f);
        }

        public static Color Border(Color colorA, Color colorB)
        {
            Color.RGBToHSV(colorA.NoAlpha(), out var hA, out var sA, out var vA);
            Color.RGBToHSV(colorB.NoAlpha(), out var hB, out var sB, out var vB);
            return Color.HSVToRGB((hA+hB)/2,(sA+sB)*0.6f,(vA+vB)*0.45f);
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

        public static CustomItem MakeAlternateItem(ItemTierDef tier, ItemTier itier, ItemDef item, ItemDisplayRule[] rules = null)
        {
            var tname = configTierAliases[itier].Value;
            var trname = tname.Replace(" ","");
            var suffix = "_AS_" + trname.ToUpper();
            var token = item.nameToken + suffix;

            var aitem = new CustomItem(
                item.name + "As" + trname, token, item.descriptionToken + suffix,
                item.loreToken, item.pickupToken + suffix, item.pickupIconSprite, 
                item.pickupModelPrefab, item.tags, itier, item.hidden, 
                item.canRemove, item.unlockableDef, rules, tier);

            alternateTokens.Add(token,aitem.ItemDef);

            ItemCatalog.availability.CallWhenAvailable(() => {
                var color = tierColors[itier];
                var sprite = item.pickupIconSprite;
                var texture = sprite.texture.ToReadable();

                texture = Stain(texture,color);
                sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.zero);
                aitem.ItemDef.pickupIconSprite = sprite;

                aitem.ItemDef.pickupModelPrefab = item.pickupModelPrefab;
                aitem.ItemDef.requiredExpansion = item.requiredExpansion;
            });

            delayedLanguage.Add(token,() => {
                delayedLanguage.Remove(aitem.ItemDef.nameToken);

                LanguageAPI.AddOverlay(aitem.ItemDef.nameToken, Language.GetString(item.nameToken, configLanguage.Value) + $" as {tname}");
                var extra = string.Format(extraDescriptionDynamic,GetColorHexString(tier.colorIndex),(int)(10000*GetScaling(item.tier,itier))/100f);
                LanguageAPI.AddOverlay(aitem.ItemDef.pickupToken, Language.GetString(item.pickupToken) + extra);
                LanguageAPI.AddOverlay(aitem.ItemDef.descriptionToken, Language.GetString(item.descriptionToken) + extra);
            });

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

            Sprite sprite = null;
            Texture2D texture = null;

            if (!tierColors.ContainsKey(item.tier))
            {
                sprite = item.pickupIconSprite;
                texture = sprite.texture.ToReadable();
                Color border;
                if (texture == null || (border=texture.GetPixel(0,0)) != null && border.r > 0.9 && border.g > 0.9 && border.b > 0.9) 
                {
                    var _tier = tiers[item.tier];
                    border = Border(GetColor(_tier.colorIndex),GetColor(_tier.darkColorIndex));
                }
                else border = Border(texture.GetPixel(0,0));
                tierColors[item.tier] = border;
            }

            if (!configTierScaling.TryGetValue(item.tier, out var _ts) || _ts.Value == 0) return null;

            if (configVoidColours.Value && tier.colorIndex == ColorIndex.VoidItem)
            {
                if (!tierAltColors.TryGetValue(item.tier, out var color))
                {
                    var bname = tier.name.ToLower().Replace("void","");
                    var btier = tiers.Select(t=>new Tuple<ItemTier,ItemTierDef>(t.Key,t.Value))
                        .FirstOrDefault(t => t.Item2.name.ToLower() == bname);

                    if (btier is null)
                        goto SKIP_VOID;
                    //{
                    //    color = tierColors[item.tier];
                    //    color = color.RGBMultiplied(lastStain);
                    //    lastStain = lastStain.RGBMultiplied(stain);
                    //}
                    //else
                    //{
                        color = tierColors[btier.Item1];
                        color = color.gamma.RGBMultiplied(stain).linear;
                    //}

                    tierAltColors[item.tier] = tierColors[item.tier] = color;

                    if (tier.bgIconTexture != null)
                        tier.bgIconTexture = tier.bgIconTexture.Duplicate(
                            (x,y,c) => c.gamma.Grayscale().RGBMultiplied(color));

                    //Color icolor = GetColor(tier.colorIndex);
                    //icolor = icolor.RGBMultiplied(color);
                    //var hex = Util.RGBToHex( icolor );

                    //tier.colorIndex = (ColorIndex)indexToColor32.Length;
                    //indexToColor32 = indexToColor32.AddItem(icolor).ToArray();
                    //indexToHexString = indexToHexString.AddItem(hex).ToArray();
                    //altColorCatalog[tier.colorIndex] = icolor;
                    //altColorCatalogHex[tier.colorIndex] = hex;
                    //indexToColor32[(int)tier.colorIndex] = icolor;
                    //indexToHexString[(int)tier.colorIndex] = hex;

                    //icolor = GetColor(tier.darkColorIndex);
                    //icolor = icolor.RGBMultiplied(color);
                    //hex = Util.RGBToHex( icolor );

                    //tier.darkColorIndex = (ColorIndex)indexToColor32.Length;
                    //indexToColor32 = indexToColor32.AddItem(icolor).ToArray();
                    //indexToHexString = indexToHexString.AddItem(hex).ToArray();
                    //altColorCatalog[tier.darkColorIndex] = icolor;
                    //altColorCatalogHex[tier.darkColorIndex] = hex;
                    //indexToColor32[(int)tier.darkColorIndex] = icolor;
                    //indexToHexString[(int)tier.darkColorIndex] = hex;
                }
                
                if (sprite is null)
                    sprite = item.pickupIconSprite;
                if (texture is null)
                    texture = sprite.texture.ToReadable();

                texture = Stain(texture,color);
                sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.zero);
                item.pickupIconSprite = sprite;
            }

            SKIP_VOID: 

            if (!configMakeAlternates.Value) return null;

            var lname = Language.GetString(item.nameToken, configLanguage.Value);
            var exclude = excludedItems.Contains(item.name.ToLower()) 
                || excludedItems.Contains(item.nameToken.ToLower()) 
                || excludedItems.Contains(lname.ToLower());
            if (configInvertBlacklist.Value) exclude = !exclude;
            if (exclude) return null;

            if (configLoadReduction.Value && random.NextDouble() < configLoadProportion.Value) return null;

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