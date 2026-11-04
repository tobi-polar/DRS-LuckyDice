# Lucky Dice — Deep Rock Galactic: Survivor (Steam)

BepInEx 6 (IL2CPP) plugin. Enemies occasionally drop a golden dice; pick it up for a random effect:
Nuke, Speed Boost, Mining Frenzy, Rapid Fire (with infinite ammo), Infinite Ammo, Magnet (not while Magnet Tweaks is AlwaysActive), One-Hit Kills, Slow-Mo, Invisible, Star Power, Nitra, Gold, Biome Minerals (rare: 2-8 of a biome-rich mineral) or Bosco Backup (Bosco temporarily gets a copy of your whole loadout).
Timed effects show as copies of the game's extraction countdown.

Works on its own or alongside Tobys Tweaks and Magnet Tweaks.

## Build & install

1. BepInEx must be working (see Troubleshooting).
2. Double-click `build.bat`; paste your mod-manager profile folder when asked.
3. `LuckyDice.dll`, `dice.mesh` and `dice.png` land in `BepInEx\plugins\LuckyDice`.
4. Launch with **Start modded**.

## Config

`BepInEx\config\tobipolar.drgs.luckydice.cfg` (reloads live when you save it):
`[Dice]`, `[Dice Effects - Weights]`, `[Dice Effects - Durations]`, `[Dice Effects - Amounts]`, `[Dice Effects - Bosco Backup]`.
Quick test: `DropChancePercent = 100`, `MinSecondsBetweenDrops = 5`.

## Troubleshooting: "Duplicate type with name '<>O'"

In `BepInEx\config\BepInEx.cfg` set `UnityBaseLibrariesSource =` (empty), delete `BepInEx\interop` and `BepInEx\unity-libs`, launch once, then rebuild.
