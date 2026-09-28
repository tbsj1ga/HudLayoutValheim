using System;
using System.Collections.Generic;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace HudLayout
{
    // Moves, resizes and restyles the player's HUD: the health, stamina, eitr and adrenaline
    // bars and the food icons. Client-side only.
    //
    // How the elements are moved without fighting the game:
    //
    //   * Every element is put into a wrapper of our own: an empty RectTransform stretched over
    //     hudroot, with hudroot's pivot, so that at rest it maps coordinates one to one. The
    //     game never touches the wrapper; our position, rotation and scale go there. What the
    //     game does to the element inside (the stamina panel's anchoredPosition, set every frame
    //     to 130 or 320; the bar widths; the animators) keeps working unchanged.
    //
    //   * The bars are wrapped panel and all: healthpanel, staminapanel, eitrpanel and
    //     adrenalinepanel keep their children, so their animators still find "Health/border",
    //     "darken", "Stamina" by path.
    //
    //   * The food slots (food0..2 and the food symbol under them) live inside healthpanel but
    //     nothing animates them. They move into a wrapper of their own, inside a frame that
    //     copies healthpanel's rectangle, so they sit exactly where they were and can then move
    //     independently of the health bar.
    //
    // The per-frame work is a postfix on Hud.Update (after the game has laid everything out
    // for the frame); the bar length multiplier is a prefix on Hud.Set*BarSize.
    [BepInPlugin(Guid, Name, Version)]
    public partial class HudLayoutPlugin : BaseUnityPlugin
    {
        public const string Guid = "j1ga.hudlayout";
        public const string Name = "HudLayout";
        public const string Version = "0.10.0";

        // Harmony patches are static; they reach the running plugin through this.
        public static HudLayoutPlugin Instance;

        private Harmony _harmony;

        private int _errorCount;
        private bool _disabledByErrors;
        private const int MaxErrors = 25;
        private readonly HashSet<string> _loggedErrors = new HashSet<string>();

        // ------------------------------------------------------------------
        // lifecycle
        // ------------------------------------------------------------------
        private void Awake()
        {
            try
            {
                Instance = this;
                BindConfig();
                BuildStyles();
                BuildPresets();
                LoadUserPresets();
                RegisterCommands();

                _harmony = new Harmony(Guid);
                _harmony.PatchAll(typeof(HudLayoutPlugin).Assembly);
                Logger.LogInfo(Name + " " + Version + " loaded.");
            }
            catch (Exception e)
            {
                Logger.LogError("Failed to start: " + e);
                _disabledByErrors = true;
            }
        }

        private void OnDestroy()
        {
            try
            {
                if (_editing) StopEditing();
                if (_harmony != null) _harmony.UnpatchSelf();
            }
            catch (Exception e) { Logger.LogError("OnDestroy: " + e); }
        }

        private void Update()
        {
            if (_disabledByErrors) return;
            try
            {
                if (ShortcutPressed(_cfgEditKey.Value) && !TypingSomewhere())
                {
                    if (_editing) StopEditing();
                    else
                    {
                        string err = StartEditing();
                        if (err != null && Player.m_localPlayer != null) Player.m_localPlayer.Message(MessageHud.MessageType.Center, err);
                    }
                }
                if (_editing && !CanEdit()) StopEditing();
            }
            catch (Exception e) { Fail("Update", e); }
        }

        // The mod does something: not switched off in the config and not inert after errors.
        internal bool Active
        {
            get { return !_disabledByErrors && _cfgEnabled != null && _cfgEnabled.Value; }
        }

        // The key with its modifiers held, whatever else is held. KeyboardShortcut.IsDown() also
        // demands that NO other key is down, so it would not fire while running.
        private static bool ShortcutPressed(BepInEx.Configuration.KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None) return false;
            BepInEx.IInputSystem input = UnityInput.Current;
            if (input == null || !input.GetKeyDown(shortcut.MainKey)) return false;
            foreach (KeyCode modifier in shortcut.Modifiers)
                if (!input.GetKey(modifier)) return false;
            return true;
        }

        // Chat, the console or a text dialog has the keyboard: the key is a letter being typed.
        private static bool TypingSomewhere()
        {
            if (Chat.instance != null && Chat.instance.HasFocus()) return true;
            if (global::Console.IsVisible()) return true;
            if (TextInput.IsVisible()) return true;
            return false;
        }

        internal void Fail(string where, Exception e)
        {
            string key = where + ": " + e.GetType().Name + ": " + e.Message;
            if (_loggedErrors.Add(key)) Logger.LogError(key + "\n" + e.StackTrace);
            if (++_errorCount >= MaxErrors && !_disabledByErrors)
            {
                _disabledByErrors = true;
                Logger.LogError("Too many errors, " + Name + " is now inert until the game restarts.");
                if (_editing) StopEditing();
            }
        }

        internal void Log(string text)
        {
            Logger.LogInfo(text);
        }
    }
}
