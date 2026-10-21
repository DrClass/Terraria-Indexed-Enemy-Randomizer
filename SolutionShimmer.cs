using Terraria.ID;
using Terraria.ModLoader;

namespace EnemyRandomizer;

/// <summary>
/// Optionally swaps Red/Purple Solution shimmer conversions while preserving the table values it replaces.
/// </summary>
public sealed class SolutionShimmer : ModSystem
{
    /// <summary>
    /// Whether original conversion entries have been captured and can safely be restored.
    /// </summary>
    private static bool initialized;
    /// <summary>
    /// Red Solution's conversion entry before this system applies its override.
    /// </summary>
    private static int originalRed;
    /// <summary>
    /// Purple Solution's conversion entry before this system applies its override.
    /// </summary>
    private static int originalPurple;

    /// <summary>
    /// Snapshots the loaded conversion table before applying the configured override.
    /// </summary>
    public override void PostSetupContent()
    {
        // Snapshot the loaded values before modifying them, including any values provided during content setup.
        originalRed = ItemID.Sets.ShimmerTransformToItem[ItemID.RedSolution];
        originalPurple = ItemID.Sets.ShimmerTransformToItem[ItemID.PurpleSolution];
        initialized = true;
        Refresh();
    }

    /// <summary>
    /// Changes only the two evil-solution entries, restoring supplied defaults when the feature is disabled.
    /// </summary>
    internal static void Apply(int[] transforms, bool enabled, int redDefault, int purpleDefault)
    {
        transforms[ItemID.RedSolution] = enabled ? ItemID.PurpleSolution : redDefault;
        transforms[ItemID.PurpleSolution] = enabled ? ItemID.RedSolution : purpleDefault;
    }

    /// <summary>
    /// Applies the current option only after the original table entries have been captured.
    /// </summary>
    internal static void Refresh()
    {
        // Config callbacks can occur before content setup, so never restore or overwrite an uncaptured table.
        if (initialized)
            Apply(ItemID.Sets.ShimmerTransformToItem,
                ModContent.GetInstance<RandomizerConfig>().EarlyEvilSolutionShimmer, originalRed, originalPurple);
    }

    /// <summary>
    /// Reapplies the configured conversion behavior as a world loads.
    /// </summary>
    public override void OnWorldLoad() => Refresh();

    /// <summary>
    /// Restores the previous table entries so the override cannot leak into a later mod load.
    /// </summary>
    public override void Unload()
    {
        if (initialized)
            Apply(ItemID.Sets.ShimmerTransformToItem, false, originalRed, originalPurple);
        initialized = false;
    }
}
