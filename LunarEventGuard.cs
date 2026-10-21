using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace EnemyRandomizer;

/// <summary>
/// Prevents another Cultist defeat from restarting an already active lunar sequence.
/// </summary>
public sealed class LunarEventGuard : ModSystem
{
    /// <summary>
    /// Hooks the lunar-apocalypse trigger rather than changing Cultist loot or defeat handling.
    /// </summary>
    public override void Load() => On_WorldGen.TriggerLunarApocalypse += Trigger;
    /// <summary>
    /// Detaches the lunar trigger guard.
    /// </summary>
    public override void Unload() => On_WorldGen.TriggerLunarApocalypse -= Trigger;

    /// <summary>
    /// Requires no active lunar flags, countdown, pillar, or settled Moon Lord before starting a new sequence.
    /// </summary>
    internal static bool CanStart(bool lunarEventActive, bool anyTowerActive, int moonLordCountdown,
        IEnumerable<NPC> npcs)
    {
        // Fast-path the normal event flags before scanning entities for transitional or randomized encounters.
        if (lunarEventActive || anyTowerActive || moonLordCountdown > 0)
            return false;
        // Check actual entities as well: flags can lag spawning/death transitions,
        // or a randomized Moon Lord can exist outside a native lunar event.
        foreach (NPC npc in npcs)
            if (npc != null && npc.active && !RandomizedNPC.WillRandomize(npc)
                && (PillarCreditNPC.IsPillar(npc.type) || npc.type == NPCID.MoonLordCore))
                return false;
        return true;
    }

    /// <summary>
    /// Lets only the host start a new lunar sequence and ignores repeated triggers during an existing one.
    /// </summary>
    private static void Trigger(On_WorldGen.orig_TriggerLunarApocalypse orig)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
            return;
        // Call the original world mutation only for a genuinely new sequence; a repeated kill does not reset it.
        if (CanStart(NPC.LunarApocalypseIsUp,
                NPC.TowerActiveSolar || NPC.TowerActiveVortex || NPC.TowerActiveNebula || NPC.TowerActiveStardust,
                NPC.MoonLordCountdown, Main.npc))
            orig();
        // A repeated Cultist kill keeps its normal loot/defeat flag but cannot
        // spawn duplicate pillars, reset shields, or restart an ongoing sequence.
    }
}
