using System;

namespace LuckyDice;

// Infinite Ammo effect (and part of Rapid Fire): no reloading while it lasts. Magazines are kept full, and any reload already
// under way (beams, turrets and other non-magazine weapons) finishes straight away.
internal static class InfiniteAmmo
{
    private static bool logged;
    private static int errors;

    public static void Tick()
    {
        var p = Game.GetPlayer();
        var weapons = p?.Weapons;
        if (weapons == null) return;

        for (int i = 0; i < weapons.Count; i++)
        {
            var w = weapons[i];
            if (w == null || w.WasCollected || !w) continue;
            try
            {
                var proj = w.TryCast<ProjectileWeaponHandler>();
                if (proj != null)
                {
                    int clip = proj.ClipSize;
                    if (clip > 0 && proj.bulletsLeft < clip)
                    {
                        proj.bulletsLeft = clip;
                        try { proj.AmmoUpdated(); } catch { } // refresh the ammo bar
                    }
                }

                // Reloading anyway (non-magazine weapon, or it emptied this frame): finish it now.
                if (w.reloadTimeLeft > 0.001f) w.reloadTimeLeft = 0.001f;
            }
            catch (Exception e) { if (errors++ < 3) Plugin.Logger.LogError($"Infinite ammo ({w.name}): {e}"); }
        }

        if (!logged) { logged = true; Plugin.Logger.LogInfo("Infinite ammo active."); }
    }

    public static void Ended() => logged = false;
}
