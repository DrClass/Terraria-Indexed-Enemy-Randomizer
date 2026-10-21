using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace EnemyRandomizer;

/// <summary>
/// Restricts seed-exclusive targets and preserves vanilla's assembled Mechdusa encounter on eligible worlds.
/// </summary>
public sealed class SpecialSeedRules : ModSystem
{
    /// <summary>
    /// Scoped flag indicating that vanilla is currently assembling Mechdusa's ordinary mechanical-boss roots.
    /// </summary>
    internal static bool CreatingMechdusa { get; private set; }

    /// <summary>
    /// Restricts Golden Slime and Jungle Mimic targets to worlds with the Celebration flag.
    /// </summary>
    internal static bool Allowed(int id, bool celebration) =>
        celebration || id is not (NPCID.GoldenSlime or NPCID.BigMimicJungle);

    /// <summary>
    /// Requires both Remix and For the Worthy flags, the combined special-world conditions used for Mechdusa.
    /// </summary>
    internal static bool MechdusaAllowed(bool remix, bool worthy) => remix && worthy;

    // Mechdusa has no NPC ID: vanilla assembles the normal four mech roots.
    // Keep native summons intact instead of treating those roots as random spawns.
    /// <summary>
    /// Hooks Mechdusa assembly so its component spawns can bypass ordinary randomization.
    /// </summary>
    public override void Load() => On_NPC.SpawnMechQueen += SpawnMechdusa;
    /// <summary>
    /// Removes the assembly hook and clears its scoped state.
    /// </summary>
    public override void Unload()
    {
        On_NPC.SpawnMechQueen -= SpawnMechdusa;
        CreatingMechdusa = false;
    }

    /// <summary>
    /// Rejects unsupported worlds and runs native assembly inside an exception-safe, nestable bypass scope.
    /// </summary>
    private static bool SpawnMechdusa(On_NPC.orig_SpawnMechQueen orig, int player)
    {
        if (!MechdusaAllowed(Main.remixWorld, Main.getGoodWorld))
            return false;
        // Preserve nesting: another caller may already be assembling a protected encounter when this hook runs.
        bool previous = CreatingMechdusa;
        CreatingMechdusa = true;
        try
        {
            return orig(player);
        }
        finally
        {
            CreatingMechdusa = previous;
        }
    }
}
