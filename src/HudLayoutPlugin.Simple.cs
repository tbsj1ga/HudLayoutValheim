using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace HudLayout
{
    // A simple element: moved, scaled, hidden and faded as a whole, nothing more. The rest of
    // the vanilla HUD (hotbar, guardian power, status effects, minimap...), elements other mods
    // put into hudroot (found by a periodic scan) and elements registered through HudLayoutApi.
    //
    // Wrapped like the bars: the element goes into a wrapper of ours stretched over its own
    // parent (not necessarily hudroot), so whatever the game or the mod does to it keeps
    // working. Its frame in the editor is measured from the graphics inside it, since several
    // of these objects are empty containers stretched over the whole screen.
    internal sealed class SimpleDef
    {
        public string Key, En, Ru;
        public Func<Hud, RectTransform, RectTransform> Find;   // (hud, hudroot) -> the object, or null (not there yet)
        public bool Direct;         // leave it in place in the hierarchy (someone finds it by path)
    }

    public partial class HudLayoutPlugin
    {
        private ConfigEntry<bool> _cfgModElements;
        private ConfigEntry<string> _cfgIgnore;

        // hudroot's children as the game ships them: anything else there is another mod's
        private static readonly HashSet<string> VanillaRootChildren = new HashSet<string>(StringComparer.Ordinal)
        {
            "Inventory tip", "HotKeyBar", "staminapanel", "adrenalinepanel", "eitrpanel", "healthpanel",
            "staggerpanel", "crosshair", "GuardianPower", "BuildHud", "ShipHud", "BuildUIV2", "MountHud",
            "StatusEffects", "KeyHints", "MiniMap", "Damaged", "LavaWarning", "EventBar", "action_progress",
            "ValheimRadial", "SaveIcon", "BadConnectionIcon",
        };

        private static RectTransform Rt(GameObject go) { return go != null ? go.transform as RectTransform : null; }
        private static RectTransform Rt(Component c) { return c != null ? c.transform as RectTransform : null; }

        private static readonly SimpleDef[] VanillaSimple =
        {
            // ExtraSlots finds it as hudroot.Find("HotKeyBar") to copy it: moved directly, not wrapped
            Direct(Def("HotKeyBar", "Hotbar", "Панель быстрого доступа", delegate(Hud h, RectTransform r) { return r.Find("HotKeyBar") as RectTransform; })),
            Def("GuardianPower", "Forsaken power", "Сила Отрёкшегося", delegate(Hud h, RectTransform r) { return h.m_gpRoot; }),
            Def("StatusEffects", "Status effects", "Эффекты", delegate(Hud h, RectTransform r) { return h.m_statusEffectListRoot; }),
            Def("Minimap", "Minimap", "Мини-карта", delegate(Hud h, RectTransform r) { return Minimap.instance != null ? Rt(Minimap.instance.m_smallRoot) : null; }),
            Def("EventBar", "Raid bar", "Полоса рейда", delegate(Hud h, RectTransform r) { return Rt(h.m_eventBar); }),
            Def("ActionProgress", "Action progress", "Прогресс действия", delegate(Hud h, RectTransform r) { return Rt(h.m_actionBarRoot); }),
            Def("Stagger", "Stagger bar", "Полоса оглушения", delegate(Hud h, RectTransform r) { return Rt(h.m_staggerAnimator); }),
            Def("Mount", "Mount panel", "Панель верхового", delegate(Hud h, RectTransform r) { return Rt(h.m_mountPanel); }),
            Def("Ship", "Ship controls", "Управление кораблём", delegate(Hud h, RectTransform r) { return Rt(h.m_shipHudRoot); }),
            Def("KeyHints", "Key hints", "Подсказки клавиш", delegate(Hud h, RectTransform r) { return r.Find("KeyHints") as RectTransform; }),
            Def("CenterMessage", "Centre message", "Сообщение по центру", delegate(Hud h, RectTransform r) { return MessageHud.instance != null ? Rt(MessageHud.instance.m_messageCenterText) : null; }),
            Def("Messages", "Top left messages", "Сообщения слева сверху", delegate(Hud h, RectTransform r) { return MessageHud.instance != null ? Rt(MessageHud.instance.m_messageText) : null; }),
            Def("SaveIcon", "Save icon", "Значок сохранения", delegate(Hud h, RectTransform r) { return Rt(h.m_saveIcon); }),
            Def("BadConnection", "Bad connection icon", "Значок плохой связи", delegate(Hud h, RectTransform r) { return Rt(h.m_badConnectionIcon); }),
        };

        private static SimpleDef Direct(SimpleDef d)
        {
            d.Direct = true;
            return d;
        }

        private static SimpleDef Def(string key, string en, string ru, Func<Hud, RectTransform, RectTransform> find)
        {
            SimpleDef d = new SimpleDef();
            d.Key = key; d.En = en; d.Ru = ru; d.Find = find;
            return d;
        }

        // ------------------------------------------------------------------
        // settings
        // ------------------------------------------------------------------
        private void BindVanillaSimple()
        {
            _cfgModElements = Config.Bind("00 General", "ModElements", true,
                "Also make movable what other mods put into the HUD (e.g. ExtraSlots' hotbars). Each gets a section '30 Mod <name>' when first seen.");
            _cfgIgnore = Config.Bind("00 General", "IgnoreModElements", "",
                "Names of other mods' HUD objects to leave alone, comma-separated (as in the '30 Mod <name>' sections).");
            for (int i = 0; i < VanillaSimple.Length; i++)
            {
                SimpleDef d = VanillaSimple[i];
                Extra.Add(BindSimple(d.Key, (10 + i).ToString("00") + " " + d.Key, d.En, d.Ru, false));
            }
        }

        private ElementSettings BindSimple(string key, string section, string en, string ru, bool isMod)
        {
            ElementSettings s = new ElementSettings();
            s.Id = ElementId.Other; s.Key = key; s.Section = section; s.IsSimple = true;
            s.LabelEn = en; s.LabelRu = ru;
            string what = isMod ? "'" + en + "' (another mod's)" : en.ToLowerInvariant();
            s.PosX = Config.Bind(section, "PositionX", -1f,
                new ConfigDescription("Horizontal position of the " + what + ", fraction of the screen width. -1 = where it normally is.",
                    new AcceptableValueRange<float>(-1f, 1f)));
            s.PosY = Config.Bind(section, "PositionY", -1f,
                new ConfigDescription("Vertical position of the " + what + ", fraction of the screen height. -1 = where it normally is.",
                    new AcceptableValueRange<float>(-1f, 1f)));
            s.Scale = Config.Bind(section, "Scale", 1f,
                new ConfigDescription("Size of the " + what + ".", new AcceptableValueRange<float>(0.25f, 4f)));
            s.Vis = Config.Bind(section, "Visibility", Visibility.Vanilla, "Vanilla = shown as usual. Hidden = not shown.");
            s.Opacity = Config.Bind(section, "Opacity", 1f,
                new ConfigDescription("Opacity of the " + what + ".", new AcceptableValueRange<float>(0.05f, 1f)));
            s.LayoutEntries.Add(s.PosX); s.LayoutEntries.Add(s.PosY); s.LayoutEntries.Add(s.Scale);
            s.StyleEntries.Add(s.Vis); s.StyleEntries.Add(s.Opacity);
            return s;
        }

        // An element found after a preset was applied still gets the preset's values.
        private Preset _pendingPreset;
        private PresetPart _pendingPart;

        private ElementSettings BindLate(string key, string en, string ru)
        {
            ElementSettings s = FindSettings(key);
            if (s != null) return s;
            s = BindSimple(key, "30 Mod " + key.Substring(key.IndexOf('.') + 1), en, ru, true);
            Extra.Add(s);
            if (_pendingPreset != null)
            {
                Preset p = _pendingPreset;
                PresetPart part = _pendingPart;
                Batch(delegate
                {
                    if (part != PresetPart.Style) ApplyEntries(p, s, s.LayoutEntries);
                    if (part != PresetPart.Layout) ApplyEntries(p, s, s.StyleEntries);
                });
            }
            return s;
        }

        // ------------------------------------------------------------------
        // wrapping
        // ------------------------------------------------------------------
        internal HudElement HudElementOf(ElementSettings s)
        {
            foreach (HudElement e in _hudElements) if (e.Settings == s) return e;
            return null;
        }

        // "ExtraSlotsQuickSlotsHotBar" -> "Extra Slots Quick Slots Hot Bar": a name that can wrap.
        private static string Words(string name)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i > 0 && char.IsUpper(c) && char.IsLower(name[i - 1])) sb.Append(' ');
                sb.Append(c == '_' ? ' ' : c);
            }
            return sb.ToString();
        }

        private static bool IsOurs(Transform t)
        {
            return t != null && t.name.StartsWith("HudLayout_", StringComparison.Ordinal);
        }

        // Wraps target in place (or, direct, leaves it there); null if it cannot be.
        private HudElement AttachSimple(ElementSettings s, RectTransform target, bool direct)
        {
            if (target == null || HudElementOf(s) != null) return null;
            RectTransform parent = target.parent as RectTransform;
            if (IsOurs(target.parent)) return null;
            if (!direct && parent == null) return null;   // a wrapper needs a RectTransform to stretch over
            foreach (HudElement other in _hudElements) if (other.Target == target) return null;
            HudElement e = new HudElement();
            e.Settings = s;
            if (direct)
            {
                e.Direct = true;
                e.Group = target.GetComponent<CanvasGroup>();
                if (e.Group == null) e.Group = target.gameObject.AddComponent<CanvasGroup>();
            }
            else
            {
                e.Wrapper = MakeWrapper("HudLayout_" + s.Key, parent, target.GetSiblingIndex());
                e.Group = e.Wrapper.gameObject.AddComponent<CanvasGroup>();
                target.SetParent(e.Wrapper, false);
            }
            e.Target = target;
            e.AutoBounds = true;
            RefreshGraphics(e);
            _hudElements.Add(e);
            return e;
        }

        private static void RefreshGraphics(HudElement e)
        {
            e.Graphics.Clear();
            if (e.Target == null) return;
            e.Target.GetComponentsInChildren<Graphic>(true, e.Graphics);
            e.GraphicsAt = Time.unscaledTime;
        }

        // The vanilla ones whose objects exist; some (minimap, messages) appear after the HUD.
        private void AttachVanillaSimple()
        {
            foreach (SimpleDef d in VanillaSimple)
            {
                ElementSettings s = FindSettings(d.Key);
                if (s == null || HudElementOf(s) != null) continue;
                try { AttachSimple(s, d.Find(_hud, _root), d.Direct); }
                catch (Exception ex) { Logger.LogWarning("Could not wrap " + d.Key + ": " + ex.Message); }
            }
        }

        private float _nextScan;

        // Every couple of seconds: vanilla elements that appeared late, other mods' objects in
        // hudroot, elements registered through the API.
        private void ScanLate()
        {
            if (Time.unscaledTime < _nextScan || _root == null) return;
            _nextScan = Time.unscaledTime + 2f;
            AttachVanillaSimple();
            AttachApiElements();
            if (!_cfgModElements.Value) return;

            HashSet<string> ignore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string n in (_cfgIgnore.Value ?? "").Split(','))
                if (n.Trim().Length > 0) ignore.Add(n.Trim());

            List<RectTransform> found = new List<RectTransform>();
            foreach (Transform ch in _root)
            {
                if (IsOurs(ch) || VanillaRootChildren.Contains(ch.name) || ignore.Contains(ch.name)) continue;
                if (IsWrappedTarget(ch)) continue;
                RectTransform rt = ch as RectTransform;
                if (rt == null || rt.GetComponentInChildren<Graphic>(true) == null) continue;   // nothing to see
                found.Add(rt);
            }
            foreach (RectTransform rt in found)
            {
                string label = Words(rt.name);
                ElementSettings s = BindLate("Mod." + rt.name, label, label);
                if (HudElementOf(s) != null) continue;   // two objects of one name: the first one
                try
                {
                    // other mods' objects stay where their code expects them: moved directly
                    if (AttachSimple(s, rt, true) != null) Logger.LogInfo("Other mod's HUD element made movable: " + rt.name);
                }
                catch (Exception ex) { Logger.LogWarning("Could not wrap " + rt.name + ": " + ex.Message); }
            }
        }

        // Elements registered through HudLayoutApi. A new object under a known id (the mod
        // rebuilt it) replaces the old one; the settings stay.
        private void AttachApiElements()
        {
            if (HudLayoutApi.Entries.Count == 0) return;
            foreach (HudLayoutApi.Entry a in new List<HudLayoutApi.Entry>(HudLayoutApi.Entries.Values))
            {
                if (a.Target == null) continue;   // destroyed; waits for a new Register
                ElementSettings s = BindLate("Api." + a.Id, a.Name, a.Name);
                HudElement cur = HudElementOf(s);
                if (cur != null)
                {
                    if (cur.Target == a.Target) continue;
                    Unhook(cur);
                }
                // found by the hudroot scan before its mod registered it: the registration wins
                foreach (HudElement other in new List<HudElement>(_hudElements))
                    if (other.Target == a.Target) Unhook(other);
                try
                {
                    if (AttachSimple(s, a.Target, !a.Wrap) != null) Logger.LogInfo("HUD element registered through the API: " + a.Id);
                }
                catch (Exception ex) { Logger.LogWarning("Could not take " + a.Id + ": " + ex.Message); }
            }
        }

        internal void ForgetApiElement(string id)
        {
            ElementSettings s = FindSettings("Api." + id);
            HudElement e = s != null ? HudElementOf(s) : null;
            if (e != null) Unhook(e);
        }

        // Lets go of an element: a direct one gets its own pose back, a wrapped one keeps its
        // wrapper (taking it out could upset its owner) but with no offset.
        private void Unhook(HudElement e)
        {
            if (e.Direct && e.Target != null && e.HasWritten)
            {
                e.Target.localPosition = e.BaseLp;
                e.Target.localScale = e.BaseScale;
                e.Target.localRotation = e.BaseRot;
            }
            else if (e.Wrapper != null)
            {
                e.Wrapper.localPosition = Vector3.zero;
                e.Wrapper.localRotation = Quaternion.identity;
                e.Wrapper.localScale = Vector3.one;
            }
            if (e.Group != null) e.Group.alpha = 1f;
            _hudElements.Remove(e);
        }

        private bool IsWrappedTarget(Transform t)
        {
            foreach (HudElement e in _hudElements) if (e.Target == t) return true;
            return false;
        }

        // A simple element's target was destroyed (a mod rebuilt its object): forget it, the
        // next scan wraps the new one. The wrapper goes too.
        private void DropDead()
        {
            for (int i = _hudElements.Count - 1; i >= 0; i--)
            {
                HudElement e = _hudElements[i];
                if (!e.AutoBounds || e.Target != null) continue;
                if (e.Wrapper != null) Destroy(e.Wrapper.gameObject);
                _hudElements.RemoveAt(i);
            }
        }
    }
}
