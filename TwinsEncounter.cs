using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace IndexedEnemyRandomizer;

/// <summary>
/// Manages paired encounters and once-per-encounter credit for both Twins and Scutlix pairs.
/// Pair identity is separate from the current NPC type so transformations do not lose accounting state.
/// </summary>
internal static class TwinsEncounter
{
    /// <summary>
    /// Shared state referenced by both halves of one encounter.
    /// </summary>
    private sealed class Pair(NPC first, NPC second)
    {
        /// <summary>
        /// First NPC instance linked into this encounter.
        /// </summary>
        public NPC First = first;
        /// <summary>
        /// Second NPC instance linked into this encounter.
        /// </summary>
        public NPC Second = second;
        /// <summary>
        /// NPC whose final death owns credit; repeated accounting queries for that same death remain valid.
        /// </summary>
        public NPC CreditedTo;
        /// <summary>
        /// Remembers whether the pair began as rider and mount, even after the rider becomes a Ray Gunner.
        /// </summary>
        public bool Scutlix = ScutlixEncounter.IsMember(first.type) && ScutlixEncounter.IsMember(second.type);
    }
    /// <summary>
    /// Weak lookup from either NPC instance to the same shared pair record.
    /// </summary>
    private static readonly ConditionalWeakTable<NPC, Pair> Partners = new();
    /// <summary>
    /// Recognizes the two vanilla mechanical-eye types.
    /// </summary>
    internal static bool IsTwin(int id) => id is NPCID.Retinazer or NPCID.Spazmatism;
    /// <summary>
    /// Reads whether Twins are grouped into one mapping entry.
    /// </summary>
    internal static bool Enabled => ModContent.GetInstance<RandomizerConfig>().CombineTwins;
    /// <summary>
    /// Clears encounter links when world-specific state is discarded.
    /// </summary>
    internal static void Reset() => Partners.Clear();
    /// <summary>
    /// Removes an instance's old link before its NPC slot is reused.
    /// </summary>
    internal static void Forget(NPC npc) => Partners.Remove(npc);
    // The rider becomes a Ray Gunner after losing its mount. Keep its original
    // encounter identity for mapped credit through that vanilla transformation.
    /// <summary>
    /// Returns the target identity used for inverse credit, preserving Scutlix identity through dismounting.
    /// </summary>
    internal static int CreditTarget(NPC npc) => Partners.TryGetValue(npc, out var pair) && pair.Scutlix
        ? NPCID.ScutlixRider : npc.netID;
    /// <summary>
    /// Replaces stale links and associates both NPCs with one new encounter record.
    /// </summary>
    internal static void Link(NPC first, NPC second)
    {
        // Both members must reference the very same Pair object for reuse detection and credit ownership to work.
        Partners.Remove(first);
        Partners.Remove(second);
        var pair = new Pair(first, second);
        Partners.Add(first, pair);
        Partners.Add(second, pair);
    }

    /// <summary>
    /// Allows credit only after the other linked member is gone, then remembers which death owns it.
    /// Unpaired NPCs can credit normally; repeated queries for the winning death are idempotent.
    /// </summary>
    internal static bool CanCredit(NPC npc)
    {
        if (!Partners.TryGetValue(npc, out var pair))
            return true;
        // Multiple accounting systems can query the same death; return the established answer without awarding the other half.
        if (pair.CreditedTo != null)
            return pair.CreditedTo == npc;
        // Check the reverse link as well as activity: the other NPC object may already have been reused for a new spawn.
        NPC other = pair.First == npc ? pair.Second : pair.First;
        if (Partners.TryGetValue(other, out var reverse) && reverse == pair
            && other.active && other.life > 0 && (pair.Scutlix || IsTwin(other.type)))
            return false;
        // The surviving partner is gone, so this death now owns event, mana, and shield-credit queries.
        pair.CreditedTo = npc;
        return true;
    }

    /// <summary>
    /// Checks for either live Twin while ignoring pending NPCs that will become another replacement.
    /// </summary>
    internal static bool AlreadyPresent(NPC original, IEnumerable<NPC> npcs)
    {
        foreach (NPC other in npcs)
            if (other != null && other != original && other.active && IsTwin(other.type)
                && !RandomizedNPC.WillRandomize(other))
                return true;
        return false;
    }

    /// <summary>
    /// Creates Spazmatism beside the first eye without rerandomizing him, copies spawn restrictions, and links the pair.
    /// </summary>
    internal static bool SpawnPartner(NPC first)
    {
        // The second eye is an implementation detail of the selected encounter and must not follow a second mapping.
        int index = NPC.NewNPC(new DirectNpcSpawnSource(), (int)first.Center.X + 160, (int)first.Bottom.Y,
            NPCID.Spazmatism, Target: first.target);
        if (index < 0 || index >= Main.maxNPCs || !Main.npc[index].active)
            return false;
        // Copy source restrictions such as statue provenance to both members rather than letting the companion escape them.
        NPC second = Main.npc[index];
        second.Bottom = first.Bottom + new Vector2(160f, 0f);
        second.SpawnedFromStatue = first.SpawnedFromStatue;
        second.releaseOwner = first.releaseOwner;
        if (first.SpawnedFromStatue)
            second.value = 0;
        // Register accounting links before publishing the companion to multiplayer clients.
        Link(first, second);
        second.netUpdate = true;
        if (Main.netMode == NetmodeID.Server)
            NetMessage.SendData(MessageID.SyncNPC, number: index);
        return true;
    }
}

// Always a paired encounter, independent of the Twins and banner settings.
/// <summary>
/// Always treats a Scutlix rider and mount as one complete encounter, independent of optional grouping settings.
/// </summary>
internal static class ScutlixEncounter
{
    /// <summary>
    /// Recognizes either member of the rider/mount encounter.
    /// </summary>
    internal static bool IsMember(int id) => id is NPCID.ScutlixRider or NPCID.Scutlix;

    /// <summary>
    /// Writes the rider's vanilla mount link and registers shared encounter credit.
    /// </summary>
    internal static void Attach(NPC rider, NPC mount)
    {
        // Match vanilla's rider AI contract and prevent it creating a second mount.
        rider.localAI[0] = 1f;
        rider.ai[0] = mount.whoAmI;
        TwinsEncounter.Link(rider, mount);
        rider.netUpdate = true;
    }

    /// <summary>
    /// Creates and attaches the required mount, returning false if Terraria cannot allocate its NPC slot.
    /// </summary>
    internal static bool SpawnMount(NPC rider)
    {
        // A real parent source preserves the mount through child handling and records its relationship to the rider.
        int index = NPC.NewNPC(new Terraria.DataStructures.EntitySource_Parent(rider),
            (int)rider.Center.X, (int)rider.Bottom.Y, NPCID.Scutlix, Target: rider.target);
        if (index < 0 || index >= Main.maxNPCs || !Main.npc[index].active)
            return false;
        // Keep the mount aligned with its rider and inherit the same statue/release provenance.
        NPC mount = Main.npc[index];
        mount.Bottom = rider.Bottom;
        mount.SpawnedFromStatue = rider.SpawnedFromStatue;
        mount.releaseOwner = rider.releaseOwner;
        if (rider.SpawnedFromStatue)
            mount.value = 0;
        Attach(rider, mount);
        mount.netUpdate = true;
        if (Main.netMode == NetmodeID.Server)
            NetMessage.SendData(MessageID.SyncNPC, number: index);
        return true;
    }
}
