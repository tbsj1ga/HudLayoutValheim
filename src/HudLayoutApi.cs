using System;
using System.Collections.Generic;
using UnityEngine;

namespace HudLayout
{
    // For other mods: make a HUD object of yours movable in HudLayout's editor, presets and
    // config, with a stable id and a proper name. Anything you put straight into hudroot is
    // picked up without this; the API is for objects elsewhere (your own canvas) and for a
    // nicer name. No hard dependency is needed, call it through reflection:
    //
    //   Type api = Type.GetType("HudLayout.HudLayoutApi, HudLayout");
    //   if (api != null)
    //       api.GetMethod("Register", new[] { typeof(string), typeof(RectTransform), typeof(string) })
    //          .Invoke(null, new object[] { "MyMod.Compass", compassRect, "Compass" });
    //
    // The object is taken over within two seconds. Register again if you recreate it (same id,
    // new object: settings stay). HudLayout composes its offset, scale and rotation onto the
    // object's own pose, so keep setting your position as you like: your value becomes the base.
    // The object stays where it is in your hierarchy, unless you pass wrap = true, which puts it
    // into a wrapper of HudLayout's (only do that if nothing looks the object up by path).
    public static class HudLayoutApi
    {
        // Bumped when the API changes incompatibly.
        public const int ApiVersion = 1;

        internal sealed class Entry
        {
            public string Id, Name;
            public RectTransform Target;
            public bool Wrap;
        }

        internal static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public static void Register(string id, RectTransform target, string displayName)
        {
            Register(id, target, displayName, false);
        }

        public static void Register(string id, RectTransform target, string displayName, bool wrap)
        {
            if (string.IsNullOrEmpty(id) || target == null) return;
            Entry e = new Entry();
            e.Id = id.Trim();
            e.Name = string.IsNullOrEmpty(displayName) ? e.Id : displayName;
            e.Target = target;
            e.Wrap = wrap;
            Entries[e.Id] = e;
        }

        // Stops moving the object. For a wrapped one, the wrapper stays (with no offset).
        public static void Unregister(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            Entries.Remove(id.Trim());
            if (HudLayoutPlugin.Instance != null) HudLayoutPlugin.Instance.ForgetApiElement(id.Trim());
        }

        // Whether the in-game editor is open (e.g. to keep showing a panel that is normally hidden).
        public static bool IsEditing
        {
            get { return HudLayoutPlugin.Instance != null && HudLayoutPlugin.Instance.Editing; }
        }
    }
}
