# Lucky Dice

Killing enemies has a small chance to drop a spinning golden dice. Grab it for a random effect:

| Effect | What it does |
|---|---|
| Nuke | Kills every enemy on the map |
| Speed Boost | +100% move speed |
| Mining Frenzy | +100% mining speed |
| Rapid Fire | +100% fire rate and infinite ammo |
| Infinite Ammo | No reloading |
| Magnet | Spawns a magnet (skipped while Magnet Tweaks is set to AlwaysActive) |
| One-Hit Kills | Every hit kills |
| Slow-Mo | Enemies move at 50% speed |
| Invisible | Bugs stop chasing you and you take no damage |
| Star Power | You take no damage and any bug that hits you dies |
| Bosco Backup | Bosco gets a copy of your whole loadout |
| Nitra / Gold | Instant resources |
| Biome Minerals (rare) | 2-8 of one of the biome's rich minerals |

Timed effects show as countdown boxes in the game's own extraction-timer style. The Nitra effect never rolls while you hold Pickled Nitra.

## Settings
`BepInEx/config/tobipolar.drgs.luckydice.cfg`, or in-game with **Mod Menu**. Changes apply live. Drop chance, cooldown, size, glow, every effect's odds, duration and strength.

## Requirements
BepInExPack IL2CPP (installed automatically by the mod manager).

## Important: BepInEx fix for the current game version

The game's current Unity version breaks BepInEx out of the box (`Duplicate type with name '<>O'` in the log, no mods load). To fix it once:

1. In your mod manager: **Settings → Browse profile folder**, then open `BepInEx\config\BepInEx.cfg`.
2. Find `UnityBaseLibrariesSource` and clear its value so the line reads `UnityBaseLibrariesSource =`
3. Delete the `BepInEx\interop` and `BepInEx\unity-libs` folders.
4. Launch modded once and let it regenerate (can take a minute or two).
