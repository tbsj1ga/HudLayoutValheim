using System;
using HarmonyLib;
using UnityEngine;

namespace HudLayout
{
    // ------------------------------------------------------------------
    // the HUD
    // ------------------------------------------------------------------
    [HarmonyPatch(typeof(Hud), "Awake")]
    internal static class HudAwakePatch
    {
        private static void Postfix(Hud __instance)
        {
            if (HudLayoutPlugin.Instance != null) HudLayoutPlugin.Instance.OnHudAwake(__instance);
        }
    }

    [HarmonyPatch(typeof(Hud), "OnDestroy")]
    internal static class HudDestroyPatch
    {
        private static void Postfix(Hud __instance)
        {
            if (HudLayoutPlugin.Instance != null) HudLayoutPlugin.Instance.OnHudDestroy(__instance);
        }
    }

    // After the game has sized, placed and animated everything for the frame.
    [HarmonyPatch(typeof(Hud), "Update")]
    internal static class HudUpdatePatch
    {
        private static void Postfix(Hud __instance)
        {
            if (HudLayoutPlugin.Instance != null) HudLayoutPlugin.Instance.OnHudUpdate(__instance);
        }
    }

    // Bar length: the game computes the width from the maximum (32 px per 25 points); the
    // multiplier goes into that width, so the bar, its fill and the panel stay consistent.
    [HarmonyPatch(typeof(Hud), "SetHealthBarSize")]
    internal static class HealthSizePatch
    {
        private static void Prefix(ref float size)
        {
            if (HudLayoutPlugin.Instance != null) size = HudLayoutPlugin.Instance.BarSize(ElementId.Health, size);
        }
    }

    [HarmonyPatch(typeof(Hud), "SetStaminaBarSize")]
    internal static class StaminaSizePatch
    {
        private static void Prefix(ref float size)
        {
            if (HudLayoutPlugin.Instance != null) size = HudLayoutPlugin.Instance.BarSize(ElementId.Stamina, size);
        }
    }

    [HarmonyPatch(typeof(Hud), "SetEitrBarSize")]
    internal static class EitrSizePatch
    {
        private static void Prefix(ref float size)
        {
            if (HudLayoutPlugin.Instance != null) size = HudLayoutPlugin.Instance.BarSize(ElementId.Eitr, size);
        }
    }

    [HarmonyPatch(typeof(Hud), "SetAdrenalineBarSize")]
    internal static class AdrenalineSizePatch
    {
        private static void Prefix(ref float size)
        {
            if (HudLayoutPlugin.Instance != null) size = HudLayoutPlugin.Instance.BarSize(ElementId.Adrenaline, size);
        }
    }

    // ------------------------------------------------------------------
    // edit mode: the mouse belongs to the editor, the character stands still
    // ------------------------------------------------------------------
    [HarmonyPatch(typeof(PlayerController), "TakeInput")]
    internal static class TakeInputPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (HudLayoutPlugin.Instance == null || !HudLayoutPlugin.Instance.Editing) return true;
            __result = false;
            return false;
        }
    }

    // The wheel resizes elements in the editor; the camera must not zoom with it meanwhile.
    // The distance is put back after every other mod's postfix too: FirstPersonMode, for one,
    // reads the wheel itself (Input.GetAxis) in its own GameCamera.UpdateCamera postfix.
    [HarmonyPatch(typeof(GameCamera), "UpdateCamera")]
    internal static class CameraZoomPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(float ___m_distance, out float __state)
        {
            __state = ___m_distance;
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ref float ___m_distance, float __state)
        {
            if (HudLayoutPlugin.Instance != null && HudLayoutPlugin.Instance.Editing) ___m_distance = __state;
        }
    }

    // And the wheel reads as still for everything that asks the game (hotbar scrolling,
    // server devcommands' wheel bindings...). The instance method, not the one-line static
    // wrapper around it, which the JIT may inline into callers.
    [HarmonyPatch(typeof(ZInput), "Internal_GetMouseScrollWheel")]
    internal static class ScrollWheelPatch
    {
        private static void Postfix(ref float __result)
        {
            if (HudLayoutPlugin.Instance != null && HudLayoutPlugin.Instance.Editing) __result = 0f;
        }
    }

    [HarmonyPatch(typeof(GameCamera), "UpdateMouseCapture")]
    internal static class MouseCapturePatch
    {
        private static void Postfix()
        {
            if (HudLayoutPlugin.Instance == null || !HudLayoutPlugin.Instance.Editing) return;
            ZCursor.LockState = CursorLockMode.None;
            ZCursor.Show();
        }
    }
}
