using System;
using BepInEx.Unity.IL2CPP;

namespace LuckyDice;

// The Magnet effect only makes sense when the magnet isn't already always on.
// Reads Magnet Tweaks' Mode setting if that mod is installed (no hard dependency).
internal static class MagnetLink
{
    private const string MagnetTweaksGuid = "tobipolar.drgs.magnettweaks";
    private static bool warned;

    // True when Magnet Tweaks is installed and set to AlwaysActive.
    public static bool MagnetAlwaysOn()
    {
        try
        {
            if (!IL2CPPChainloader.Instance.Plugins.TryGetValue(MagnetTweaksGuid, out var info)) return false;
            if (info?.Instance is not BepInEx.Unity.IL2CPP.BasePlugin plugin) return false;
            foreach (var kv in plugin.Config)
                if (kv.Key.Section == "Magnet" && kv.Key.Key == "Mode")
                    return kv.Value.BoxedValue?.ToString() == "AlwaysActive";
        }
        catch (Exception e)
        {
            if (!warned) { warned = true; Plugin.Logger.LogWarning($"Couldn't read Magnet Tweaks' mode ({e.Message}); Magnet effect stays available."); }
        }
        return false;
    }

    // Drop a real magnet pickup at the dwarf's feet.
    public static void SpawnMagnet()
    {
        var p = Game.GetPlayer();
        var spawner = Game.GetSpawner();
        if (p == null || spawner == null) { Plugin.Logger.LogWarning("Magnet effect: no player/pickup spawner yet."); return; }
        spawner.SpawnMagnet(p.transform.position);
        Plugin.Logger.LogInfo("Magnet effect: magnet spawned.");
    }
}
