using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace LuckyDice;

// Shows each active dice effect as a copy of the game's own extraction countdown box, stacked under it.
internal static class EffectTimers
{
    private class Timer
    {
        public GameObject Go;
        public TMPro.TMP_Text Counter;
        public TMPro.TMP_Text Label; // null when the box only has one text field
        public int LastShown = -1;
    }

    private static readonly Dictionary<DiceEffect, Timer> Timers = new();
    private static DRS.UI.UIDropPodCountdown template;
    private static bool warned;

    public static bool UsingNativeUi { get; private set; }

    private static DRS.UI.UIDropPodCountdown FindTemplate()
    {
        if (template != null && !template.WasCollected && template) return template;
        template = null;
        foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<DRS.UI.UIDropPodCountdown>()))
        {
            var c = o?.TryCast<DRS.UI.UIDropPodCountdown>();
            if (c != null && c.gameObject.scene.IsValid()) { template = c; break; }
        }
        return template;
    }

    public static void Tick(IEnumerable<KeyValuePair<DiceEffect, float>> active)
    {
        bool any = false;
        foreach (var kv in active) if (kv.Value > 0f) { any = true; break; }
        if (!any && Timers.Count == 0) return; // nothing to show: skip the (costly) UI search

        var tpl = FindTemplate();
        UsingNativeUi = tpl != null && tpl.content != null;
        if (!UsingNativeUi)
        {
            if (!warned) { warned = true; Plugin.Logger.LogWarning("Effect timers: extraction countdown UI not found yet; using fallback text."); }
            return;
        }

        var seen = new HashSet<DiceEffect>();
        // The real extraction countdown keeps the top slot while it's showing.
        int slot = tpl.content.gameObject.activeInHierarchy ? 1 : 0;

        foreach (var kv in active)
        {
            if (kv.Value <= 0f) continue;
            seen.Add(kv.Key);
            if (!Timers.TryGetValue(kv.Key, out var t) || t.Go == null || t.Go.WasCollected || !t.Go)
            {
                t = Create(tpl, kv.Key);
                if (t == null) continue;
                Timers[kv.Key] = t;
            }
            Place(tpl, t, slot++);

            int secs = Mathf.CeilToInt(kv.Value);
            if (secs != t.LastShown)
            {
                t.LastShown = secs;
                if (t.Label != null) t.Counter.text = secs.ToString();
                else t.Counter.text = $"<size=50%>{Dice.Label(kv.Key)}</size>  {secs}";
            }
        }

        foreach (var e in new List<DiceEffect>(Timers.Keys))
            if (!seen.Contains(e)) Remove(e);
    }

    private static Timer Create(DRS.UI.UIDropPodCountdown tpl, DiceEffect effect)
    {
        try
        {
            var src = tpl.content;
            var parent = tpl.transform.parent != null ? tpl.transform.parent : tpl.transform;
            var go = UnityEngine.Object.Instantiate(src.gameObject);
            go.name = $"LuckyDice.Timer.{effect}";
            go.transform.SetParent(parent, false);

            // Strip anything that would drive or overwrite the copy: the countdown script and localisation.
            foreach (var comp in go.GetComponentsInChildren<Component>(true))
            {
                if (comp == null) continue;
                var tn = comp.GetIl2CppType().FullName;
                if (tn.Contains("UIDropPodCountdown") || tn.Contains("Localize") || tn.Contains("FontSwap"))
                    UnityEngine.Object.Destroy(comp);
            }

            // Undo any hidden state from the countdown's own show/hide animation.
            go.SetActive(true);
            foreach (var cg in go.GetComponentsInChildren<CanvasGroup>(true)) cg.alpha = 1f;
            if (go.transform.localScale.sqrMagnitude < 0.01f) go.transform.localScale = Vector3.one;

            // Find the copy of the counter text by its path inside the box.
            TMPro.TMP_Text counter = null, label = null;
            var rel = RelativePath(src.transform, tpl.counterText != null ? tpl.counterText.transform : null);
            if (rel != null) counter = (rel.Length == 0 ? go.transform : go.transform.Find(rel))?.GetComponent<TMPro.TMP_Text>();

            foreach (var txt in go.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                if (counter == null) { counter = txt; continue; }
                if (txt.Pointer != counter.Pointer) { label = txt; break; }
            }
            if (counter == null) { UnityEngine.Object.Destroy(go); return null; }

            if (label != null) label.text = Dice.Label(effect);
            else FitInBox(counter, go.GetComponent<RectTransform>());
            Plugin.Logger.LogInfo($"Effect timer created for {effect} (label field: {(label != null ? "yes" : "no")}).");
            return new Timer { Go = go, Counter = counter, Label = label };
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError($"Effect timer create failed: {e}");
            return null;
        }
    }

    // Only one text field: stretch it across the box and let TMP shrink the text so it never spills out.
    private static void FitInBox(TMPro.TMP_Text text, RectTransform box)
    {
        try
        {
            float max = text.fontSize;
            var rt = text.rectTransform;
            if (box != null && rt.Pointer != box.Pointer)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.offsetMin = new Vector2(12f, 6f);
                rt.offsetMax = new Vector2(-12f, -6f);
            }
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.fontSizeMax = max;
            text.fontSizeMin = 8f;
            text.enableAutoSizing = true;
            text.richText = true;
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"Timer text fit: {e.Message}"); }
    }

    private static void Place(DRS.UI.UIDropPodCountdown tpl, Timer t, int slot)
    {
        var src = tpl.content;
        var rt = t.Go.GetComponent<RectTransform>();
        if (rt == null) return;

        // Same spot as the real countdown (converted into our parent's space), then one box-height lower per slot.
        var size = src.rect.size;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;

        var parent = rt.parent;
        Vector3 local = parent.InverseTransformPoint(src.TransformPoint(src.rect.center));
        var prt = parent.TryCast<RectTransform>();
        Vector2 centre = prt != null ? prt.rect.center : Vector2.zero;
        float h = Mathf.Max(size.y * src.localScale.y, 40f) + 8f;
        rt.anchoredPosition = new Vector2(local.x - centre.x, local.y - centre.y - slot * h);
    }

    private static string RelativePath(Transform root, Transform target)
    {
        if (target == null) return null;
        var parts = new List<string>();
        for (var t = target; t != null; t = t.parent)
        {
            if (t.Pointer == root.Pointer) { parts.Reverse(); return string.Join("/", parts); }
            parts.Add(t.name);
        }
        return null; // counter isn't inside the content box
    }

    private static void Remove(DiceEffect e)
    {
        if (Timers.TryGetValue(e, out var t) && t.Go != null && !t.Go.WasCollected && t.Go)
            UnityEngine.Object.Destroy(t.Go);
        Timers.Remove(e);
    }

    public static void Clear()
    {
        foreach (var e in new List<DiceEffect>(Timers.Keys)) Remove(e);
        template = null;
        warned = false;
    }
}
