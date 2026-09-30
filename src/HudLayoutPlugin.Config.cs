using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace HudLayout
{
    // Other: a simple element (the rest of the vanilla HUD, other mods' elements), told apart by Key.
    public enum ElementId { Health, Stamina, Eitr, Adrenaline, Food, Other }

    // Default: as the game draws it (health and food vertical, the other bars horizontal).
    public enum Orientation { Default, Horizontal, Vertical }

    // The point of a bar that stays put at the configured position while the bar grows:
    // its start (left or bottom), its middle, or its end.
    public enum BarAnchor { Start, Center, End }

    // Vanilla: as the game does it (stamina, eitr and adrenaline fade out when not in use).
    // Always: never fade out. Hidden: never shown. NotFull: hidden while full, shown the
    // moment it drops below 100%.
    public enum Visibility { Vanilla, Always, Hidden, NotFull }

    public enum TextMode { Vanilla, Hidden, Current, CurrentMax, Percent }

    // Where the number sits on a bar: as in the game (health: middle of the fill, the others:
    // middle of the bar), always the middle of the bar, or the middle of the filled part.
    public enum TextPosition { Vanilla, Center, Fill }

    // One element's settings. The keys are the same in every section, so presets and
    // styles address them as "<Element>.<Key>".
    internal sealed class ElementSettings
    {
        public ElementId Id;
        public string Key;          // "Health", ...
        public string Section;      // "01 Health", ...
        public bool IsBar;
        public bool NativeVertical; // how the game draws it
        public bool IsSimple;       // position, scale, visibility and opacity only
        public string LabelEn, LabelRu;

        public ConfigEntry<float> PosX, PosY, Scale, Length, Thickness, Opacity, TextSize;
        public ConfigEntry<bool> FixedLength, Icon, Segments;
        public ConfigEntry<bool> Visible;   // food and simple elements: shown or not (the bars have Vis)
        public ConfigEntry<float> SegmentSize;
        public ConfigEntry<Orientation> Orient;
        public ConfigEntry<BarAnchor> Anchor;
        public ConfigEntry<string> Style, BarColor;
        public ConfigEntry<Visibility> Vis;
        public ConfigEntry<TextMode> Text;
        public ConfigEntry<TextPosition> TextPos;
        public ConfigEntry<bool> Timers;

        // Where the element is and how big: the part a layout preset carries.
        public readonly List<ConfigEntryBase> LayoutEntries = new List<ConfigEntryBase>();
        // How it looks: the part a style (and a style preset) carries. Style itself first.
        public readonly List<ConfigEntryBase> StyleEntries = new List<ConfigEntryBase>();

        // BarColor parsed; HasColor false = the game's colour.
        public bool HasColor;
        public Color Color;

        public bool HasPosition { get { return PosX.Value >= 0f && PosY.Value >= 0f; } }

        public IEnumerable<ConfigEntryBase> AllEntries()
        {
            foreach (ConfigEntryBase e in LayoutEntries) yield return e;
            foreach (ConfigEntryBase e in StyleEntries) yield return e;
        }
    }

    public partial class HudLayoutPlugin
    {
        // ------------------------------------------------------------------
        // config
        // ------------------------------------------------------------------
        private ConfigEntry<bool> _cfgEnabled;
        private ConfigEntry<KeyboardShortcut> _cfgEditKey;
        private ConfigEntry<bool> _cfgBuildShift;
        private ConfigEntry<float> _cfgEditorOpacity;
        private static ConfigEntry<string> _cfgLanguage;

        internal readonly ElementSettings[] Elements = new ElementSettings[5];
        // The simple elements: the rest of the vanilla HUD (bound at start) and other mods'
        // elements (bound when first found).
        internal readonly List<ElementSettings> Extra = new List<ElementSettings>();

        internal IEnumerable<ElementSettings> AllSettings()
        {
            foreach (ElementSettings s in Elements) if (s != null) yield return s;
            foreach (ElementSettings s in Extra) yield return s;
        }

        internal ElementSettings FindSettings(string key)
        {
            foreach (ElementSettings s in AllSettings())
                if (string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        private const string CustomStyle = "Custom";

        // English or Russian by 00 General/Language (Auto follows the game's language).
        internal static string L(string en, string ru)
        {
            string lang = _cfgLanguage != null ? _cfgLanguage.Value : "Auto";
            if (lang == "Russian") return ru;
            if (lang == "English") return en;
            try
            {
                Localization loc = Localization.instance;
                return loc != null && loc.GetSelectedLanguage() == "Russian" ? ru : en;
            }
            catch { return en; }
        }

        // ConfigurationManager: the order of the options within a section (it sorts by Order,
        // highest first) and "advanced" for the ones few players need. Matched by name.
        private sealed class ConfigurationManagerAttributes
        {
#pragma warning disable 0414, 0649
            public int? Order;
            public bool? IsAdvanced;
#pragma warning restore 0414, 0649
        }

        private int _order;

        private ConfigDescription D(string text)
        {
            return D(text, null, false);
        }

        private ConfigDescription D(string text, AcceptableValueBase values)
        {
            return D(text, values, false);
        }

        private ConfigDescription D(string text, AcceptableValueBase values, bool advanced)
        {
            ConfigurationManagerAttributes a = new ConfigurationManagerAttributes();
            a.Order = _order--;
            if (advanced) a.IsAdvanced = true;
            return new ConfigDescription(text, values, a);
        }

        private static AcceptableValueRange<float> Range(float min, float max)
        {
            return new AcceptableValueRange<float>(min, max);
        }

        private const string EditHint = " Easiest to set in the F7 editor.";

        private void BindConfig()
        {
            const string g = "00 General";
            _order = 1000;
            _cfgEnabled = Config.Bind(g, "Enabled", true,
                D("Off = the HUD exactly as the game draws it. Your settings are kept."));
            _cfgEditKey = Config.Bind(g, "EditModeKey", new KeyboardShortcut(KeyCode.F7),
                D("Opens the editor: drag the HUD elements with the mouse, resize them, pick styles and presets. Esc or the same key closes it."));
            _cfgLanguage = Config.Bind(g, "Language", "Auto",
                D("Language of the editor. Auto = as the game.", new AcceptableValueList<string>("Auto", "English", "Russian")));
            _cfgBuildShift = Config.Bind(g, "FollowBuildShift", true,
                D("In build mode and at a ship's helm the game lifts the stamina, eitr and adrenaline bars out of the way. On = moved bars are lifted too.", null, true));
            _cfgEditorOpacity = Config.Bind(g, "EditorOpacity", 0.95f,
                D("How opaque the editor window's background is.", Range(0.3f, 1f), true));
            BindModOptions(g);

            Elements[0] = BindElement(ElementId.Health, "01 Health", "Health", true, true, BarAnchor.Start);
            Elements[1] = BindElement(ElementId.Stamina, "02 Stamina", "Stamina", true, false, BarAnchor.Center);
            Elements[2] = BindElement(ElementId.Eitr, "03 Eitr", "Eitr", true, false, BarAnchor.Center);
            Elements[3] = BindElement(ElementId.Adrenaline, "04 Adrenaline", "Adrenaline", true, false, BarAnchor.Center);
            Elements[4] = BindElement(ElementId.Food, "05 Food", "Food", false, true, BarAnchor.Center);

            foreach (ElementSettings s in Elements) ParseColor(s);
            BindVanillaSimple();
            DropRetiredOptions();
            Config.SettingChanged += OnSettingChanged;
        }

        private ElementSettings BindElement(ElementId id, string section, string key, bool isBar, bool nativeVertical, BarAnchor anchor)
        {
            ElementSettings s = new ElementSettings();
            s.Id = id; s.Key = key; s.Section = section; s.IsBar = isBar; s.NativeVertical = nativeVertical;
            string what = isBar ? "the " + key.ToLowerInvariant() + " bar" : "the food icons";
            _order = 1000;

            // the order here is the order ConfigurationManager shows: look first, then place, size, details
            string[] styles = StyleNamesFor(isBar);
            s.Style = Config.Bind(section, "Style", "Vanilla",
                D("A ready-made look for " + what + ". Picking one fills in the options below; changing one of them makes it Custom.",
                    new AcceptableValueList<string>(styles)));

            s.PosX = Config.Bind(section, "PositionX", -1f,
                D("Where " + what + " is across the screen: 0 = left edge, 1 = right edge, -1 = where the game puts it." + EditHint, Range(-1f, 1f)));
            s.PosY = Config.Bind(section, "PositionY", -1f,
                D("Where " + what + " is up the screen: 0 = bottom, 1 = top, -1 = where the game puts it." + EditHint, Range(-1f, 1f)));
            s.Scale = Config.Bind(section, "Scale", 1f,
                D("Size of " + what + ". 1 = as in the game.", Range(0.25f, 4f)));
            s.Orient = Config.Bind(section, "Orientation", Orientation.Default,
                D(isBar ? "Horizontal or vertical. Default = as in the game (health vertical, the others horizontal)."
                        : "Vertical = a column (as in the game), Horizontal = a row."));
            s.LayoutEntries.Add(s.PosX); s.LayoutEntries.Add(s.PosY); s.LayoutEntries.Add(s.Scale); s.LayoutEntries.Add(s.Orient);
            if (isBar)
            {
                s.Length = Config.Bind(section, "Length", 1f,
                    D("How long the bar is. 1 = as in the game.", Range(0.25f, 4f)));
                s.Thickness = Config.Bind(section, "Thickness", 1f,
                    D("How thick the bar is. 1 = as in the game.", Range(0.25f, 4f)));
                s.FixedLength = Config.Bind(section, "FixedLength", false,
                    D("On = the bar keeps the same length as your maximum grows; only the number changes. Off = it grows, as in the game."));
                s.Anchor = Config.Bind(section, "Anchor", anchor,
                    D("Which end stays in place while the bar grows: Start (left / bottom), Center or End.", null, true));
                s.LayoutEntries.Add(s.Anchor); s.LayoutEntries.Add(s.Length); s.LayoutEntries.Add(s.Thickness); s.LayoutEntries.Add(s.FixedLength);

                s.Vis = Config.Bind(section, "Visibility", Visibility.Vanilla,
                    D("When the bar shows. Vanilla = as in the game, Always = never fades out, Hidden = never, NotFull = only when it isn't full."));
            }
            else
            {
                s.Visible = Config.Bind(section, "Visible", true, D("Show " + what + "."));
            }
            s.Opacity = Config.Bind(section, "Opacity", 1f,
                D("How opaque " + what + " is. 1 = solid.", Range(0.05f, 1f)));
            s.StyleEntries.Add(s.Style); s.StyleEntries.Add(isBar ? (ConfigEntryBase)s.Vis : s.Visible); s.StyleEntries.Add(s.Opacity);
            if (isBar)
            {
                s.Text = Config.Bind(section, "Text", TextMode.Vanilla,
                    D("The number on the bar: Vanilla (as in the game), Hidden, Current (75), CurrentMax (75/100) or Percent (75%)."));
                s.TextSize = Config.Bind(section, "TextSize", 1f,
                    D("Size of the number. 1 = as in the game.", Range(0.5f, 3f)));
                s.TextPos = Config.Bind(section, "TextPosition", TextPosition.Vanilla,
                    D("Where the number sits: Vanilla (as in the game), Center (middle of the bar) or Fill (middle of the filled part)."));
                s.Segments = Config.Bind(section, "Segments", false,
                    D("Divide the bar into cells, one per SegmentSize points of your maximum."));
                s.SegmentSize = Config.Bind(section, "SegmentSize", 10f,
                    D("Points per cell.", Range(1f, 100f)));
                s.BarColor = Config.Bind(section, "BarColor", "",
                    D("Colour of the bar, #RRGGBB. Empty = the game's colour."));
                s.StyleEntries.Add(s.Text); s.StyleEntries.Add(s.TextSize); s.StyleEntries.Add(s.TextPos); s.StyleEntries.Add(s.BarColor);
                s.StyleEntries.Add(s.Segments); s.StyleEntries.Add(s.SegmentSize);
            }
            else
            {
                s.Timers = Config.Bind(section, "Timers", true, D("Show the time left on each food icon."));
                s.TextSize = Config.Bind(section, "TextSize", 1f,
                    D("Size of the time left. 1 = as in the game.", Range(0.5f, 3f)));
                s.StyleEntries.Add(s.Timers); s.StyleEntries.Add(s.TextSize);
            }
            if (id == ElementId.Health || id == ElementId.Food)
            {
                s.Icon = Config.Bind(section, "Icon", true,
                    D(id == ElementId.Health ? "Show the heart under the health bar." : "Show the food symbol next to the food icons."));
                s.StyleEntries.Add(s.Icon);
            }
            if (s.Visible != null) MigrateVisibility(s);
            return s;
        }

        // Hidden in the editor and in the game: Vis = Hidden for a bar, Visible off for the rest.
        internal static bool IsHidden(ElementSettings s)
        {
            if (s.Vis != null) return s.Vis.Value == Visibility.Hidden;
            return s.Visible != null && !s.Visible.Value;
        }

        // ------------------------------------------------------------------
        // options of older versions, taken out of the file so it only holds what does something
        // ------------------------------------------------------------------
        private Dictionary<ConfigDefinition, string> Orphans()
        {
            try
            {
                PropertyInfo p = AccessTools.Property(typeof(ConfigFile), "OrphanedEntries");
                return p != null ? p.GetValue(Config, null) as Dictionary<ConfigDefinition, string> : null;
            }
            catch { return null; }
        }

        private bool _orphansDropped;

        private string TakeOrphan(string section, string key)
        {
            Dictionary<ConfigDefinition, string> o = Orphans();
            if (o == null) return null;
            ConfigDefinition d = new ConfigDefinition(section, key);
            string v;
            if (!o.TryGetValue(d, out v)) return null;
            o.Remove(d);
            _orphansDropped = true;
            return v;
        }

        // Food and simple elements had a four-way Visibility of which only Vanilla / Hidden
        // meant anything; it is the Visible switch now.
        private void MigrateVisibility(ElementSettings s)
        {
            string old = TakeOrphan(s.Section, "Visibility");
            if (old != null && old.Trim() == "Hidden") s.Visible.Value = false;
        }

        private void DropRetiredOptions()
        {
            TakeOrphan("00 General", "SnapToGrid");
            TakeOrphan("00 General", "GridStep");
            TakeOrphan("00 General", "LastPreset");
            if (_orphansDropped)
            {
                _orphansDropped = false;
                Config.Save();
            }
        }

        internal ElementSettings Get(ElementId id)
        {
            return Elements[(int)id];
        }

        private ElementSettings OwnerOf(ConfigEntryBase entry, out bool isStyleEntry)
        {
            isStyleEntry = false;
            foreach (ElementSettings s in AllSettings())
            {
                if (s.LayoutEntries.Contains(entry)) return s;
                if (s.StyleEntries.Contains(entry)) { isStyleEntry = true; return s; }
            }
            return null;
        }

        // ------------------------------------------------------------------
        // Style <-> options. Picking a style writes its options; editing an option by hand
        // selects whichever style now matches (usually Custom). _applying suppresses both
        // while we write several entries ourselves.
        // ------------------------------------------------------------------
        private int _applying;

        private void OnSettingChanged(object sender, SettingChangedEventArgs args)
        {
            try
            {
                ConfigEntryBase entry = args.ChangedSetting;
                bool isStyle;
                ElementSettings s = OwnerOf(entry, out isStyle);
                if (s == null) return;
                if (entry == s.BarColor) ParseColor(s);
                if (_applying > 0 || !isStyle) return;

                if (entry == s.Style) ApplyStyle(s, s.Style.Value);
                else DetectStyle(s);
            }
            catch (Exception e) { Fail("SettingChanged", e); }
        }

        private static void ParseColor(ElementSettings s)
        {
            s.HasColor = false;
            if (s.BarColor == null) return;
            string v = (s.BarColor.Value ?? "").Trim();
            if (v.Length == 0) return;
            if (!v.StartsWith("#")) v = "#" + v;
            Color c;
            if (ColorUtility.TryParseHtmlString(v, out c)) { s.HasColor = true; s.Color = c; }
        }

        // Runs body with the style bookkeeping switched off and the config saved once at the end
        // instead of after every entry.
        internal void Batch(Action body)
        {
            bool save = Config.SaveOnConfigSet;
            Config.SaveOnConfigSet = false;
            _applying++;
            try { body(); }
            finally
            {
                _applying--;
                Config.SaveOnConfigSet = save;
                if (save) Config.Save();
            }
        }

        // The value an entry would have after being set from this text, as text; null if the
        // text does not parse. Used to compare values regardless of formatting.
        internal static string Normalize(ConfigEntryBase entry, string text)
        {
            try
            {
                object v = TomlTypeConverter.ConvertToValue(text, entry.SettingType);
                if (entry.Description != null && entry.Description.AcceptableValues != null)
                    v = entry.Description.AcceptableValues.Clamp(v);
                return TomlTypeConverter.ConvertToString(v, entry.SettingType);
            }
            catch { return null; }
        }

        internal static string DefaultText(ConfigEntryBase entry)
        {
            return TomlTypeConverter.ConvertToString(entry.DefaultValue, entry.SettingType);
        }

        internal static string F(float v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
