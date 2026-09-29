using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace HudLayout
{
    // The in-game edit mode (EditModeKey, F7 by default): frames over the elements to drag and
    // resize, and a window with the selected element's options, the presets and a few
    // settings. Everything it changes goes straight into the config entries, so the config
    // file, ConfigurationManager and the editor always show the same values. The file is
    // written once, when the edit mode closes.
    public partial class HudLayoutPlugin
    {
        private bool _editing;
        internal bool Editing { get { return _editing; } }

        private ElementSettings _sel;       // the selected element (null = the first one)

        private enum DragKind { None, Move, Scale, Edge, ResizeWindow }
        private DragKind _drag;
        private Vector2 _grab;              // pivot minus mouse, screen space
        private float _dragStartScale, _dragStartDist;
        private ConfigEntry<float> _edgeTarget;
        private bool _edgeAlongX;

        private Rect _win;                  // position; the size is _winW x _winH (GUILayout.Window would undo a size change made inside it)
        private float _winW = 470f, _winH = 700f;
        private bool _collapsed;            // the window folded to its title bar, to see and drag the HUD under it
        private const float TitleH = 26f;

        // The height the window has now: folded or not.
        private float WinH { get { return _collapsed ? TitleH : _winH; } }
        private bool _winPlaced;
        private Vector2 _scroll;
        private string _saveName = "";
        private string _colorBuf = "";
        private string _confirmDelete;
        private float _confirmUntil;
        private string _msg = "";
        private float _msgUntil;
        private int _escFrame = -1;
        private bool _savedSaveOnSet = true;

        private GUIStyle _labelStyle, _shadowStyle, _headerStyle, _hintStyle, _smallButton, _wrapLabel, _wrapToggle;
        private GUIStyle _windowStyle, _gridStyle;
        private Texture2D _windowBg;
        private float _windowBgAlpha = -1f;

        private const int WindowId = 0x4A1D7;

        // ------------------------------------------------------------------
        // open / close
        // ------------------------------------------------------------------
        private bool CanEdit()
        {
            return Active && _root != null && _hudElements.Count > 0 && Player.m_localPlayer != null;
        }

        internal string StartEditing()
        {
            if (_editing) return null;
            if (!CanEdit())
                return !Active ? L("HudLayout is disabled (00 General / Enabled).", "HudLayout выключен (00 General / Enabled).")
                               : L("Enter a world first.", "Сначала зайдите в мир.");
            if (Hud.IsUserHidden()) return L("The HUD is hidden (Ctrl+F3); show it first.", "HUD скрыт (Ctrl+F3); сначала покажите его.");
            _editing = true;
            _drag = DragKind.None;
            _savedSaveOnSet = Config.SaveOnConfigSet;
            Config.SaveOnConfigSet = false;
            Select(_sel ?? Get(ElementId.Health));
            return null;
        }

        internal void StopEditing()
        {
            if (!_editing) return;
            _editing = false;
            _drag = DragKind.None;
            _confirmDelete = null;
            GUIUtility.keyboardControl = 0;
            Config.SaveOnConfigSet = _savedSaveOnSet;
            try { Config.Save(); }
            catch (Exception e) { Logger.LogWarning("Could not save the config: " + e.Message); }
        }

        // Esc closes the editor instead of opening the game menu.
        internal bool BlockMenu
        {
            get { return _editing || Time.frameCount == _escFrame; }
        }

        private void Say(string text)
        {
            _msg = text;
            _msgUntil = Time.unscaledTime + 6f;
        }

        private void Select(ElementSettings s)
        {
            _sel = s;
            _colorBuf = s.BarColor != null ? s.BarColor.Value : "";
            GUIUtility.keyboardControl = 0;
        }

        internal static string ElementLabel(ElementSettings s)
        {
            if (s.Id == ElementId.Other)
                return s.Key.StartsWith("Mod.", StringComparison.Ordinal) || s.Key.StartsWith("Api.", StringComparison.Ordinal)
                    ? L("Mod: ", "Мод: ") + L(s.LabelEn, s.LabelRu ?? s.LabelEn)
                    : L(s.LabelEn, s.LabelRu ?? s.LabelEn);
            return ElementLabel(s.Id);
        }

        internal static string ElementLabel(ElementId id)
        {
            switch (id)
            {
                case ElementId.Health: return L("Health", "Здоровье");
                case ElementId.Stamina: return L("Stamina", "Выносливость");
                case ElementId.Eitr: return L("Eitr", "Эйтр");
                case ElementId.Adrenaline: return L("Adrenaline", "Адреналин");
                default: return L("Food", "Еда");
            }
        }

        // ------------------------------------------------------------------
        // screen geometry
        // ------------------------------------------------------------------
        private Camera UiCamera
        {
            get
            {
                if (_canvas == null) return null;
                Canvas c = _canvas.rootCanvas;
                return c == null || c.renderMode == RenderMode.ScreenSpaceOverlay ? null : c.worldCamera;
            }
        }

        private Vector2 ToScreen(HudElement e, Vector2 wrapperPoint)
        {
            return RectTransformUtility.WorldToScreenPoint(UiCamera, e.ToWorld(wrapperPoint));
        }

        private static Vector2 GuiToScreen(Vector2 g) { return new Vector2(g.x, Screen.height - g.y); }
        private static Vector2 ScreenToGui(Vector2 s) { return new Vector2(s.x, Screen.height - s.y); }

        // The element's frame on screen, in GUI coordinates (y down), at least 24 px each way.
        private Rect GuiRect(HudElement e)
        {
            Vector2 a = e.BMin, b = e.BMax;
            Vector2 p0 = ScreenToGui(ToScreen(e, new Vector2(a.x, a.y)));
            Vector2 p1 = ScreenToGui(ToScreen(e, new Vector2(a.x, b.y)));
            Vector2 p2 = ScreenToGui(ToScreen(e, new Vector2(b.x, b.y)));
            Vector2 p3 = ScreenToGui(ToScreen(e, new Vector2(b.x, a.y)));
            Vector2 min = Vector2.Min(Vector2.Min(p0, p1), Vector2.Min(p2, p3));
            Vector2 max = Vector2.Max(Vector2.Max(p0, p1), Vector2.Max(p2, p3));
            Rect r = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            if (r.width < 24f) { r.x -= (24f - r.width) * 0.5f; r.width = 24f; }
            if (r.height < 24f) { r.y -= (24f - r.height) * 0.5f; r.height = 24f; }
            return r;
        }

        private Vector2 PivotScreen(HudElement e)
        {
            return ToScreen(e, e.Pivot);
        }

        // Screen point -> position as the config stores it (fractions of hudroot), without the
        // game's build-mode lift, which is added back when the position is applied.
        private bool ScreenToPosition(HudElement e, Vector2 screen, out Vector2 pos)
        {
            pos = Vector2.zero;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, UiCamera, out local)) return false;
            if (_cfgBuildShift.Value) local -= e.Shift;
            Rect r = _root.rect;
            if (r.width <= 0f || r.height <= 0f) return false;
            pos = new Vector2((local.x - r.xMin) / r.width, (local.y - r.yMin) / r.height);
            return true;
        }

        private static float AxisDistance(Vector2 a, Vector2 b, bool alongX)
        {
            return Mathf.Abs(alongX ? a.x - b.x : a.y - b.y);
        }

        private HudElement HitTest(Vector2 gui)
        {
            HudElement best = null;
            float bestArea = float.MaxValue;
            foreach (HudElement e in _hudElements)
            {
                Rect r = GuiRect(e);
                if (!r.Contains(gui)) continue;
                float area = r.width * r.height;
                if (area < bestArea) { best = e; bestArea = area; }
            }
            return best;
        }

        private static Rect[] Handles(Rect r)
        {
            const float h = 12f;
            return new Rect[]
            {
                new Rect(r.xMin - h * 0.5f, r.yMin - h * 0.5f, h, h),
                new Rect(r.xMax - h * 0.5f, r.yMin - h * 0.5f, h, h),
                new Rect(r.xMax - h * 0.5f, r.yMax - h * 0.5f, h, h),
                new Rect(r.xMin - h * 0.5f, r.yMax - h * 0.5f, h, h),
            };
        }

        // Middles of the sides of a bar: left, right, top, bottom (GUI space).
        private static Rect[] EdgeHandles(Rect r)
        {
            const float h = 10f;
            float cx = r.center.x, cy = r.center.y;
            return new Rect[]
            {
                new Rect(r.xMin - h * 0.5f, cy - h * 0.5f, h, h),
                new Rect(r.xMax - h * 0.5f, cy - h * 0.5f, h, h),
                new Rect(cx - h * 0.5f, r.yMin - h * 0.5f, h, h),
                new Rect(cx - h * 0.5f, r.yMax - h * 0.5f, h, h),
            };
        }

        // Whether the bar runs up and down on screen (the game's orientation, turned or not).
        private static bool VerticalOnScreen(ElementSettings s)
        {
            return s.NativeVertical != (AngleFor(s) != 0f);
        }

        // The option a side handle changes: along the bar = Length, across it = Thickness.
        private static ConfigEntry<float> EdgeTarget(ElementSettings s, int edge)
        {
            bool horizontalHandle = edge < 2;   // left / right change the extent along x
            bool alongBar = horizontalHandle != VerticalOnScreen(s);
            return alongBar ? s.Length : s.Thickness;
        }

        // ------------------------------------------------------------------
        // changing values
        // ------------------------------------------------------------------
        private void SetPosition(ElementSettings s, Vector2 p, bool snap)
        {
            if (snap)
            {
                float step = _cfgGrid.Value;
                p.x = Mathf.Round(p.x / step) * step;
                p.y = Mathf.Round(p.y / step) * step;
                if (Mathf.Abs(p.x - 0.5f) < step * 2f) p.x = 0.5f;   // the middle of the screen pulls
            }
            p.x = Mathf.Clamp01(p.x);
            p.y = Mathf.Clamp01(p.y);
            float x = (float)Math.Round(p.x, 4), y = (float)Math.Round(p.y, 4);
            if (s.PosX.Value != x) s.PosX.Value = x;
            if (s.PosY.Value != y) s.PosY.Value = y;
        }

        private static float Snapped(float v, float min, float max, bool snap)
        {
            v = Mathf.Clamp(v, min, max);
            return snap ? Mathf.Round(v * 20f) / 20f : (float)Math.Round(v, 2);
        }

        private void ResetPosition(ElementSettings s)
        {
            Batch(delegate { s.PosX.Value = -1f; s.PosY.Value = -1f; });
        }

        private void ResetLayout(ElementSettings s)
        {
            Batch(delegate { foreach (ConfigEntryBase e in s.LayoutEntries) e.BoxedValue = e.DefaultValue; });
        }

        // Current position as fractions, measured if the element is where the game put it.
        private bool CurrentPosition(HudElement e, out Vector2 pos)
        {
            ElementSettings s = e.Settings;
            if (s.HasPosition) { pos = new Vector2(s.PosX.Value, s.PosY.Value); return true; }
            return ScreenToPosition(e, PivotScreen(e), out pos);
        }

        private void Nudge(HudElement e, Vector2 dir, bool big)
        {
            Vector2 pos;
            if (!CurrentPosition(e, out pos)) return;
            float step = _cfgGrid.Value * (big ? 10f : 1f);
            SetPosition(e.Settings, pos + dir * step, false);
        }

        // ------------------------------------------------------------------
        // IMGUI
        // ------------------------------------------------------------------
        private void OnGUI()
        {
            if (!_editing) return;
            try
            {
                if (!CanEdit()) return;
                EnsureGuiStyles();
                float k = UiScale();
                float sw = Screen.width / k, sh = Screen.height / k;   // the screen in window units
                _winW = Mathf.Clamp(_winW, 360f, sw - 20f);
                _winH = Mathf.Clamp(_winH, 300f, sh - 20f);
                if (!_winPlaced)
                {
                    _winH = Mathf.Min(760f, sh * 0.75f);
                    _win = new Rect(sw - _winW - 20f, sh * 0.15f, _winW, _winH);
                    _winPlaced = true;
                }

                DrawOverlay();
                HandleInput(Event.current);

                Matrix4x4 old = GUI.matrix;
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(k, k, 1f));
                string title = L("HudLayout — edit mode", "HudLayout — редактор HUD");
                if (_collapsed)
                    // a plain window of exactly this rectangle: a layout window with no content
                    // inside collapses to nothing
                    _win = GUI.Window(WindowId, new Rect(_win.x, _win.y, _winW, TitleH), DrawWindow, title, WindowStyle());
                else
                    _win = GUILayout.Window(WindowId, new Rect(_win.x, _win.y, _winW, WinH), DrawWindow, title, WindowStyle(),
                        GUILayout.Width(_winW), GUILayout.Height(WinH));
                // the whole window stays on the screen
                _win.width = _winW; _win.height = WinH;
                _win.x = Mathf.Clamp(_win.x, 0f, sw - _winW);
                _win.y = Mathf.Clamp(_win.y, 0f, sh - WinH);
                GUI.matrix = old;
            }
            catch (Exception e) { Fail("editor", e); }
        }

        // The window grows with the screen so it stays readable at 1440p and 4K.
        private static float UiScale()
        {
            return Mathf.Clamp(Screen.height / 1080f, 1f, 2f);
        }

        private Rect WindowOnScreen()
        {
            float k = UiScale();
            return new Rect(_win.x * k, _win.y * k, _winW * k, WinH * k);
        }

        // The grip in the window's bottom right corner that resizes it (GUI space).
        private Rect ResizeGrip()
        {
            Rect w = WindowOnScreen();
            float g = 20f * UiScale();
            return new Rect(w.xMax - g, w.yMax - g, g, g);
        }

        private void EnsureGuiStyles()
        {
            if (_labelStyle != null) return;
            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.fontStyle = FontStyle.Bold;
            _labelStyle.normal.textColor = Color.white;
            _shadowStyle = new GUIStyle(_labelStyle);
            _shadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
            _headerStyle = new GUIStyle(GUI.skin.label);
            _headerStyle.fontStyle = FontStyle.Bold;
            _headerStyle.normal.textColor = new Color(1f, 0.85f, 0.45f);
            _hintStyle = new GUIStyle(GUI.skin.label);
            _hintStyle.wordWrap = true;
            _hintStyle.fontSize = Mathf.Max(10, GUI.skin.label.fontSize - 1);
            _hintStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
            _smallButton = new GUIStyle(GUI.skin.button);
            _smallButton.padding = new RectOffset(4, 4, 2, 2);
            _smallButton.wordWrap = true;
            _gridStyle = new GUIStyle(GUI.skin.button);
            _gridStyle.wordWrap = true;
            // long texts wrap instead of widening the window past the screen
            _wrapLabel = new GUIStyle(GUI.skin.label);
            _wrapLabel.wordWrap = true;
            _wrapToggle = new GUIStyle(GUI.skin.toggle);
            _wrapToggle.wordWrap = true;
        }

        // The skin's window is see-through; ours is a dark panel of EditorOpacity, so the game
        // behind does not make the text hard to read. Kept in step with the setting.
        private GUIStyle WindowStyle()
        {
            float a = _cfgEditorOpacity.Value;
            if (_windowStyle != null && _windowBg != null && Mathf.Approximately(a, _windowBgAlpha)) return _windowStyle;
            if (_windowBg == null)
            {
                _windowBg = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _windowBg.hideFlags = HideFlags.HideAndDontSave;
            }
            _windowBg.SetPixel(0, 0, new Color(0.08f, 0.07f, 0.06f, a));
            _windowBg.Apply();
            _windowBgAlpha = a;
            if (_windowStyle == null)
            {
                _windowStyle = new GUIStyle(GUI.skin.window);
                _windowStyle.normal.textColor = new Color(1f, 0.85f, 0.45f);
                _windowStyle.onNormal.textColor = _windowStyle.normal.textColor;
                _windowStyle.fontStyle = FontStyle.Bold;
            }
            _windowStyle.normal.background = _windowBg;
            _windowStyle.onNormal.background = _windowBg;
            _windowStyle.focused.background = _windowBg;
            _windowStyle.onFocused.background = _windowBg;
            _windowStyle.hover.background = _windowBg;
            _windowStyle.onHover.background = _windowBg;
            _windowStyle.active.background = _windowBg;
            _windowStyle.onActive.background = _windowBg;
            _windowStyle.border = new RectOffset(0, 0, 0, 0);
            return _windowStyle;
        }

        private static void Fill(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        private static void Outline(Rect r, Color c, float w)
        {
            Fill(new Rect(r.xMin, r.yMin, r.width, w), c);
            Fill(new Rect(r.xMin, r.yMax - w, r.width, w), c);
            Fill(new Rect(r.xMin, r.yMin, w, r.height), c);
            Fill(new Rect(r.xMax - w, r.yMin, w, r.height), c);
        }

        private void Shadowed(Rect r, string text)
        {
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, _shadowStyle);
            GUI.Label(r, text, _labelStyle);
        }

        private void DrawOverlay()
        {
            if (Event.current.type != EventType.Repaint) return;
            Color sel = new Color(1f, 0.78f, 0.2f, 1f);
            Color other = new Color(1f, 1f, 1f, 0.6f);
            foreach (HudElement e in _hudElements)
            {
                bool isSel = e.Settings == _sel;
                Rect r = GuiRect(e);
                Color c = isSel ? sel : other;
                Fill(r, new Color(c.r, c.g, c.b, isSel ? 0.14f : 0.06f));
                Outline(r, c, isSel ? 2f : 1f);
                string label = ElementLabel(e.Settings);
                if (e.Settings.Vis.Value == Visibility.Hidden) label += L(" (hidden)", " (скрыт)");
                Shadowed(new Rect(r.xMin, r.yMin - 22f, 260f, 22f), label);
                if (!isSel) continue;
                foreach (Rect h in Handles(r)) { Fill(h, sel); Outline(h, Color.black, 1f); }
                if (e.Settings.IsBar)
                    foreach (Rect h in EdgeHandles(r)) { Fill(h, new Color(0.3f, 0.9f, 1f, 1f)); Outline(h, Color.black, 1f); }
                Vector2 pv = ScreenToGui(PivotScreen(e));
                Fill(new Rect(pv.x - 5f, pv.y - 1f, 10f, 2f), Color.black);
                Fill(new Rect(pv.x - 1f, pv.y - 5f, 2f, 10f), Color.black);
            }
            // the middle of the screen while something sits on it
            if (_drag == DragKind.Move)
            {
                ElementSettings s = _sel;
                if (s != null && s.HasPosition && Mathf.Abs(s.PosX.Value - 0.5f) < 0.0001f)
                    Fill(new Rect(Screen.width * 0.5f - 0.5f, 0f, 1f, Screen.height), new Color(0.3f, 0.9f, 1f, 0.6f));
            }
        }

        private void HandleInput(Event ev)
        {
            Vector2 gui = ev.mousePosition;
            bool overWin = WindowOnScreen().Contains(gui);
            HudElement hit;
            switch (ev.type)
            {
                case EventType.MouseDown:
                    if (ev.button == 0 && !_collapsed && ResizeGrip().Contains(gui))
                    {
                        _drag = DragKind.ResizeWindow;
                        ev.Use();
                        return;
                    }
                    if (overWin) return;
                    if (ev.button == 0)
                    {
                        HudElement cur = HudElementOf(_sel);
                        if (cur != null && cur.Settings.IsBar)
                        {
                            Rect[] edges = EdgeHandles(GuiRect(cur));
                            for (int i = 0; i < edges.Length; i++)
                            {
                                if (!edges[i].Contains(gui)) continue;
                                _drag = DragKind.Edge;
                                _edgeTarget = EdgeTarget(cur.Settings, i);
                                _edgeAlongX = i < 2;
                                _dragStartScale = _edgeTarget.Value;
                                _dragStartDist = Mathf.Max(4f, AxisDistance(GuiToScreen(gui), PivotScreen(cur), _edgeAlongX));
                                ev.Use();
                                return;
                            }
                        }
                        if (cur != null)
                        {
                            foreach (Rect h in Handles(GuiRect(cur)))
                            {
                                if (!h.Contains(gui)) continue;
                                _drag = DragKind.Scale;
                                _dragStartScale = cur.Settings.Scale.Value;
                                _dragStartDist = Mathf.Max(4f, Vector2.Distance(GuiToScreen(gui), PivotScreen(cur)));
                                ev.Use();
                                return;
                            }
                        }
                        hit = HitTest(gui);
                        if (hit != null)
                        {
                            Select(hit.Settings);
                            _drag = DragKind.Move;
                            _grab = PivotScreen(hit) - GuiToScreen(gui);
                            ev.Use();
                        }
                        else GUIUtility.keyboardControl = 0;
                    }
                    else if (ev.button == 1)
                    {
                        hit = HitTest(gui);
                        if (hit == null) return;
                        Select(hit.Settings);
                        if (ev.shift) ResetLayout(hit.Settings);
                        else ResetPosition(hit.Settings);
                        ev.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (_drag == DragKind.None) return;
                    if (_drag == DragKind.ResizeWindow)
                    {
                        float k = UiScale();
                        _winW += ev.delta.x / k;
                        _winH += ev.delta.y / k;
                        ev.Use();
                        return;
                    }
                    HudElement e = HudElementOf(_sel);
                    if (e == null) { _drag = DragKind.None; return; }
                    if (_drag == DragKind.Move)
                    {
                        Vector2 pos;
                        if (ScreenToPosition(e, GuiToScreen(gui) + _grab, out pos))
                            SetPosition(e.Settings, pos, _cfgSnap.Value && !ev.control);
                    }
                    else if (_drag == DragKind.Edge)
                    {
                        float d = AxisDistance(GuiToScreen(gui), PivotScreen(e), _edgeAlongX);
                        float v = Snapped(_dragStartScale * d / _dragStartDist, 0.25f, 4f, _cfgSnap.Value && !ev.control);
                        if (_edgeTarget.Value != v) _edgeTarget.Value = v;
                    }
                    else
                    {
                        float d = Vector2.Distance(GuiToScreen(gui), PivotScreen(e));
                        float v = Snapped(_dragStartScale * d / _dragStartDist, 0.25f, 4f, _cfgSnap.Value && !ev.control);
                        if (e.Settings.Scale.Value != v) e.Settings.Scale.Value = v;
                    }
                    ev.Use();
                    break;

                case EventType.MouseUp:
                    if (_drag != DragKind.None) { _drag = DragKind.None; ev.Use(); }
                    break;

                case EventType.ScrollWheel:
                    if (overWin) return;
                    hit = HitTest(gui);
                    if (hit == null) return;
                    Select(hit.Settings);
                    float step = ev.delta.y > 0f ? -0.05f : 0.05f;
                    ConfigEntry<float> target = hit.Settings.Scale;
                    if (ev.control && hit.Settings.Length != null) target = hit.Settings.Length;
                    else if (ev.shift && hit.Settings.Thickness != null) target = hit.Settings.Thickness;
                    target.Value = Snapped(target.Value + step, 0.25f, 4f, false);
                    ev.Use();
                    break;

                case EventType.KeyDown:
                    if (ev.keyCode == KeyCode.Escape)
                    {
                        _escFrame = Time.frameCount;
                        StopEditing();
                        ev.Use();
                        return;
                    }
                    if (GUIUtility.keyboardControl != 0) return;   // typing in a field of the window
                    if (ev.keyCode == KeyCode.H)
                    {
                        _collapsed = !_collapsed;
                        ev.Use();
                        return;
                    }
                    HudElement cur2 = HudElementOf(_sel);
                    if (ev.keyCode == KeyCode.Tab)
                    {
                        int n = _hudElements.Count;
                        if (n == 0) return;
                        int i = cur2 != null ? _hudElements.IndexOf(cur2) : -1;
                        Select(_hudElements[((i + (ev.shift ? -1 : 1)) % n + n) % n].Settings);
                        ev.Use();
                        return;
                    }
                    if (cur2 == null) return;
                    Vector2 dir = Vector2.zero;
                    if (ev.keyCode == KeyCode.LeftArrow) dir = Vector2.left;
                    else if (ev.keyCode == KeyCode.RightArrow) dir = Vector2.right;
                    else if (ev.keyCode == KeyCode.UpArrow) dir = Vector2.up;
                    else if (ev.keyCode == KeyCode.DownArrow) dir = Vector2.down;
                    if (dir == Vector2.zero) return;
                    Nudge(cur2, dir, ev.shift);
                    ev.Use();
                    break;
            }
        }

        // ------------------------------------------------------------------
        // the window
        // ------------------------------------------------------------------
        private void DrawWindow(int id)
        {
            // fold / unfold, in the title bar (before DragWindow, which would take the click)
            if (GUI.Button(new Rect(_winW - 30f, 3f, 26f, 18f), _collapsed ? "+" : "–", _smallButton))
            {
                _collapsed = !_collapsed;
                GUIUtility.keyboardControl = 0;
            }
            if (_collapsed)
            {
                GUI.DragWindow(new Rect(0f, 0f, _winW - 34f, TitleH));
                return;
            }

            // vertical scrolling only; the content is exactly as wide as the window's inside
            _scroll = GUILayout.BeginScrollView(_scroll, false, true, GUIStyle.none, GUI.skin.verticalScrollbar);
            GUILayout.BeginVertical(GUILayout.Width(_winW - 46f));

            GUILayout.Label(L("Drag an element to move it, a yellow corner to resize it, a blue side to change length or thickness. " +
                              "Wheel: size, Ctrl+wheel: length, Shift+wheel: thickness. " +
                              "Right click: back to the game's place (Shift: whole layout). Arrows: nudge (Shift ×10), Tab: next element. " +
                              "Ctrl while dragging: no snapping. The corner of this window resizes it, – or H folds it. Esc or " + _cfgEditKey.Value + ": done.",
                              "Тащите элемент, чтобы передвинуть; жёлтый уголок — размер; синяя середина стороны — длина или толщина. " +
                              "Колесо: размер, Ctrl+колесо: длина, Shift+колесо: толщина. " +
                              "ПКМ: вернуть на место игры (Shift — всю раскладку). Стрелки: сдвиг (Shift ×10), Tab: следующий элемент. " +
                              "Ctrl при перетаскивании: без привязки. Уголок этого окна меняет его размер, – или H сворачивает его. Esc или " + _cfgEditKey.Value + ": готово."), _hintStyle);

            // the elements in three groups; long names wrap instead of widening the window
            ElementGroup(L("Bars and food", "Полосы и еда"), 0);
            ElementGroup(L("Rest of the HUD", "Остальной HUD"), 1);
            ElementGroup(L("Other mods", "Другие моды"), 2);

            ElementSettings s = _sel ?? Get(ElementId.Health);
            DrawLayoutSection(s);
            DrawStyleSection(s);
            DrawPresetSection();
            DrawSettingsSection();

            if (_msg.Length > 0 && Time.unscaledTime < _msgUntil)
            {
                GUILayout.Space(4f);
                GUILayout.Label(_msg, _headerStyle);
            }
            GUILayout.Space(6f);
            if (GUILayout.Button(L("Done", "Готово"))) StopEditing();

            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            GUI.Label(new Rect(_winW - 18f, _winH - 20f, 18f, 18f), "◢", _hintStyle);
            GUI.DragWindow(new Rect(0f, 0f, _winW - 34f, 22f));
        }

        // 0 bars and food, 1 the rest of the vanilla HUD, 2 other mods (found or registered)
        private static int GroupOf(ElementSettings s)
        {
            if (!s.IsSimple) return 0;
            return s.Key.StartsWith("Mod.", StringComparison.Ordinal) || s.Key.StartsWith("Api.", StringComparison.Ordinal) ? 2 : 1;
        }

        private void ElementGroup(string title, int group)
        {
            List<HudElement> list = new List<HudElement>();
            foreach (HudElement e in _hudElements) if (GroupOf(e.Settings) == group) list.Add(e);
            if (list.Count == 0) return;
            GUILayout.Label(title, _hintStyle);
            string[] names = new string[list.Count];
            int sel = -1;
            for (int i = 0; i < list.Count; i++)
            {
                names[i] = group == 2 ? L(list[i].Settings.LabelEn, list[i].Settings.LabelRu) : ElementLabel(list[i].Settings);
                if (list[i].Settings == _sel) sel = i;
            }
            int n = GUILayout.SelectionGrid(sel, names, group == 2 ? 2 : 3, _gridStyle);
            if (n != sel && n >= 0) Select(list[n].Settings);
        }

        private void Header(string text)
        {
            GUILayout.Space(8f);
            GUILayout.Label(text, _headerStyle);
        }

        private void Slider(string label, ConfigEntry<float> e, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(110f));
            float v = GUILayout.HorizontalSlider(e.Value, min, max);
            GUILayout.Label(e.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), GUILayout.Width(40f));
            GUILayout.EndHorizontal();
            // only when the slider was moved: rounding alone must not rewrite a value typed by hand
            if (Mathf.Abs(v - e.Value) > 0.0001f) e.Value = (float)Math.Round(v, 2);
        }

        // A row of buttons for some values of an enum option.
        private void Choice<T>(string label, ConfigEntry<T> e, T[] values, string[] labels, int columns)
        {
            GUILayout.Label(label, _wrapLabel);
            int cur = Array.IndexOf(values, e.Value);
            int n = GUILayout.SelectionGrid(cur, labels, columns, _gridStyle);
            if (n != cur && n >= 0 && n < values.Length) e.Value = values[n];
        }

        private void DrawLayoutSection(ElementSettings s)
        {
            Header(L("Position and size", "Положение и размер"));
            GUILayout.BeginHorizontal();
            GUILayout.Label(s.HasPosition
                ? "X " + F(s.PosX.Value) + "   Y " + F(s.PosY.Value)
                : L("Where the game puts it", "Там, где его ставит игра"), _wrapLabel);
            if (s.HasPosition && GUILayout.Button(L("Game's place", "Место игры"), _smallButton, GUILayout.Width(110f))) ResetPosition(s);
            GUILayout.EndHorizontal();

            Slider(L("Scale", "Размер"), s.Scale, 0.25f, 4f);
            if (s.Length != null) Slider(L("Length", "Длина"), s.Length, 0.25f, 4f);
            if (s.Thickness != null) Slider(L("Thickness", "Толщина"), s.Thickness, 0.25f, 4f);
            if (s.FixedLength != null)
            {
                bool fixedLen = GUILayout.Toggle(s.FixedLength.Value,
                    L(" Fixed length: the bar does not grow with the maximum, only the number does",
                      " Фиксированная длина: полоса не растёт с максимумом, растёт только число"), _wrapToggle);
                if (fixedLen != s.FixedLength.Value) s.FixedLength.Value = fixedLen;
            }

            if (s.Orient != null)
            Choice(L("Orientation", "Ориентация"), s.Orient,
                new[] { Orientation.Default, Orientation.Horizontal, Orientation.Vertical },
                s.IsBar ? new[] { L("As in game", "Как в игре"), L("Horizontal", "Горизонтально"), L("Vertical", "Вертикально") }
                        : new[] { L("As in game", "Как в игре"), L("Row", "Строкой"), L("Column", "Столбцом") }, 3);
            if (s.Anchor != null)
                Choice(L("Stays in place while growing", "При росте на месте остаётся"), s.Anchor,
                    new[] { BarAnchor.Start, BarAnchor.Center, BarAnchor.End },
                    new[] { L("Start", "Начало"), L("Middle", "Середина"), L("End", "Конец") }, 3);

            if (GUILayout.Button(L("Reset position and size", "Сбросить положение и размер"))) ResetLayout(s);
        }

        private static readonly string[] Swatches = { "", "#FF3B3B", "#FF8A1F", "#FFD21F", "#5BE35B", "#3FD5E8", "#4A8BFF", "#B86BFF", "#FFFFFF" };

        private void DrawStyleSection(ElementSettings s)
        {
            Header(L("Look", "Внешний вид"));

            if (s.Style != null)
            {
                List<StyleDef> styles = StylesOf(s);
                string[] labels = new string[styles.Count];
                int cur = -1;
                for (int i = 0; i < styles.Count; i++)
                {
                    labels[i] = L(styles[i].Name, styles[i].NameRu);
                    if (styles[i].Name == s.Style.Value) cur = i;
                }
                GUILayout.Label(L("Style: ", "Стиль: ") + StyleLabel(s, s.Style.Value));
                int n = GUILayout.SelectionGrid(cur, labels, 3, _gridStyle);
                if (n != cur && n >= 0) ApplyStyle(s, styles[n].Name);
            }

            if (s.IsBar)
                Choice(L("Visibility", "Видимость"), s.Vis,
                    new[] { Visibility.Vanilla, Visibility.Always, Visibility.Hidden, Visibility.NotFull },
                    new[] { L("As in game", "Как в игре"), L("Always", "Всегда"), L("Hidden", "Скрыта"), L("When not full", "Когда не полная") }, 2);
            else
                Choice(L("Visibility", "Видимость"), s.Vis,
                    new[] { Visibility.Vanilla, Visibility.Hidden },
                    new[] { L("Shown", "Видна"), L("Hidden", "Скрыта") }, 2);
            Slider(L("Opacity", "Непрозрачность"), s.Opacity, 0.05f, 1f);
            if (s.Icon != null)
            {
                bool icon = GUILayout.Toggle(s.Icon.Value, s.Id == ElementId.Health
                    ? L(" Heart icon", " Значок сердца")
                    : L(" Food symbol under the icons", " Символ еды под значками"), _wrapToggle);
                if (icon != s.Icon.Value) s.Icon.Value = icon;
            }

            if (s.IsBar)
            {
                Choice(L("Number on the bar", "Число на полосе"), s.Text,
                    new[] { TextMode.Vanilla, TextMode.Hidden, TextMode.Current, TextMode.CurrentMax, TextMode.Percent },
                    new[] { L("As in game", "Как в игре"), L("None", "Нет"), "75", "75/100", "75%" }, 3);
                Slider(L("Number size", "Размер числа"), s.TextSize, 0.5f, 3f);
                bool seg = GUILayout.Toggle(s.Segments.Value,
                    L(" Cells: one cell per N points of the maximum", " Ячейки: одна ячейка на N единиц максимума"), _wrapToggle);
                if (seg != s.Segments.Value) s.Segments.Value = seg;
                if (s.Segments.Value)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(L("Points per cell", "Единиц в ячейке"), GUILayout.Width(110f));
                    float ss = GUILayout.HorizontalSlider(s.SegmentSize.Value, 1f, 50f);
                    GUILayout.Label(Mathf.RoundToInt(s.SegmentSize.Value).ToString(), GUILayout.Width(40f));
                    GUILayout.EndHorizontal();
                    ss = Mathf.Round(ss);
                    if (ss != s.SegmentSize.Value) s.SegmentSize.Value = ss;
                }
                Choice(L("Number position", "Положение числа"), s.TextPos,
                    new[] { TextPosition.Vanilla, TextPosition.Center, TextPosition.Fill },
                    new[] { L("As in game", "Как в игре"), L("Middle of the bar", "Середина полосы"), L("Middle of the fill", "Середина заполнения") }, 3);

                GUILayout.Label(L("Bar colour", "Цвет полосы"), _wrapLabel);
                GUILayout.BeginHorizontal();
                foreach (string sw in Swatches)
                {
                    Color c;
                    bool vanilla = sw.Length == 0;
                    Color oldBg = GUI.backgroundColor;
                    if (!vanilla && ColorUtility.TryParseHtmlString(sw, out c)) GUI.backgroundColor = c;
                    bool picked = GUILayout.Button(vanilla ? L("Game", "Игра") : " ", _smallButton, GUILayout.Width(vanilla ? 44f : 26f));
                    GUI.backgroundColor = oldBg;
                    if (picked) { s.BarColor.Value = sw; _colorBuf = sw; }
                }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                _colorBuf = GUILayout.TextField(_colorBuf ?? "", GUILayout.Width(110f));
                if (GUILayout.Button(L("Apply #RRGGBB", "Применить #RRGGBB"), _smallButton))
                {
                    string v = (_colorBuf ?? "").Trim();
                    Color c;
                    if (v.Length == 0 || ColorUtility.TryParseHtmlString(v.StartsWith("#") ? v : "#" + v, out c))
                    {
                        s.BarColor.Value = v.Length == 0 || v.StartsWith("#") ? v : "#" + v;
                        GUIUtility.keyboardControl = 0;
                    }
                    else Say(L("Not a colour: ", "Это не цвет: ") + v);
                }
                GUILayout.EndHorizontal();
            }
            else if (s.Timers != null)
            {
                bool t = GUILayout.Toggle(s.Timers.Value, L(" Time left on the icons", " Оставшееся время на значках"), _wrapToggle);
                if (t != s.Timers.Value) s.Timers.Value = t;
                Slider(L("Time size", "Размер времени"), s.TextSize, 0.5f, 3f);
            }

            if (GUILayout.Button(L("Reset look", "Сбросить внешний вид")))
            {
                if (s.Style != null) ApplyStyle(s, "Vanilla");
                else Batch(delegate { foreach (ConfigEntryBase e in s.StyleEntries) e.BoxedValue = e.DefaultValue; });
            }
        }

        private void DrawPresetSection()
        {
            Header(L("Presets (all elements)", "Пресеты (все элементы)"));
            GUILayout.Label(L("Apply: all / only positions and sizes / only looks.",
                              "Применить: всё / только положение и размер / только внешний вид."), _hintStyle);
            Preset remove = null;
            foreach (Preset p in AllPresets())
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(PresetLabel(p) + (p.BuiltIn ? "" : L("  (mine)", "  (мой)")), _wrapLabel, GUILayout.Width(Mathf.Max(110f, _winW - 300f)));
                if (GUILayout.Button(L("All", "Всё"), _smallButton)) { ApplyPreset(p, PresetPart.All); Say(L("Applied: ", "Применён: ") + PresetLabel(p)); }
                if (GUILayout.Button(L("Layout", "Место"), _smallButton)) { ApplyPreset(p, PresetPart.Layout); Say(L("Layout applied: ", "Расположение применено: ") + PresetLabel(p)); }
                if (GUILayout.Button(L("Look", "Вид"), _smallButton)) { ApplyPreset(p, PresetPart.Style); Say(L("Look applied: ", "Вид применён: ") + PresetLabel(p)); }
                if (!p.BuiltIn)
                {
                    bool confirming = _confirmDelete == p.Name && Time.unscaledTime < _confirmUntil;
                    if (GUILayout.Button(confirming ? L("Sure?", "Точно?") : "✕", _smallButton, GUILayout.Width(52f)))
                    {
                        if (confirming) { remove = p; _confirmDelete = null; }
                        else { _confirmDelete = p.Name; _confirmUntil = Time.unscaledTime + 3f; }
                    }
                }
                GUILayout.EndHorizontal();
            }
            if (remove != null)
            {
                string err = DeleteUserPreset(remove.Name);
                Say(err ?? L("Deleted: ", "Удалён: ") + remove.Name);
            }

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            _saveName = GUILayout.TextField(_saveName ?? "", GUILayout.Width(Mathf.Max(120f, _winW - 250f)));
            if (GUILayout.Button(L("Save current as", "Сохранить текущее как"), _smallButton))
            {
                Preset p = Capture(_saveName);
                string err = SaveUserPreset(p);
                Say(err ?? L("Saved: ", "Сохранён: ") + p.Name);
                if (err == null) GUIUtility.keyboardControl = 0;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L("Copy current to clipboard", "Копировать текущее в буфер"), _smallButton))
            {
                string name = (_saveName ?? "").Trim();
                GUIUtility.systemCopyBuffer = Export(Capture(name.Length > 0 ? name : "Shared layout"));
                Say(L("Copied. Send it to a friend; they press Import.", "Скопировано. Отправьте другу — он нажмёт «Импорт»."));
            }
            if (GUILayout.Button(L("Import from clipboard", "Импорт из буфера"), _smallButton))
            {
                Preset p = Import(GUIUtility.systemCopyBuffer);
                if (p == null) Say(L("The clipboard holds no preset.", "В буфере нет пресета."));
                else
                {
                    p.Name = FreeName(p.Name);
                    string err = SaveUserPreset(p);
                    Say(err ?? L("Imported as: ", "Импортирован как: ") + p.Name);
                }
            }
            GUILayout.EndHorizontal();
        }

        private void DrawSettingsSection()
        {
            Header(L("Settings", "Настройки"));
            bool snap = GUILayout.Toggle(_cfgSnap.Value, L(" Snap to grid", " Привязка к сетке"), _wrapToggle);
            if (snap != _cfgSnap.Value) _cfgSnap.Value = snap;
            bool lift = GUILayout.Toggle(_cfgBuildShift.Value, L(" Lift bars in build mode like the game", " Поднимать полосы в режиме строительства, как игра"), _wrapToggle);
            if (lift != _cfgBuildShift.Value) _cfgBuildShift.Value = lift;
            GUILayout.BeginHorizontal();
            GUILayout.Label(L("Window background", "Фон окна"), GUILayout.Width(110f));
            float wo = GUILayout.HorizontalSlider(_cfgEditorOpacity.Value, 0.3f, 1f);
            GUILayout.EndHorizontal();
            if (Mathf.Abs(wo - _cfgEditorOpacity.Value) > 0.0001f) _cfgEditorOpacity.Value = (float)Math.Round(wo, 2);
        }
    }

    // Esc in the edit mode closes the editor, not the game: the menu waits.
    [HarmonyPatch(typeof(Menu), "Update")]
    internal static class MenuUpdatePatch
    {
        private static bool Prefix()
        {
            return HudLayoutPlugin.Instance == null || !HudLayoutPlugin.Instance.BlockMenu;
        }
    }
}
