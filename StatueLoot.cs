using Terraria.ID;
using Terraria.ModLoader;

namespace IndexedEnemyRandomizer;

/// <summary>
/// Extends vanilla's statue loot tables without replacing its existing rules.
/// Vanilla checks the final NPC type, so randomized statues use their replacement's rules.
/// </summary>
public sealed class StatueLoot : ModSystem
{
    private static float[] originalRates;
    private static bool[] originalEarlyBlocks;

    public override void PostSetupContent()
    {
        // Capture content defaults once, including rules supplied by other mods.
        originalRates = (float[])NPCID.Sets.StatueSpawnedDropRarity.Clone();
        originalEarlyBlocks = (bool[])NPCID.Sets.NoEarlymodeLootWhenSpawnedFromStatue.Clone();
        Refresh();
    }

    /// <summary>
    /// Rebuilds the overrides from the originals so turning either option off restores its rules.
    /// </summary>
    internal static void Apply(float[] rates, bool[] earlyBlocks, float[] defaults,
        bool[] defaultBlocks, bool blockHardmode, bool reduceLoot)
    {
        for (int id = 0; id < rates.Length; id++)
        {
            // -1 means vanilla has no multiplier. Explicit rates, including zero,
            // always win over our fallback; never multiply an existing reduction again.
            rates[id] = reduceLoot && defaults[id] == -1f ? 0.2f : defaults[id];

            // Use the species' native tier, not its world-evil mapping pool.
            // Only vanilla species have a known tier in our catalog.
            bool hardmodeSpecies = id > 0 && id < NPCID.Count && !NpcCatalog.IsPreHardmode(id);
            earlyBlocks[id] = defaultBlocks[id] || (blockHardmode && hardmodeSpecies);
        }
    }

    internal static void Refresh()
    {
        // Configuration loads before content tables are ready.
        if (originalRates == null)
            return;

        var config = ModContent.GetInstance<RandomizerConfig>();
        Apply(NPCID.Sets.StatueSpawnedDropRarity, NPCID.Sets.NoEarlymodeLootWhenSpawnedFromStatue,
            originalRates, originalEarlyBlocks, config.BlockEarlyHardmodeStatueLoot, config.ReduceStatueLoot);
    }

    public override void OnWorldLoad() => Refresh();

    public override void Unload()
    {
        // Do not leave modified static tables behind after unloading the mod.
        if (originalRates != null)
            Apply(NPCID.Sets.StatueSpawnedDropRarity, NPCID.Sets.NoEarlymodeLootWhenSpawnedFromStatue,
                originalRates, originalEarlyBlocks, false, false);
        originalRates = null;
        originalEarlyBlocks = null;
    }
}
