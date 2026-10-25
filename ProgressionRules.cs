using Terraria.ID;

namespace IndexedEnemyRandomizer;

// Source requirements in vanilla 1.4.4. These remain restrictions even when
// a forced spawn could produce that source early. No live defeat flags:
// a world's mappings must not change as bosses are defeated.
/// <summary>
/// Static access dependencies that prevent progression bosses from replacing enemies locked behind them.
/// Uses normal source availability rather than current defeat flags so assignments stay stable.
/// </summary>
internal static class ProgressionRules
{
    /// <summary>
    /// Recognizes ordinary and upgraded dungeon sources that can depend on defeating Skeletron.
    /// </summary>
    public static bool IsDungeon(int id) => IsHardmodeDungeon(id) || id is
        NPCID.AngryBones or NPCID.AngryBonesBig or NPCID.AngryBonesBigMuscle or
        NPCID.AngryBonesBigHelmet or NPCID.DarkCaster or NPCID.WaterSphere or
        NPCID.CursedSkull or NPCID.DungeonSlime or NPCID.SpikeBall or NPCID.BlazingWheel;

    /// <summary>
    /// Recognizes the contiguous upgraded dungeon enemy range unlocked after Plantera.
    /// </summary>
    public static bool IsHardmodeDungeon(int id) =>
        id >= NPCID.RustyArmoredBonesAxe && id <= NPCID.SkeletonCommando;

    /// <summary>
    /// Recognizes the Lihzahrd forms and Flying Snake used as Temple-dependent sources.
    /// </summary>
    public static bool IsTemple(int id) => id is NPCID.Lihzahrd or NPCID.LihzahrdCrawler or NPCID.FlyingSnake;

    /// <summary>
    /// Recognizes Pumpkin/Frost Moon sources whose normal summon recipes require dungeon materials.
    /// </summary>
    private static bool IsMoonEvent(int id) =>
        (id >= NPCID.Scarecrow1 && id <= NPCID.HeadlessHorseman) ||
        (id >= NPCID.MourningWood && id <= NPCID.Poltergeist) ||
        (id >= NPCID.ZombieElf && id <= NPCID.Flocko);

    // Moon summons need Dungeon ectoplasm; the native Cultist/Lunar sequence
    // requires Skeletron. These gates also matter when progression pools merge.
    /// <summary>
    /// Includes direct dungeon dependencies and later content that transitively depends on dungeon access.
    /// </summary>
    public static bool RequiresSkeletron(int id) => id is NPCID.Clothier or NPCID.Mechanic or NPCID.Princess
        || IsDungeon(id) || IsMoonEvent(id) ||
        IsPillar(id) || id is NPCID.CultistBoss or NPCID.MoonLordCore;

    /// <summary>
    /// Classifies lunar enemy sources for progression safety; this is not the pillar-controller exclusion list.
    /// </summary>
    public static bool IsPillar(int id) =>
        (id >= NPCID.StardustWormHead && id <= NPCID.VortexSoldier) ||
        id is NPCID.SolarSpearman or NPCID.SolarGoop or NPCID.SolarFlare;

    /// <summary>
    /// Recognizes post-Golem sources, including lunar content, Martians, and the third Old One's Army tier.
    /// </summary>
    public static bool RequiresGolem(int id) => IsPillar(id) ||
        // This contiguous block contains Martians and the Moon Lord encounter.
        (id >= NPCID.BrainScrambler && id <= NPCID.MoonLordLeechBlob) ||
        id is NPCID.MartianWalker or NPCID.DD2Betsy or NPCID.DD2GoblinT3 or
        NPCID.DD2GoblinBomberT3 or NPCID.DD2WyvernT3 or NPCID.DD2JavelinstT3 or
        NPCID.DD2DarkMageT3 or NPCID.DD2SkeletonT3 or NPCID.DD2WitherBeastT3 or
        NPCID.DD2DrakinT3 or NPCID.DD2KoboldWalkerT3 or NPCID.DD2KoboldFlyerT3 or
        NPCID.DD2OgreT3 or NPCID.DD2LightningBugT3 or NPCID.CultistBoss or
        NPCID.CultistBossClone or NPCID.CultistDevote or NPCID.CultistArcherBlue or
        NPCID.AncientLight or NPCID.AncientDoom ||
        (id >= NPCID.CultistDragonHead && id <= NPCID.CultistDragonTail);

    /// <summary>
    /// Recognizes Temple, upgraded dungeon, late Eclipse, and downstream sources that can gate Plantera.
    /// </summary>
    public static bool RequiresPlantera(int id) => id is NPCID.Cyborg or NPCID.Princess
        || RequiresGolem(id) || IsTemple(id) ||
        IsHardmodeDungeon(id) ||
        // Pumpkin/Frost Moon summons require ectoplasm from the upgraded dungeon.
        IsMoonEvent(id) ||
        id is NPCID.Golem or NPCID.GolemHead or NPCID.GolemHeadFree or
        NPCID.GolemFistLeft or NPCID.GolemFistRight or NPCID.EmpressButterfly or
        NPCID.HallowBoss or NPCID.Butcher or NPCID.Nailhead or NPCID.Psycho or
        NPCID.DeadlySphere or NPCID.DrManFly or NPCID.Mothron or NPCID.MothronEgg or
        NPCID.MothronSpawn or NPCID.PlanterasHook or NPCID.PlanterasTentacle or NPCID.Spore;

    /// <summary>
    /// Recognizes sources requiring a mechanical-boss defeat or a later progression milestone.
    /// </summary>
    public static bool RequiresMech(int id) => RequiresPlantera(id) || id is NPCID.Steampunker or
        NPCID.Plantera or NPCID.RedDevil or NPCID.Lavabat or
        NPCID.DD2GoblinT2 or NPCID.DD2GoblinBomberT2 or NPCID.DD2WyvernT2 or
        NPCID.DD2JavelinstT2 or NPCID.DD2WitherBeastT2 or NPCID.DD2DrakinT2 or
        NPCID.DD2KoboldWalkerT2 or NPCID.DD2KoboldFlyerT2 or NPCID.DD2OgreT2 or
        // Even the early Eclipse enemies need a mech kill for a natural eclipse,
        // or Temple access for a Solar Tablet. Reaper needs all three mechs.
        NPCID.Frankenstein or NPCID.SwampThing or NPCID.Vampire or NPCID.VampireBat or
        NPCID.Eyezor or NPCID.Reaper or NPCID.CreatureFromTheDeep or NPCID.Fritz or NPCID.ThePossessed;

    /// <summary>
    /// Recognizes mechanical bosses and their dependent components to prevent circular mech assignments.
    /// </summary>
    private static bool IsMech(int id) => id is NPCID.Retinazer or NPCID.Spazmatism or
        NPCID.TheDestroyer or NPCID.SkeletronPrime or NPCID.Probe or
        NPCID.TheDestroyerBody or NPCID.TheDestroyerTail or NPCID.PrimeCannon or
        NPCID.PrimeSaw or NPCID.PrimeVice or NPCID.PrimeLaser;

    /// <summary>
    /// Tests one original-source to replacement-target edge for known progression dependencies.
    /// Non-progression targets are unrestricted by this rule; catalog eligibility is handled elsewhere.
    /// </summary>
    public static bool Allows(int source, int target) => target switch
    {
        NPCID.SkeletronHead => !RequiresSkeletron(source),
        // Also avoid mutually dependent mech mappings and the Destroyer's probes.
        NPCID.Retinazer or NPCID.Spazmatism or NPCID.TheDestroyer or NPCID.SkeletronPrime =>
            !RequiresMech(source) && !IsMech(source),
        NPCID.Plantera => !RequiresPlantera(source),
        NPCID.Golem => !RequiresGolem(source),
        NPCID.CultistBoss => !IsPillar(source) && source != NPCID.MoonLordCore,
        _ => true
    };
}
