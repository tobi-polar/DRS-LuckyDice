using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace LuckyDice;

// Shows a message in the game's own centre-screen announcement (the "SWARM INCOMING!" banner).
// The game only announces localised strings, so we borrow the swarm announcement and swap in our text.
[HarmonyPatch]
internal static class Announcer
{
    private static string pending;
    private static readonly Dictionary<IntPtr, string> Overrides = new();
    private static DRS.UI.UICoreForm form;
    private static bool warned;

    public static void Show(string text)
    {
        try
        {
            if (form == null || form.WasCollected || !form) form = UnityEngine.Object.FindObjectOfType<DRS.UI.UICoreForm>();
            if (form == null) { Warn("game HUD not found"); return; }
            var msg = form.locSwarmIncAnnouncement;
            if (msg == null) { Warn("no announcement text to borrow"); return; }
            pending = text;
            try { form.DoAnnouncement(msg, null); }
            finally { pending = null; }
        }
        catch (Exception e) { Warn(e.Message); }
    }

    private static void Warn(string why)
    {
        if (warned) return;
        warned = true;
        Plugin.Logger.LogWarning($"Dice announcement unavailable ({why}).");
    }

    private static void Apply(DRS.UI.UICoreAnnouncement a)
    {
        if (a == null || !Overrides.TryGetValue(a.Pointer, out var t)) return;
        try { if (a.text != null) a.text.text = t; } catch { }
    }

    // The announcement box picked for our message: remember it gets our text.
    [HarmonyPatch(typeof(DRS.UI.UICoreAnnouncement), nameof(DRS.UI.UICoreAnnouncement.Show))]
    [HarmonyPrefix]
    private static void ShowPrefix(DRS.UI.UICoreAnnouncement __instance)
    {
        if (pending != null) Overrides[__instance.Pointer] = pending;
        else Overrides.Remove(__instance.Pointer); // reused for a real game announcement
    }

    [HarmonyPatch(typeof(DRS.UI.UICoreAnnouncement), nameof(DRS.UI.UICoreAnnouncement.Show))]
    [HarmonyPostfix]
    private static void ShowPostfix(DRS.UI.UICoreAnnouncement __instance) => Apply(__instance);

    // The game re-reads the localised text (e.g. on language change): keep ours.
    [HarmonyPatch(typeof(DRS.UI.UICoreAnnouncement), nameof(DRS.UI.UICoreAnnouncement.UpdateText))]
    [HarmonyPostfix]
    private static void UpdateTextPostfix(DRS.UI.UICoreAnnouncement __instance) => Apply(__instance);

    [HarmonyPatch(typeof(DRS.UI.UICoreAnnouncement), nameof(DRS.UI.UICoreAnnouncement.AnimateIn))]
    [HarmonyPostfix]
    private static void AnimateInPostfix(DRS.UI.UICoreAnnouncement __instance) => Apply(__instance);

    [HarmonyPatch(typeof(DRS.UI.UICoreAnnouncement), nameof(DRS.UI.UICoreAnnouncement.OnDisappearComplete))]
    [HarmonyPostfix]
    private static void Done(DRS.UI.UICoreAnnouncement __instance) => Overrides.Remove(__instance.Pointer);
}
