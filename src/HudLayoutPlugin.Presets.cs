using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;

namespace HudLayout
{
    // A saved state of the whole HUD: values of the elements' options by "<Element>.<Key>".
    // Built-in presets live in the code; the player's own are text files in
    // BepInEx/config/j1ga.hudlayout.presets, one per preset, readable and editable by hand.
    internal sealed class Preset
    {
        public string Name;
        public string NameRu;       // built-in only
        public bool BuiltIn;
        public string File;         // user only
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    // Which part of a preset to apply: everything, only positions and sizes, or only looks.
    public enum PresetPart { All, Layout, Style }

    public partial class HudLayoutPlugin
    {
        private readonly List<Preset> _builtIn = new List<Preset>();
        private readonly List<Preset> _user = new List<Preset>();

        private const string ExportPrefix = "HUDLAYOUT1:";
        private const string VanillaPreset = "Vanilla";

        private static string PresetDir
        {
            get { return Path.Combine(Paths.ConfigPath, Guid + ".presets"); }
        }

        internal IEnumerable<Preset> AllPresets()
        {
            foreach (Preset p in _builtIn) yield return p;
            foreach (Preset p in _user) yield return p;
        }

        internal Preset FindPreset(string name)
        {
            foreach (Preset p in AllPresets())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        internal static string PresetLabel(Preset p)
        {
            return p.BuiltIn && p.NameRu != null ? L(p.Name, p.NameRu) : p.Name;
        }

        // ------------------------------------------------------------------
        // built-in presets
        // ------------------------------------------------------------------
        private Preset NewBuiltIn(string name, string ru)
        {
            Preset p = new Preset();
            p.Name = name; p.NameRu = ru; p.BuiltIn = true;
            // start from the defaults, so a built-in preset always describes every option
            foreach (ElementSettings s in Elements)
                foreach (ConfigEntryBase e in s.AllEntries())
                    p.Values[s.Key + "." + e.Definition.Key] = DefaultText(e);
            _builtIn.Add(p);
            return p;
        }

        private static void Set(Preset p, ElementId id, string key, string value)
        {
            p.Values[id + "." + key] = value;
        }

        private static void Place(Preset p, ElementId id, float x, float y, Orientation o, BarAnchor a)
        {
            Set(p, id, "PositionX", F(x));
            Set(p, id, "PositionY", F(y));
            Set(p, id, "Orientation", o.ToString());
            if (id != ElementId.Food) Set(p, id, "Anchor", a.ToString());
        }

        private void UseStyle(Preset p, ElementId id, string style)
        {
            foreach (StyleDef d in StylesOf(Get(id)))
            {
                if (d.Name != style) continue;
                Set(p, id, "Style", style);
                foreach (KeyValuePair<string, string> kv in d.Values) Set(p, id, kv.Key, kv.Value);
            }
        }

        private void UseBarStyle(Preset p, string style)
        {
            UseStyle(p, ElementId.Health, style);
            UseStyle(p, ElementId.Stamina, style);
            UseStyle(p, ElementId.Eitr, style);
            UseStyle(p, ElementId.Adrenaline, style);
        }

        // The bars in a stack at the bottom centre, the food as a row above them.
        private static void CenteredLayout(Preset p)
        {
            Place(p, ElementId.Food, 0.5f, 0.235f, Orientation.Horizontal, BarAnchor.Center);
            Place(p, ElementId.Health, 0.5f, 0.17f, Orientation.Horizontal, BarAnchor.Center);
            Place(p, ElementId.Stamina, 0.5f, 0.135f, Orientation.Horizontal, BarAnchor.Center);
            Place(p, ElementId.Eitr, 0.5f, 0.11f, Orientation.Horizontal, BarAnchor.Center);
            Place(p, ElementId.Adrenaline, 0.5f, 0.085f, Orientation.Horizontal, BarAnchor.Center);
        }

        private void BuildPresets()
        {
            _builtIn.Clear();
            NewBuiltIn(VanillaPreset, "Как в игре");

            Preset p = NewBuiltIn("Centered", "По центру");
            CenteredLayout(p);

            p = NewBuiltIn("CenteredMinimal", "По центру, минимум");
            CenteredLayout(p);
            UseBarStyle(p, "Minimal");
            UseStyle(p, ElementId.Food, "IconsOnly");
            foreach (ElementId id in new[] { ElementId.Health, ElementId.Stamina, ElementId.Eitr, ElementId.Adrenaline, ElementId.Food })
                Set(p, id, "Scale", "0.85");

            // Everything in the bottom left corner: horizontal bars growing to the right.
            p = NewBuiltIn("BottomLeft", "Слева внизу");
            Place(p, ElementId.Food, 0.07f, 0.235f, Orientation.Horizontal, BarAnchor.Center);
            Place(p, ElementId.Health, 0.025f, 0.17f, Orientation.Horizontal, BarAnchor.Start);
            Place(p, ElementId.Stamina, 0.025f, 0.135f, Orientation.Horizontal, BarAnchor.Start);
            Place(p, ElementId.Eitr, 0.025f, 0.11f, Orientation.Horizontal, BarAnchor.Start);
            Place(p, ElementId.Adrenaline, 0.025f, 0.085f, Orientation.Horizontal, BarAnchor.Start);

            p = NewBuiltIn("Minimal", "Минимум");
            UseBarStyle(p, "Minimal");
            UseStyle(p, ElementId.Food, "IconsOnly");

            p = NewBuiltIn("Numbers", "Числа");
            UseBarStyle(p, "Numeric");
        }

        // ------------------------------------------------------------------
        // current state <-> preset
        // ------------------------------------------------------------------
        internal Preset Capture(string name)
        {
            Preset p = new Preset();
            p.Name = name;
            foreach (ElementSettings s in Elements)
                foreach (ConfigEntryBase e in s.AllEntries())
                    p.Values[s.Key + "." + e.Definition.Key] = e.GetSerializedValue();
            return p;
        }

        internal void ApplyPreset(Preset p, PresetPart part)
        {
            Batch(delegate
            {
                foreach (ElementSettings s in Elements)
                {
                    if (part != PresetPart.Style) ApplyEntries(p, s, s.LayoutEntries);
                    if (part != PresetPart.Layout)
                    {
                        ApplyEntries(p, s, s.StyleEntries);
                        ParseColor(s);
                    }
                }
                _cfgLastPreset.Value = p.Name + (part == PresetPart.All ? "" : " (" + part + ")");
            });
            // a preset from an older version may lack Style or carry a stale one
            if (part != PresetPart.Layout)
                foreach (ElementSettings s in Elements) DetectStyle(s);
        }

        // An option the preset does not mention goes back to its default: a preset always
        // describes the whole state, also when written by hand with only a few lines.
        private static void ApplyEntries(Preset p, ElementSettings s, List<ConfigEntryBase> entries)
        {
            foreach (ConfigEntryBase e in entries)
            {
                string v;
                if (!p.Values.TryGetValue(s.Key + "." + e.Definition.Key, out v) || Normalize(e, v) == null)
                    v = DefaultText(e);
                e.SetSerializedValue(v);
            }
        }

        // ------------------------------------------------------------------
        // text form: "Key = value" lines, the same in files, exports and imports
        // ------------------------------------------------------------------
        internal static string ToText(Preset p)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("# ").Append(Name).Append(" preset. Lines are <Element>.<Option> = value, as in ").Append(Guid).Append(".cfg;\n");
            sb.Append("# an option left out takes its default.\n");
            sb.Append("Name = ").Append(p.Name).Append('\n');
            foreach (KeyValuePair<string, string> kv in p.Values)
                sb.Append(kv.Key).Append(" = ").Append(kv.Value).Append('\n');
            return sb.ToString();
        }

        internal static Preset FromText(string text)
        {
            Preset p = new Preset();
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                if (key.Equals("Name", StringComparison.OrdinalIgnoreCase)) p.Name = value;
                else p.Values[key] = value;
            }
            return p;
        }

        internal static string Export(Preset p)
        {
            return ExportPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(ToText(p)));
        }

        // Accepts an export string or the plain text of a preset file. Null if neither.
        internal static Preset Import(string data)
        {
            if (string.IsNullOrEmpty(data)) return null;
            string s = data.Trim();
            string text;
            if (s.StartsWith(ExportPrefix, StringComparison.OrdinalIgnoreCase))
            {
                try { text = Encoding.UTF8.GetString(Convert.FromBase64String(s.Substring(ExportPrefix.Length).Trim())); }
                catch { return null; }
            }
            else text = s;
            Preset p = FromText(text);
            if (p.Values.Count == 0) return null;
            if (string.IsNullOrEmpty(p.Name)) p.Name = "Imported";
            return p;
        }

        // ------------------------------------------------------------------
        // the player's presets on disk
        // ------------------------------------------------------------------
        private void LoadUserPresets()
        {
            _user.Clear();
            try
            {
                if (!Directory.Exists(PresetDir)) return;
                foreach (string file in Directory.GetFiles(PresetDir, "*.txt"))
                {
                    try
                    {
                        Preset p = FromText(File.ReadAllText(file, Encoding.UTF8));
                        if (string.IsNullOrEmpty(p.Name)) p.Name = Path.GetFileNameWithoutExtension(file);
                        if (FindPreset(p.Name) != null) { Logger.LogWarning("Preset '" + p.Name + "' in " + file + " has the name of another one; skipped."); continue; }
                        p.File = file;
                        _user.Add(p);
                    }
                    catch (Exception e) { Logger.LogWarning("Could not read preset " + file + ": " + e.Message); }
                }
                _user.Sort(delegate(Preset a, Preset b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            }
            catch (Exception e) { Logger.LogWarning("Could not read presets from " + PresetDir + ": " + e.Message); }
        }

        private static string SafeFileName(string name)
        {
            StringBuilder sb = new StringBuilder();
            char[] bad = Path.GetInvalidFileNameChars();
            foreach (char c in name) sb.Append(Array.IndexOf(bad, c) >= 0 ? '_' : c);
            string s = sb.ToString().Trim().TrimEnd('.');
            return s.Length == 0 ? "preset" : s;
        }

        // Saves the preset under its name (a user preset of that name is overwritten).
        // Returns an error text, or null.
        internal string SaveUserPreset(Preset p)
        {
            string name = (p.Name ?? "").Trim();
            if (name.Length == 0) return L("Enter a name.", "Введите имя.");
            p.Name = name;
            Preset existing = FindPreset(name);
            if (existing != null && existing.BuiltIn) return L("A built-in preset has this name.", "Так называется встроенный пресет.");
            Directory.CreateDirectory(PresetDir);
            string file = existing != null && existing.File != null ? existing.File : Path.Combine(PresetDir, SafeFileName(name) + ".txt");
            File.WriteAllText(file, ToText(p), new UTF8Encoding(false));
            LoadUserPresets();
            return null;
        }

        internal string DeleteUserPreset(string name)
        {
            Preset p = FindPreset(name);
            if (p == null) return L("No such preset.", "Нет такого пресета.");
            if (p.BuiltIn) return L("Built-in presets cannot be deleted.", "Встроенные пресеты не удаляются.");
            if (p.File != null && File.Exists(p.File)) File.Delete(p.File);
            LoadUserPresets();
            return null;
        }

        // An imported preset keeps its name unless it is taken; then " (2)", " (3)"...
        internal string FreeName(string name)
        {
            if (FindPreset(name) == null) return name;
            for (int i = 2; ; i++)
            {
                string n = name + " (" + i + ")";
                if (FindPreset(n) == null) return n;
            }
        }
    }
}
