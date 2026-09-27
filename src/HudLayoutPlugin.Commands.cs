using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HudLayout
{
    public partial class HudLayoutPlugin
    {
        // ------------------------------------------------------------------
        // console command: hudlayout
        // ------------------------------------------------------------------
        private const string Usage =
            "hudlayout edit | presets | apply <name> [all|layout|style] | save <name> | delete <name> | export [name] | import | reset | dump";

        private void RegisterCommands()
        {
            new Terminal.ConsoleCommand("hudlayout",
                "HudLayout: " + Usage,
                delegate(Terminal.ConsoleEventArgs args) { RunCommand(args); },
                false, false, false, false, false, false,
                delegate { return new List<string> { "edit", "presets", "apply", "save", "delete", "export", "import", "reset", "dump" }; });
        }

        private static void Say(Terminal.ConsoleEventArgs args, string text)
        {
            if (args != null && args.Context != null) args.Context.AddString(text);
        }

        // Everything after the subcommand, as one name (names may have spaces).
        private static string Rest(string[] a, int from, int toExclusive)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = from; i < toExclusive && i < a.Length; i++)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(a[i]);
            }
            return sb.ToString().Trim();
        }

        private void RunCommand(Terminal.ConsoleEventArgs args)
        {
            try
            {
                string[] a = args.Args;
                string sub = a.Length > 1 ? a[1].ToLowerInvariant() : "";
                switch (sub)
                {
                    case "edit":
                    {
                        string err = StartEditing();
                        Say(args, err ?? L("Edit mode on. Close the console to use it.", "Редактор открыт. Закройте консоль, чтобы им пользоваться."));
                        return;
                    }
                    case "presets":
                    {
                        StringBuilder sb = new StringBuilder(L("Presets:", "Пресеты:"));
                        foreach (Preset p in AllPresets())
                            sb.Append("\n  ").Append(p.Name).Append(p.BuiltIn ? L("  (built-in)", "  (встроенный)") : "");
                        Say(args, sb.ToString());
                        return;
                    }
                    case "apply":
                    {
                        PresetPart part = PresetPart.All;
                        int end = a.Length;
                        if (a.Length > 3)
                        {
                            string last = a[a.Length - 1].ToLowerInvariant();
                            if (last == "all") { end--; }
                            else if (last == "layout") { part = PresetPart.Layout; end--; }
                            else if (last == "style") { part = PresetPart.Style; end--; }
                        }
                        string name = Rest(a, 2, end);
                        Preset p = FindPreset(name);
                        if (p == null) { Say(args, L("No preset '", "Нет пресета '") + name + "'. hudlayout presets"); return; }
                        ApplyPreset(p, part);
                        Say(args, L("Applied: ", "Применён: ") + p.Name + (part == PresetPart.All ? "" : " (" + part + ")"));
                        return;
                    }
                    case "save":
                    {
                        Preset p = Capture(Rest(a, 2, a.Length));
                        string err = SaveUserPreset(p);
                        Say(args, err ?? L("Saved: ", "Сохранён: ") + p.Name);
                        return;
                    }
                    case "delete":
                    {
                        string name = Rest(a, 2, a.Length);
                        string err = DeleteUserPreset(name);
                        Say(args, err ?? L("Deleted: ", "Удалён: ") + name);
                        return;
                    }
                    case "export":
                    {
                        string name = Rest(a, 2, a.Length);
                        Preset p = name.Length > 0 ? FindPreset(name) : Capture("Shared layout");
                        if (p == null) { Say(args, L("No preset '", "Нет пресета '") + name + "'"); return; }
                        GUIUtility.systemCopyBuffer = Export(p);
                        Say(args, L("Copied to the clipboard.", "Скопировано в буфер обмена."));
                        return;
                    }
                    case "import":
                    {
                        Preset p = Import(GUIUtility.systemCopyBuffer);
                        if (p == null) { Say(args, L("The clipboard holds no preset.", "В буфере нет пресета.")); return; }
                        p.Name = FreeName(p.Name);
                        string err = SaveUserPreset(p);
                        Say(args, err ?? L("Imported as: ", "Импортирован как: ") + p.Name + L(". Apply: hudlayout apply ", ". Применить: hudlayout apply ") + p.Name);
                        return;
                    }
                    case "reset":
                    {
                        ApplyPreset(FindPreset(VanillaPreset), PresetPart.All);
                        Say(args, L("Back to the game's HUD.", "HUD как в игре."));
                        return;
                    }
                    case "dump":
                        Say(args, DumpHud());
                        return;
                }
                Say(args, Usage);
            }
            catch (Exception e)
            {
                Say(args, "hudlayout: " + e.Message);
                Fail("command", e);
            }
        }
    }
}
