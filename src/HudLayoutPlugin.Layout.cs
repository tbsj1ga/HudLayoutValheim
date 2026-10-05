using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HudLayout
{
    // One element on the HUD as it is found and wrapped: what to move, what to measure, what
    // to keep upright, and the game's own parts we restyle.
    internal sealed class HudElement
    {
        public ElementSettings Settings;
        public RectTransform Wrapper;       // ours; position, rotation and scale go here
        public CanvasGroup Group;           // ours; opacity and hiding
        public RectTransform Panel;         // the game's panel the game moves itself (bars), for the build-mode lift
        public Vector2 PanelBase;           // its anchoredPosition as authored (not in build mode)

        public readonly List<RectTransform> Bounds = new List<RectTransform>();    // what the element looks like
        public RectTransform Target;        // simple elements: the wrapped object
        public bool AutoBounds;             // simple elements: measured from the graphics inside
        public readonly List<Graphic> Graphics = new List<Graphic>();
        public float GraphicsAt;

        // Direct: no wrapper, the object stays where it is in the hierarchy (someone may look it
        // up by path, as ExtraSlots does with HotKeyBar) and our transform is composed onto its
        // own pose. A pose written by anyone else becomes the new base.
        public bool Direct;
        public bool OwnGroup;               // the CanvasGroup is ours (wrapper, or added by us); else its owner's
        public float BaseAlpha = 1f, WrittenAlpha = -1f;
        public Vector2 BaseAp;              // its own anchoredPosition: what it is placed by, whatever the parent's size
        public Vector3 BaseLp, BaseScale = Vector3.one;   // BaseLp: that position in parent space, this frame
        public Quaternion BaseRot = Quaternion.identity;
        public Vector2 WrittenAp;
        public Vector3 WrittenScale;
        public Quaternion WrittenRot;
        public bool HasWritten;
        public Vector3 MPos;                // our transform: parent-space point = MPos + MRot * (x * MScale)
        public Quaternion MRot = Quaternion.identity;
        public float MScale = 1f;

        // A point of the element's layout space (the wrapper's, or for Direct the parent's with
        // the object at its base pose) to the world, and back.
        public Vector3 ToWorld(Vector2 p)
        {
            if (!Direct) return Wrapper.TransformPoint(p);
            Transform parent = Target.parent;
            Vector3 q = MPos + MRot * ((Vector3)p * MScale);
            return parent != null ? parent.TransformPoint(q) : q;
        }

        public Vector2 ToLayout(Vector3 world)
        {
            if (!Direct) return Wrapper.InverseTransformPoint(world);
            Vector3 l = Target.InverseTransformPoint(world);
            return BaseLp + BaseRot * Vector3.Scale(BaseScale, l);
        }
        public readonly List<Transform> Upright = new List<Transform>();          // counter-rotated: numbers, icons
        public readonly List<Quaternion> UprightBase = new List<Quaternion>();

        public GuiBar Fast;                 // the bar whose colour BarColor sets
        public TMP_Text Text;               // the number on the bar
        public Animator Anim;               // "Visible" of stamina, eitr, adrenaline
        public readonly List<TMP_Text> Texts = new List<TMP_Text>();              // text whose size TextSize sets
        public readonly List<float[]> TextBase = new List<float[]>();             // fontSize, min, max
        public readonly List<TextWrappingModes> WrapBase = new List<TextWrappingModes>();

        // state of the last frame
        public float Angle;
        public bool ColorSet;
        public bool TextHidden;
        public float TextScale = 1f;
        public TextMode LastMode = TextMode.Vanilla;
        public int LastA = int.MinValue, LastB = int.MinValue;
        public string LastText;

        // thickness: the bar's own rectangles, resized across the bar (the numbers keep their size)
        public RectTransform Root;          // healthpanel's "Health" or the stamina/eitr/adrenaline panel
        public float RootBase;              // its size across the bar
        public readonly List<RectTransform> Fills = new List<RectTransform>();   // GuiBar.m_bar of fast and slow
        public readonly List<float> FillBase = new List<float>();
        public float Thick = 1f;
        public GameObject Icon;             // healthicon, the food symbol
        public bool IconHidden;

        // where the number sits: moved out of the fill so the fill can be scaled for thickness
        public readonly List<Vector2> TextSizeBase = new List<Vector2>();
        public readonly List<TextOverflowModes> OverflowBase = new List<TextOverflowModes>();
        public Transform TextParent;        // the game's parent of the number
        public int TextSibling;
        public Vector2 TextAnchorMin, TextAnchorMax, TextPivot, TextPos;
        public RectTransform CenterHolder;  // the bar's own rectangle: the number in its middle
        public RectTransform FillHolder;    // the fast GuiBar's rectangle (unscaled), the number follows the fill in it
        public int TextPlace = -1;          // 0 the game's, 1 middle, 2 fill (applied)
        public Action<float> SetSize;       // Hud.SetAdrenalineBarSize, for a bar the game does not size at zero
        public float LastSetSize = -1f;

        // cells: the fill's own tiled pattern (one tile = one cell) scaled so a tile covers SegmentSize points
        public readonly List<Image> FillImages = new List<Image>();
        public float Mult = 1f;             // pixelsPerUnitMultiplier applied

        // measured this frame, in wrapper space (for the editor)
        public Vector2 BMin, BMax;
        public Vector2 Pivot;               // the point it is scaled and turned about now
        public Vector2 Anchor;              // the point a set position puts in place (= Pivot once one is set)
        public Vector2 Shift;               // the game's build-mode lift, in hudroot space
    }

    public partial class HudLayoutPlugin
    {
        private Hud _hud;
        private RectTransform _root;        // hudroot
        private Canvas _canvas;
        private readonly List<HudElement> _hudElements = new List<HudElement>();

        internal HudElement HudElementOf(ElementId id)
        {
            foreach (HudElement e in _hudElements) if (e.Settings.Id == id) return e;
            return null;
        }

        // ------------------------------------------------------------------
        // wrapping, once per Hud
        // ------------------------------------------------------------------
        internal void OnHudAwake(Hud hud)
        {
            if (_disabledByErrors) return;
            try { Attach(hud); }
            catch (Exception e) { Fail("attach", e); }
        }

        internal void OnHudDestroy(Hud hud)
        {
            if (hud != _hud) return;
            if (_editing) StopEditing();
            _hud = null; _root = null; _canvas = null;
            _hudElements.Clear();
        }

        private void Attach(Hud hud)
        {
            if (hud == null || hud == _hud) return;
            _hudElements.Clear();
            _hud = hud;
            _root = hud.m_rootObject != null ? hud.m_rootObject.transform as RectTransform : null;
            if (_root == null) { Logger.LogWarning("Hud.m_rootObject not found; the HUD is left as it is."); return; }
            _canvas = _root.GetComponentInParent<Canvas>();

            TryAttach("health", delegate { AttachHealth(hud); });
            TryAttach("food", delegate { AttachFood(hud); });
            TryAttach("stamina", delegate { AttachBar(hud, ElementId.Stamina, hud.m_staminaBar2Root, hud.m_staminaBar2Fast, hud.m_staminaBar2Slow, hud.m_staminaText, hud.m_staminaAnimator); });
            TryAttach("eitr", delegate { AttachBar(hud, ElementId.Eitr, hud.m_eitrBarRoot, hud.m_eitrBarFast, hud.m_eitrBarSlow, hud.m_eitrText, hud.m_eitrAnimator); });
            TryAttach("adrenaline", delegate { AttachBar(hud, ElementId.Adrenaline, hud.m_adrenalineBarRoot, hud.m_adrenalineBarFast, hud.m_adrenalineBarSlow, hud.m_adrenalineText, hud.m_adrenalineAnimator); });
            AttachVanillaSimple();
            _nextScan = 0f;
            Logger.LogInfo("HUD found: " + _hudElements.Count + " elements made movable.");
        }

        private void TryAttach(string what, Action body)
        {
            try { body(); }
            catch (Exception e) { Logger.LogWarning("Could not wrap the " + what + " element, it stays as the game draws it: " + e.Message); }
        }

        // An empty RectTransform over the whole of hudroot, pivot as hudroot's, so that at rest a
        // point has the same coordinates in it as in hudroot.
        private RectTransform MakeWrapper(string name, int siblingIndex)
        {
            return MakeWrapper(name, _root, siblingIndex);
        }

        // Over the whole of parent, with parent's pivot: at rest a point has the same
        // coordinates in the wrapper as in the parent.
        private RectTransform MakeWrapper(string name, RectTransform parent, int siblingIndex)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = parent.pivot;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;
            rt.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, parent.childCount - 1));
            return rt;
        }

        private HudElement NewElement(ElementId id, string name, int siblingIndex)
        {
            HudElement e = new HudElement();
            e.Settings = Get(id);
            e.Wrapper = MakeWrapper("HudLayout_" + name, siblingIndex);
            e.Group = e.Wrapper.gameObject.AddComponent<CanvasGroup>();
            e.OwnGroup = true;
            return e;
        }

        private static void AddUpright(HudElement e, Transform t)
        {
            if (t == null || e.Upright.Contains(t)) return;
            e.Upright.Add(t);
            e.UprightBase.Add(t.localRotation);
        }

        private static void AddText(HudElement e, TMP_Text t)
        {
            if (t == null) return;
            e.Texts.Add(t);
            e.TextBase.Add(new float[] { t.fontSize, t.fontSizeMin, t.fontSizeMax });
            e.WrapBase.Add(t.textWrappingMode);
            e.TextSizeBase.Add(t.rectTransform.sizeDelta);
            e.OverflowBase.Add(t.overflowMode);
        }

        // Remembers where the game keeps the number, and the two places we can put it.
        private static void SetupTextPlace(HudElement e, RectTransform center, GuiBar fast)
        {
            if (e.Text == null) return;
            RectTransform t = e.Text.rectTransform;
            e.TextParent = t.parent;
            e.TextSibling = t.GetSiblingIndex();
            e.TextAnchorMin = t.anchorMin; e.TextAnchorMax = t.anchorMax;
            e.TextPivot = t.pivot; e.TextPos = t.anchoredPosition;
            e.CenterHolder = center;
            e.FillHolder = fast != null ? fast.transform as RectTransform : null;
        }

        // healthpanel as a whole: its animator flashes "Health/border" and "darken" by path.
        private void AttachHealth(Hud hud)
        {
            RectTransform panel = hud.m_healthPanel;
            if (panel == null || panel.parent != _root) throw new Exception("healthpanel is not where expected");
            HudElement e = NewElement(ElementId.Health, "Health", panel.GetSiblingIndex());
            panel.SetParent(e.Wrapper, false);

            if (hud.m_healthBarRoot != null) e.Bounds.Add(hud.m_healthBarRoot);
            Transform icon = panel.Find("healthicon");
            if (icon != null) { e.Bounds.Add((RectTransform)icon); AddUpright(e, icon); e.Icon = icon.gameObject; }
            if (e.Bounds.Count == 0) e.Bounds.Add(panel);

            SetupThickness(e, hud.m_healthBarRoot, hud.m_healthBarFast, hud.m_healthBarSlow);
            e.Fast = hud.m_healthBarFast;
            e.Text = hud.m_healthText;
            if (e.Text != null) { AddUpright(e, e.Text.transform); AddText(e, e.Text); }
            SetupTextPlace(e, hud.m_healthBarRoot, hud.m_healthBarFast);
            _hudElements.Add(e);
        }

        // food0..2 and the food symbol under them, out of healthpanel into a frame that copies
        // healthpanel's rectangle: they stay in place and move on their own from then on.
        private void AttachFood(Hud hud)
        {
            RectTransform panel = hud.m_healthPanel;
            if (panel == null || hud.m_foodIcons == null) throw new Exception("food icons not found");
            List<Transform> slots = new List<Transform>();
            foreach (Image icon in hud.m_foodIcons)
            {
                if (icon == null) continue;
                Transform slot = icon.transform.parent != null && icon.transform.parent != panel ? icon.transform.parent : icon.transform;
                if (!slots.Contains(slot)) slots.Add(slot);
            }
            if (slots.Count == 0) throw new Exception("food icons not found");
            // the symbol under the column ("foodicon (1)"); the inactive leftover "foodicon" stays
            Transform symbol = null;
            foreach (Transform ch in panel)
            {
                if (!ch.name.StartsWith("foodicon", StringComparison.OrdinalIgnoreCase) || !ch.gameObject.activeSelf) continue;
                if (hud.m_foodIcon != null && ch == hud.m_foodIcon.transform) continue;
                if (slots.Contains(ch)) continue;
                symbol = ch;
                break;
            }

            HudElement health = HudElementOf(ElementId.Health);
            int index = health != null ? health.Wrapper.GetSiblingIndex() + 1 : _root.childCount;
            HudElement e = NewElement(ElementId.Food, "Food", index);

            GameObject frameGo = new GameObject("HudLayout_FoodFrame", typeof(RectTransform));
            frameGo.layer = panel.gameObject.layer;
            RectTransform frame = (RectTransform)frameGo.transform;
            frame.SetParent(e.Wrapper, false);
            frame.anchorMin = panel.anchorMin;
            frame.anchorMax = panel.anchorMax;
            frame.pivot = panel.pivot;
            frame.anchoredPosition = panel.anchoredPosition;
            frame.sizeDelta = panel.sizeDelta;
            frame.localRotation = panel.localRotation;
            frame.localScale = panel.localScale;

            if (symbol != null) { slots.Insert(0, symbol); e.Icon = symbol.gameObject; }
            foreach (Transform slot in slots)
            {
                slot.SetParent(frame, false);
                RectTransform rt = slot as RectTransform;
                if (rt != null) e.Bounds.Add(rt);
                AddUpright(e, slot);
            }
            if (hud.m_foodTime != null)
                foreach (TMP_Text t in hud.m_foodTime) AddText(e, t);
            _hudElements.Add(e);
        }

        // staminapanel, eitrpanel, adrenalinepanel as a whole: the game sets their
        // anchoredPosition every frame and their animators work by path inside them.
        private void AttachBar(Hud hud, ElementId id, RectTransform panel, GuiBar fast, GuiBar slow, TMP_Text text, Animator anim)
        {
            if (panel == null || panel.parent != _root) throw new Exception("panel is not where expected");
            HudElement e = NewElement(id, id.ToString(), panel.GetSiblingIndex());
            e.Panel = panel;
            e.PanelBase = panel.anchoredPosition;
            panel.SetParent(e.Wrapper, false);

            // the inner "Stamina" rectangle is what shows (eitr and adrenaline sit 23 up and down in it)
            RectTransform inner = fast != null ? fast.transform.parent as RectTransform : null;
            e.Bounds.Add(inner != null ? inner : panel);
            SetupThickness(e, panel, fast, slow);
            e.Fast = fast;
            e.Text = text;
            e.Anim = anim;
            if (text != null) { AddUpright(e, text.transform); AddText(e, text); }
            SetupTextPlace(e, inner != null ? inner : panel, fast);
            if (id == ElementId.Adrenaline)
            {
                // the game sizes the adrenaline bar only while there is adrenaline
                MethodInfo m = AccessTools.Method(typeof(Hud), "SetAdrenalineBarSize");
                if (m != null) e.SetSize = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), hud, m);
            }
            _hudElements.Add(e);
        }

        // Thickness is done on the rectangles, not by scaling, so the number on the bar keeps
        // its shape. Across the bar: the root (Health, or the panel whose inner rectangle is
        // 16 px smaller) and the fill of both GuiBars; the game only ever sets their width.
        private static void SetupThickness(HudElement e, RectTransform root, GuiBar fast, GuiBar slow)
        {
            if (root == null) return;
            e.Root = root;
            e.RootBase = root.rect.height;
            foreach (GuiBar g in new[] { fast, slow })
            {
                if (g == null || g.m_bar == null) continue;
                e.Fills.Add(g.m_bar);
                e.FillBase.Add(g.m_bar.rect.height);
                Image img = g.m_bar.GetComponent<Image>();
                if (img != null) e.FillImages.Add(img);
            }
        }

        // Thickness and cells, both on the fill. The fill is a tiled image: a 32 px tile with a
        // dark column at each edge, and the game makes the bar 32 px per 25 points, so a tile
        // is a 25-point cell. Cells: pixelsPerUnitMultiplier makes a tile cover SegmentSize
        // points. That scales the tile's height too, so the fill's rectangle is made exactly
        // one tile high and brought to the bar's thickness by scale: the pattern stretches,
        // never repeats or crops. The frame and background (sliced / simple images) stretch
        // with the root instead.
        private void ApplyFill(HudElement e, bool on, Player p)
        {
            if (e.Root == null || e.Settings.Thickness == null) return;
            ElementSettings s = e.Settings;
            float th = on ? s.Thickness.Value : 1f;

            float mult = 1f;
            if (on && p != null && s.Segments.Value && e.FillHolder != null && e.FillImages.Count > 0)
            {
                float cur, max;
                CurrentAndMax(s.Id, p, out cur, out max);
                if (max <= 0f && s.Id == ElementId.Adrenaline && _editing) max = 50f;   // as SizeIdleAdrenaline draws it
                Image img = e.FillImages[0];
                float width = e.FillHolder.rect.width;
                if (max > 0f && width > 0f && img.sprite != null && img.pixelsPerUnit > 0f)
                {
                    float cell = width * Mathf.Max(1f, s.SegmentSize.Value) / max;   // wanted tile width
                    cell = Mathf.Max(cell, 3f);                                     // never a smear of edges
                    mult = img.sprite.rect.width / (img.pixelsPerUnit * cell);
                }
            }

            if (Mathf.Approximately(th, e.Thick) && Mathf.Approximately(mult, e.Mult)) return;
            float fill = e.FillBase.Count > 0 ? e.FillBase[0] : e.RootBase;
            e.Root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, e.RootBase + fill * (th - 1f));
            for (int i = 0; i < e.Fills.Count; i++)
            {
                RectTransform f = e.Fills[i];
                if (f == null) continue;
                // one tile high (the tile is as high as the fill at multiplier 1), then scaled to the thickness
                f.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, e.FillBase[i] / mult);
                Vector3 ls = f.localScale;
                ls.y = th * mult;
                f.localScale = ls;
            }
            foreach (Image img in e.FillImages)
                if (img != null) img.pixelsPerUnitMultiplier = mult;
            e.Thick = th;
            e.Mult = mult;
        }

        // Where the number sits. Vanilla is the game's look (health: in the middle of the fill,
        // the others: in the middle of the bar), but done by us too, so the number is never a
        // child of the fill, which gets scaled for thickness.
        private static void ApplyTextPlace(HudElement e, bool on)
        {
            if (e.Text == null || e.TextParent == null || e.Settings.TextPos == null) return;
            RectTransform t = e.Text.rectTransform;
            int place = 0;
            if (on)
            {
                TextPosition p = e.Settings.TextPos.Value;
                if (p == TextPosition.Vanilla) p = e.Settings.Id == ElementId.Health ? TextPosition.Fill : TextPosition.Center;
                place = p == TextPosition.Fill && e.FillHolder != null && e.Fast != null && e.Fast.m_bar != null ? 2 : 1;
                if (place == 1 && e.CenterHolder == null) place = 0;
            }
            if (place != e.TextPlace)
            {
                if (place == 0)
                {
                    t.SetParent(e.TextParent, false);
                    t.SetSiblingIndex(Mathf.Min(e.TextSibling, e.TextParent.childCount - 1));
                    t.anchorMin = e.TextAnchorMin; t.anchorMax = e.TextAnchorMax;
                    t.pivot = e.TextPivot; t.anchoredPosition = e.TextPos;
                }
                else
                {
                    t.SetParent(place == 1 ? e.CenterHolder : e.FillHolder, false);
                    t.SetAsLastSibling();   // over the fill
                    Vector2 a = place == 1 ? new Vector2(0.5f, 0.5f) : new Vector2(0f, 0.5f);
                    t.anchorMin = a; t.anchorMax = a;
                    t.pivot = new Vector2(0.5f, 0.5f);
                    t.anchoredPosition = place == 1 ? new Vector2(0f, e.Settings.Id == ElementId.Health ? 0f : e.TextPos.y) : Vector2.zero;
                }
                e.TextPlace = place;
            }
            if (place == 2)
            {
                // the middle of the filled part: the fill grows from its pivot at the left
                RectTransform bar = e.Fast.m_bar;
                float x = bar.anchoredPosition.x + bar.rect.width * (0.5f - bar.pivot.x);
                Vector2 pos = new Vector2(x, 0f);
                if (t.anchoredPosition != pos) t.anchoredPosition = pos;
            }
        }

        // The game sizes the adrenaline bar only while there is adrenaline; at zero (edit mode,
        // Visibility Always) it would keep an old length. Size it the game's way meanwhile.
        private static FieldInfo _lastMaxAdrenaline;

        private void SizeIdleAdrenaline(HudElement e, Player p)
        {
            if (e.SetSize == null || p == null || p.GetAdrenaline() > 0f) { e.LastSetSize = -1f; return; }
            float max = p.GetMaxAdrenaline();
            if (max <= 0f && _hud != null)
            {
                if (_lastMaxAdrenaline == null) _lastMaxAdrenaline = AccessTools.Field(typeof(Hud), "m_lastMaxAdrenaline");
                if (_lastMaxAdrenaline != null) max = (float)_lastMaxAdrenaline.GetValue(_hud);
            }
            if (max <= 0f && _editing) max = 50f;   // never had any: a length to see and drag
            if (max <= 0f) return;
            float size = BarSize(ElementId.Adrenaline, max / 25f * 32f * 2f);
            if (Mathf.Approximately(size, e.LastSetSize)) return;
            e.SetSize(max / 25f * 32f * 2f);   // the prefix applies Length / FixedLength
            e.LastSetSize = size;
        }

        private static void ApplyIcon(HudElement e, bool on)
        {
            if (e.Icon == null || e.Settings.Icon == null) return;
            bool hide = on && !e.Settings.Icon.Value;
            if (hide == e.IconHidden) return;
            e.Icon.SetActive(!hide);
            e.IconHidden = hide;
        }

        // ------------------------------------------------------------------
        // every frame, after the game's Hud.Update
        // ------------------------------------------------------------------
        internal void OnHudUpdate(Hud hud)
        {
            if (_disabledByErrors) return;
            try
            {
                if (hud != _hud) Attach(hud);
                if (_root == null) return;
                DropDead();
                ScanLate();
                bool on = Active;
                Player p = Player.m_localPlayer;
                foreach (HudElement e in _hudElements) ApplyElement(e, on, p);
            }
            catch (Exception e) { Fail("HUD update", e); }
        }

        private static readonly int VisibleHash = Animator.StringToHash("Visible");

        private void ApplyElement(HudElement e, bool on, Player p)
        {
            ElementSettings s = e.Settings;

            // --- visibility and opacity
            float alpha = 1f;
            bool show = false;              // keep the game's fade-out animation from hiding it
            bool fade = false;
            if (on)
            {
                alpha = s.Opacity.Value;
                if (IsHidden(s)) alpha = 0f;
                else if (s.Vis != null && s.Vis.Value == Visibility.Always) show = HasMax(s.Id, p);
                else if (s.Vis != null && s.Vis.Value == Visibility.NotFull && p != null && s.IsBar)
                {
                    float cur, max;
                    CurrentAndMax(s.Id, p, out cur, out max);
                    if (max <= 0f || cur >= max - 0.05f) alpha = 0f;
                    else show = true;
                    fade = true;
                }
            }
            if (_editing) { alpha = Mathf.Max(alpha, 0.35f); fade = false; }   // hidden ones can still be found and moved
            if (fade && e.Group != null) alpha = Mathf.MoveTowards(e.Group.alpha, alpha, Time.deltaTime * 6f);   // a quick fade, not a blink
            ApplyAlpha(e, alpha);

            if (e.Anim != null && on && e.Anim.isActiveAndEnabled && (_editing || show))
                e.Anim.SetBool(VisibleHash, true);

            // --- look
            ApplyText(e, on, p);
            ApplyColor(e, on);
            ApplyFill(e, on, p);
            ApplyTextPlace(e, on);
            ApplyIcon(e, on);
            if (on && s.Id == ElementId.Adrenaline) SizeIdleAdrenaline(e, p);
            if (s.Id == ElementId.Food && on && !s.Timers.Value && _hud != null && _hud.m_foodTime != null)
                foreach (TMP_Text t in _hud.m_foodTime)
                    if (t != null && t.gameObject.activeSelf) t.gameObject.SetActive(false);

            // --- place
            ApplyTransform(e, on);
        }

        // Opacity. On a CanvasGroup of ours it is simply set. On one the element's owner has
        // (another mod's panel that fades itself) ours multiplies theirs, and what they write is
        // their new value — the same rule as the pose, so the two never feed into each other.
        // Moved-in-place elements get a group of ours only once they need one: one added to the
        // vanilla hotbar would be copied into every hotbar ExtraSlots clones from it.
        private static void ApplyAlpha(HudElement e, float alpha)
        {
            if (e.Group == null)
            {
                if (alpha == 1f || e.Target == null) return;
                e.Group = e.Target.gameObject.AddComponent<CanvasGroup>();
                e.OwnGroup = true;
            }
            if (e.OwnGroup)
            {
                if (e.Group.alpha != alpha) e.Group.alpha = alpha;
                bool blocks = alpha > 0f;
                if (e.Group.blocksRaycasts != blocks) e.Group.blocksRaycasts = blocks;
                return;
            }
            if (e.WrittenAlpha < 0f || Mathf.Abs(e.Group.alpha - e.WrittenAlpha) > 0.0001f) e.BaseAlpha = e.Group.alpha;
            float a = e.BaseAlpha * alpha;
            if (e.Group.alpha != a) e.Group.alpha = a;
            e.WrittenAlpha = e.Group.alpha;
        }

        // Whether the bar has anything to show (no eitr before the first eitr food, no
        // adrenaline without a trinket): Always must not show an empty frame then.
        private static bool HasMax(ElementId id, Player p)
        {
            if (p == null) return false;
            switch (id)
            {
                case ElementId.Eitr: return p.GetMaxEitr() > 0f;
                case ElementId.Adrenaline: return p.GetMaxAdrenaline() > 0f;
            }
            return true;
        }

        private void ApplyText(HudElement e, bool on, Player p)
        {
            ElementSettings s = e.Settings;
            if (s.TextSize == null) return;
            float scale = on ? s.TextSize.Value : 1f;
            TextMode mode = on && s.Text != null ? s.Text.Value : TextMode.Vanilla;
            if (!Mathf.Approximately(scale, e.TextScale) || mode != e.LastMode)
            {
                for (int i = 0; i < e.Texts.Count; i++)
                {
                    TMP_Text t = e.Texts[i];
                    if (t == null) continue;
                    float[] fb = e.TextBase[i];
                    t.fontSize = fb[0] * scale;
                    t.fontSizeMin = fb[1] * scale;
                    t.fontSizeMax = fb[2] * scale;
                    // the rectangle grows too: the small bars' numbers sit in 40×16, where
                    // auto-sizing would keep a bigger font (or "75/100") from ever showing
                    Vector2 sb = e.TextSizeBase[i];
                    bool custom = on && (scale != 1f || mode != TextMode.Vanilla);
                    if (custom && t == e.Text) sb.x = Mathf.Max(sb.x, 100f);
                    t.rectTransform.sizeDelta = custom ? sb * scale : e.TextSizeBase[i];
                    t.overflowMode = custom ? TextOverflowModes.Overflow : e.OverflowBase[i];
                }
                e.TextScale = scale;
            }

            if (e.Text == null || s.Text == null) return;
            bool hide = mode == TextMode.Hidden;
            if (hide != e.TextHidden || e.Text.enabled == hide)
            {
                e.Text.enabled = !hide;
                e.TextHidden = hide;
            }
            if (mode != e.LastMode)
            {
                // our numbers can be wider than the game's rectangle: no wrapping onto two lines
                int i = e.Texts.IndexOf(e.Text);
                if (i >= 0) e.Text.textWrappingMode = mode == TextMode.Vanilla ? e.WrapBase[i] : TextWrappingModes.NoWrap;
                e.LastMode = mode;
                e.LastA = e.LastB = int.MinValue;
                e.LastText = null;
            }
            if (p == null || mode == TextMode.Vanilla || mode == TextMode.Hidden) return;

            float cur, max;
            CurrentAndMax(s.Id, p, out cur, out max);
            int a, b;
            switch (mode)
            {
                case TextMode.Current:
                    a = s.Id == ElementId.Adrenaline ? Mathf.FloorToInt(cur) : Mathf.CeilToInt(cur);
                    b = 0;
                    break;
                case TextMode.CurrentMax:
                    a = s.Id == ElementId.Adrenaline ? Mathf.FloorToInt(cur) : Mathf.CeilToInt(cur);
                    b = Mathf.CeilToInt(max);
                    break;
                default:
                    a = max > 0f ? Mathf.RoundToInt(Mathf.Clamp01(cur / max) * 100f) : 0;
                    b = -1;
                    break;
            }
            // the string is built only when the numbers change...
            if (a != e.LastA || b != e.LastB || e.LastText == null)
            {
                if (mode == TextMode.Current) e.LastText = a.ToString(CultureInfo.InvariantCulture);
                else if (mode == TextMode.CurrentMax) e.LastText = a.ToString(CultureInfo.InvariantCulture) + "/" + b.ToString(CultureInfo.InvariantCulture);
                else e.LastText = a.ToString(CultureInfo.InvariantCulture) + "%";
                e.LastA = a; e.LastB = b;
            }
            // ...but written every frame: the game writes its own number every frame before us
            if (e.Text.text != e.LastText) e.Text.text = e.LastText;
        }

        private static void CurrentAndMax(ElementId id, Player p, out float cur, out float max)
        {
            switch (id)
            {
                case ElementId.Stamina: cur = p.GetStamina(); max = p.GetMaxStamina(); return;
                case ElementId.Eitr: cur = p.GetEitr(); max = p.GetMaxEitr(); return;
                case ElementId.Adrenaline: cur = p.GetAdrenaline(); max = p.GetMaxAdrenaline(); return;
                default: cur = p.GetHealth(); max = p.GetMaxHealth(); return;
            }
        }

        private static void ApplyColor(HudElement e, bool on)
        {
            if (e.Fast == null) return;
            ElementSettings s = e.Settings;
            if (on && s.HasColor)
            {
                e.Fast.SetColor(s.Color);
                e.ColorSet = true;
            }
            else if (e.ColorSet)
            {
                e.Fast.ResetColor();
                e.ColorSet = false;
            }
        }

        // Rotation of the wrapper for the configured orientation, in degrees.
        private static float AngleFor(ElementSettings s)
        {
            if (s.Orient == null) return 0f;
            Orientation o = s.Orient.Value;
            if (o == Orientation.Default) return 0f;
            bool wantVertical = o == Orientation.Vertical;
            if (wantVertical == s.NativeVertical) return 0f;
            // vertical -> horizontal: "up" becomes "right"; horizontal -> vertical: "right" becomes "up"
            return s.NativeVertical ? -90f : 90f;
        }

        private void ApplyTransform(HudElement e, bool on)
        {
            ElementSettings s = e.Settings;
            if (e.Direct)
            {
                if (e.Target == null) return;
                // What someone else set since our last write is the new base, each part on its own
                // and only beyond rounding. The position is followed as the anchoredPosition, not
                // the localPosition: Unity rebuilds the localPosition from the anchors whenever the
                // parent's size changes — at every world load the HUD is built before the canvas
                // has the screen's size — and that jump, taken for someone else's move, made our
                // own written pose the base: the hotbar's scale ran away and its "game's place"
                // drifted. The anchoredPosition only changes when someone really moves it.
                RectTransform tt = e.Target;
                Vector2 ap = tt.anchoredPosition;
                if (!e.HasWritten)
                {
                    e.BaseAp = ap; e.BaseScale = tt.localScale; e.BaseRot = tt.localRotation;
                }
                else
                {
                    if ((ap - e.WrittenAp).sqrMagnitude > PosTolerance * PosTolerance) e.BaseAp = ap;
                    if ((tt.localScale - e.WrittenScale).sqrMagnitude > ScaleTolerance * ScaleTolerance) e.BaseScale = tt.localScale;
                    if (Quaternion.Angle(tt.localRotation, e.WrittenRot) > AngleTolerance) e.BaseRot = tt.localRotation;
                }
                // the base position in parent space as of now: localPosition and anchoredPosition
                // differ by the anchor's point in the parent, the same for any position
                Vector3 lp0 = tt.localPosition;
                e.BaseLp = new Vector3(lp0.x - ap.x + e.BaseAp.x, lp0.y - ap.y + e.BaseAp.y, lp0.z);
            }

            // An element left where the game puts it, at its size and turn, needs no measuring:
            // its transform is the identity whatever it covers. Most of the simple ones are like
            // that, and measuring them means walking all their graphics every frame. The editor
            // needs the frames, so it always measures.
            bool atRest = !_editing && (!on || (!s.HasPosition && s.Scale.Value == 1f && AngleFor(s) == 0f));
            Vector2 c = Vector2.zero;
            if (!atRest) c = Measure(e, on);

            ApplyPose(e, on, c);
        }

        // What the element covers, in its layout space (the wrapper's; for Direct the parent's
        // at the base pose), and the point of it that stays at the configured position.
        private Vector2 Measure(HudElement e, bool on)
        {
            ElementSettings s = e.Settings;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            Vector3[] corners = _corners;
            if (e.AutoBounds)
            {
                // what is drawn now; everything it could draw if nothing is (so it can be found)
                if (Time.unscaledTime - e.GraphicsAt > 2f) RefreshGraphics(e);
                bool any = false;
                for (int pass = 0; pass < 2 && !any; pass++)
                    foreach (Graphic g in e.Graphics)
                    {
                        if (g == null || (pass == 0 && !g.isActiveAndEnabled)) continue;
                        AddCorners(e, g.rectTransform, ref min, ref max);
                        any = true;
                    }
                if (!any && e.Target != null) AddCorners(e, e.Target, ref min, ref max);
            }
            foreach (RectTransform rt in e.Bounds)
            {
                if (rt == null) continue;
                if (e.IconHidden && rt.gameObject == e.Icon) continue;   // a hidden icon takes no room
                AddCorners(e, rt, ref min, ref max);
            }
            if (min.x > max.x) { min = max = Vector2.zero; }
            e.BMin = min; e.BMax = max;

            // the point that stays at the configured position
            Vector2 c = (min + max) * 0.5f;
            if (s.IsBar && on)
            {
                int axis = s.NativeVertical ? 1 : 0;
                switch (s.Anchor.Value)
                {
                    case BarAnchor.Start: c[axis] = min[axis]; break;
                    case BarAnchor.End: c[axis] = max[axis]; break;
                }
            }
            e.Anchor = c;

            // At the game's place a simple element is scaled about the point the game pins it
            // by — its own pivot: the hotbar's top left corner, the minimap's top right... —
            // so a bigger hotbar still starts in the corner. (About the middle of its frame,
            // as for a set position, it slid out of the corner.)
            if (s.IsSimple && !s.HasPosition && e.Target != null)
                c = e.Direct ? (Vector2)e.BaseLp : e.ToLayout(e.Target.position);
            e.Pivot = c;
            return c;
        }

        private void ApplyPose(HudElement e, bool on, Vector2 c)
        {
            ElementSettings s = e.Settings;

            // the game's own lift of the bar in build mode / at the helm
            e.Shift = e.Panel != null ? e.Panel.anchoredPosition - e.PanelBase : Vector2.zero;

            float angle = on ? AngleFor(s) : 0f;
            float scale = on ? s.Scale.Value : 1f;
            Vector2 target = c;
            if (on && s.HasPosition)
            {
                Rect r = _root.rect;
                target = new Vector2(r.xMin + s.PosX.Value * r.width, r.yMin + s.PosY.Value * r.height);
                if (_cfgBuildShift.Value) target += e.Shift;
                // positions are fractions of hudroot; the wrapper may sit in another parent
                Transform parent = e.Direct ? e.Target.parent : e.Wrapper.parent;
                if (parent == null) target = _root.TransformPoint(target);   // a root object: its local space is the world
                else if (parent != _root) target = parent.InverseTransformPoint(_root.TransformPoint(target));
            }

            Quaternion rot = Quaternion.Euler(0f, 0f, angle);
            Vector2 rc = rot * (c * scale);
            Vector3 pos = new Vector3(target.x - rc.x, target.y - rc.y, 0f);
            e.MPos = pos; e.MRot = rot; e.MScale = scale;
            if (e.Direct)
            {
                RectTransform tt = e.Target;
                Vector3 lp = pos + rot * (e.BaseLp * scale);
                // written as an anchoredPosition: the same offset from the base as in parent space
                Vector2 ap = new Vector2(e.BaseAp.x + lp.x - e.BaseLp.x, e.BaseAp.y + lp.y - e.BaseLp.y);
                Vector3 ls = new Vector3(e.BaseScale.x * scale, e.BaseScale.y * scale, e.BaseScale.z);
                Quaternion lr = rot * e.BaseRot;
                if (tt.anchoredPosition != ap) tt.anchoredPosition = ap;
                if (tt.localScale != ls) tt.localScale = ls;
                if (tt.localRotation != lr) tt.localRotation = lr;
                e.WrittenAp = tt.anchoredPosition; e.WrittenScale = tt.localScale; e.WrittenRot = tt.localRotation;
                e.HasWritten = true;
                return;
            }
            if (e.Wrapper.localPosition != pos) e.Wrapper.localPosition = pos;
            if (e.Wrapper.localRotation != rot) e.Wrapper.localRotation = rot;
            Vector3 sc = new Vector3(scale, scale, 1f);
            if (e.Wrapper.localScale != sc) e.Wrapper.localScale = sc;

            if (angle != e.Angle)
            {
                Quaternion back = Quaternion.Euler(0f, 0f, -angle);
                for (int i = 0; i < e.Upright.Count; i++)
                    if (e.Upright[i] != null) e.Upright[i].localRotation = e.UprightBase[i] * back;
                e.Angle = angle;
            }
        }

        private readonly Vector3[] _corners = new Vector3[4];

        // How far a moved-in-place element's pose must be from what we wrote to count as
        // someone else's change: well above float rounding, well below any real move.
        private const float PosTolerance = 0.05f;     // UI units (about pixels)
        private const float ScaleTolerance = 0.0005f;
        private const float AngleTolerance = 0.05f;   // degrees

        private void AddCorners(HudElement e, RectTransform rt, ref Vector2 min, ref Vector2 max)
        {
            rt.GetWorldCorners(_corners);
            for (int i = 0; i < 4; i++)
            {
                Vector2 l = e.ToLayout(_corners[i]);
                min = Vector2.Min(min, l);
                max = Vector2.Max(max, l);
            }
        }

        // The width Hud.Set*BarSize gets: the game's (grows with the maximum, 32 px per 25
        // points) times Length, or with FixedLength a constant base times Length.
        internal float BarSize(ElementId id, float size)
        {
            if (!Active) return size;
            ElementSettings s = Get(id);
            if (s == null || s.Length == null) return size;
            if (s.FixedLength.Value) return FixedBase(id) * s.Length.Value;
            return size * s.Length.Value;
        }

        // The fixed length: the health bar as authored (160, i.e. 125 health), the others as
        // the game would draw 100 points.
        private static float FixedBase(ElementId id)
        {
            return id == ElementId.Health ? 160f : 128f;
        }

        // ------------------------------------------------------------------
        // diagnostics: the hierarchy under hudroot, into the log
        // ------------------------------------------------------------------
        internal string DumpHud()
        {
            if (_root == null) return L("No HUD yet: enter a world first.", "HUD ещё нет: сначала зайдите в мир.");
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            DumpNode(sb, _root, 0);
            // the elements moved in place: what they are based on, what we wrote, what is there now
            foreach (HudElement e in _hudElements)
            {
                if (!e.Direct || e.Target == null) continue;
                sb.Append("in place ").Append(e.Settings.Key)
                  .Append(": base ap=").Append(e.BaseAp).Append(" scale=").Append(e.BaseScale)
                  .Append(" | written ap=").Append(e.WrittenAp).Append(" scale=").Append(e.WrittenScale)
                  .Append(" | now ap=").Append(e.Target.anchoredPosition).Append(" scale=").Append(e.Target.localScale)
                  .Append(" | settings pos=").Append(F(e.Settings.PosX.Value)).Append(',').Append(F(e.Settings.PosY.Value))
                  .Append(" scale=").Append(F(e.Settings.Scale.Value)).Append('\n');
            }
            Logger.LogInfo("HUD hierarchy:\n" + sb);
            return L("HUD hierarchy written to the log.", "Иерархия HUD записана в лог.");
        }

        private static void DumpNode(System.Text.StringBuilder sb, Transform t, int depth)
        {
            sb.Append(' ', depth * 2).Append(t.name);
            if (!t.gameObject.activeSelf) sb.Append(" [inactive]");
            RectTransform rt = t as RectTransform;
            if (rt != null)
                sb.Append(" pos=").Append(rt.anchoredPosition).Append(" size=").Append(rt.sizeDelta)
                  .Append(" anchors=").Append(rt.anchorMin).Append('-').Append(rt.anchorMax).Append(" pivot=").Append(rt.pivot);
            if (t.localEulerAngles.z != 0f) sb.Append(" rot=").Append(t.localEulerAngles.z);
            if (t.localScale != Vector3.one) sb.Append(" scale=").Append(t.localScale);
            sb.Append('\n');
            if (depth < 6)
                foreach (Transform ch in t) DumpNode(sb, ch, depth + 1);
        }
    }
}
