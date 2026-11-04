using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace LuckyDice;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "tobipolar.drgs.luckydice";
    public const string Name = "Lucky Dice";
    public const string Version = "1.3.2";

    internal static ManualLogSource Logger;

    // Plugin ID / name changes: carry the old settings file over the first time (first match wins).
    private void MigrateOldConfig(params string[] oldNames)
    {
        try
        {
            var newPath = Config.ConfigFilePath;
            bool empty = !System.IO.File.Exists(newPath) || new System.IO.FileInfo(newPath).Length == 0;
            if (!empty) return;
            foreach (var oldName in oldNames)
            {
                var oldPath = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, oldName);
                if (!System.IO.File.Exists(oldPath)) continue;
                System.IO.File.Copy(oldPath, newPath, true);
                Config.Reload();
                Log.LogInfo($"Settings copied from the old {oldName}. You can delete that file.");
                return;
            }
        }
        catch (Exception e) { Log.LogWarning($"Couldn't copy old settings: {e.Message}"); }
    }

    public override void Load()
    {
        Logger = Log;
        MigrateOldConfig("toby.drgs.luckydice.cfg");
        Dice.BindConfig(Config);

        Harmony.CreateAndPatchAll(typeof(GamePatches), Guid);
        Harmony.CreateAndPatchAll(typeof(DicePatches), Guid);
        try { Harmony.CreateAndPatchAll(typeof(Announcer), Guid); }
        catch (Exception e) { Log.LogWarning($"Announcements unavailable: {e.Message}"); }
        try { Harmony.CreateAndPatchAll(typeof(InvisibleNoSeek), Guid); }
        catch (Exception e) { Logger.LogError($"Invisibility patch failed: {e}"); }

        Dice.Init();
        ConfigReload.Start(Config);
        Logger.LogInfo($"{Name} {Version} loaded. Enabled = {Dice.Enabled.Value}, chance = {Dice.DropChancePercent.Value}%");
    }
}
