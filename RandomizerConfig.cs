using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace IndexedEnemyRandomizer;

/// <summary>
/// Server-owned options for pool composition, encounter handling, and presentation.
/// DefaultValue attributes supply tModLoader's serialized/reset defaults; map-affecting options are checked lazily.
/// </summary>
public sealed class RandomizerConfig : ModConfig
{
    /// <summary>
    /// Makes the host's settings authoritative for the shared world.
    /// </summary>
    public override ConfigScope Mode => ConfigScope.ServerSide;

    /// <summary>
    /// Includes the other world evil in the late pool, even for its normally pre-Hardmode species.
    /// </summary>
    [DefaultValue(true)]
    public bool IncludeOppositeEvil { get; set; } = true;

    /// <summary>
    /// Rolls normal versus shimmer appearance when a supported town NPC spawns or is rescued.
    /// </summary>
    [DefaultValue(true)]
    public bool RandomizeTownShimmer { get; set; } = true;

    /// <summary>
    /// Opts ordinary town residents into source and target pools; pets, rescues, and protected NPCs stay excluded.
    /// </summary>
    [DefaultValue(false)]
    public bool RandomizeTownNpcs { get; set; }

    /// <summary>
    /// Groups compatible tiers of each Old One's Army species, subject to progression-pool separation.
    /// </summary>
    [DefaultValue(true)]
    public bool GroupOldOnesArmyTiers { get; set; } = true;

    /// <summary>
    /// Rejects additional spawns of a boss family while an encounter of that family remains active.
    /// </summary>
    [DefaultValue(true)]
    public bool LimitBosses { get; set; } = true;

    /// <summary>
    /// Groups supported normal/gold critters and gives the gold subset a ten-percent chance per spawn.
    /// </summary>
    [DefaultValue(true)]
    public bool GroupGoldCritters { get; set; } = true;

    /// <summary>
    /// Allows Red and Purple Solution to shimmer into each other without the usual late-game requirement.
    /// </summary>
    [DefaultValue(true)]
    public bool EarlyEvilSolutionShimmer { get; set; } = true;

    /// <summary>
    /// Refreshes the shared shimmer conversion table immediately after configuration changes.
    /// </summary>
    public override void OnChanged() => SolutionShimmer.Refresh();

    /// <summary>
    /// Keeps NPC-created children, including children reached through projectile chains, in their original forms.
    /// </summary>
    [DefaultValue(true)]
    public bool PreserveNpcChildren { get; set; } = true;

    /// <summary>
    /// Applies the one-active-encounter rule to the explicit miniboss list independently of the boss toggle.
    /// </summary>
    [DefaultValue(true)]
    public bool LimitMiniBosses { get; set; } = true;

    /// <summary>
    /// Keeps early and late species in separate permutations; disabling it creates one combined pool.
    /// </summary>
    [DefaultValue(true)]
    public bool SeparateProgressionPools { get; set; } = true;

    /// <summary>
    /// Groups eligible NPC variants by shared banner, respecting tier boundaries when separate pools are enabled.
    /// </summary>
    [DefaultValue(true)]
    public bool GroupNpcVariants { get; set; }

    /// <summary>
    /// Treats the Twins as one mapping entry that spawns both eyes and earns one encounter credit.
    /// </summary>
    [DefaultValue(true)]
    public bool CombineTwins { get; set; } = true;

    /// <summary>
    /// Removes boss roots from randomization, preserving their normal source behavior.
    /// </summary>
    [DefaultValue(false)]
    public bool ExcludeBosses { get; set; }

    /// <summary>
    /// Uses actual NPC types for event credit instead of the source types that map to them.
    /// </summary>
    [DefaultValue(false)]
    public bool LegacyEventCredit { get; set; }

    /// <summary>
    /// Uses actual pillar-enemy types for shield credit instead of inverse mapping, independently of events.
    /// </summary>
    [DefaultValue(false)]
    public bool LegacyPillarCredit { get; set; }
}
