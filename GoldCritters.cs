using System;
using System.Collections.Generic;
using System.Linq;
using Terraria.ID;

namespace IndexedEnemyRandomizer;

/// <summary>
/// Defines supported normal/gold critter families and their weighted per-spawn variant selection.
/// </summary>
internal static class GoldCritters
{
    // Only normal critters and their actual gold equivalents. Gem, evil,
    // explosive, and unrelated bird species retain their own mappings.
    /// <summary>
    /// Returns a stable family key, or zero for species outside the normal/gold grouping feature.
    /// </summary>
    internal static int Family(int id) => id switch
    {
        NPCID.Bird or NPCID.BirdBlue or NPCID.BirdRed or NPCID.GoldBird => NPCID.Bird,
        NPCID.Bunny or NPCID.BunnySlimed or NPCID.BunnyXmas or NPCID.PartyBunny or NPCID.GoldBunny => NPCID.Bunny,
        NPCID.Goldfish or NPCID.GoldfishWalker or NPCID.GoldGoldfish or NPCID.GoldGoldfishWalker => NPCID.Goldfish,
        NPCID.Butterfly or NPCID.GoldButterfly => NPCID.Butterfly,
        NPCID.Frog or NPCID.GoldFrog => NPCID.Frog,
        NPCID.Grasshopper or NPCID.GoldGrasshopper => NPCID.Grasshopper,
        NPCID.Mouse or NPCID.GoldMouse => NPCID.Mouse,
        NPCID.Worm or NPCID.GoldWorm => NPCID.Worm,
        NPCID.Squirrel or NPCID.SquirrelRed or NPCID.SquirrelGold => NPCID.Squirrel,
        >= NPCID.BlackDragonfly and <= NPCID.GoldDragonfly => NPCID.BlackDragonfly,
        NPCID.LadyBug or NPCID.GoldLadyBug => NPCID.LadyBug,
        NPCID.WaterStrider or NPCID.GoldWaterStrider => NPCID.WaterStrider,
        NPCID.Seahorse or NPCID.GoldSeahorse => NPCID.Seahorse,
        _ => 0
    };

    /// <summary>
    /// Uses Terraria's gold-critter metadata to distinguish gold forms from ordinary variants.
    /// </summary>
    internal static bool IsGold(int id) => NPCID.Sets.GoldCrittersCollection.Contains(id);

    /// <summary>
    /// Chooses the gold subset with a ten-percent chance, then chooses uniformly inside the selected subset.
    /// If only one subset exists, uses it directly; the supplied RNG returns values below its exclusive bound.
    /// </summary>
    /// <param name="members">Nonempty member list for a supported critter family.</param>
    /// <param name="next">Random integer provider taking an exclusive upper bound, such as Main.rand.Next.</param>
    /// <returns>A concrete member, with ten-percent gold probability when both subsets exist.</returns>
    internal static int Pick(IReadOnlyList<int> members, Func<int, int> next)
    {
        // Split by gold status before rolling so families with several normal costumes still get exactly ten-percent gold.
        int[] gold = members.Where(IsGold).ToArray();
        int[] normal = members.Where(id => !IsGold(id)).ToArray();
        // Handle incomplete families without selecting an empty subset; only two-sided families need the ten-way roll.
        int[] pool;
        if (gold.Length == 0)
            pool = normal;
        else if (normal.Length == 0)
            pool = gold;
        else if (next(10) == 0)
            pool = gold;
        else
            pool = normal;

        // After choosing rarity, pick the specific form uniformly within that rarity subset.
        if (pool.Length == 1)
            return pool[0];

        return pool[next(pool.Length)];
    }
}
