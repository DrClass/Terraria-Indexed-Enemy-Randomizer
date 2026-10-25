using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace IndexedEnemyRandomizer;

/// <summary>
/// Schedules host-side NPC replacement and applies it immediately before the first AI update.
/// Deferral lets vanilla finish its spawn setup before replacement-specific defaults are installed.
/// </summary>
public sealed class RandomizedNPC : GlobalNPC
{
    /// <summary>
    /// Synchronizes whether a vine uses an artificial anchor so clients run the same anchor check.
    /// </summary>
    public override void SendExtraAI(NPC npc, BitWriter bitWriter, BinaryWriter binaryWriter) =>
        binaryWriter.Write(VineSpawn.AnchorExists(false, npc));

    /// <summary>
    /// Restores the host's floating-vine marker on the receiving client.
    /// </summary>
    public override void ReceiveExtraAI(NPC npc, BitReader bitReader, BinaryReader binaryReader) =>
        VineSpawn.SetFloating(npc, binaryReader.ReadBoolean());

    /// <summary>
    /// Stores one spawn decision outside GlobalNPC instances so SetDefaults cannot erase it.
    /// </summary>
    private sealed class SpawnIntent(bool randomize, ulong tick)
    {
        /// <summary>
        /// Whether the original spawn source allowed replacement when its callback was registered.
        /// </summary>
        public bool Randomize { get; } = randomize;
        /// <summary>
        /// Spawn tick used to recognize the two eyes created together by one Twins summon.
        /// </summary>
        public ulong Tick { get; } = tick;
        /// <summary>
        /// Marks the redundant half of a paired source for cancellation before its first AI update.
        /// </summary>
        public bool Suppress { get; set; }
    }
    /// <summary>
    /// Weakly associates NPC instances with their one-time pending spawn decisions.
    /// </summary>
    private static readonly ConditionalWeakTable<NPC, SpawnIntent> Pending = new();
    /// <summary>
    /// Tracks nested vanilla form transformations so their callbacks cannot schedule another randomization.
    /// </summary>
    private static readonly HashSet<NPC> Transforming = new();

    // Some integrations dispatch spawn callbacks while resetting a transformed
    // NPC. Keep both pending initial spawns and existing encounter links intact.
    /// <summary>
    /// Runs a form change inside a nesting-safe guard and restores that guard even if the change throws.
    /// </summary>
    internal static void DuringTransformation(NPC npc, System.Action transform)
    {
        // Nested transforms share the guard; only the caller that first added it is responsible for removing it.
        bool outermost = Transforming.Add(npc);
        try
        {
            transform();
        }
        finally
        {
            if (outermost)
                Transforming.Remove(npc);
        }
    }

    /// <summary>
    /// Reports whether an NPC still awaits an eligible replacement; duplicate checks ignore such source identities.
    /// </summary>
    internal static bool WillRandomize(NPC npc) => Pending.TryGetValue(npc, out var intent)
        && intent.Randomize && RandomizerSystem.CanRandomize(npc);

    /// <summary>
    /// Clears all per-world spawn bookkeeping, including companion links and projectile ancestry.
    /// </summary>
    internal static void ClearPendingSpawns()
    {
        // Discard weak-table entries as well as ordinary collections so reused slots cannot carry state between worlds.
        Pending.Clear();
        VineSpawn.Reset();
        Transforming.Clear();
        NpcChildProjectiles.Reset();
        TwinsEncounter.Reset();
    }

    /// <summary>
    /// Registers newly spawned NPCs on the host; clients receive the host's final synchronized NPC.
    /// </summary>
    public override void OnSpawn(NPC npc, IEntitySource source)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
            return;

        RegisterSpawn(npc, source, Main.GameUpdateCount, ModContent.GetInstance<RandomizerConfig>().PreserveNpcChildren);
    }

    /// <summary>
    /// Resets reused-slot bookkeeping and records whether this spawn should randomize.
    /// Direct summons, protected children, and Mechdusa assembly bypass replacement.
    /// </summary>
    /// <param name="npc">Newly created or reused NPC instance.</param>
    /// <param name="source">Terraria provenance used to distinguish natural, child, and direct spawns.</param>
    /// <param name="tick">Game update count used to recognize paired source spawns.</param>
    /// <param name="preserveChildren">Whether ordinary NPC descendants bypass randomization; mandatory lifecycle exceptions still apply.</param>
    internal static void RegisterSpawn(NPC npc, IEntitySource source, ulong tick = 0, bool preserveChildren = true)
    {
        // A wall/ground or other form change belongs to an existing creature, not a new spawn opportunity.
        if (Transforming.Contains(npc))
            return;
        // A new spawn may reuse an old NPC object, so remove old anchor and encounter associations first.
        VineSpawn.Forget(npc);
        TwinsEncounter.Forget(npc);
        if (npc.type == NPCID.Scutlix && source is EntitySource_Parent { Entity: NPC rider }
            && rider.type == NPCID.ScutlixRider)
            ScutlixEncounter.Attach(rider, npc);
        // Record provenance now, but defer replacement until vanilla has finished writing its initial AI fields.
        Pending.Remove(npc);
        bool randomize = ShouldRandomizeSpawn(npc, source, preserveChildren);
        Pending.Add(npc, new SpawnIntent(randomize, tick));
    }

    // Keep the provenance exceptions in one readable checklist. Boss exclusion
    // is still rechecked in PreAI, once the NPC's final source defaults are known.
    private static bool ShouldRandomizeSpawn(NPC npc, IEntitySource source, bool preserveChildren)
    {
        if (SpecialSeedRules.CreatingMechdusa)
            return false;
        if (source is DirectNpcSpawnSource)
            return false;
        if (!NpcCatalog.CanReplace(npc, includeTownNpcs: RandomizerSystem.TownNpcsIncluded))
            return false;
        if (NpcChildProjectiles.PreserveSpawn(npc.type, source, preserveChildren))
            return false;

        return true;
    }

    /// <summary>
    /// Finds the opposite Twin spawned on the same tick for the same target and marks it redundant.
    /// The surviving source produces one mapped encounter instead of two independent replacements.
    /// </summary>
    /// <param name="npc">The first Twin source being processed.</param>
    /// <param name="tick">Its original spawn tick.</param>
    /// <param name="npcs">NPC instances to search for the opposite eye.</param>
    /// <returns>True if the other source was found and marked for cancellation.</returns>
    internal static bool SuppressPairedSource(NPC npc, ulong tick, IEnumerable<NPC> npcs)
    {
        // Match the other eye from this summon rather than an unrelated Twin already alive elsewhere.
        foreach (NPC other in npcs)
        {
            if (other == null || other == npc || !other.active)
                continue;
            if (!TwinsEncounter.IsTwin(other.type) || other.type == npc.type || other.target != npc.target)
                continue;
            if (!Pending.TryGetValue(other, out var pending))
                continue;
            if (!pending.Randomize || pending.Suppress || pending.Tick != tick)
                continue;

            pending.Suppress = true;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Deactivates and synchronizes a rejected spawn without invoking death, loot, or progression logic.
    /// </summary>
    private static bool Cancel(NPC npc)
    {
        npc.active = false;
        npc.netUpdate = true;
        if (Main.netMode == NetmodeID.Server)
            NetMessage.SendData(MessageID.SyncNPC, number: npc.whoAmI);
        return false;
    }

    /// <summary>
    /// Consumes a pending spawn once, enforces encounter limits, installs replacement defaults,
    /// creates required partners, then announces and synchronizes the completed spawn.
    /// </summary>
    public override bool PreAI(NPC npc)
    {
        // Ordinary subsequent AI ticks take this fast path; replacement is strictly a one-time spawn operation.
        if (!Pending.TryGetValue(npc, out var intent))
            return true;
        // Consume the decision before any SetDefaults or companion creation can cause another callback.
        Pending.Remove(npc);
        if (intent.Suppress)
            return Cancel(npc);

        // Recheck current eligibility so configuration changes cannot force a now-excluded NPC through the pending path.
        bool randomize = intent.Randomize && RandomizerSystem.CanRandomize(npc);
        bool originalBoss = BossRules.IsBossRoot(npc);
        if (randomize && TwinsEncounter.Enabled && TwinsEncounter.IsTwin(npc.type))
            SuppressPairedSource(npc, intent.Tick, Main.npc);
        // Select the actual variant before checking limits, because the candidate's final encounter family matters.
        int replacement = randomize ? RandomizerSystem.Replacement(npc) : npc.netID;
        bool twins = randomize && TwinsEncounter.Enabled && TwinsEncounter.IsTwin(replacement);
        bool scutlix = randomize && replacement == NPCID.ScutlixRider;
        NPC candidate = randomize ? ContentSamples.NpcsByNetId[replacement] : npc;
        var config = ModContent.GetInstance<RandomizerConfig>();

        // Either existing eye blocks a new combined Twins encounter when the boss limit is enabled.
        if (twins && config.LimitBosses && TwinsEncounter.AlreadyPresent(npc, Main.npc))
            return Cancel(npc);

        // Paired targets need a spare slot; never leave behind only half an encounter.
        bool needsPartner = twins || scutlix;
        if (needsPartner && !System.Array.Exists(Main.npc, other => !other.active))
            return Cancel(npc);

        // A rejected spawn gets no loot or death/progression effects. Do not reroll it
        // or leave its original boss alive as a substitute for the intended target.
        if (BossRules.AlreadyPresent(candidate, npc, Main.npc, WillRandomize,
            config.LimitMiniBosses, config.LimitBosses))
            return Cancel(npc);
        if (!randomize)
        {
            // A bypassed resident still receives optional appearance randomization even though its species stays unchanged.
            TownNpcAppearance.Randomize(npc);
            return true;
        }

        // Spawn callers often write source-specific ai[] values AFTER OnSpawn.
        // Reset immediately before the first AI, before worms/bosses build children.
        // The marker lives outside GlobalNPC instances because vanilla also calls
        // SetDefaults after OnSpawn when choosing Pinky/size/costume variants.
        var bottom = npc.Bottom;
        bool statue = npc.SpawnedFromStatue;
        short releaseOwner = npc.releaseOwner;
        int target = npc.target;
        // Install fresh AI/stats for the target. Preserve only spawn context that remains meaningful across species.
        npc.SetDefaults(replacement);
        npc.Bottom = bottom;
        // Vine anchor coordinates must be derived after restoring the new NPC's position, never from the old source AI.
        VineSpawn.Initialize(npc, Main.maxTilesX, Main.maxTilesY, (x, y) => Main.tile[x, y].HasTile);
        npc.target = target;
        npc.SpawnedFromStatue = statue;
        npc.releaseOwner = releaseOwner;
        // A randomized resident needs ordinary town initialization plus its saved/optional shimmer appearance.
        if (npc.townNPC)
        {
            npc.GivenName = npc.getNewNPCName();
            npc.homeless = true;
            if (TownNpcAppearance.SupportsShimmer(npc.type))
                npc.townNpcVariationIndex = NPC.ShimmeredTownNPCs[npc.type] ? 1 : 0;
            TownNpcAppearance.Randomize(npc);
        }
        // Do not let a statue-spawned replacement acquire coin value from the target's normal defaults.
        if (statue)
            npc.value = 0f;
        npc.TargetClosest();
        // Create mandatory companions before announcing success; allocation failure cancels the root too.
        if (twins && !TwinsEncounter.SpawnPartner(npc))
            return Cancel(npc);
        if (scutlix && !ScutlixEncounter.SpawnMount(npc))
            return Cancel(npc);
        // Only now is the replacement final and complete, so the awakening message names the real result.
        SpawnMessages.Announce(npc, originalBoss);
        npc.netUpdate = true;
        if (Main.netMode == NetmodeID.Server)
        {
            // Clients need both ordinary NPC state and the separate resident-info packet for town appearance/name data.
            NetMessage.SendData(MessageID.SyncNPC, number: npc.whoAmI);
            if (npc.townNPC)
                NetMessage.SendData(MessageID.UniqueTownNPCInfoSyncRequest, number: npc.whoAmI);
        }
        return true;
    }
}

