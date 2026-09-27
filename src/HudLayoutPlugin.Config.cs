using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace HudLayout
{
    public enum ElementId { Health, Stamina, Eitr, Adrenaline, Food }

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

        public ConfigEntry<float> PosX, PosY, Scale, Length, Thickness, Opacity, TextSize;
        public ConfigEntry<bool> FixedLength, Icon, Segments;
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
        private ConfigEntry<bool> _cfgSnap;
        private ConfigEntry<float> _cfgGrid;
        private ConfigEntry<bool> _cfgBuildShift;
        private ConfigEntry<string> _cfgLastPreset;
        private static ConfigEntry<string> _cfgLanguage;

        internal readonly ElementSettings[] Elements = new ElementSettings[5];

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

        private void BindConfig()
        {
            const string g = "00 General";
            _cfgEnabled = Config.Bind(g, "Enabled", true,
                "Apply the layout and styles. Off = the HUD exactly as the game draws it (settings are kept).");
            _cfgEditKey = Config.Bind(g, "EditModeKey", new KeyboardShortcut(KeyCode.F7),
                "Opens and closes the in-game edit mode: drag the elements, resize them by the corners, pick styles and presets. Esc also closes it.");
            _cfgSnap = Config.Bind(g, "SnapToGrid", true,
                "In edit mode, snap positions to the grid and scale to steps of 0.05. Hold Ctrl while dragging to place freely.");
            _cfgGrid = Config.Bind(g, "GridStep", 0.005f,
                new ConfigDescription("Grid step, as a fraction of the screen.", new AcceptableValueRange<float>(0.001f, 0.05f)));
            _cfgBuildShift = Config.Bind(g, "FollowBuildShift", true,
                "In build mode and at a ship's helm the game lifts the stamina, eitr and adrenaline bars so the build panel does not cover them. On = moved bars get lifted by the same amount.");
            _cfgLastPreset = Config.Bind(g, "LastPreset", "",
                "The preset applied last (for information; applying one again is done in edit mode or with 'hudlayout apply').");
            _cfgLanguage = Config.Bind(g, "Language", "Auto",
                new ConfigDescription("Language of the edit mode. Auto follows the game.", new AcceptableValueList<string>("Auto", "English", "Russian")));

            Elements[0] = BindElement(ElementId.Health, "01 Health", "Health", true, true, BarAnchor.Start);
            Elements[1] = BindElement(ElementId.Stamina, "02 Stamina", "Stamina", true, false, BarAnchor.Center);
            Elements[2] = BindElement(ElementId.Eitr, "03 Eitr", "Eitr", true, false, BarAnchor.Center);
            Elements[3] = BindElement(ElementId.Adrenaline, "04 Adrenaline", "Adrenaline", true, false, BarAnchor.Center);
            Elements[4] = BindElement(ElementId.Food, "05 Food", "Food", false, true, BarAnchor.Center);

            foreach (ElementSettings s in Elements) ParseColor(s);
            Config.SettingChanged += OnSettingChanged;
        }

        private ElementSettings BindElement(ElementId id, string section, string key, bool isBar, bool nativeVertical, BarAnchor anchor)
        {
            ElementSettings s = new ElementSettings();
            s.Id = id; s.Key = key; s.Section = section; s.IsBar = isBar; s.NativeVertical = nativeVertical;
            string what = isBar ? key.ToLowerInvariant() + " bar" : "food icons";

            // --- layout
            s.PosX = Config.Bind(section, "PositionX", -1f,
                new ConfigDescription("Horizontal position of the " + what + ", fraction of the screen width (0 = left edge, 1 = right). -1 = where the game puts it.",
                    new AcceptableValueRange<float>(-1f, 1f)));
            s.PosY = Config.Bind(section, "PositionY", -1f,
                new ConfigDescription("Vertical position of the " + what + ", fraction of the screen height (0 = bottom, 1 = top). -1 = where the game puts it.",
                    new AcceptableValueRange<float>(-1f, 1f)));
            s.Scale = Config.Bind(section, "Scale", 1f,
                new ConfigDescription("Size of the " + what + ". 1 = as in the game.", new AcceptableValueRange<float>(0.25f, 4f)));
            s.Orient = Config.Bind(section, "Orientation", Orientation.Default,
                isBar ? "Horizontal or vertical bar. Default = as in the game (health vertical, the others horizontal). The numbers stay upright."
                      : "A column (Vertical, as in the game) or a row (Horizontal) of food icons.");
            s.LayoutEntries.Add(s.PosX); s.LayoutEntries.Add(s.PosY); s.LayoutEntries.Add(s.Scale); s.LayoutEntries.Add(s.Orient);
            if (isBar)
            {
                s.Anchor = Config.Bind(section, "Anchor", anchor,
                    "Which point of the bar stays at the position while the bar grows with the maximum: Start (left / bottom), Center or End. Used when a position is set.");
                s.Length = Config.Bind(section, "Length", 1f,
                    new ConfigDescription("Length multiplier of the bar (the game makes it longer as the maximum grows). 1 = as in the game.",
                        new AcceptableValueRange<float>(0.25f, 4f)));
                s.Thickness = Config.Bind(section, "Thickness", 1f,
                    new ConfigDescription("Thickness of the bar (across it), independent of the length. The number keeps its size. 1 = as in the game.",
                        new AcceptableValueRange<float>(0.25f, 4f)));
                s.FixedLength = Config.Bind(section, "FixedLength", false,
                    "On = the bar keeps one length (the game's base length × Length) whatever the maximum; a bigger maximum only changes the number (use Text = CurrentMax to see it). Off = it grows with the maximum as in the game.");
                s.LayoutEntries.Add(s.Anchor); s.LayoutEntries.Add(s.Length); s.LayoutEntries.Add(s.Thickness); s.LayoutEntries.Add(s.FixedLength);
            }

            // --- style
            string[] styles = StyleNamesFor(isBar);
            s.Style = Config.Bind(section, "Style", "Vanilla",
                new ConfigDescription("A ready-made look for the " + what + ". Picking one sets the options below; changing any of them makes it Custom.",
                    new AcceptableValueList<string>(styles)));
            s.Vis = Config.Bind(section, "Visibility", Visibility.Vanilla,
                isBar ? "Vanilla = as in the game (stamina, eitr and adrenaline fade out when not in use). Always = never fade out. Hidden = not shown. NotFull = hidden while full, shown as soon as it drops below 100%."
                      : "Vanilla = shown. Hidden = not shown.");
            s.Opacity = Config.Bind(section, "Opacity", 1f,
                new ConfigDescription("Opacity of the " + what + ".", new AcceptableValueRange<float>(0.05f, 1f)));
            s.StyleEntries.Add(s.Style); s.StyleEntries.Add(s.Vis); s.StyleEntries.Add(s.Opacity);
            if (isBar)
            {
                s.Text = Config.Bind(section, "Text", TextMode.Vanilla,
                    "The number on the bar: Vanilla (as in the game), Hidden, Current, CurrentMax (75/100) or Percent.");
                s.TextSize = Config.Bind(section, "TextSize", 1f,
                    new ConfigDescription("Size of the number on the bar. 1 = as in the game.", new AcceptableValueRange<float>(0.5f, 3f)));
                s.BarColor = Config.Bind(section, "BarColor", "",
                    "Colour of the bar as #RRGGBB or #RRGGBBAA. Empty = the game's colour.");
                s.TextPos = Config.Bind(section, "TextPosition", TextPosition.Vanilla,
                    "Where the number sits: Vanilla (health: middle of the filled part, the others: middle of the bar), Center (always the middle of the bar) or Fill (middle of the filled part, moves as it empties).");
                s.Segments = Config.Bind(section, "Segments", false,
                    "Divide the bar into cells of SegmentSize points each: a bigger maximum means more cells (with FixedLength the cells get narrower instead of the bar longer).");
                s.SegmentSize = Config.Bind(section, "SegmentSize", 10f,
                    new ConfigDescription("Points per cell when Segments is on.", new AcceptableValueRange<float>(1f, 100f)));
                s.StyleEntries.Add(s.Text); s.StyleEntries.Add(s.TextSize); s.StyleEntries.Add(s.TextPos); s.StyleEntries.Add(s.BarColor);
                s.StyleEntries.Add(s.Segments); s.StyleEntries.Add(s.SegmentSize);
            }
            else
            {
                s.Timers = Config.Bind(section, "Timers", true, "Show the time left on each food icon.");
                s.TextSize = Config.Bind(section, "TextSize", 1f,
                    new ConfigDescription("Size of the time left. 1 = as in the game.", new AcceptableValueRange<float>(0.5f, 3f)));
                s.StyleEntries.Add(s.Timers); s.StyleEntries.Add(s.TextSize);
            }
            if (id == ElementId.Health || id == ElementId.Food)
            {
                s.Icon = Config.Bind(section, "Icon", true,
                    id == ElementId.Health ? "Show the heart icon under the health bar." : "Show the food symbol under the food icons.");
                s.StyleEntries.Add(s.Icon);
            }
            return s;
        }

        internal ElementSettings Get(ElementId id)
        {
            return Elements[(int)id];
        }

        private ElementSettings OwnerOf(ConfigEntryBase entry, out bool isStyleEntry)
        {
            isStyleEntry = false;
            foreach (ElementSettings s in Elements)
            {
                if (s == null) continue;
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
