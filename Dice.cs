using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace LuckyDice;

public enum DiceEffect
{
    Nuke,
    MoveSpeed,
    MiningSpeed,
    FireRate,
    OneHitKills,
    SlowMo,
    Invisible,
    Star,
    Nitra,
    Gold,
    BiomeMineral,
    BoscoBackup,
    InfiniteAmmo,
    Magnet
}

internal static class Dice
{
    // ---------------- Config ----------------
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<float> DropChancePercent;
    internal static ConfigEntry<float> MinSecondsBetweenDrops;
    internal static ConfigEntry<int> MaxOnMap;
    internal static ConfigEntry<float> LifetimeSeconds;
    internal static ConfigEntry<float> DiceSize;
    internal static ConfigEntry<float> Glow;
    internal static ConfigEntry<bool> DropsDuringExtraction;

    internal static readonly Dictionary<DiceEffect, ConfigEntry<int>> Weight = new();
    internal static readonly Dictionary<DiceEffect, ConfigEntry<float>> Duration = new();
    internal static ConfigEntry<float> MoveSpeedAmount, MiningSpeedAmount, FireRateAmount;
    internal static ConfigEntry<float> SlowSpeedPercent;
    internal static ConfigEntry<int> NitraAmount, GoldAmount;

    public static void BindConfig(ConfigFile cfg)
    {
        const string d = "Dice";
        Enabled = cfg.Bind(d, "Enabled", true, "Enemies can drop a golden dice that triggers a random effect when picked up.");
        DropChancePercent = cfg.Bind(d, "DropChancePercent", 0.4f,
            new ConfigDescription("Chance (%) for each enemy kill to drop a dice (subject to the cooldown below).", new AcceptableValueRange<float>(0f, 100f)));
        MinSecondsBetweenDrops = cfg.Bind(d, "MinSecondsBetweenDrops", 25f, "Cooldown after a dice drops before another can.");
        MaxOnMap = cfg.Bind(d, "MaxOnMap", 1, "Most dice that can be lying on the ground at once.");
        LifetimeSeconds = cfg.Bind(d, "LifetimeSeconds", 60f, "A dice disappears if not picked up within this time (blinks for the last 5 s).");
        DropsDuringExtraction = cfg.Bind(d, "DropsDuringExtraction", false, "Allow dice to drop once the extraction countdown has started.");
        DiceSize = cfg.Bind(d, "Size", 0f, "Dice size in world units. 0 = automatic (based on the dwarf's size).");
        Glow = cfg.Bind(d, "Glow", 0.35f, new ConfigDescription("How much the dice glows so it stands out (0 = none).", new AcceptableValueRange<float>(0f, 3f)));

        const string w = "Dice Effects - Weights";
        const string t = "Dice Effects - Durations";
        void Effect(DiceEffect e, int weight, float dur, string desc)
        {
            Weight[e] = cfg.Bind(w, e.ToString(), weight, $"{desc} Relative chance; 0 disables it.");
            if (dur > 0f)
                Duration[e] = cfg.Bind(t, e.ToString(), dur, $"Seconds the {e} effect lasts.");
        }
        Effect(DiceEffect.Nuke, 1, 0, "Kills every enemy on the map.");
        Effect(DiceEffect.MoveSpeed, 4, 20, "Move speed boost.");
        Effect(DiceEffect.MiningSpeed, 4, 20, "Mining speed boost.");
        Effect(DiceEffect.FireRate, 4, 15, "Fire rate boost with infinite ammo.");
        Effect(DiceEffect.OneHitKills, 2, 8, "Every hit on an enemy kills it.");
        Effect(DiceEffect.SlowMo, 4, 20, "All enemies move very slowly.");
        Effect(DiceEffect.Invisible, 2, 15, "Bugs stop chasing and can't attack you; you take no damage.");
        Effect(DiceEffect.Star, 3, 15, "Mario star: you take no damage and any bug that hits you dies.");
        Effect(DiceEffect.Nitra, 2, 0, "Instant nitra.");
        Effect(DiceEffect.Gold, 2, 0, "Instant gold.");
        Effect(DiceEffect.InfiniteAmmo, 3, 10, "Infinite ammo: no reloading.");
        Effect(DiceEffect.Magnet, 3, 0, "Spawns a magnet (pulls in all XP). Never rolled while Magnet Tweaks is set to AlwaysActive.");
        Effect(DiceEffect.BoscoBackup, 3, 20, "Bosco gets a copy of every weapon you're holding.");
        Effect(DiceEffect.BiomeMineral, 1, 0, "2-8 of one of the current biome's rich minerals (rare by design so upgrades don't get out of hand).");

        BoscoBackup.BindConfig(cfg);

        const string a = "Dice Effects - Amounts";
        MoveSpeedAmount = cfg.Bind(a, "MoveSpeedAmount", 1.0f, "MoveSpeed bonus. 1.0 = +100% (double).");
        MiningSpeedAmount = cfg.Bind(a, "MiningSpeedAmount", 1.0f, "MiningSpeed bonus. 1.0 = +100% (double).");
        FireRateAmount = cfg.Bind(a, "FireRateAmount", 1.0f, "FireRate bonus. 1.0 = +100% (double).");
        SlowSpeedPercent = cfg.Bind(a, "SlowSpeedPercent", 50f,
            new ConfigDescription("During SlowMo enemies move at this % of their normal speed (50 = half speed).", new AcceptableValueRange<float>(1f, 100f)));
        NitraAmount = cfg.Bind(a, "NitraAmount", 25, "Nitra given by the Nitra effect.");
        GoldAmount = cfg.Bind(a, "GoldAmount", 25, "Gold given by the Gold effect.");
    }

    // ---------------- Runtime state ----------------
    internal static bool InternalDamage;   // set while the mod itself is damaging something
    private static readonly System.Random Rng = new();
    private static readonly Dictionary<DiceEffect, float> Active = new();
    private static readonly List<DiceDrop> Drops = new();
    private static float cooldown;
    private static float fearTick;
    private static float statLogTimer = -1f;
    private static string statLogName;
    private static Func<Player, double> statLogRead;
    private static double statBefore;
    internal static int BlockedHits, StarKills;
    private static int timerErrors;

    private class DiceDrop
    {
        public GameObject Go;
        public float Age;
        public float BaseY;
        public bool Flying;
    }

    public static bool IsActive(DiceEffect e) => Active.TryGetValue(e, out var t) && t > 0f;

    public static void Init()
    {
        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<DiceHud>();
            var go = new GameObject("LuckyDice.Hud");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<DiceHud>();
        }
        catch (Exception e) { Plugin.Logger.LogError($"HUD setup failed (effects still work): {e}"); }
    }

    public static void ClearStage()
    {
        foreach (var d in Drops)
            if (d.Go != null && !d.Go.WasCollected && d.Go) UnityEngine.Object.Destroy(d.Go);
        Drops.Clear();
        Active.Clear();
        BoscoBackup.Forget();
        EffectTimers.Clear();
        cooldown = 0f;
        statLogTimer = -1f;
    }

    // ---------------- Dropping ----------------
    public static void OnEnemyKilled(Enemy enemy)
    {
        if (!Enabled.Value || InternalDamage || !Game.OutOfPod) return;
        if (Game.ExtractionStarted && !DropsDuringExtraction.Value) return;
        if (cooldown > 0f || Drops.Count >= MaxOnMap.Value) return;
        if (Rng.NextDouble() * 100.0 >= DropChancePercent.Value) return;

        var pos = enemy.transform.position;
        Spawn(pos);
    }

    public static void Spawn(Vector3 pos)
    {
        var go = DiceModel.Create();
        if (go == null) return;

        float size = DiceSize.Value;
        if (size <= 0f)
        {
            size = 1f;
            try
            {
                var col = Game.GetPlayer()?.Collider;
                if (col != null) size = Mathf.Clamp(col.bounds.size.y * 0.55f, 0.3f, 5f);
            }
            catch { }
        }
        go.transform.localScale = Vector3.one * size;
        var start = pos + Vector3.up * size * 0.9f;
        go.transform.position = start;
        go.transform.rotation = Quaternion.Euler(Rng.Next(360), Rng.Next(360), Rng.Next(360));

        Drops.Add(new DiceDrop { Go = go, BaseY = start.y });
        cooldown = MinSecondsBetweenDrops.Value;
        Plugin.Logger.LogInfo($"Dice dropped at {pos} (size {size:0.00}).");
    }

    // ---------------- Per-frame ----------------
    public static void Tick()
    {
        var dt = Time.deltaTime;
        if (dt <= 0f) return; // paused

        if (cooldown > 0f) cooldown -= dt;
        TickDrops(dt);
        TickEffects(dt);
        try { EffectTimers.Tick(Active); } catch (Exception e) { if (timerErrors++ < 3) Plugin.Logger.LogError($"Effect timers: {e}"); }

        if (statLogTimer > 0f && (statLogTimer -= dt) <= 0f)
        {
            var p = Game.GetPlayer();
            if (p != null && statLogRead != null)
                try { Plugin.Logger.LogInfo($"{statLogName} check: {statBefore:0.###} -> {statLogRead(p):0.###}"); } catch { }
        }
    }

    private static void TickDrops(float dt)
    {
        if (Drops.Count == 0) return;
        var player = Game.GetPlayer();
        Vector3 pp = player != null ? player.transform.position : Vector3.zero;
        float grab = 2.5f;
        try { if (player != null) grab = Mathf.Max(2.5f, (float)player.pickupRadius.Value); } catch { }

        for (int i = Drops.Count - 1; i >= 0; i--)
        {
            var d = Drops[i];
            if (d.Go == null || d.Go.WasCollected || !d.Go) { Drops.RemoveAt(i); continue; }

            d.Age += dt;
            var tr = d.Go.transform;
            tr.Rotate(new Vector3(35f, 70f, 20f) * dt, Space.World);

            if (d.Age >= LifetimeSeconds.Value)
            {
                UnityEngine.Object.Destroy(d.Go);
                Drops.RemoveAt(i);
                continue;
            }
            // Blink during the last 5 seconds.
            float left = LifetimeSeconds.Value - d.Age;
            d.Go.SetActive(left > 5f || Mathf.Repeat(left, 0.4f) > 0.15f);

            if (player == null || !player.IsAlive) continue;

            var pos = tr.position;
            var flat = new Vector3(pp.x - pos.x, 0, pp.z - pos.z);
            float dist = flat.magnitude;

            if (!d.Flying && dist <= grab) d.Flying = true;

            if (d.Flying)
            {
                float speed = Mathf.Max(12f, dist * 6f);
                var target = new Vector3(pp.x, d.BaseY, pp.z);
                tr.position = Vector3.MoveTowards(pos, target, speed * dt);
                if (dist < 0.8f)
                {
                    UnityEngine.Object.Destroy(d.Go);
                    Drops.RemoveAt(i);
                    Roll();
                }
            }
            else
            {
                tr.position = new Vector3(pos.x, d.BaseY + Mathf.Sin(d.Age * 3f) * 0.15f * tr.localScale.y, pos.z);
            }
        }
    }

    private static void TickEffects(float dt)
    {
        if (Active.Count == 0) return;
        foreach (var e in new List<DiceEffect>(Active.Keys))
        {
            Active[e] -= dt;
            if (Active[e] <= 0f)
            {
                Active.Remove(e);
                if (e == DiceEffect.BoscoBackup) BoscoBackup.Remove();
                if ((e == DiceEffect.FireRate || e == DiceEffect.InfiniteAmmo) && !IsActive(DiceEffect.FireRate) && !IsActive(DiceEffect.InfiniteAmmo)) global::LuckyDice.InfiniteAmmo.Ended();
                var extra = e == DiceEffect.Invisible ? $" (blocked {BlockedHits} hits)"
                          : e == DiceEffect.Star ? $" (blocked {BlockedHits} hits, killed {StarKills} attackers)" : "";
                Plugin.Logger.LogInfo($"Dice effect ended: {e}{extra}");
            }
        }

        if (IsActive(DiceEffect.FireRate) || IsActive(DiceEffect.InfiniteAmmo)) global::LuckyDice.InfiniteAmmo.Tick();

        // Invisible: keep bugs scared so they keep away from you.
        if (IsActive(DiceEffect.Invisible) && (fearTick -= dt) <= 0f)
        {
            fearTick = 0.5f;
            foreach (var en in Game.LiveEnemies(out _))
                try { en.ApplyFear(1.0f); } catch { }
        }
    }

    // ---------------- Effects ----------------
    private static void Roll()
    {
        // Never hand out nitra while Pickled Nitra is equipped (more nitra = slower dwarf).
        bool skipNitra = Game.HasPickledNitra();
        bool noBosco = !BoscoBackup.Available();
        bool noMagnet = MagnetLink.MagnetAlwaysOn();
        int W(DiceEffect e, ConfigEntry<int> c) =>
            (skipNitra && e == DiceEffect.Nitra) || (noBosco && e == DiceEffect.BoscoBackup) || (noMagnet && e == DiceEffect.Magnet) ? 0 : Math.Max(0, c.Value);

        int total = 0;
        foreach (var kv in Weight) total += W(kv.Key, kv.Value);
        if (total <= 0) { Plugin.Logger.LogWarning("Dice picked up but every effect weight is 0."); return; }

        int r = Rng.Next(total);
        foreach (var kv in Weight)
        {
            r -= W(kv.Key, kv.Value);
            if (r < 0) { Apply(kv.Key); return; }
        }
    }

    public static void Apply(DiceEffect e)
    {
        Plugin.Logger.LogInfo($"Dice rolled: {e}");
        string announce = null;
        try
        {
            switch (e)
            {
                case DiceEffect.Nuke:
                    Game.KillAllEnemies("dice nuke");
                    break;

                case DiceEffect.MoveSpeed:
                    ApplyBuff("MoveSpeed", EStatModID.PICKUP_MOVESPEED, EStatType.MOVESPEED, MoveSpeedAmount.Value, Duration[e].Value, p => p.moveSpeed.Value);
                    break;

                case DiceEffect.MiningSpeed:
                    ApplyBuff("MiningSpeed", EStatModID.PICKUP_MINESPEED, EStatType.MINING_SPEED, MiningSpeedAmount.Value, Duration[e].Value, p => p.miningSpeed.Value);
                    break;

                case DiceEffect.FireRate:
                    ApplyBuff("FireRate", EStatModID.PICKUP_BERSERK, EStatType.FIRE_RATE, FireRateAmount.Value, Duration[e].Value, null);
                    break;

                case DiceEffect.BoscoBackup:
                    BoscoBackup.Give();
                    break;

                case DiceEffect.Magnet:
                    MagnetLink.SpawnMagnet();
                    break;

                case DiceEffect.Nitra:
                    GiveCurrency(ECurrency.NITRA, NitraAmount.Value);
                    break;

                case DiceEffect.Gold:
                    GiveCurrency(ECurrency.GOLD, GoldAmount.Value);
                    break;

                case DiceEffect.BiomeMineral:
                {
                    var rich = RichMinerals();
                    var c = rich[Rng.Next(rich.Length)];
                    int amount = Rng.Next(2, 9); // 2..8, fixed by design
                    GiveCurrency(c, amount);
                    Plugin.Logger.LogInfo($"Biome mineral: +{amount} {MineralName(c)} (rich here: {string.Join(", ", rich)}).");
                    announce = $"+{amount} {MineralName(c).ToUpperInvariant()}";
                    break;
                }

                case DiceEffect.SlowMo:
                    ApplySlow(Game.LiveEnemies(out _), Duration[e].Value, log: true);
                    break;

                case DiceEffect.Invisible:
                case DiceEffect.Star:
                    fearTick = 0f;
                    BlockedHits = 0;
                    StarKills = 0;
                    break;
            }
        }
        catch (Exception ex) { Plugin.Logger.LogError($"Dice effect {e} failed: {ex}"); }

        if (Duration.TryGetValue(e, out var dur))
            Active[e] = Math.Max(Active.TryGetValue(e, out var cur) ? cur : 0f, dur.Value);
        else
            Announcer.Show(announce ?? Label(e)); // instant effects: the game's centre-screen banner (timed ones get a countdown)
    }

    private static void ApplyBuff(string name, EStatModID id, EStatType stat, float amount, float seconds, Func<Player, double> read)
    {
        var p = Game.GetPlayer();
        if (p == null) return;
        statLogName = name;
        statLogRead = read;
        if (read != null) { statBefore = read(p); statLogTimer = 0.5f; }

        var mods = new Il2CppReferenceArray<StatMod>(1);
        mods[0] = new StatMod(stat, EModGroupType.PICKUP, amount);
        p.Stats.AddTimedMod(new TimedBuff
        {
            BuffID = id,
            Guid = System.Guid.NewGuid().ToString(),
            MaxStacks = 1,
            Duration = seconds,
            StatMods = mods,
            TagStatMods = new Il2CppReferenceArray<TagStatMod>(0)
        });
    }

    // Rich minerals per biome (the two the biome is known for).
    private static ECurrency[] RichMinerals()
    {
        var biome = EBiomeType.NONE;
        try { biome = Game.GetPlayer().runSettingsManager.BiomeData.BiomeType; } catch { }
        switch (biome)
        {
            case EBiomeType.CRYSTALLINE_CAVERNS: return new[] { ECurrency.BISMOR, ECurrency.JADIZ };
            case EBiomeType.SALT_PITS:           return new[] { ECurrency.BISMOR, ECurrency.ENOR_PEARL };
            case EBiomeType.GLACIAL_STRATA:      return new[] { ECurrency.BISMOR, ECurrency.JADIZ };
            case EBiomeType.MAGMA_CORE:          return new[] { ECurrency.CROPPA, ECurrency.MAGNITE };
            case EBiomeType.AZURE_WEALD:         return new[] { ECurrency.CROPPA, ECurrency.UMANITE };
            case EBiomeType.HOLLOW_BOUGH:        return new[] { ECurrency.ENOR_PEARL, ECurrency.UMANITE };
        }
        // Unknown biome: fall back to the game's own primary materials for the level, else any mineral.
        try
        {
            var prim = Game.GetPlayer().runSettingsManager.BiomeData.PrimaryCraftingMaterials;
            if (prim != null && prim.Length > 0)
            {
                var list = new List<ECurrency>();
                for (int i = 0; i < prim.Length; i++) list.Add(prim[i]);
                return list.ToArray();
            }
        }
        catch { }
        return Minerals;
    }

    private static readonly ECurrency[] Minerals =
        { ECurrency.BISMOR, ECurrency.CROPPA, ECurrency.ENOR_PEARL, ECurrency.JADIZ, ECurrency.MAGNITE, ECurrency.UMANITE };

    private static string MineralName(ECurrency c) => c switch
    {
        ECurrency.ENOR_PEARL => "ENOR PEARL",
        _ => c.ToString()
    };

    // Spawn real pickups at the dwarf's feet so the game counts them exactly like normal loot.
    private static void GiveCurrency(ECurrency currency, int amount)
    {
        if (amount <= 0) return;
        var p = Game.GetPlayer();
        var spawner = Game.GetSpawner();
        if (p != null && spawner != null)
        {
            spawner.SpawnMaterial(currency, amount, p.transform.position, 1f);
            Plugin.Logger.LogInfo($"Spawned {amount} {currency}.");
            return;
        }
        // Fallback: straight into the wallet.
        var wallet = p?.wallet;
        if (wallet != null)
        {
            wallet.Add(currency, amount, false);
            Plugin.Logger.LogInfo($"Added {amount} {currency} to wallet.");
        }
        else Plugin.Logger.LogWarning($"Couldn't give {currency}: no spawner or wallet found.");
    }

    // How the game reads a timed speed modifier: 0 = not known yet, 1 = added to 100% (-0.5 = half), 2 = straight multiplier (0.5 = half).
    private static int slowMode;

    public static void ApplySlow(List<Enemy> enemies, float seconds, bool log)
    {
        float target = Mathf.Clamp(SlowSpeedPercent.Value, 1f, 100f) / 100f;
        int n = 0;
        foreach (var en in enemies)
        {
            try
            {
                if (slowMode == 0)
                {
                    // Work it out on this enemy: try the "added" reading and see what speed comes out.
                    double before = en.MoveSpeed.Value;
                    AddSlow(en, seconds, target - 1f);
                    double after = en.MoveSpeed.Value;
                    double ratio = before > 0 ? after / before : 1;
                    slowMode = ratio < target * 0.5 ? 2 : 1;
                    Plugin.Logger.LogInfo($"SlowMo calibration: speed {before:0.###} -> {after:0.###} (ratio {ratio:0.##}, wanted {target:0.##}) => {(slowMode == 1 ? "additive" : "multiplier")} mode");
                }
                else
                {
                    AddSlow(en, seconds, slowMode == 1 ? target - 1f : target);
                }
                n++;
            }
            catch (Exception ex) { if (n == 0) Plugin.Logger.LogError($"Slow failed: {ex}"); }
        }
        if (log) Plugin.Logger.LogInfo($"SlowMo applied to {n} enemies at {target:P0} speed.");
    }

    private static void AddSlow(Enemy en, float seconds, float value)
    {
        en.Stats.AddTimedMod(new TimedEnemyStatBuff
        {
            Source = EnemyStatHandler.TimedMod.ESource.AW_HOLE,
            Type = EnemyStat.EType.MOVESPEED,
            Duration = seconds,
            Value = value
        });
    }

    public static void OnEnemySpawned(Enemy enemy)
    {
        if (!Active.TryGetValue(DiceEffect.SlowMo, out var left) || left <= 0f) return;
        ApplySlow(new List<Enemy> { enemy }, left, log: false);
    }

    public static string Label(DiceEffect e) => e switch
    {
        DiceEffect.Nuke => "NUKE!",
        DiceEffect.MoveSpeed => "SPEED BOOST",
        DiceEffect.MiningSpeed => "MINING FRENZY",
        DiceEffect.FireRate => "RAPID FIRE",
        DiceEffect.InfiniteAmmo => "INFINITE AMMO",
        DiceEffect.Magnet => "MAGNET!",
        DiceEffect.Nitra => $"+{NitraAmount.Value} NITRA",
        DiceEffect.Gold => $"+{GoldAmount.Value} GOLD",
        DiceEffect.BiomeMineral => "BIOME MINERALS",
        DiceEffect.BoscoBackup => "BOSCO BACKUP",
        DiceEffect.OneHitKills => "ONE-HIT KILLS",
        DiceEffect.SlowMo => "SLOW-MO",
        DiceEffect.Invisible => "INVISIBLE",
        DiceEffect.Star => "STAR POWER",
        _ => e.ToString()
    };

    public static IEnumerable<KeyValuePair<DiceEffect, float>> ActiveEffects() => Active;
}

// ---------------- Model loading ----------------
internal static class DiceModel
{
    private static Mesh mesh;
    private static Material material;

    public static void ResetMaterial() => material = null;
    private static bool failed;

    private static string Folder => Path.GetDirectoryName(typeof(Plugin).Assembly.Location);

    public static GameObject Create()
    {
        if (failed) return null;
        try
        {
            if (mesh == null || mesh.WasCollected) mesh = LoadMesh(Path.Combine(Folder, "dice.mesh"));
            if (material == null || material.WasCollected) material = MakeMaterial(Path.Combine(Folder, "dice.png"));

            var go = new GameObject("LuckyDice.Dice");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go;
        }
        catch (Exception e)
        {
            failed = true;
            Plugin.Logger.LogError($"Couldn't build the dice model (is dice.mesh/dice.png next to the DLL?): {e}");
            return null;
        }
    }

    private static Mesh LoadMesh(string path)
    {
        var b = File.ReadAllBytes(path);
        if (b.Length < 12 || b[0] != 'T' || b[1] != 'T' || b[2] != 'M' || b[3] != '1') throw new Exception("bad dice.mesh header");
        int nv = BitConverter.ToInt32(b, 4), ni = BitConverter.ToInt32(b, 8), o = 12;

        var pos = new Vector3[nv]; var nrm = new Vector3[nv]; var uv = new Vector2[nv]; var idx = new int[ni];
        for (int i = 0; i < nv; i++, o += 12) pos[i] = new Vector3(BitConverter.ToSingle(b, o), BitConverter.ToSingle(b, o + 4), BitConverter.ToSingle(b, o + 8));
        for (int i = 0; i < nv; i++, o += 12) nrm[i] = new Vector3(BitConverter.ToSingle(b, o), BitConverter.ToSingle(b, o + 4), BitConverter.ToSingle(b, o + 8));
        for (int i = 0; i < nv; i++, o += 8) uv[i] = new Vector2(BitConverter.ToSingle(b, o), BitConverter.ToSingle(b, o + 4));
        for (int i = 0; i < ni; i++, o += 4) idx[i] = BitConverter.ToInt32(b, o);

        var m = new Mesh { name = "LuckyDice.DiceMesh" };
        if (nv > 65535) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.vertices = pos;
        m.normals = nrm;
        m.uv = uv;
        m.triangles = idx;
        m.RecalculateBounds();
        m.hideFlags = HideFlags.DontUnloadUnusedAsset;
        Plugin.Logger.LogInfo($"Dice mesh loaded: {nv} verts, {ni / 3} tris.");
        return m;
    }

    private static Material MakeMaterial(string texPath)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
        if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(texPath)))
            throw new Exception("couldn't decode dice.png");
        tex.hideFlags = HideFlags.DontUnloadUnusedAsset;

        Shader shader = null;
        foreach (var name in new[] { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Universal Render Pipeline/Unlit", "Standard", "Unlit/Texture" })
        {
            shader = Shader.Find(name);
            if (shader != null) { Plugin.Logger.LogInfo($"Dice shader: {name}"); break; }
        }

        Material mat;
        if (shader != null) mat = new Material(shader);
        else
        {
            // Borrow whatever the game's own pickups render with.
            var any = UnityEngine.Object.FindObjectOfType<MeshRenderer>();
            if (any == null || any.sharedMaterial == null) throw new Exception("no usable shader found");
            mat = new Material(any.sharedMaterial);
            Plugin.Logger.LogInfo($"Dice shader borrowed: {mat.shader.name}");
        }

        mat.mainTexture = tex;
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.3f);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.55f);

        float glow = Dice.Glow.Value;
        if (glow > 0f && mat.HasProperty("_EmissionColor"))
        {
            mat.EnableKeyword("_EMISSION");
            if (mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", tex);
            mat.SetColor("_EmissionColor", Color.white * glow);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        mat.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return mat;
    }
}

// ---------------- On-screen text ----------------
public class DiceHud : MonoBehaviour
{
    public DiceHud(IntPtr ptr) : base(ptr) { }


    private void Update()
    {
        ConfigReload.Tick();
    }

    // Pop-up banners removed by request; effect timers use the game's countdown UI instead.
    public static void Announce(string text) { }

    private void OnGUI()
    {
        try
        {
            if (EffectTimers.UsingNativeUi) return; // fallback text only if the game's countdown UI isn't available

            var lines = "";
            foreach (var kv in Dice.ActiveEffects())
                if (kv.Value > 0f) lines += $"{Dice.Label(kv.Key)}  {Mathf.CeilToInt(kv.Value)}s\n";
            if (lines.Length > 0)
                Shadowed(new Rect(0, Screen.height * 0.12f, Screen.width, Screen.height * 0.2f), lines,
                    Mathf.RoundToInt(Screen.height * 0.022f), TextAnchor.UpperCenter, new Color(0.5f, 1f, 0.9f));
        }
        catch { /* IMGUI unavailable: effects still work, console still logs */ }
    }

    // The game strips GUIStyle's copy constructor, so reuse the skin's label style and set it per draw.
    private static void Shadowed(Rect r, string text, int size, TextAnchor align, Color color)
    {
        var style = GUI.skin.label;
        style.fontSize = size;
        style.alignment = align;
        style.normal.textColor = new Color(0, 0, 0, 0.8f);
        GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, style);
        style.normal.textColor = color;
        GUI.Label(r, text, style);
    }
}

// ---------------- Invisibility: bugs stop chasing ----------------
// Only the base behaviour's seek step is hooked. CanAttack is NOT hooked: the game's compiler merged several
// enemy types' tiny CanAttack methods into one shared function, and hooking it per-type crashed the game.
// Damage is blocked separately in PlayerTakeDamage, so attacks that still happen do nothing.
[HarmonyPatch(typeof(BaseEnemyBehaviour), "HandleSeekPathing")]
internal static class InvisibleNoSeek
{
    private static bool Prefix() => !Dice.IsActive(DiceEffect.Invisible);
}

// ---------------- Hooks ----------------
internal static class DicePatches
{
    // Roll for a dice on every kill.
    [HarmonyPatch(typeof(PickupSpawner), nameof(PickupSpawner.OnEnemyDeath))]
    [HarmonyPostfix]
    private static void EnemyDied(Enemy __0)
    {
        try { Dice.OnEnemyKilled(__0); }
        catch (Exception e) { Plugin.Logger.LogError($"Dice drop failed: {e}"); }
    }

    // New enemies arriving during slow-mo are slowed too.
    [HarmonyPatch(typeof(CoreGameEvents), nameof(CoreGameEvents.TriggerEnemySpawn))]
    [HarmonyPostfix]
    private static void EnemySpawned(EnemySpawnArgs __0)
    {
        try { if (__0 != null) Dice.OnEnemySpawned(__0.Enemy); } catch { }
    }

    // One-hit kills: any damage to an enemy becomes lethal.
    [HarmonyPatch(typeof(Enemy), nameof(Enemy.TakeDamage))]
    [HarmonyPrefix]
    private static void EnemyTakeDamage(Enemy __instance, ref long __0)
    {
        if (Dice.IsActive(DiceEffect.OneHitKills) && __0 > 0 && !Dice.InternalDamage)
            __0 = Math.Max(__0, Game.LethalDamage(__instance));
    }

    // Star / Invisible: the player takes no damage; with Star, the bug that hit you dies.
    [HarmonyPatch(typeof(Player), nameof(Player.TakeDamage))]
    [HarmonyPrefix]
    private static bool PlayerTakeDamage(Entity __5, ref bool __result)
    {
        bool star = Dice.IsActive(DiceEffect.Star);
        if (!star && !Dice.IsActive(DiceEffect.Invisible)) return true;

        Dice.BlockedHits++;
        if (star && __5 != null)
        {
            try
            {
                var enemy = __5.TryCast<Enemy>();
                if (enemy != null && Game.Kill(enemy)) Dice.StarKills++;
            }
            catch { }
        }
        __result = false;
        return false;
    }
}
