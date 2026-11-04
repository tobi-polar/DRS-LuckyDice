using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace LuckyDice;

// "Bosco Backup": Bosco gets a temporary copy of every weapon the dwarf is holding.
internal static class BoscoBackup
{
    internal static ConfigEntry<bool> CopyOverclocks;
    internal static ConfigEntry<bool> SkipTurretsAndDrones;

    private static readonly List<WeaponHandler> Copies = new();

    public static void BindConfig(ConfigFile cfg)
    {
        const string s = "Dice Effects - Bosco Backup";
        CopyOverclocks = cfg.Bind(s, "CopyOverclocks", true,
            "Give Bosco's copies the same overclocks as your weapons. Turn off if an overclock's side effect doubles up badly.");
        SkipTurretsAndDrones = cfg.Bind(s, "SkipTurretsAndDrones", false,
            "Don't copy weapons that place turrets or drones (they may behave oddly coming from Bosco).");
    }

    public static bool Available()
    {
        try
        {
            var p = Game.GetPlayer();
            var bosco = p?.Bosco;
            return bosco != null && Game.Alive(bosco) && bosco.gameObject.activeInHierarchy && p.Weapons != null && p.Weapons.Count > 0;
        }
        catch { return false; }
    }

    public static bool Active => Copies.Count > 0;

    public static void Give()
    {
        if (Active) return; // already backed up: the dice just extends the timer
        var p = Game.GetPlayer();
        if (p == null) return;

        var weapons = p.Weapons;
        int given = 0, skipped = 0;
        for (int i = 0; i < weapons.Count; i++)
        {
            var src = weapons[i];
            var data = src?.Data;
            if (data == null) continue;

            if (SkipTurretsAndDrones.Value && IsSpawner(src, data)) { skipped++; Plugin.Logger.LogInfo($"Bosco Backup: skipped {data.Title} (places turrets/drones)."); continue; }

            try
            {
                var copy = p.EquipBoscoWeapon(data);
                if (copy == null) continue;
                Copies.Add(copy);
                given++;

                int levels = src.WeaponLevel - copy.WeaponLevel;
                if (levels > 0) copy.AddWeaponLevel(levels, false);

                if (CopyOverclocks.Value)
                {
                    var ocs = src.AllOverclocks;
                    for (int j = 0; ocs != null && j < ocs.Count; j++)
                    {
                        var oc = ocs[j];
                        if (oc != null && !copy.HasOverlock(oc)) copy.AddOverclock(oc);
                    }
                }
                Plugin.Logger.LogInfo($"Bosco Backup: copied {data.Title} (level {copy.WeaponLevel}).");
            }
            catch (Exception e) { Plugin.Logger.LogError($"Bosco Backup: couldn't copy {data.Title}: {e}"); }
        }
        Plugin.Logger.LogInfo($"Bosco Backup: {given} weapons given, {skipped} turret/drone weapons skipped.");
    }

    public static void Remove()
    {
        if (Copies.Count == 0) return;
        var p = Game.GetPlayer();
        int removed = 0;
        foreach (var copy in Copies)
        {
            try
            {
                if (copy == null || copy.WasCollected) continue;
                p?.BoscoWeapons?.Remove(copy);
                if (copy) { UnityEngine.Object.Destroy(copy); removed++; }
            }
            catch (Exception e) { Plugin.Logger.LogError($"Bosco Backup: couldn't remove a weapon: {e}"); }
        }
        Copies.Clear();
        Plugin.Logger.LogInfo($"Bosco Backup ended: {removed} weapons removed from Bosco.");
    }

    // New stage: the old copies went with the old scene.
    public static void Forget() => Copies.Clear();

    // Check the weapon's behaviour class too: e.g. the LMG Gun Platform's data isn't a "spawn weapon",
    // but its handler is a SpawnWeaponHandler. Orphaned turrets from a removed copy spam errors every frame.
    private static bool IsSpawner(WeaponHandler h, WeaponSkillData d) =>
        h.TryCast<SpawnWeaponHandler>() != null ||
        h.TryCast<OrbitingDroneWeaponHandler>() != null ||
        d.TryCast<SpawnWeaponSkillData>() != null ||
        d.TryCast<DefenseDroneWeaponSkillData>() != null;
}
