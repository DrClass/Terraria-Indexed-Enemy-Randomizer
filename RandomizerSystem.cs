using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace IndexedEnemyRandomizer;

/// <summary>
/// Connects pure mapping logic to loaded Terraria content, world data, and configuration.
/// Caches one mapping and selects a concrete variant only when an actual spawn needs it.
/// </summary>
public sealed class RandomizerSystem : ModSystem
{
    /// <summary>
    /// All eligible vanilla net IDs, including optional town residents before configuration filtering.
    /// </summary>
    private static int[] eligible = [];
    /// <summary>
    /// Lazily constructed mapping; null after a world/content reset.
    /// </summary>
    private static WorldMapping mapping;
    // Snapshot of the inputs used to construct the cached map; a changed option invalidates it on next access.
    private static string mappingSeed;
    private static bool mappingCrimson;
    private static bool mappingGrouped;
    private static bool mappingCelebration;
    private static bool mappingTwins;
    private static bool mappingExcludeBosses;
    private static bool mappingSeparatePools;
    private static bool mappingGoldCritters;
    private static bool mappingMiscVariants;
    private static bool mappingArmyTiers;
    private static bool mappingOppositeEvil;
    private static bool mappingTownNpcs;
    /// <summary>
    /// Reads the town-pool opt-in, safely defaulting to false before configuration is available.
    /// </summary>
    internal static bool TownNpcsIncluded => ModContent.GetInstance<RandomizerConfig>()?.RandomizeTownNpcs ?? false;
    /// <summary>
    /// Reads the boss exclusion option, safely defaulting to false during early initialization.
    /// </summary>
    internal static bool BossesExcluded => !(ModContent.GetInstance<RandomizerConfig>()?.IncludeBosses ?? true);
    /// <summary>
    /// Applies the catalog's eligibility rules with the current boss and town configuration.
    /// </summary>
    internal static bool CanRandomize(NPC npc) => NpcCatalog.CanReplace(npc, BossesExcluded, TownNpcsIncluded);
    /// <summary>
    /// Vanilla banner keys indexed by net ID, used only when banner grouping is enabled.
    /// </summary>
    private static Dictionary<int, int> banners = new();

    /// <summary>
    /// Builds the vanilla candidate and banner catalogs after tModLoader has initialized NPC samples.
    /// </summary>
    public override void PostSetupContent()
    {
        // Catalog optional town residents up front so turning their setting on does not require reloading content.
        eligible = ContentSamples.NpcsByNetId
            .Where(pair => pair.Key < NPCID.Count && NpcCatalog.CanReplace(pair.Value, includeTownNpcs: true))
            .Select(pair => pair.Key).OrderBy(id => id).ToArray();
        // Resolve a variant's effective banner through the sample NPC, rather than assuming its net ID is the banner key.
        banners = eligible.ToDictionary(id => id,
            id => Item.NPCtoBanner(ContentSamples.NpcsByNetId[id].BannerID()));
    }

    /// <summary>
    /// Discards the previous mapping so the loaded world's seed will be used on first access.
    /// </summary>
    public override void OnWorldLoad() => ResetMapping();
    /// <summary>
    /// Clears mapping and encounter bookkeeping before another world is opened.
    /// </summary>
    public override void OnWorldUnload() => ResetMapping();
    /// <summary>
    /// Releases cached content samples and world-specific mapping state when the mod unloads.
    /// </summary>
    public override void Unload()
    {
        ResetMapping();
        eligible = [];
        banners.Clear();
    }

    /// <summary>
    /// Invalidates the cached mapping and clears pending spawns, child provenance, vine anchors, and pairs.
    /// </summary>
    private static void ResetMapping()
    {
        mapping = null;
        mappingSeed = null;
        RandomizedNPC.ClearPendingSpawns();
    }

    /// <summary>
    /// Returns the current world's mapping, rebuilding only when its seed, evil, or relevant options change.
    /// Live Hardmode and boss-defeat flags are deliberately absent from the cache key.
    /// </summary>
    internal static WorldMapping GetMapping()
    {
        // Build lazily: world seed/evil must already be loaded (including on a
        // dedicated server). Never rebuild on a Hardmode transition.
        string seed = Main.ActiveWorldFileData.SeedText;
        // Read mapping options once for this lookup, then compare them with the cached construction inputs.
        var config = ModContent.GetInstance<RandomizerConfig>();
        bool grouped = config.GroupNpcVariants;
        bool twins = config.CombineTwins;
        bool separatePools = config.SeparateProgressionPools;
        bool goldCritters = config.GroupGoldCritters;
        bool miscVariants = config.GroupMiscVariants;
        bool armyTiers = config.GroupOldOnesArmyTiers;
        bool oppositeEvil = config.IncludeOppositeEvil;
        if (mapping == null || mappingSeed != seed || mappingCrimson != WorldGen.crimson || mappingGrouped != grouped
            || mappingCelebration != Main.tenthAnniversaryWorld || mappingTwins != twins || mappingExcludeBosses != BossesExcluded
            || mappingSeparatePools != separatePools || mappingGoldCritters != goldCritters || mappingArmyTiers != armyTiers
            || mappingOppositeEvil != oppositeEvil || mappingTownNpcs != TownNpcsIncluded
            || mappingMiscVariants != miscVariants)
        {
            // Record the complete cache key alongside the new mapping so all later lookups share the same instance.
            mappingSeed = seed;
            mappingCrimson = WorldGen.crimson;
            mappingGrouped = grouped;
            mappingCelebration = Main.tenthAnniversaryWorld;
            mappingTwins = twins;
            mappingExcludeBosses = BossesExcluded;
            mappingSeparatePools = separatePools;
            mappingGoldCritters = goldCritters;
            mappingMiscVariants = miscVariants;
            mappingArmyTiers = armyTiers;
            mappingOppositeEvil = oppositeEvil;
            mappingTownNpcs = TownNpcsIncluded;
            // Name the optional arguments so the meaning of each boolean is visible at the call site.
            mapping = new WorldMapping(seed, mappingCrimson, EligibleSources,
                banners: grouped ? banners : null,
                celebration: mappingCelebration,
                combineTwins: twins,
                separateProgressionPools: separatePools,
                groupGoldCritters: goldCritters,
                groupArmyTiers: armyTiers,
                includeOppositeEvil: oppositeEvil,
                groupMiscVariants: miscVariants);
        }
        return mapping;
    }

    /// <summary>
    /// Resolves an NPC's seeded target group and chooses the concrete NPC to spawn.
    /// Encounter roots and Truffle Worms have fixed forms; ordinary variants use gameplay RNG.
    /// </summary>
    internal static int Replacement(NPC npc)
    {
        // Modded NPCs use qualified names; numeric vanilla identities are culture-independent, including negative variants.
        string source = npc.ModNPC?.FullName ?? npc.netID.ToString(CultureInfo.InvariantCulture);
        // A forced vanilla Hardmode spawn always gets its Hardmode mapping,
        // even in pre-Hardmode. Unknown modded sources follow world progression.
        WorldMapping current = GetMapping();
        bool early = npc.ModNPC == null ? current.IsEarlySource(npc.netID) : !Main.hardMode;
        int target = current.Replacement(npc.netID, source, early);
        // Paired encounters start with a known root; PreAI will create its required partner after initialization.
        if (current.IsTwinsGroup(target))
            return NPCID.Retinazer;
        if (current.IsScutlixGroup(target))
            return NPCID.ScutlixRider;
        if (current.IsTruffleWormGroup(target))
            return NPCID.TruffleWorm;
        if (current.IsArmyGroup(target))
            return ArmyFamilies.ForTier(target, ArmyFamilies.CurrentTier);
        // Only the member roll varies from spawn to spawn. The selected family itself remains fixed by the seed.
        IReadOnlyList<int> variants = current.Members(target);
        if (current.IsGoldCritterGroup(target))
        {
            // Vanilla natural spawns roll the nearby player's luck. For replacements
            // use the closest living player, with an ordinary roll when none exists.
            int player = Player.FindClosest(npc.position, npc.width, npc.height);
            if (player >= 0 && player < Main.maxPlayers && Main.player[player].active && !Main.player[player].dead)
                return GoldCritters.Pick(variants, Main.rand.Next, Main.player[player].RollLuck);
            return GoldCritters.Pick(variants, Main.rand.Next);
        }
        return variants.Count == 1 ? variants[0] : variants[Main.rand.Next(variants.Count)];
    }

    /// <summary>
    /// Looks up the original spawn replaced by the requested target in the current mapping.
    /// </summary>
    internal static bool TryFindSource(int targetNetId, out int sourceNetId) =>
        GetMapping().TryFindSource(targetNetId, out sourceNetId);

    /// <summary>
    /// Exposes the current mapping's variants for commands and other presentation code.
    /// </summary>
    internal static IReadOnlyList<int> GroupMembers(int id) => GetMapping().Members(id);

    /// <summary>
    /// Filters the cached vanilla catalog using the current boss-exclusion and town-inclusion options.
    /// </summary>
    internal static int[] EligibleSources => eligible.Where(id => CanRandomize(ContentSamples.NpcsByNetId[id])).ToArray();
}


