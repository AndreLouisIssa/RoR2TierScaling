using System;
using System.Collections.Generic;
using UnityEngine;
using System.IO;

namespace RoR2TierScaling
{
    public static class Utils
    {
        public static Texture2D LoadTexture(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            Texture2D tex = new Texture2D(0, 0) { wrapMode = TextureWrapMode.Clamp };
            ImageConversion.LoadImage(tex, data);
            return tex;
        }

        public static IEnumerable<Tuple<int, int>> GetEnumerator(this Texture2D texture)
        {
            for (int x = 0; x < texture.width; x++) for (int y = 0; y < texture.height; y++)
                {
                    yield return new Tuple<int, int>(x, y);
                }
        }

        public static Texture2D Duplicate(this Texture texture, Rect? proj = null)
        {
            if (proj is null) proj = new Rect(0, 0, texture.width, texture.height);
            var rect = (Rect)proj;
            texture.filterMode = FilterMode.Point;
            RenderTexture rt = RenderTexture.GetTemporary(texture.width, texture.height);
            rt.filterMode = FilterMode.Point;
            RenderTexture.active = rt;
            Graphics.Blit(texture, rt);
            Texture2D texture2 = new Texture2D((int)rect.width, (int)rect.height);
            texture2.ReadPixels(new Rect(rect.x, texture.height - rect.height - rect.y, rect.width, rect.height), 0, 0);
            texture2.Apply();
            RenderTexture.active = null;
            return texture2;
        }

        public static Texture2D ToTexture2D(this Texture texture)
        {
            return (texture is Texture2D t2D) ? t2D : texture.Duplicate();
        }

        public static Texture2D ToReadable(this Texture texture)
        {
            var t2D = texture.ToTexture2D();
            return (t2D.isReadable) ? t2D : t2D.Duplicate();
        }

        public static IEnumerable<Tuple<int, int>> GetEnumerator(this Texture texture)
        {
            return texture.ToTexture2D().GetEnumerator();
        }

        public static Texture2D Duplicate(this Texture texture, Func<int, int, Color, Color> func, Rect? proj = null)
        {
            if (proj is null) { proj = new Rect(0, 0, texture.width, texture.height); }
            var t = texture.Duplicate(proj);
            foreach (var xy in t.GetEnumerator())
            {
                var x = xy.Item1; var y = xy.Item2;
                t.SetPixel(x, y, func(x, y, t.GetPixel(x, y)));
            }
            t.Apply();
            return t;
        }

        public static Rect RectOrTexture(Texture texture, Rect? proj = null)
        {
            float w; float h; float x; float y;
            if (proj is Rect rect)
            {
                w = rect.width; h = rect.height;
                x = rect.x; y = rect.y;
            }
            else
            {
                w = texture.width; h = texture.height;
                x = 0f; y = 0f;
            }
            return new Rect(x, y, w, h);
        }

        public static Rect GetRegionRect(Texture texture, Rect? proj = null)
        {
            int w; int h;
            if (proj is Rect rect) { w = (int)rect.width; h = (int)rect.height; }
            else { w = texture.width; h = texture.height; }
            var w2 = w / 2; var h2 = h / 2;
            return new Rect(-w2, -h2, w, h);
        }

        public static Color AlphaFlattened(this Color color) 
        {
            if (color.a <= 0) return color;
            return color.AlphaMultiplied(1/color.a);
        }

        public static Color NoAlpha(this Color color) 
        {
            return new Color(color.r, color.g, color.b);
        }

        public static Color Grayscale(this Color color) 
        {
            return new Color(color.grayscale, color.grayscale, color.grayscale, color.a);
        }
    }
}
