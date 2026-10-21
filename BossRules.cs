using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;

namespace EnemyRandomizer;

/// <summary>
/// Defines encounter families, independent boss/miniboss limits, and biome-override eligibility.
/// </summary>
internal static class BossRules
{
    // Root and surviving segments count as the same encounter. Eater splits
    // transform existing segments, so they are never rejected as new bosses.
    /// <summary>
    /// Normalizes surviving parts and alternate tiers to their encounter's identity for duplicate detection.
    /// </summary>
    internal static int Family(int type) => type switch
    {
        NPCID.EaterofWorldsBody or NPCID.EaterofWorldsTail => NPCID.EaterofWorldsHead,
        NPCID.TheDestroyerBody or NPCID.TheDestroyerTail => NPCID.TheDestroyer,
        NPCID.SkeletronHand => NPCID.SkeletronHead,
        NPCID.PrimeCannon or NPCID.PrimeSaw or NPCID.PrimeVice or NPCID.PrimeLaser => NPCID.SkeletronPrime,
        NPCID.GolemHead or NPCID.GolemHeadFree or NPCID.GolemFistLeft or NPCID.GolemFistRight => NPCID.Golem,
        NPCID.PlanterasHook or NPCID.PlanterasTentacle => NPCID.Plantera,
        NPCID.Creeper => NPCID.BrainofCthulhu,
        NPCID.MoonLordHead or NPCID.MoonLordHand or NPCID.MoonLordFreeEye => NPCID.MoonLordCore,
        NPCID.PumpkingBlade => NPCID.Pumpking,
        NPCID.MartianSaucer or NPCID.MartianSaucerTurret or NPCID.MartianSaucerCannon => NPCID.MartianSaucerCore,
        NPCID.PirateShipCannon => NPCID.PirateShip,
        NPCID.DD2DarkMageT3 => NPCID.DD2DarkMageT1,
        NPCID.DD2OgreT3 => NPCID.DD2OgreT2,
        _ => type
    };

    /// <summary>
    /// Recognizes independently spawnable bosses and event bosses while excluding detached parts and the Guardian.
    /// </summary>
    internal static bool IsBossRoot(NPC npc)
    {
        if (NpcCatalog.IsPart(npc.type) || npc.type == NPCID.DungeonGuardian)
            return false;
        if (npc.boss || NPCID.Sets.ShouldBeCountedAsBoss[npc.type])
            return true;

        // These event bosses also count even when vanilla's boss flag is unset.
        return npc.type is NPCID.MourningWood or NPCID.Pumpking or NPCID.Everscream
            or NPCID.IceQueen or NPCID.SantaNK1 or NPCID.PirateShip or NPCID.MartianSaucerCore
            or NPCID.DD2DarkMageT1 or NPCID.DD2DarkMageT3 or NPCID.DD2OgreT2 or NPCID.DD2OgreT3;
    }

    /// <summary>
    /// Checks whether a live NPC belongs to the encounter family being requested.
    /// </summary>
    internal static bool Conflicts(int desiredType, NPC other) =>
        other.active && Family(desiredType) == Family(other.type);

    /// <summary>
    /// Defines the explicit miniboss set controlled by the separate miniboss limit option.
    /// </summary>
    internal static bool IsMiniBoss(int type) => type is
        NPCID.MourningWood or NPCID.Pumpking or NPCID.Everscream or NPCID.IceQueen or NPCID.SantaNK1
        or NPCID.PirateShip or NPCID.MartianSaucerCore or NPCID.DD2DarkMageT1 or NPCID.DD2DarkMageT3
        or NPCID.DD2OgreT2 or NPCID.DD2OgreT3 or NPCID.BloodNautilus or NPCID.Mothron
        or NPCID.IceGolem or NPCID.SandElemental or NPCID.Paladin or NPCID.GoblinSummoner
        or NPCID.PirateCaptain or NPCID.BigMimicCorruption or NPCID.BigMimicCrimson
        or NPCID.BigMimicHallow or NPCID.BigMimicJungle;

    /// <summary>
    /// Chooses the appropriate limit toggle; minibosses use their own toggle even when also classified as bosses.
    /// </summary>
    internal static bool IsLimited(NPC npc, bool limitMiniBosses, bool limitBosses = true)
    {
        if (IsMiniBoss(npc.type))
            return limitMiniBosses;

        return limitBosses && IsBossRoot(npc);
    }

    /// <summary>
    /// Searches for a conflicting active encounter, ignoring the current slot and NPCs still awaiting replacement.
    /// </summary>
    /// <param name="candidate">Requested final NPC type, often a content sample.</param>
    /// <param name="original">Current spawn instance to exclude, or null for a command request.</param>
    /// <param name="npcs">NPC instances to scan.</param>
    /// <param name="willRandomize">Identifies pending sources whose current identities should not block the target.</param>
    /// <param name="limitMiniBosses">Enables the independent miniboss-family limit.</param>
    /// <param name="limitBosses">Enables the ordinary boss-family limit.</param>
    /// <returns>True when a configured limit rejects this requested encounter.</returns>
    internal static bool AlreadyPresent(NPC candidate, NPC original, IEnumerable<NPC> npcs,
        Func<NPC, bool> willRandomize, bool limitMiniBosses = true, bool limitBosses = true)
    {
        // The two limit toggles are independent; an unrestricted candidate does not need an active-NPC scan.
        if (!IsLimited(candidate, limitMiniBosses, limitBosses))
            return false;
        // Count surviving parts through Family, but ignore a pending source that will soon turn into something else.
        foreach (NPC other in npcs)
            if (other != null && other != original && !willRandomize(other)
                && Conflicts(candidate.type, other))
                return true;
        return false;
    }

    /// <summary>
    /// Identifies bosses, dependent parts, and burrowing worms that need habitat checks relaxed during AI.
    /// </summary>
    internal static bool IgnoreBiome(NPC npc) => npc != null &&
        (IsBurrowingWorm(npc.type) || npc.boss || Family(npc.type) != npc.type
         || npc.type is NPCID.QueenBee or NPCID.Plantera or NPCID.Golem or NPCID.Deerclops);

    /// <summary>
    /// Lists worm families whose vanilla depth checks could send a surface replacement underground.
    /// </summary>
    internal static bool IsBurrowingWorm(int type) => type is
        NPCID.DevourerHead or NPCID.DevourerBody or NPCID.DevourerTail or
        NPCID.GiantWormHead or NPCID.GiantWormBody or NPCID.GiantWormTail or
        NPCID.EaterofWorldsHead or NPCID.EaterofWorldsBody or NPCID.EaterofWorldsTail or
        NPCID.BoneSerpentHead or NPCID.BoneSerpentBody or NPCID.BoneSerpentTail or
        NPCID.SeekerHead or NPCID.SeekerBody or NPCID.SeekerTail or
        NPCID.DiggerHead or NPCID.DiggerBody or NPCID.DiggerTail or
        NPCID.DuneSplicerHead or NPCID.DuneSplicerBody or NPCID.DuneSplicerTail or
        NPCID.TombCrawlerHead or NPCID.TombCrawlerBody or NPCID.TombCrawlerTail;
}
