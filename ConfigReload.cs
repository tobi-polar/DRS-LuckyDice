using System;
using System.IO;
using BepInEx.Configuration;

namespace LuckyDice;

// BepInEx only reads .cfg files at startup. This watches ours and reloads it (on the main thread) when you save it.
internal static class ConfigReload
{
    private static ConfigFile config;
    private static FileSystemWatcher watcher;
    private static volatile bool pending;
    private static DateTime dueAt;

    public static void Start(ConfigFile cfg)
    {
        config = cfg;
        try
        {
            watcher = new FileSystemWatcher(Path.GetDirectoryName(cfg.ConfigFilePath), Path.GetFileName(cfg.ConfigFilePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };
            watcher.Changed += (_, _) => Queue();
            watcher.Created += (_, _) => Queue();
            watcher.Renamed += (_, _) => Queue();
            watcher.EnableRaisingEvents = true;
            Plugin.Logger.LogInfo("Watching config file for live changes.");
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"Live config reload unavailable: {e.Message}"); }
    }

    private static void Queue()
    {
        dueAt = DateTime.UtcNow.AddMilliseconds(300); // editors often write in several steps
        pending = true;
    }

    // Called every frame from the main thread.
    public static void Tick()
    {
        if (!pending || DateTime.UtcNow < dueAt) return;
        pending = false;
        try
        {
            config.Reload();
            DiceModel.ResetMaterial(); // glow is baked into the material
            Plugin.Logger.LogInfo($"Config reloaded. Dice chance = {Dice.DropChancePercent.Value}%");
        }
        catch (Exception e) { Plugin.Logger.LogError($"Config reload failed (file mid-save?): {e.Message}"); pending = true; dueAt = DateTime.UtcNow.AddSeconds(1); }
    }
}
