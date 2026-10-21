using System;
using Terraria;
using Terraria.GameContent.Events;
using Terraria.ModLoader;

namespace EnemyRandomizer;

/// <summary>
/// Keeps an active event's progress bar visible even when randomized NPCs do not match its native roster.
/// </summary>
public sealed class EventProgressDisplay : ModSystem
{
    /// <summary>
    /// Hooks the progress display on graphical clients; dedicated servers have no display to update.
    /// </summary>
    public override void Load()
    {
        if (!Main.dedServ)
            On_Main.CheckInvasionProgressDisplay += KeepActiveEventVisible;
    }

    /// <summary>
    /// Removes the client display hook.
    /// </summary>
    public override void Unload()
    {
        if (!Main.dedServ)
            On_Main.CheckInvasionProgressDisplay -= KeepActiveEventVisible;
    }

    // Same event priority/icons as Main.SyncAnInvasion. No NPC, location, or
    // depth checks: randomized enemies need not resemble their event's roster.
    /// <summary>
    /// Chooses the active event icon using vanilla's event priority, or zero when no supported event is active.
    /// </summary>
    internal static int ActiveIcon(bool snowMoon, bool pumpkinMoon, bool oldOnesArmy, int invasionType)
    {
        if (snowMoon)
            return 1;
        if (pumpkinMoon)
            return 2;
        if (oldOnesArmy)
            return 3;
        if (invasionType is >= 1 and <= 4)
            return invasionType + 3;

        return 0;
    }

    /// <summary>
    /// Bootstraps missing event progress and refreshes visibility without overwriting received authoritative progress.
    /// </summary>
    private static void KeepActiveEventVisible(On_Main.orig_CheckInvasionProgressDisplay orig)
    {
        int icon = ActiveIcon(Main.snowMoon, Main.pumpkinMoon, DD2Event.Ongoing, Main.invasionType);
        // Respect the user's Hide setting and fall back to vanilla whenever there is no active event to keep visible.
        if (Main.gameMenu || icon == 0 || Main.invasionProgressMode == 0)
        {
            orig();
            return;
        }

        // A packet may already have supplied authoritative progress. Never
        // overwrite it with client-side waveKills/invasionSize every frame.
        if (Main.invasionProgressIcon != icon || Main.invasionProgress < 0)
        {
            if (icon == 3)
                DD2Event.ReportEventProgress();
            else if (icon is 1 or 2)
            {
                // Clamp before indexing the wave requirements; transition states can temporarily carry an out-of-range wave.
                int wave = Math.Clamp(NPC.waveNumber, 0, NPC.MoonEventRequiredPointsPerWaveLookup.Length - 1);
                Main.ReportInvasionProgress((int)NPC.waveKills,
                    NPC.MoonEventRequiredPointsPerWaveLookup[wave], icon, wave);
            }
            else
                Main.ReportInvasionProgress(Main.invasionSizeStart - Main.invasionSize,
                    Math.Max(1, Main.invasionSizeStart), icon, 0);
        }
        // Keep only visibility alive here; actual progress still comes from normal event accounting and sync.
        Main.invasionProgressNearInvasion = true;
        Main.invasionProgressDisplayLeft = 160;
        // Vanilla drawing still handles fades, progress text, and the user's
        // explicit Hide setting. Once the event ends, its normal timeout resumes.
    }
}
