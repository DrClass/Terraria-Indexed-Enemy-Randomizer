using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace EnemyRandomizer;

/// <summary>
/// Initializes Man Eater-style vine anchors near the replacement's actual spawn position.
/// When no suitable tile exists, a synchronized marker lets the anchor remain in empty space.
/// </summary>
internal static class VineSpawn
{
    /// <summary>
    /// Presence-only value for a vine whose anchor does not require a real tile.
    /// </summary>
    private sealed class Marker { }
    /// <summary>
    /// Weak per-instance markers that survive SetDefaults but are cleared when an NPC slot is reused.
    /// </summary>
    private static readonly ConditionalWeakTable<NPC, Marker> Floating = new();
    /// <summary>
    /// Removes an old floating-anchor marker before reusing an NPC instance.
    /// </summary>
    internal static void Forget(NPC npc) => Floating.Remove(npc);
    /// <summary>
    /// Clears every floating anchor when leaving or resetting a world.
    /// </summary>
    internal static void Reset() => Floating.Clear();
    /// <summary>
    /// Accepts either a real anchor tile or this mod's explicitly marked floating anchor.
    /// </summary>
    internal static bool AnchorExists(bool tileExists, NPC npc) => tileExists || Floating.TryGetValue(npc, out _);
    /// <summary>
    /// Applies a received floating-anchor flag, replacing any previous marker.
    /// </summary>
    internal static void SetFloating(NPC npc, bool floating)
    {
        // Apply both true and false updates so a reused client-side NPC cannot keep a stale floating anchor.
        Forget(npc);
        if (floating)
            Floating.Add(npc, new Marker());
    }

    /// <summary>
    /// Sets vine AI anchor coordinates to the nearest tile within five tiles of the spawn.
    /// Falls back to a fixed local anchor without placing or modifying world tiles.
    /// </summary>
    /// <param name="npc">Replacement NPC with final defaults and restored spawn position.</param>
    /// <param name="worldWidth">World width in tiles.</param>
    /// <param name="worldHeight">World height in tiles.</param>
    /// <param name="hasTile">Terrain query receiving tile coordinates; injectable for tests without a live world.</param>
    internal static void Initialize(NPC npc, int worldWidth, int worldHeight, Func<int, int, bool> hasTile)
    {
        // Only vine AI stores an anchor in ai[0]/ai[1]; other species must keep their own AI values.
        Forget(npc);
        if (npc.aiStyle != NPCAIStyleID.ManEater)
            return;
        // Convert pixel coordinates to tile coordinates and leave a one-tile margin for safe terrain access.
        int x = Math.Clamp((int)(npc.Center.X / 16f), 1, worldWidth - 2);
        int y = Math.Clamp((int)(npc.Center.Y / 16f), 1, worldHeight - 2);
        int bestX = x, bestY = y, bestDistance = int.MaxValue;
        // Stay within the shortest vanilla vine's reach (Fungi Bulb: 100px).
        for (int dx = -5; dx <= 5; dx++)
        for (int dy = -5; dy <= 5; dy++)
        {
            // Squared distance avoids square roots; reject tiles outside the circular search radius or farther than the best.
            int distance = dx * dx + dy * dy;
            int tx = x + dx, ty = y + dy;
            if (distance > 25 || distance >= bestDistance || tx < 1 || ty < 1
                || tx >= worldWidth - 1 || ty >= worldHeight - 1 || !hasTile(tx, ty))
                continue;
            bestX = tx;
            bestY = ty;
            bestDistance = distance;
        }
        // Vanilla vine AI expects anchor tile coordinates, not the head's pixel position.
        npc.ai[0] = bestX;
        npc.ai[1] = bestY;
        // With no local tile, keep the spawn-centered coordinates and permit that anchor without editing terrain.
        if (bestDistance == int.MaxValue)
            Floating.Add(npc, new Marker());
    }

    /// <summary>
    /// Extends vanilla's Man Eater anchor-existence check to accept marked floating anchors.
    /// Throws if the expected IL pattern is missing so an incompatible patch cannot fail silently.
    /// </summary>
    internal static void Patch(ILContext il)
    {
        var cursor = new ILCursor(il);
        if (!cursor.TryGotoNext(MoveType.After, i => i.MatchLdfld<NPC>(nameof(NPC.aiStyle)), i => i.MatchLdcI4(13))
            || !cursor.TryGotoNext(MoveType.After, i => i.MatchCall<Tile>("active")))
            throw new InvalidOperationException("Cannot locate vine anchor tile check.");
        // The existing tile-check result is on the IL stack. Add this NPC and replace that boolean with our combined check.
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.EmitDelegate<Func<bool, NPC, bool>>(AnchorExists);
    }
}

// A few For the Worthy helpers incorrectly use a natural-spawn source in vanilla
// (Skeletron's casters, Prime's imps, Eater death spawns). Restore their parent.
/// <summary>
/// Corrects vanilla child spawns that incorrectly claim to be natural spawns.
/// Accurate parent provenance lets the child-preservation option work for special-seed helpers.
/// </summary>
public sealed class NpcChildSpawnSources : ModSystem
{
    /// <summary>
    /// Patches child-source creation in vanilla AI and death handling after locating both instance methods.
    /// </summary>
    public override void Load()
    {
        foreach (string name in new[] { "VanillaAI_Inner", "checkDead" })
        {
            var method = typeof(NPC).GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(typeof(NPC).FullName, name);
            MonoModHooks.Modify(method, Patch);
        }
    }

    /// <summary>
    /// Replaces natural-source factory calls with parent sources referencing the NPC executing the method.
    /// </summary>
    internal static void Patch(ILContext il)
    {
        var cursor = new ILCursor(il);
        int count = 0;
        while (cursor.TryGotoNext(MoveType.Before, i => i.MatchCall<NPC>("GetSpawnSourceForNaturalSpawn")))
        {
            // Reuse the original instruction so branch targets stay valid, loading this NPC instead of a natural source.
            cursor.Next.OpCode = OpCodes.Ldarg_0;
            cursor.Next.Operand = null;
            cursor.Index++;
            cursor.EmitDelegate<Func<NPC, IEntitySource>>(npc => new EntitySource_Parent(npc));
            count++;
        }
        // Fail visibly on an incompatible game method rather than silently losing child-provenance protection.
        if (count == 0)
            throw new InvalidOperationException("Expected NPC child spawn sources not found.");
    }
}

// Preserve provenance through NPC -> projectile -> projectile -> NPC chains.
/// <summary>
/// Carries NPC ancestry through projectile chains so projectile-created helpers can remain unrandomized.
/// </summary>
public sealed class NpcChildProjectiles : GlobalProjectile
{
    /// <summary>
    /// Presence-only value indicating that a projectile descends from an ordinary NPC.
    /// </summary>
    private sealed class Marker { }
    /// <summary>
    /// Weak ancestry markers; entries are explicitly replaced because Terraria reuses projectile instances.
    /// </summary>
    private static readonly ConditionalWeakTable<Projectile, Marker> FromNpc = new();

    /// <summary>
    /// Recognizes either a direct NPC parent or a projectile already marked as descending from one.
    /// Event controllers are excluded so their ordinary encounter spawns can still randomize.
    /// </summary>
    internal static bool HasNpcParent(IEntitySource source)
    {
        if (source is not EntitySource_Parent parent)
            return false;

        if (parent.Entity is NPC npc)
            return !IsEncounterController(npc.type);

        if (parent.Entity is Projectile projectile)
            return FromNpc.TryGetValue(projectile, out _);

        return false;
    }

    /// <summary>
    /// Distinguishes event infrastructure from creatures creating their own attack helpers.
    /// </summary>
    private static bool IsEncounterController(int type) => type is NPCID.CultistTablet
        or NPCID.DD2LanePortal or NPCID.DD2EterniaCrystal
        or NPCID.LunarTowerSolar or NPCID.LunarTowerVortex or NPCID.LunarTowerNebula or NPCID.LunarTowerStardust;

    /// <summary>
    /// Applies child preservation, the mandatory Scutlix exception, and the Pirate Ghost's unusual source type.
    /// </summary>
    internal static bool PreserveSpawn(int type, IEntitySource source, bool enabled)
    {
        if (!enabled && !NpcCatalog.IsLifecycleException(type))
            return false;

        if (HasNpcParent(source))
            return true;

        // Vanilla gives the Captain's death-spawned ghost a player boss source.
        return type == NPCID.PirateGhost && source is EntitySource_BossSpawn;
    }

    /// <summary>
    /// Computes ancestry before replacing a projectile slot's old marker with the new spawn's provenance.
    /// </summary>
    internal static void Register(Projectile projectile, IEntitySource source)
    {
        // Inspect the parent before clearing this slot, then store only the ancestry needed by future child spawns.
        bool fromNpc = HasNpcParent(source);
        FromNpc.Remove(projectile); // Projectile slots are reused.
        if (fromNpc)
            FromNpc.Add(projectile, new Marker());
    }

    /// <summary>
    /// Records ancestry as soon as each projectile is created.
    /// </summary>
    public override void OnSpawn(Projectile projectile, IEntitySource source) => Register(projectile, source);
    /// <summary>
    /// Drops ancestry markers when world-specific spawn state is reset.
    /// </summary>
    internal static void Reset() => FromNpc.Clear();
}

/// <summary>
/// Randomizes supported town-NPC shimmer appearances and protects form transformations from rerandomization.
/// </summary>
public sealed class TownNpcAppearance : ModSystem
{
    /// <summary>
    /// Hooks transformations so rescued residents receive the same appearance handling as new spawns.
    /// </summary>
    public override void Load() => On_NPC.Transform += Transform;
    /// <summary>
    /// Removes the transformation hook when the mod unloads.
    /// </summary>
    public override void Unload() => On_NPC.Transform -= Transform;

    /// <summary>
    /// Checks the vanilla shimmer metadata with bounds protection for unsupported NPC IDs.
    /// </summary>
    internal static bool SupportsShimmer(int type) => type > 0 && type < NPCID.Sets.ShimmerTownTransform.Length
        && NPCID.Sets.ShimmerTownTransform[type];

    /// <summary>
    /// Sets a supported resident's normal/shimmer appearance and requests an NPC update.
    /// </summary>
    internal static void SetVariant(NPC npc, bool shimmer)
    {
        if (!SupportsShimmer(npc.type))
            return;
        // Terraria represents normal and shimmer town appearances with variation indices zero and one.
        npc.townNpcVariationIndex = shimmer ? 1 : 0;
        npc.netUpdate = true;
    }

    /// <summary>
    /// Performs the host's fifty-fifty appearance roll, records the town type's saved state, and synchronizes it.
    /// </summary>
    internal static void Randomize(NPC npc)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || !SupportsShimmer(npc.type)
            || !ModContent.GetInstance<RandomizerConfig>().RandomizeTownShimmer)
            return;
        // The host rolls appearance once and stores the same result in the world's per-town-type shimmer state.
        SetVariant(npc, Main.rand.NextBool());
        NPC.ShimmeredTownNPCs[npc.type] = npc.IsShimmerVariant;
        // Town appearance is carried by the unique town-info packet, not just SyncNPC.
        if (Main.netMode == NetmodeID.Server)
            NetMessage.SendData(MessageID.UniqueTownNPCInfoSyncRequest, number: npc.whoAmI);
    }

    /// <summary>
    /// Guards vanilla form changes and rolls appearance only when entering a shimmer-capable town type.
    /// </summary>
    private static void Transform(On_NPC.orig_Transform orig, NPC npc, int newType)
    {
        // Detect entry into a resident form before the transform changes the type; ordinary form swaps should not reroll appearance.
        bool becomingTownNpc = !SupportsShimmer(npc.type) && SupportsShimmer(newType);
        RandomizedNPC.DuringTransformation(npc, () => orig(npc, newType));
        if (becomingTownNpc)
            Randomize(npc); // Rescued NPCs transform rather than calling NewNPC.
    }
}
