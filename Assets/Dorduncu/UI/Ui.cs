// Small UGUI toolkit: everything in Dördüncü is built from code (no prefabs / scene wiring), so the project opens and runs in
// any Unity 6 scene. Reference resolution 1920×1080 (CanvasScaler "Expand": the whole 16:9 area is always visible).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Dorduncu.UI
{
    public static class Pal
    {
        public static readonly Color Cream = Hex("F6E7C1");
        public static readonly Color Gold = Hex("E9B949");
        public static readonly Color GoldDark = Hex("A97A2A");
        public static readonly Color Wood = Hex("3B2412");
        public static readonly Color WoodDark = Hex("24150A");
        public static readonly Color WoodLight = Hex("5A3A1E");
        public static readonly Color Green = Hex("2E9E4F");
        public static readonly Color Red = Hex("C8362B");
        public static readonly Color Ivory = Hex("F4ECD8");
        public static readonly Color IvoryShade = Hex("D9CBAA");
        public static readonly Color Dim = new Color(0, 0, 0, 0.62f);

        /// <summary>Tile number colors: kırmızı, sarı, mavi, siyah.</summary>
        public static Color TileColor(string c) => c == "R" ? Hex("D7262B") : c == "Y" ? Hex("E39A0E") : c == "B" ? Hex("1E7FD0") : Hex("1C1C1C");

        public static Color Hex(string h)
        {
            ColorUtility.TryParseHtmlString("#" + h, out var c);
            return c;
        }
    }

    public static class Ui
    {
        public static Font Display, Body;
        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        private static Sprite _round, _roundSmall, _circle, _diamond;

        public static void LoadFonts()
        {
            var fallback = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Display = Resources.Load<Font>("Dorduncu/Fonts/Oswald-SemiBold") ?? fallback;
            Body = Resources.Load<Font>("Dorduncu/Fonts/Nunito-ExtraBold") ?? fallback;
        }

        /// <summary>Art from Resources/Dorduncu/Art (imported as sprites by the editor postprocessor; textures also work).</summary>
        public static Sprite Art(string name)
        {
            if (Sprites.TryGetValue(name, out var s)) return s;
            s = Resources.Load<Sprite>("Dorduncu/Art/" + name);
            if (s == null)
            {
                var tex = Resources.Load<Texture2D>("Dorduncu/Art/" + name);
                if (tex != null) s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100);
            }
            Sprites[name] = s;
            return s;
        }

        // ---------------------------------------------------------------- generated shapes
        private static Sprite Shape(int size, Func<float, float, float> alpha, int border)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(x + 0.5f, y + 0.5f)) * 255));
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        private static Sprite RoundRect(int size, float r) => Shape(size, (x, y) =>
        {
            float dx = Mathf.Max(0, Mathf.Max(r - x, x - (size - r)));
            float dy = Mathf.Max(0, Mathf.Max(r - y, y - (size - r)));
            return r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f;
        }, (int)r + 1);

        /// <summary>9-sliced rounded rectangle (radius 22 px at 1×).</summary>
        public static Sprite Round => _round ?? (_round = RoundRect(64, 22));
        public static Sprite RoundSmall => _roundSmall ?? (_roundSmall = RoundRect(32, 9));
        public static Sprite Circle => _circle ?? (_circle = Shape(128, (x, y) => 64 - Mathf.Sqrt((x - 64) * (x - 64) + (y - 64) * (y - 64)) + 0.5f, 0));
        public static Sprite Diamond => _diamond ?? (_diamond = Shape(64, (x, y) => 32 - (Mathf.Abs(x - 32) + Mathf.Abs(y - 32)) + 0.5f, 0));

        // ---------------------------------------------------------------- building blocks
        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Center-anchored placement in canvas pixels (origin = parent center).</summary>
        public static RectTransform Place(this RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static RectTransform Stretch(this RectTransform rt, float inset = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        public static Image Img(Transform parent, string name, Sprite sprite, Color color, bool raycast = false)
        {
            var rt = Node(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = raycast;
            if (sprite != null && sprite.border != Vector4.zero) img.type = Image.Type.Sliced;
            return img;
        }

        public static Image Art(Transform parent, string name, string art, bool preserveAspect = true)
        {
            var img = Img(parent, name, Art(art), Color.white);
            img.preserveAspect = preserveAspect;
            return img;
        }

        public static Text Txt(Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleCenter, bool display = true)
        {
            var rt = Node("Text", parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = display ? Display : Body;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.text = text;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        /// <summary>Gold-framed rounded panel.</summary>
        public static RectTransform Frame(Transform parent, string name, Color fill, Color border, float thickness = 3)
        {
            var outer = Img(parent, name, Round, border, true);
            var inner = Img(outer.transform, "Fill", Round, fill);
            inner.rectTransform.Stretch(thickness);
            return outer.rectTransform;
        }

        public enum Style { Wood, Primary, Gold, Danger, Ghost }

        public static Button Btn(Transform parent, string label, Style style, UnityAction onClick, int fontSize = 30)
        {
            Color fill = style == Style.Primary ? Pal.Green : style == Style.Gold ? Pal.Gold : style == Style.Danger ? Pal.Red : style == Style.Ghost ? new Color(0, 0, 0, 0.35f) : Pal.WoodLight;
            Color text = style == Style.Gold ? Pal.WoodDark : Pal.Cream;
            var rt = Frame(parent, "Button " + label, fill, style == Style.Gold ? Pal.GoldDark : Pal.Gold, 3);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = rt.GetComponent<Image>();
            var cb = b.colors;
            cb.highlightedColor = new Color(1, 0.95f, 0.85f);
            cb.pressedColor = new Color(0.8f, 0.75f, 0.65f);
            cb.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.7f);
            b.colors = cb;
            var t = Txt(rt, label.ToUpper(new System.Globalization.CultureInfo("tr-TR")), fontSize, text);
            t.rectTransform.Stretch(6);
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = 14;
            t.resizeTextMaxSize = fontSize;
            t.name = "Label";
            if (onClick != null) b.onClick.AddListener(onClick);
            return b;
        }

        public static void SetLabel(this Button b, string label)
        {
            var t = b.transform.Find("Label")?.GetComponent<Text>();
            if (t != null) t.text = label.ToUpper(new System.Globalization.CultureInfo("tr-TR"));
        }

        /// <summary>Disabled buttons also dim their frame and label.</summary>
        public static void SetEnabled(this Button b, bool on)
        {
            if (b.interactable == on) return;
            b.interactable = on;
            var cg = b.GetComponent<CanvasGroup>() ?? b.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = on ? 1f : 0.45f;
        }

        public static InputField Field(Transform parent, string placeholder, int limit)
        {
            var rt = Frame(parent, "Input", Pal.WoodDark, Pal.GoldDark, 3);
            var field = rt.gameObject.AddComponent<InputField>();
            var text = Txt(rt, "", 30, Pal.Cream, TextAnchor.MiddleLeft, false);
            text.rectTransform.Stretch(16);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.supportRichText = false;
            var ph = Txt(rt, placeholder, 30, new Color(Pal.Cream.r, Pal.Cream.g, Pal.Cream.b, 0.4f), TextAnchor.MiddleLeft, false);
            ph.rectTransform.Stretch(16);
            ph.fontStyle = FontStyle.Italic;
            field.textComponent = text;
            field.placeholder = ph;
            field.characterLimit = limit;
            field.targetGraphic = rt.GetComponent<Image>();
            return field;
        }

        /// <summary>Full-screen dimmed overlay with a centered card; returns the card.</summary>
        public static RectTransform Overlay(Transform parent, string name, float w, float h, out GameObject root)
        {
            var dim = Img(parent, name, null, Pal.Dim, true);
            dim.rectTransform.Stretch();
            root = dim.gameObject;
            var card = Frame(dim.transform, "Card", Pal.Wood, Pal.Gold, 4);
            card.Place(0, 0, w, h);
            return card;
        }

        public static Text Title(Transform card, string text, float y, int size = 64)
        {
            var t = Txt(card, text, size, Pal.Gold);
            t.rectTransform.Place(0, y, 900, size + 20);
            return t;
        }

        /// <summary>Avatar portrait: avatars sheet is a 2×2 grid (0 Hüsnü Usta, 1 Derya, 2 player, 3 Kerem).</summary>
        public static Sprite Avatar(int index)
        {
            var key = "avatar:" + (index & 3);
            if (Sprites.TryGetValue(key, out var s)) return s;
            var sheet = Art("avatars");
            if (sheet == null) return null;
            var tex = sheet.texture;
            var r = sheet.textureRect;
            float half = r.width / 2;
            int i = index & 3;
            float x = r.x + (i % 2) * half, y = r.y + (i < 2 ? half : 0);   // texture origin is bottom-left
            s = Sprite.Create(tex, new Rect(x, y, half, half), new Vector2(0.5f, 0.5f), 100);
            Sprites[key] = s;
            return s;
        }
    }
}
