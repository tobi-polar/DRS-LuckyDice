using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace LuckyDice;

// Everything Lucky Dice needs to know about the game, independent of any other mod.
internal static class Game
{
    private static Player player;
    private static PickupSpawner spawner;
    private static EnemySpawner enemySpawner;

    public static bool OutOfPod;          // player has left the landing pod this stage
    public static bool ExtractionStarted; // extraction countdown is running

    public static bool Alive(UnityEngine.Object o) => o != null && !o.WasCollected && o;

    public static Player GetPlayer()
    {
        if (Alive(player)) return player;
        try { player = UnityEngine.Object.FindObjectOfType<Player>(); } catch { player = null; }
        return Alive(player) ? player : null;
    }

    public static PickupSpawner GetSpawner()
    {
        if (Alive(spawner)) return spawner;
        try { spawner = UnityEngine.Object.FindObjectOfType<PickupSpawner>(); } catch { spawner = null; }
        return Alive(spawner) ? spawner : null;
    }

    public static EnemySpawner GetEnemySpawner()
    {
        if (Alive(enemySpawner)) return enemySpawner;
        try { enemySpawner = UnityEngine.Object.FindObjectOfType<EnemySpawner>(); } catch { enemySpawner = null; }
        return Alive(enemySpawner) ? enemySpawner : null;
    }

    internal static void SetSpawner(PickupSpawner s) => spawner = s;
    internal static void SetPlayer(Player p) => player = p;

    public static List<Enemy> LiveEnemies(out string from)
    {
        var targets = new List<Enemy>();
        from = "none";
        try
        {
            var list = GetEnemySpawner()?.enemies;
            if (list != null && list.Count > 0)
            {
                for (int i = 0; i < list.Count; i++) targets.Add(list[i]);
                from = "spawner list";
            }
            else
            {
                foreach (var e in UnityEngine.Object.FindObjectsOfType<Enemy>()) targets.Add(e);
                from = "scene search";
            }
        }
        catch (Exception e) { Plugin.Logger.LogError($"Couldn't list enemies: {e}"); }

        targets.RemoveAll(e => !Alive(e) || e.IsDead || !e.gameObject.activeInHierarchy);
        return targets;
    }

    // Just enough damage to kill, so damage stats and achievements stay sane.
    public static long LethalDamage(Enemy enemy)
    {
        try
        {
            double hp = enemy.CurrentHP;
            double mod = 1.0;
            try { mod = enemy.DamageTakenMod.Value; } catch { }
            if (mod < 0.2 || mod > 5) mod = 1.0;
            return (long)Math.Ceiling(hp / mod * 1.2) + 1;
        }
        catch { return 1_000_000L; }
    }

    public static bool Kill(Enemy enemy)
    {
        if (!Alive(enemy) || enemy.IsDead) return false;
        Dice.InternalDamage = true;
        try
        {
            var dmg = LethalDamage(enemy);
            enemy.TakeDamage(dmg, false, EDamageType.KINETIC, false, EDamageSource.MUTATOR, null, false);
            if (!enemy.IsDead) enemy.Die(EDamageType.KINETIC, dmg, false);
        }
        finally { Dice.InternalDamage = false; }
        return enemy.IsDead;
    }

    public static void KillAllEnemies(string reason)
    {
        var targets = LiveEnemies(out var from);
        int killed = 0, failed = 0;
        foreach (var enemy in targets)
        {
            try { if (Kill(enemy)) killed++; else failed++; }
            catch (Exception e) { if (failed++ == 0) Plugin.Logger.LogError($"Kill failed: {e}"); }
        }
        Plugin.Logger.LogInfo($"Kill all [{reason}] ({from}): {targets.Count} found, {killed} killed, {failed} survived/failed");
    }

    // Pickled Nitra = nitra-scaling artifact whose per-stack mods include move speed.
    public static bool HasPickledNitra()
    {
        try
        {
            var list = GetPlayer()?.EquippedArtifacts;
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                var d = list[i]?.Data?.TryCast<ScaleStatWithWalletArtifactData>();
                if (d == null || d.CurrencyType != ECurrency.NITRA) continue;
                var mods = d.StatModPrCurrency;
                for (int j = 0; mods != null && j < mods.Length; j++)
                    if (mods[j] != null && mods[j].StatType == EStatType.MOVESPEED) return true;
            }
        }
        catch { }
        return false;
    }

    public static void ResetStage()
    {
        OutOfPod = false;
        ExtractionStarted = false;
    }
}

internal static class GamePatches
{
    [HarmonyPatch(typeof(CoreGameEvents), nameof(CoreGameEvents.TriggerStageBegin))]
    [HarmonyPostfix]
    private static void StageBegin()
    {
        Game.ResetStage();
        Dice.ClearStage();
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnExitDropPod))]
    [HarmonyPostfix]
    private static void ExitPod(Player __instance)
    {
        Game.SetPlayer(__instance);
        Game.ResetStage();
        Game.OutOfPod = true;
    }

    [HarmonyPatch(typeof(CoreGameEvents), nameof(CoreGameEvents.TriggerPlayerEnteredDropPod))]
    [HarmonyPostfix]
    private static void EnteredPod() => Game.ResetStage();

    [HarmonyPatch(typeof(DRS.UI.UIDropPodCountdown), nameof(DRS.UI.UIDropPodCountdown.Show))]
    [HarmonyPostfix]
    private static void CountdownShown() { if (Game.OutOfPod) Game.ExtractionStarted = true; }

    [HarmonyPatch(typeof(PickupSpawner), nameof(PickupSpawner.SpawnXP))]
    [HarmonyPostfix]
    private static void CacheSpawner(PickupSpawner __instance) => Game.SetSpawner(__instance);

    // Per-frame driver (only runs during a dive).
    [HarmonyPatch(typeof(GameController), nameof(GameController.Update))]
    [HarmonyPostfix]
    private static void Tick()
    {
        try { Dice.Tick(); }
        catch (Exception e) { if (errors++ < 5) Plugin.Logger.LogError($"Tick failed: {e}"); }
    }
    private static int errors;
}
