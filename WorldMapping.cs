using System.Collections.Generic;
using System.Linq;

namespace IndexedEnemyRandomizer;

// Separate from world globals so progression, biome filtering, and coverage can
// be verified without starting Terraria. World Hardmode state is not an input.
/// <summary>
/// Owns the deterministic mapping and variant groups for one seed and configuration.
/// Contains no live world progression state: defeating a boss or entering Hardmode never reshuffles it.
/// </summary>
internal sealed class WorldMapping
{
    /// <summary>
    /// Seed text retained for deterministic selection of sources outside the main permutation.
    /// </summary>
    private readonly string seed;
    /// <summary>
    /// Native world evil used to classify opposite-evil species into the late pool.
    /// </summary>
    private readonly bool crimson;
    /// <summary>
    /// Forward lookup from each source group's representative to its replacement representative.
    /// </summary>
    private readonly Dictionary<int, int> mapping;
    /// <summary>
    /// Inverse lookup used by hints and mapped event credit; each target has exactly one source group.
    /// </summary>
    private readonly Dictionary<int, int> sourcesByTarget;
    /// <summary>
    /// Resolves every grouped member ID to the single ID used as its mapping key.
    /// </summary>
    private readonly Dictionary<int, int> representatives = new();
    /// <summary>
    /// Lists the actual spawnable members belonging to each representative.
    /// </summary>
    private readonly Dictionary<int, int[]> members = new();
    /// <summary>
    /// Sorted group representatives classified as pre-Hardmode for this world's native evil.
    /// </summary>
    public int[] EarlySources { get; }
    /// <summary>
    /// Sorted late-tier representatives, including eligible opposite-evil species.
    /// </summary>
    public int[] LateSources { get; }
    /// <summary>
    /// Sorted representatives across both tiers; used when progression pools are combined.
    /// </summary>
    public int[] AllSources { get; }
    /// <summary>
    /// Whether source and target groups must stay inside their early or late tier.
    /// </summary>
    public bool SeparateProgressionPools { get; }
    /// <summary>
    /// Whether supported normal/gold families use the weighted variant selection rule.
    /// </summary>
    public bool GroupGoldCritters { get; }

    /// <summary>Whether army species share one mapping across all their tiers.</summary>
    public bool GroupArmyTiers { get; }

    /// <summary>
    /// Filters eligible IDs, forms variant/encounter groups, then builds the forward and inverse maps.
    /// Special groups take precedence over banners; progression checks apply to every member pair.
    /// </summary>
    /// <param name="seed">Stable world seed text.</param>
    /// <param name="crimson">Whether Crimson is the native world evil.</param>
    /// <param name="eligible">Candidate net IDs already screened for independent-spawn eligibility.</param>
    /// <param name="banners">Optional effective banner keys; null disables ordinary banner grouping.</param>
    /// <param name="celebration">World flag allowing Celebration-exclusive targets.</param>
    /// <param name="combineTwins">Whether Retinazer and Spazmatism occupy one paired entry.</param>
    /// <param name="separateProgressionPools">Whether early and late classifications form separate permutations.</param>
    /// <param name="groupGoldCritters">Whether supported normal/gold critters share entries.</param>
    /// <param name="groupArmyTiers">Whether all tiers of each army species share one earliest-tier entry.</param>
    /// <param name="includeOppositeEvil">Whether the other evil is admitted to the late tier, or the combined pool.</param>
    /// <param name="groupMiscVariants">Whether miscellaneous size/color families share entries.</param>
    public WorldMapping(string seed, bool crimson, IEnumerable<int> eligible,
        IReadOnlyDictionary<int, int> banners = null, bool celebration = false, bool combineTwins = false,
        bool separateProgressionPools = true, bool groupGoldCritters = false, bool groupArmyTiers = false,
        bool includeOppositeEvil = true, bool groupMiscVariants = false)
    {
        // Keep only construction inputs; current world time, Hardmode, and defeat flags never enter this object.
        this.seed = seed;
        this.crimson = crimson;
        SeparateProgressionPools = separateProgressionPools;
        GroupGoldCritters = groupGoldCritters;
        GroupArmyTiers = groupArmyTiers;
        // Filter before grouping so an excluded helper cannot sneak into a valid species through a shared banner.
        int[] candidates = eligible.Where(id => !NpcCatalog.IsExclusiveHelper(id) && SpecialSeedRules.Allowed(id, celebration)
                && (includeOppositeEvil || NpcCatalog.AllowedInWorld(id, crimson)))
            .Distinct().OrderBy(id => id).ToArray();
        // The group key combines tier, family/banner, and an optional singleton ID.
        // Negative banner keys are reserved namespaces for special families, avoiding real banner collisions.
        var groups = candidates.GroupBy(id => GetGroupKey(id, banners, combineTwins, groupArmyTiers, groupMiscVariants));
        foreach (var group in groups)
        {
            // Prefer positive base IDs over negative size/costume IDs for stable keys and concise display names.
            int[] variants = group.OrderBy(id => id < 0).ThenBy(id => id).ToArray();
            int representative = variants[0];
            members.Add(representative, variants);
            foreach (int id in variants)
                representatives.Add(id, representative);
        }
        // Shuffle group representatives, not individual variants, so large cosmetic families do not get extra slots.
        EarlySources = members.Keys.Where(IsEarlySource).OrderBy(id => id).ToArray();
        LateSources = members.Keys.Where(id => !IsEarlySource(id)).OrderBy(id => id).ToArray();
        AllSources = members.Keys.OrderBy(id => id).ToArray();
        // Include native evil in the deterministic salt; keep separate salts for early, late, and combined pools.
        string evil = crimson ? "crimson" : "corruption";
        if (separateProgressionPools)
        {
            mapping = SeededSelection.ConstrainedShuffle(seed, evil + ":pre", EarlySources, AllowedGroups);
            foreach (var entry in SeededSelection.ConstrainedShuffle(seed, evil + ":hard", LateSources, AllowedGroups))
                mapping.Add(entry.Key, entry.Value);
        }
        else
            mapping = SeededSelection.ConstrainedShuffle(seed, evil + ":combined", AllSources, AllowedGroups);
        // A permutation has a unique inverse: hints and event credit can find exactly one original source group.
        sourcesByTarget = mapping.ToDictionary(pair => pair.Value, pair => pair.Key);
    }

    // Rules are checked in priority order. Army tiers have an explicit ungrouped
    // rule, so disabling that option cannot accidentally group them by banner.
    private (bool Early, int Banner, int Single) GetGroupKey(int id,
        IReadOnlyDictionary<int, int> banners, bool combineTwins, bool groupArmyTiers, bool groupMiscVariants)
    {
        bool early = SeparateProgressionPools && IsEarlySource(id);

        if (combineTwins && TwinsEncounter.IsTwin(id))
            return (early, -1, 0);
        if (ScutlixEncounter.IsMember(id))
            return (early, -2, 0);
        if (NpcCatalog.IsTruffleWorm(id))
            return (early, -3, 0);

        int armyFamily = ArmyFamilies.Family(id);
        if (armyFamily != 0)
        {
            if (groupArmyTiers)
                return (early, -2000 - armyFamily, 0);

            // Disabled army grouping keeps tiers separate even if they share a banner.
            return (early, 0, id);
        }

        int goldFamily = GoldCritters.Family(id);
        if (GroupGoldCritters && goldFamily != 0)
            return (early, -1000 - goldFamily, 0);

        // Gold rules take priority. Miscellaneous groups precede movement pairs
        // so the two duck colors can share one entry when this option is on.
        int miscFamily = MiscFamilies.Family(id);
        if (groupMiscVariants && miscFamily != 0)
            return (early, -4000 - miscFamily, 0);

        // Movement forms describe the same critter, with or without a banner.
        // Gold grouping runs first so its rarity roll retains both fish colors.
        int critterFamily = CritterForms.Family(id);
        if (critterFamily != 0)
            return (early, -3000 - critterFamily, 0);

        bool canUseBanner = !NpcCatalog.IsLifecycleException(id) && banners != null;
        if (canUseBanner && banners.TryGetValue(id, out int banner) && banner > 0)
            return (early, banner, 0);

        // Without an applicable family/banner, this ID gets its own mapping entry.
        return (early, 0, id);
    }

    /// <summary>
    /// Finds the original spawn group that produces a requested replacement, accepting any variant ID.
    /// </summary>
    /// <param name="targetNetId">Desired replacement ID, including any member of a grouped family.</param>
    /// <param name="sourceNetId">Original source representative when found; otherwise the dictionary default.</param>
    /// <returns>True when the target belongs to the main permutation and therefore has an inverse source.</returns>
    public bool TryFindSource(int targetNetId, out int sourceNetId) =>
        sourcesByTarget.TryGetValue(Representative(targetNetId), out sourceNetId);

    // Opposite-evil species belong to this world's Hardmode tier, even when
    // they are normally pre-Hardmode. Never rebuild the map on entering Hardmode.
    /// <summary>
    /// Classifies an ID by species and native world evil, independently of the world's current Hardmode flag.
    /// </summary>
    public bool IsEarlySource(int id)
    {
        // A family's earliest tier owns its permanent pool. Later forms must
        // resolve to that same entry instead of disappearing when the event upgrades.
        int source = ProgressionSource(id);
        return NpcCatalog.IsPreHardmode(source) && NpcCatalog.AllowedInWorld(source, crimson);
    }

    internal int ProgressionSource(int id) => IsArmyGroup(id) ? ArmyFamilies.Family(id) : id;

    public bool IsArmyGroup(int id) => GroupArmyTiers && ArmyFamilies.Family(id) != 0;

    /// <summary>
    /// Normalizes a known variant to its group key; unknown IDs retain their own identity.
    /// </summary>
    private int Representative(int id) => representatives.TryGetValue(id, out int root) ? root : id;

    /// <summary>
    /// Returns all members of an ID's group, or a singleton for an ungrouped/unknown ID.
    /// </summary>
    public IReadOnlyList<int> Members(int id) =>
        members.TryGetValue(Representative(id), out int[] variants) ? variants : new[] { id };

    /// <summary>
    /// Checks that the ID belongs to a two-member group containing precisely the two Twins.
    /// </summary>
    public bool IsTwinsGroup(int id) => TwinsEncounter.IsTwin(id) && Members(id).Count == 2
        && Members(id).All(TwinsEncounter.IsTwin);

    /// <summary>
    /// Checks that the ID belongs to the paired Scutlix rider-and-mount group.
    /// </summary>
    public bool IsScutlixGroup(int id) => ScutlixEncounter.IsMember(id) && Members(id).Count == 2
        && Members(id).All(ScutlixEncounter.IsMember);

    /// <summary>
    /// Recognizes both forms of the Truffle Worm, which share a mapping and spawn in normal form.
    /// </summary>
    public bool IsTruffleWormGroup(int id) => NpcCatalog.IsTruffleWorm(id);
    /// <summary>
    /// Identifies families that choose gold versus normal appearance using vanilla rarity.
    /// </summary>
    public bool IsGoldCritterGroup(int id) => GroupGoldCritters && GoldCritters.Family(id) != 0;

    /// <summary>
    /// Allows an assignment only when every source variant can safely lead to every target variant.
    /// This prevents a grouped late-tier variant from hiding a progression dependency.
    /// </summary>
    private bool AllowedGroups(int source, int target)
    {
        foreach (int sourceMember in Members(source))
        {
            foreach (int targetMember in Members(target))
            {
                // All army tiers represent the earliest accessible source.
                // Later forms must not falsely lock that source behind Golem.
                if (!ProgressionRules.Allows(ProgressionSource(sourceMember), targetMember))
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Returns a seeded target representative, using the main permutation when possible.
    /// Sources excluded from that permutation receive a deterministic progression-safe fallback.
    /// </summary>
    /// <param name="sourceNetId">Original species/variant ID, before randomization.</param>
    /// <param name="sourceIdentity">Stable identity used only if the source lacks a main-permutation entry.</param>
    /// <param name="preHardmode">Fallback tier hint; known entries always retain their assigned tier and mapping.</param>
    /// <returns>A target representative; concrete variant rolls and paired spawns happen elsewhere.</returns>
    public int Replacement(int sourceNetId, string sourceIdentity, bool preHardmode)
    {
        // Known entries ignore the caller's tier hint; forced early Hardmode spawns keep their own fixed mapping.
        if (mapping.TryGetValue(Representative(sourceNetId), out int target))
            return target;
        // Special-seed-excluded and modded sources are still randomized. They do not
        // displace entries in the complete permutation of eligible vanilla NPCs.
        int[] pool;
        if (!SeparateProgressionPools)
            pool = AllSources;
        else if (preHardmode)
            pool = EarlySources;
        else
            pool = LateSources;

        int[] allowedTargets = pool.Where(candidate => AllowedGroups(sourceNetId, candidate)).ToArray();
        return SeededSelection.Pick(seed, sourceIdentity, allowedTargets, sourceNetId);
    }

}

