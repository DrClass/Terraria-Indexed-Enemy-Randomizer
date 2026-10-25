using System.Collections.Generic;
using Terraria;
using Terraria.ID;

namespace IndexedEnemyRandomizer;

/// <summary>
/// Central catalog of progression tiers, world-evil membership, and independent-spawn eligibility.
/// Classification is separate from eligibility: a listed pre-Hardmode helper can still be excluded from pools.
/// </summary>
internal static class NpcCatalog
{
    // The mount belongs to its rider encounter; never reroll its child spawn.
    /// <summary>
    /// Identifies the Scutlix mount, whose rider relationship must survive even when child preservation is disabled.
    /// </summary>
    internal static bool IsLifecycleException(int type) => type == NPCID.Scutlix;

    // Attack NPCs and exclusive summons are not independent species. Exclude
    // them before banner grouping as well as when an actual helper spawns.
    /// <summary>
    /// Recognizes attack entities and lifecycle-only children that must never be standalone replacements.
    /// </summary>
    internal static bool IsExclusiveHelper(int type) => type is
        NPCID.ServantofCthulhu or NPCID.SlimeSpiked or NPCID.Probe
        or NPCID.Sharkron or NPCID.Sharkron2
        or NPCID.QueenSlimeMinionBlue or NPCID.QueenSlimeMinionPink or NPCID.QueenSlimeMinionPurple
        or NPCID.BloodSquid or NPCID.MothronEgg or NPCID.MothronSpawn
        or NPCID.DD2SkeletonT1 or NPCID.DD2SkeletonT3 or NPCID.PirateGhost
        or NPCID.ShadowFlameApparition or NPCID.ChatteringTeethBomb
        or NPCID.MartianTurret or NPCID.StardustSpiderSmall
        or NPCID.StardustCellSmall or NPCID.VortexLarva or NPCID.DungeonSpirit;

    /// <summary>
    /// Recognizes both the idle and fleeing net IDs of the same Truffle Worm species.
    /// </summary>
    internal static bool IsTruffleWorm(int type) => type is NPCID.TruffleWorm or NPCID.TruffleWormDigger;

    // NPC IDs are not ordered by progression. Keep this an explicit allowlist;
    // new and unknown enemies cannot accidentally enter pre-Hardmode worlds.
    /// <summary>
    /// Explicit early-tier net IDs, including negative size variants and seasonal forms.
    /// This is a classification table, not the final spawn pool; eligibility filters run separately.
    /// </summary>
    private static readonly HashSet<int> PreHardmode = new()
    {
        // Negative net IDs encode vanilla size/appearance variants; classify them explicitly alongside their base species.
        NPCID.BigHornetStingy,
        NPCID.LittleHornetStingy,
        NPCID.BigHornetSpikey,
        NPCID.LittleHornetSpikey,
        NPCID.BigHornetLeafy,
        NPCID.LittleHornetLeafy,
        NPCID.BigHornetHoney,
        NPCID.LittleHornetHoney,
        NPCID.BigHornetFatty,
        NPCID.LittleHornetFatty,
        // Raincoat and skeleton size variants keep their own net IDs even when banners later group them.
        NPCID.BigRainZombie,
        NPCID.SmallRainZombie,
        NPCID.BigPantlessSkeleton,
        NPCID.SmallPantlessSkeleton,
        NPCID.BigMisassembledSkeleton,
        NPCID.SmallMisassembledSkeleton,
        NPCID.BigHeadacheSkeleton,
        NPCID.SmallHeadacheSkeleton,
        NPCID.BigSkeleton,
        NPCID.SmallSkeleton,
        NPCID.BigFemaleZombie,
        NPCID.SmallFemaleZombie,
        // Alternate eye appearances belong to the same early progression tier as ordinary Demon Eyes.
        NPCID.DemonEye2,
        NPCID.PurpleEye2,
        NPCID.GreenEye2,
        NPCID.DialatedEye2,
        NPCID.SleepyEye2,
        NPCID.CataractEye2,
        NPCID.BigTwiggyZombie,
        NPCID.SmallTwiggyZombie,
        NPCID.BigSwampZombie,
        NPCID.SmallSwampZombie,
        NPCID.BigSlimedZombie,
        NPCID.SmallSlimedZombie,
        NPCID.BigPincushionZombie,
        NPCID.SmallPincushionZombie,
        NPCID.BigBaldZombie,
        NPCID.SmallBaldZombie,
        NPCID.BigZombie,
        NPCID.SmallZombie,
        // Evil size variants must retain their native early tier; opposite-evil promotion happens in WorldMapping.
        NPCID.BigCrimera,
        NPCID.LittleCrimera,
        NPCID.BigStinger,
        NPCID.LittleStinger,
        NPCID.HeavySkeleton,
        NPCID.BigBoned,
        NPCID.ShortBones,
        NPCID.BigEater,
        NPCID.LittleEater,
        // Slime colors and sizes include negative net IDs as well as ordinary positive NPC types.
        NPCID.JungleSlime,
        NPCID.YellowSlime,
        NPCID.RedSlime,
        NPCID.PurpleSlime,
        NPCID.BlackSlime,
        NPCID.BabySlime,
        NPCID.Pinky,
        NPCID.GreenSlime,
        // The main early catalog includes encounters and their components for classification.
        // CanReplace separately removes protected entities and dependent parts before a pool is built.
        NPCID.BlueSlime,
        NPCID.DemonEye,
        NPCID.Zombie,
        NPCID.EyeofCthulhu,
        NPCID.ServantofCthulhu,
        NPCID.EaterofSouls,
        NPCID.DevourerHead,
        NPCID.DevourerBody,
        NPCID.DevourerTail,
        NPCID.GiantWormHead,
        NPCID.GiantWormBody,
        NPCID.GiantWormTail,
        NPCID.EaterofWorldsHead,
        NPCID.EaterofWorldsBody,
        NPCID.EaterofWorldsTail,
        NPCID.MotherSlime,
        NPCID.Merchant,
        NPCID.Nurse,
        NPCID.ArmsDealer,
        NPCID.Dryad,
        NPCID.Skeleton,
        NPCID.Guide,
        NPCID.MeteorHead,
        NPCID.FireImp,
        NPCID.BurningSphere,
        NPCID.GoblinPeon,
        NPCID.GoblinThief,
        NPCID.GoblinWarrior,
        NPCID.GoblinSorcerer,
        NPCID.ChaosBall,
        NPCID.AngryBones,
        NPCID.DarkCaster,
        NPCID.WaterSphere,
        NPCID.CursedSkull,
        NPCID.SkeletronHead,
        NPCID.SkeletronHand,
        NPCID.OldMan,
        NPCID.Demolitionist,
        NPCID.BoneSerpentHead,
        NPCID.BoneSerpentBody,
        NPCID.BoneSerpentTail,
        NPCID.Hornet,
        NPCID.ManEater,
        NPCID.UndeadMiner,
        NPCID.Tim,
        NPCID.Bunny,
        NPCID.CorruptBunny,
        NPCID.Harpy,
        NPCID.CaveBat,
        NPCID.KingSlime,
        NPCID.JungleBat,
        NPCID.DoctorBones,
        NPCID.TheGroom,
        NPCID.Clothier,
        NPCID.Goldfish,
        NPCID.Snatcher,
        NPCID.CorruptGoldfish,
        NPCID.Piranha,
        NPCID.LavaSlime,
        NPCID.Hellbat,
        NPCID.Vulture,
        NPCID.Demon,
        NPCID.BlueJellyfish,
        NPCID.PinkJellyfish,
        NPCID.Shark,
        NPCID.VoodooDemon,
        NPCID.Crab,
        NPCID.DungeonGuardian,
        NPCID.Antlion,
        NPCID.SpikeBall,
        NPCID.DungeonSlime,
        NPCID.BlazingWheel,
        NPCID.GoblinScout,
        NPCID.GoblinArcher,
        NPCID.Bird,
        NPCID.BaldZombie,
        NPCID.IceSlime,
        NPCID.Penguin,
        NPCID.PenguinBlack,
        NPCID.IceBat,
        NPCID.ZombieEskimo,
        // Ground and wall forms are both classified; transforming between them is guarded in the spawn lifecycle.
        NPCID.WallCreeper,
        NPCID.WallCreeperWall,
        NPCID.UndeadViking,
        NPCID.CorruptPenguin,
        NPCID.Crimera,
        NPCID.FaceMonster,
        NPCID.SpikedIceSlime,
        NPCID.SnowFlinx,
        NPCID.PincushionZombie,
        NPCID.SlimedZombie,
        NPCID.SwampZombie,
        NPCID.TwiggyZombie,
        NPCID.CataractEye,
        NPCID.SleepyEye,
        NPCID.DialatedEye,
        NPCID.GreenEye,
        NPCID.PurpleEye,
        NPCID.LostGirl,
        NPCID.Nymph,
        NPCID.FemaleZombie,
        NPCID.HeadacheSkeleton,
        NPCID.MisassembledSkeleton,
        NPCID.PantlessSkeleton,
        NPCID.SpikedJungleSlime,
        NPCID.Bee,
        NPCID.BeeSmall,
        NPCID.CochinealBeetle,
        NPCID.CyanBeetle,
        NPCID.LacBeetle,
        NPCID.SeaSnail,
        NPCID.Squid,
        NPCID.QueenBee,
        // Raincoat Zombie remains early, but its distinct banner keeps it separate from ordinary Zombie grouping.
        NPCID.ZombieRaincoat,
        NPCID.FlyingFish,
        NPCID.UmbrellaSlime,
        NPCID.GoldfishWalker,
        NPCID.HornetFatty,
        NPCID.HornetHoney,
        NPCID.HornetLeafy,
        NPCID.HornetSpikey,
        NPCID.HornetStingy,
        NPCID.BloodCrawler,
        NPCID.BloodCrawlerWall,
        NPCID.ZombieMushroom,
        NPCID.ZombieMushroomHat,
        NPCID.AnomuraFungus,
        NPCID.MushiLadybug,
        NPCID.FungiBulb,
        NPCID.BrainofCthulhu,
        NPCID.Creeper,
        NPCID.AngryBonesBig,
        NPCID.AngryBonesBigMuscle,
        NPCID.AngryBonesBigHelmet,
        NPCID.BirdBlue,
        NPCID.BirdRed,
        NPCID.Squirrel,
        NPCID.Mouse,
        NPCID.Raven,
        NPCID.SlimeMasked,
        NPCID.BunnySlimed,
        NPCID.Ghost,
        NPCID.DemonEyeOwl,
        NPCID.DemonEyeSpaceship,
        // Seasonal costumes are always available as mapping targets; no calendar check filters this catalog.
        NPCID.ZombieDoctor,
        NPCID.ZombieSuperman,
        NPCID.ZombiePixie,
        NPCID.SkeletonTopHat,
        NPCID.SkeletonAstonaut,
        NPCID.SkeletonAlien,
        NPCID.ZombieXmas,
        NPCID.ZombieSweater,
        NPCID.SlimeRibbonWhite,
        NPCID.SlimeRibbonYellow,
        NPCID.SlimeRibbonGreen,
        NPCID.SlimeRibbonRed,
        NPCID.BunnyXmas,
        // Passive critters participate alongside enemies; progression classification does not imply hostility.
        NPCID.Firefly,
        NPCID.Butterfly,
        NPCID.Worm,
        NPCID.Snail,
        NPCID.GlowingSnail,
        NPCID.Frog,
        NPCID.Duck,
        NPCID.Duck2,
        NPCID.DuckWhite,
        NPCID.DuckWhite2,
        NPCID.ScorpionBlack,
        NPCID.Scorpion,
        NPCID.Grasshopper,
        NPCID.ArmedZombie,
        NPCID.ArmedZombieEskimo,
        NPCID.ArmedZombiePincussion,
        NPCID.ArmedZombieSlimed,
        NPCID.ArmedZombieSwamp,
        NPCID.ArmedZombieTwiggy,
        NPCID.ArmedZombieCenx,
        // Gold rarity is handled by GoldCritters when enabled, not by moving these species to a later tier.
        NPCID.GoldBird,
        NPCID.GoldBunny,
        NPCID.GoldButterfly,
        NPCID.GoldFrog,
        NPCID.GoldGrasshopper,
        NPCID.GoldMouse,
        NPCID.GoldWorm,
        NPCID.BoneThrowingSkeleton,
        NPCID.BoneThrowingSkeleton2,
        NPCID.BoneThrowingSkeleton3,
        NPCID.BoneThrowingSkeleton4,
        NPCID.CrimsonBunny,
        NPCID.CrimsonGoldfish,
        NPCID.CrimsonPenguin,
        NPCID.GreekSkeleton,
        NPCID.GraniteGolem,
        NPCID.GraniteFlyer,
        NPCID.EnchantedNightcrawler,
        NPCID.Grubby,
        NPCID.Sluggy,
        NPCID.Buggy,
        NPCID.BloodZombie,
        NPCID.Drippler,
        // All cavern-family variants are eligible; the natural-spawn hook also removes the two-family world restriction.
        NPCID.Crawdad,
        NPCID.Crawdad2,
        NPCID.GiantShelly,
        NPCID.GiantShelly2,
        NPCID.Salamander,
        NPCID.Salamander2,
        NPCID.Salamander3,
        NPCID.Salamander4,
        NPCID.Salamander5,
        NPCID.Salamander6,
        NPCID.Salamander7,
        NPCID.Salamander8,
        NPCID.Salamander9,
        NPCID.GiantWalkingAntlion,
        NPCID.GiantFlyingAntlion,
        NPCID.TombCrawlerHead,
        NPCID.TombCrawlerBody,
        NPCID.TombCrawlerTail,
        NPCID.SlimeSpiked,
        NPCID.TheBride,
        NPCID.SandSlime,
        NPCID.Tumbleweed,
        NPCID.SquirrelRed,
        NPCID.SquirrelGold,
        NPCID.PartyBunny,
        // Only early army tiers belong here; higher tiers are classified late and may group only where pool rules allow.
        NPCID.DD2GoblinT1,
        NPCID.DD2GoblinBomberT1,
        NPCID.DD2WyvernT1,
        NPCID.DD2JavelinstT1,
        NPCID.DD2DarkMageT1,
        NPCID.DD2SkeletonT1,
        NPCID.WalkingAntlion,
        NPCID.FlyingAntlion,
        NPCID.LarvaeAntlion,
        NPCID.FairyCritterPink,
        NPCID.FairyCritterGreen,
        NPCID.FairyCritterBlue,
        NPCID.ZombieMerman,
        NPCID.EyeballFlyingFish,
        NPCID.TorchZombie,
        NPCID.ArmedTorchZombie,
        NPCID.GoldGoldfish,
        NPCID.GoldGoldfishWalker,
        NPCID.WindyBalloon,
        NPCID.BlackDragonfly,
        NPCID.BlueDragonfly,
        NPCID.GreenDragonfly,
        NPCID.OrangeDragonfly,
        NPCID.RedDragonfly,
        NPCID.YellowDragonfly,
        NPCID.GoldDragonfly,
        NPCID.Seagull,
        NPCID.Seagull2,
        NPCID.LadyBug,
        NPCID.GoldLadyBug,
        NPCID.Maggot,
        NPCID.Pupfish,
        NPCID.Grebe,
        NPCID.Grebe2,
        NPCID.Rat,
        NPCID.Owl,
        NPCID.WaterStrider,
        NPCID.GoldWaterStrider,
        NPCID.ExplosiveBunny,
        NPCID.Dolphin,
        NPCID.Turtle,
        NPCID.TurtleJungle,
        NPCID.Gnome,
        NPCID.SeaTurtle,
        NPCID.Seahorse,
        NPCID.GoldSeahorse,
        NPCID.Dandelion,
        NPCID.MaggotZombie,
        NPCID.SporeBat,
        NPCID.SporeSkeleton,
        // Gem critters are early species but intentionally remain outside the normal/gold family grouping.
        NPCID.GemSquirrelAmethyst,
        NPCID.GemSquirrelTopaz,
        NPCID.GemSquirrelSapphire,
        NPCID.GemSquirrelEmerald,
        NPCID.GemSquirrelRuby,
        NPCID.GemSquirrelDiamond,
        NPCID.GemSquirrelAmber,
        NPCID.GemBunnyAmethyst,
        NPCID.GemBunnyTopaz,
        NPCID.GemBunnySapphire,
        NPCID.GemBunnyEmerald,
        NPCID.GemBunnyRuby,
        NPCID.GemBunnyDiamond,
        NPCID.GemBunnyAmber,
        NPCID.HellButterfly,
        NPCID.Lavafly,
        NPCID.MagmaSnail,
        // Golden Slime is early by progression, but SpecialSeedRules still restricts its target eligibility.
        NPCID.GoldenSlime,
        NPCID.Deerclops,
        NPCID.Stinkbug,
        NPCID.ScarletMacaw,
        NPCID.BlueMacaw,
        NPCID.Toucan,
        NPCID.YellowCockatiel,
        NPCID.GrayCockatiel,
        NPCID.ShimmerSlime,
        NPCID.Shimmerfly,
    };

    /// <summary>
    /// Encounter controllers, required special NPCs, and rescue forms that must retain vanilla identities.
    /// </summary>
    private static readonly HashSet<int> Protected = new()
    {
        NPCID.DungeonGuardian,
        NPCID.CultistDevote, NPCID.CultistArcherBlue, NPCID.CultistArcherWhite,
        NPCID.CultistBossClone, NPCID.CultistTablet,
        NPCID.AncientLight, NPCID.AncientDoom,
        NPCID.CultistDragonHead, NPCID.CultistDragonBody1, NPCID.CultistDragonBody2,
        NPCID.CultistDragonBody3, NPCID.CultistDragonBody4, NPCID.CultistDragonTail,
        NPCID.LunarTowerSolar, NPCID.LunarTowerVortex, NPCID.LunarTowerNebula, NPCID.LunarTowerStardust,
        NPCID.WallofFlesh, NPCID.WallofFleshEye, NPCID.TheHungry, NPCID.TheHungryII,
        NPCID.LeechHead, NPCID.LeechBody, NPCID.LeechTail,
        NPCID.OldMan, NPCID.TravellingMerchant, NPCID.SkeletonMerchant,
        NPCID.BoundGoblin, NPCID.BoundWizard, NPCID.BoundMechanic,
        NPCID.WebbedStylist, NPCID.SleepingAngler, NPCID.BartenderUnconscious,
        NPCID.GolferRescue, NPCID.DemonTaxCollector,
        NPCID.BoundTownSlimeOld, NPCID.BoundTownSlimePurple, NPCID.BoundTownSlimeYellow
    };

    // These are parts of a single creature, not independently usable enemies.
    // Preserve their links and let the randomized root create them normally.
    /// <summary>
    /// Dependent body parts whose root encounter owns their creation and AI links.
    /// </summary>
    private static readonly HashSet<int> Parts = new()
    {
        NPCID.DevourerBody, NPCID.DevourerTail, NPCID.GiantWormBody, NPCID.GiantWormTail,
        NPCID.EaterofWorldsBody, NPCID.EaterofWorldsTail, NPCID.SkeletronHand,
        NPCID.BoneSerpentBody, NPCID.BoneSerpentTail,
        NPCID.WyvernLegs, NPCID.WyvernBody, NPCID.WyvernBody2, NPCID.WyvernBody3, NPCID.WyvernTail,
        NPCID.DiggerBody, NPCID.DiggerTail, NPCID.SeekerBody, NPCID.SeekerTail,
        NPCID.PrimeCannon, NPCID.PrimeSaw, NPCID.PrimeVice, NPCID.PrimeLaser,
        NPCID.TheDestroyerBody, NPCID.TheDestroyerTail,
        NPCID.GolemHead, NPCID.GolemFistLeft, NPCID.GolemFistRight, NPCID.GolemHeadFree,
        NPCID.PlanterasHook, NPCID.PlanterasTentacle, NPCID.Creeper, NPCID.PumpkingBlade,
        NPCID.MartianSaucer, NPCID.MartianSaucerTurret, NPCID.MartianSaucerCannon,
        NPCID.MoonLordHead, NPCID.MoonLordHand, NPCID.MoonLordFreeEye, NPCID.MoonLordLeechBlob,
        NPCID.StardustWormBody, NPCID.StardustWormTail, NPCID.StardustJellyfishSmall,
        NPCID.SolarCrawltipedeBody, NPCID.SolarCrawltipedeTail,
        NPCID.CultistBossClone, NPCID.CultistDragonHead, NPCID.CultistDragonBody1,
        NPCID.CultistDragonBody2, NPCID.CultistDragonBody3, NPCID.CultistDragonBody4, NPCID.CultistDragonTail,
        NPCID.PirateShipCannon, NPCID.DuneSplicerBody, NPCID.DuneSplicerTail,
        NPCID.TombCrawlerBody, NPCID.TombCrawlerTail, NPCID.BloodEelBody, NPCID.BloodEelTail,
        NPCID.ForceBubble
    };

    /// <summary>
    /// Non-creature controllers, unused prototypes, and utility NPCs unsuitable as independent replacements.
    /// </summary>
    private static readonly HashSet<int> Utilities = new()
    {
        NPCID.None, NPCID.None2, NPCID.None3, NPCID.TargetDummy,
        NPCID.CultistTablet, NPCID.DD2AttackerTest, NPCID.DD2EterniaCrystal,
        NPCID.DD2LanePortal, NPCID.TorchGod,
        // Unused vanilla prototypes have IDs/defaults but no supported encounter.
        NPCID.CultistArcherWhite, NPCID.SolarSpearman, NPCID.AncientCultistSquidhead,
        // Shielded event controllers cannot function as standalone replacements.
        NPCID.LunarTowerSolar, NPCID.LunarTowerVortex, NPCID.LunarTowerNebula, NPCID.LunarTowerStardust
    };

    /// <summary>
    /// Checks early-tier membership, including ordinary pre-Hardmode town residents when optionally enabled.
    /// </summary>
    public static bool IsPreHardmode(int netId) => PreHardmode.Contains(netId) || netId is
        NPCID.GoblinTinkerer or NPCID.Mechanic or NPCID.Stylist or NPCID.Angler or NPCID.Golfer
        or NPCID.DD2Bartender or NPCID.BestiaryGirl or NPCID.PartyGirl or NPCID.Painter or NPCID.DyeTrader;
    /// <summary>
    /// Tests whether an NPC type is a dependent encounter component rather than an independent root.
    /// </summary>
    public static bool IsPart(int type) => Parts.Contains(type);

    /// <summary>
    /// All catalogued Corruption-specific forms used for native-evil classification and optional filtering.
    /// </summary>
    private static readonly HashSet<int> Corruption = new()
    {
        NPCID.EaterofSouls, NPCID.BigEater, NPCID.LittleEater, NPCID.DevourerHead,
        NPCID.DevourerBody, NPCID.DevourerTail, NPCID.EaterofWorldsHead,
        NPCID.EaterofWorldsBody, NPCID.EaterofWorldsTail,
        NPCID.CorruptBunny, NPCID.CorruptGoldfish, NPCID.CorruptPenguin,
        NPCID.CorruptSlime, NPCID.Slimer, NPCID.Slimer2, NPCID.Slimeling,
        NPCID.Corruptor, NPCID.Clinger, NPCID.CursedHammer, NPCID.DarkMummy,
        NPCID.SeekerHead, NPCID.SeekerBody, NPCID.SeekerTail,
        NPCID.PigronCorruption, NPCID.BigMimicCorruption,
        NPCID.DesertGhoulCorruption, NPCID.SandsharkCorrupt
    };

    /// <summary>
    /// All catalogued Crimson-specific forms used for native-evil classification and optional filtering.
    /// </summary>
    private static readonly HashSet<int> Crimson = new()
    {
        NPCID.Crimera, NPCID.BigCrimera, NPCID.LittleCrimera, NPCID.FaceMonster,
        NPCID.BloodCrawler, NPCID.BloodCrawlerWall, NPCID.BloodFeeder, NPCID.BloodJelly,
        NPCID.BrainofCthulhu, NPCID.Creeper, NPCID.Herpling, NPCID.FloatyGross,
        NPCID.Crimslime, NPCID.BigCrimslime, NPCID.LittleCrimslime,
        NPCID.CrimsonAxe, NPCID.IchorSticker, NPCID.PigronCrimson,
        NPCID.BigMimicCrimson, NPCID.DesertGhoulCrimson, NPCID.SandsharkCrimson,
        NPCID.BloodMummy, NPCID.CrimsonBunny, NPCID.CrimsonGoldfish, NPCID.CrimsonPenguin
    };

    // Native-evil classification for tier assignment, not an overall pool ban:
    // opposite-evil NPCs enter the world's Hardmode pool.
    /// <summary>
    /// Returns false for the world's opposite evil. Callers use this for tier assignment or an optional pool ban.
    /// </summary>
    public static bool AllowedInWorld(int netId, bool crimson) =>
        !(crimson ? Corruption : Crimson).Contains(netId);

    /// <summary>
    /// Recognizes town residents, town-like NPCs, pets, and explicit protected encounter types.
    /// </summary>
    public static bool IsProtected(NPC npc) => npc.townNPC || npc.isLikeATownNPC
        || NPCID.Sets.IsTownPet[npc.type] || Protected.Contains(npc.type);

    /// <summary>
    /// Selects ordinary town residents eligible for the town-randomization opt-in without admitting protected types.
    /// </summary>
    internal static bool IsTownPoolCandidate(NPC npc) => npc.townNPC && !NPCID.Sets.IsTownPet[npc.type]
        && !Protected.Contains(npc.type);

    /// <summary>
    /// Determines whether an NPC may be a source or target, applying opt-ins before excluding parts and helpers.
    /// </summary>
    public static bool CanReplace(NPC npc, bool excludeBosses = false, bool includeTownNpcs = false)
    {
        if (npc.type <= NPCID.None)
            return false;

        // The town option lifts protection only for ordinary eligible residents.
        if (IsProtected(npc))
        {
            if (!includeTownNpcs || !IsTownPoolCandidate(npc))
                return false;
        }

        if (excludeBosses && BossRules.IsBossRoot(npc))
            return false;

        // Components, controllers, and attack helpers cannot stand alone as replacements.
        if (Parts.Contains(npc.type) || Utilities.Contains(npc.type) || IsExclusiveHelper(npc.type))
            return false;

        return !NPCID.Sets.ProjectileNPC[npc.type];
    }

    /// <summary>
    /// Selects vanilla NPCs whose AI may ignore retreat conditions; town and protected controllers retain their AI.
    /// </summary>
    public static bool KeepActiveAI(NPC npc) => npc.type > NPCID.None && npc.type < NPCID.Count
        && !IsProtected(npc) && !Utilities.Contains(npc.type)
        && !NPCID.Sets.ProjectileNPC[npc.type];
}
