using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace HudLayout
{
    // A ready-made look for one element: values for its style options, by config key.
    internal sealed class StyleDef
    {
        public string Name;
        public string NameRu;
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>();
    }

    public partial class HudLayoutPlugin
    {
        // ------------------------------------------------------------------
        // built-in styles of the elements
        // ------------------------------------------------------------------
        private static readonly string[] BarStyleNames = { "Vanilla", "Minimal", "Numeric", "Percent", "Faded", "Contrast", CustomStyle };
        private static readonly string[] FoodStyleNames = { "Vanilla", "IconsOnly", "LargeTimers", "Faded", CustomStyle };

        // Per element, in the order of the names above (without Custom).
        private readonly Dictionary<ElementId, List<StyleDef>> _styles = new Dictionary<ElementId, List<StyleDef>>();

        private static string[] StyleNamesFor(bool isBar)
        {
            return isBar ? BarStyleNames : FoodStyleNames;
        }

        // A bright colour of each bar for the Contrast style.
        private static string ContrastColor(ElementId id)
        {
            switch (id)
            {
                case ElementId.Health: return "#FF3B3B";
                case ElementId.Stamina: return "#FFD21F";
                case ElementId.Eitr: return "#B86BFF";
                case ElementId.Adrenaline: return "#FF8A1F";
            }
            return "";
        }

        private static StyleDef Bar(string name, string ru, Visibility vis, float opacity, TextMode text, float textSize, string color)
        {
            StyleDef d = new StyleDef();
            d.Name = name; d.NameRu = ru;
            d.Values["Visibility"] = vis.ToString();
            d.Values["Opacity"] = F(opacity);
            d.Values["Text"] = text.ToString();
            d.Values["TextSize"] = F(textSize);
            d.Values["BarColor"] = color;
            return d;
        }

        private static StyleDef Food(string name, string ru, Visibility vis, float opacity, bool timers, float textSize)
        {
            StyleDef d = new StyleDef();
            d.Name = name; d.NameRu = ru;
            d.Values["Visibility"] = vis.ToString();
            d.Values["Opacity"] = F(opacity);
            d.Values["Timers"] = timers ? "true" : "false";
            d.Values["TextSize"] = F(textSize);
            return d;
        }

        private void BuildStyles()
        {
            foreach (ElementSettings s in Elements)
            {
                List<StyleDef> list = new List<StyleDef>();
                if (s.IsBar)
                {
                    list.Add(Bar("Vanilla", "Как в игре", Visibility.Vanilla, 1f, TextMode.Vanilla, 1f, ""));
                    list.Add(Bar("Minimal", "Минимум", Visibility.Vanilla, 0.85f, TextMode.Hidden, 1f, ""));
                    list.Add(Bar("Numeric", "Числа", Visibility.Always, 1f, TextMode.CurrentMax, 1f, ""));
                    list.Add(Bar("Percent", "Проценты", Visibility.Always, 1f, TextMode.Percent, 1f, ""));
                    list.Add(Bar("Faded", "Прозрачный", Visibility.Vanilla, 0.5f, TextMode.Vanilla, 1f, ""));
                    list.Add(Bar("Contrast", "Контраст", Visibility.Always, 1f, TextMode.Current, 1.25f, ContrastColor(s.Id)));
                }
                else
                {
                    list.Add(Food("Vanilla", "Как в игре", Visibility.Vanilla, 1f, true, 1f));
                    list.Add(Food("IconsOnly", "Без таймеров", Visibility.Vanilla, 1f, false, 1f));
                    list.Add(Food("LargeTimers", "Крупные таймеры", Visibility.Vanilla, 1f, true, 1.4f));
                    list.Add(Food("Faded", "Прозрачный", Visibility.Vanilla, 0.6f, true, 1f));
                }
                _styles[s.Id] = list;
            }
        }

        internal List<StyleDef> StylesOf(ElementSettings s)
        {
            List<StyleDef> list;
            return _styles.TryGetValue(s.Id, out list) ? list : new List<StyleDef>();
        }

        internal string StyleLabel(ElementSettings s, string name)
        {
            if (s.Style == null) return "";
            if (name == CustomStyle) return L("Custom", "Своё");
            foreach (StyleDef d in StylesOf(s))
                if (d.Name == name) return L(d.Name, d.NameRu);
            return name;
        }

        // Writes the style's options; the Style entry itself is set too (it may be applied by
        // name from the editor or a command).
        internal void ApplyStyle(ElementSettings s, string name)
        {
            if (name == CustomStyle || s.Style == null) return;
            StyleDef def = null;
            foreach (StyleDef d in StylesOf(s)) if (d.Name == name) def = d;
            if (def == null) return;
            Batch(delegate
            {
                foreach (ConfigEntryBase e in s.StyleEntries)
                {
                    string v;
                    if (def.Values.TryGetValue(e.Definition.Key, out v)) e.SetSerializedValue(v);
                }
                if (s.Style.Value != name) s.Style.Value = name;
                ParseColor(s);
            });
        }

        // Sets Style to the style whose options all match the current ones, or Custom.
        internal void DetectStyle(ElementSettings s)
        {
            if (s.Style == null) return;
            string found = CustomStyle;
            foreach (StyleDef d in StylesOf(s))
            {
                bool all = true;
                foreach (ConfigEntryBase e in s.StyleEntries)
                {
                    if (e == s.Style) continue;
                    string v;
                    if (!d.Values.TryGetValue(e.Definition.Key, out v)) continue;
                    if (Normalize(e, v) != e.GetSerializedValue()) { all = false; break; }
                }
                if (all) { found = d.Name; break; }
            }
            if (s.Style.Value != found)
            {
                _applying++;
                try { s.Style.Value = found; }
                finally { _applying--; }
            }
        }
    }
}
