using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using MonoMod.Cil;
using MonoMod.Utils;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace IndexedEnemyRandomizer;

/// <summary>
/// Standalone regression harness using Terraria metadata and copied method bodies without launching a world.
/// IL validation compiles patched copies; it does not simulate live combat or multiplayer sessions.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Total assertions reached, including successful checks across seed/configuration combinations.
    /// </summary>
    private static int checks;

    /// <summary>
    /// Counts a check and stops immediately with its diagnostic message if the expected invariant fails.
    /// </summary>
    private static void Assert(bool condition, string message)
    {
        // Count before failing so the first diagnostic still corresponds to the exact assertion reached.
        checks++;
        if (!condition)
            throw new Exception(message);
    }

    /// <summary>
    /// Runs feature regressions and broad catalog/mapping checks, then prints the total completed assertions.
    /// </summary>
    private static void Main()
    {
        TestCritterForms();
        TestMiscFamilies();
        TestStatueLootSettings();
        // Verify the new army semantics before comparing the versioned fingerprint.
        TestArmyProgression();
        TestMappingCompatibility();
        TestArmoredSkeletonTierAndGrouping();
        TestHintNotesAndRandomCalls();
        // Run focused feature regressions before the broad seed and catalog sweeps below.
        TestTownAndEvilOptions();
        TestArmyAndCavernFamilies();
        TestNewSettings();
        Assert(new RandomizerConfig().EarlyEvilSolutionShimmer, "Early evil solution shimmer must default on");
        // Use a private conversion table to test enable/disable restoration without mutating Terraria's shared table.
        int[] transforms = Enumerable.Repeat(-1, ItemID.Count).ToArray();
        transforms[ItemID.GreenSolution] = 123;
        foreach (bool enabled in new[] { true, false, true })
        {
            SolutionShimmer.Apply(transforms, enabled, 456, 789);
            Assert(transforms[ItemID.RedSolution] == (enabled ? ItemID.PurpleSolution : 456), "Red solution conversion/toggle failed");
            Assert(transforms[ItemID.PurpleSolution] == (enabled ? ItemID.RedSolution : 789), "Purple solution conversion/toggle failed");
            Assert(transforms[ItemID.GreenSolution] == 123 && transforms[ItemID.Clentaminator] == -1,
                "Solution setting changed other shimmer recipes");
        }
        TestSpawnMessages();
        TestVineAnchors();
        TestFormTransitions();
        TestExclusiveHelpers();
        // A small mixed pool makes deterministic fallback, self-exclusion, and total candidate coverage easy to verify.
        int[] pool = [1, 2, 3, 4, 46, 50];
        var outcomes = new HashSet<int>();
        for (int seed = 0; seed < 1000; seed++)
        {
            foreach (int source in pool)
            {
                int replacement = SeededSelection.Pick(seed.ToString(), source.ToString(), pool, source);
                Assert(pool.Contains(replacement), "Replacement outside pool");
                Assert(replacement != source, "Identity mapping");
                Assert(replacement == SeededSelection.Pick(seed.ToString(), source.ToString(), pool, source), "Unstable seed");
                outcomes.Add(replacement);
            }
        }
        Assert(outcomes.Count == pool.Length, "Some candidates never selected");
        Assert(SeededSelection.Pick("seed", "source", [46], 46) == 46, "Single candidate");

        // These hand-selected species catch tier regressions that an algorithm-only test would miss.
        int[] earlyExamples = [NPCID.Bunny, NPCID.BlueSlime, NPCID.Zombie, NPCID.EyeofCthulhu,
            NPCID.QueenBee, NPCID.BrainofCthulhu, NPCID.Deerclops, NPCID.DD2DarkMageT1,
            NPCID.GoldButterfly, NPCID.GemBunnyDiamond, NPCID.HellButterfly,
            NPCID.GoblinArcher, NPCID.Tumbleweed, NPCID.AnomuraFungus, NPCID.MushiLadybug, NPCID.FungiBulb];
        foreach (int id in earlyExamples)
            Assert(NpcCatalog.IsPreHardmode(id), $"Missing pre-Hardmode NPC {id}");

        // Include historically problematic enemies and early-looking variants that actually belong to Hardmode.
        int[] lateExamples = [NPCID.Wraith, NPCID.Pixie, NPCID.Mimic, NPCID.Medusa,
            NPCID.TruffleWorm, NPCID.EmpressButterfly, NPCID.HoppinJack, NPCID.ZombieElf,
            NPCID.SnowmanGangsta, NPCID.GoblinSummoner, NPCID.DD2GoblinT2,
            NPCID.DD2DarkMageT3, NPCID.DD2Betsy, NPCID.TheDestroyer, NPCID.MoonLordCore,
            NPCID.BigCrimslime, NPCID.ToxicSludge, NPCID.FungoFish,
            NPCID.BloodFeeder, NPCID.BloodJelly, NPCID.RockGolem, NPCID.GiantFungiBulb];
        foreach (int id in lateExamples)
            Assert(!NpcCatalog.IsPreHardmode(id), $"Hardmode leak: {id}");

        // Exercise the real NPCID metadata without starting the game or a world.
        foreach (int id in new[] { NPCID.DungeonGuardian, NPCID.WallofFlesh, NPCID.WallofFleshEye, NPCID.TheHungry,
                     NPCID.BoundGoblin, NPCID.BoundWizard, NPCID.BoundMechanic,
                     NPCID.WebbedStylist, NPCID.SleepingAngler, NPCID.GolferRescue,
                     NPCID.BartenderUnconscious, NPCID.DemonTaxCollector,
                     NPCID.BoundTownSlimeOld, NPCID.BoundTownSlimePurple, NPCID.BoundTownSlimeYellow })
        {
            var npc = new NPC { type = id };
            Assert(!NpcCatalog.CanReplace(npc), $"Protected source/target {id}");
            Assert(!NpcCatalog.KeepActiveAI(npc), $"Protected AI {id}");
        }
        Assert(!NpcCatalog.CanReplace(new NPC { type = NPCID.Guide, townNPC = true }), "Town NPC changed");
        foreach (int id in new[] { NPCID.SkeletronHand, NPCID.EaterofWorldsBody, NPCID.Creeper,
                     NPCID.PrimeSaw, NPCID.MoonLordHead, NPCID.PirateShipCannon, NPCID.TheDestroyerTail })
            Assert(!NpcCatalog.CanReplace(new NPC { type = id }), $"Detached body part {id}");
        foreach (int id in earlyExamples)
            Assert(NpcCatalog.CanReplace(new NPC { type = id }), $"Eligible NPC excluded {id}");

        // Exercise mapping invariants separately from content classification and runtime spawn bookkeeping.
        TestWorldMappings();
        TestOppositeEvilHardmode();
        TestNativeEvilVariants();
        TestSpawnBehavior();
        TestLunarEventGuard();
        TestCombinedPools();
        TestVariantGroups();
        TestProgressionRules();
        TestConstrainedMatching();
        TestMappedCreditAndSeeds();
        TestCombinedTwinsAndExport();
        Assert(EventProgressDisplay.ActiveIcon(false, false, false, 0) == 0, "No event bar without an event");
        for (int invasion = 1; invasion <= 4; invasion++)
            Assert(EventProgressDisplay.ActiveIcon(false, false, false, invasion) == invasion + 3, "Invasion bar icon");
        Assert(EventProgressDisplay.ActiveIcon(true, false, false, 0) == 1, "Frost Moon bar");
        Assert(EventProgressDisplay.ActiveIcon(false, true, false, 0) == 2, "Pumpkin Moon bar");
        Assert(EventProgressDisplay.ActiveIcon(false, false, true, 0) == 3, "Old One's Army bar");
        Assert(EventProgressDisplay.ActiveIcon(true, true, true, 4) == 1, "Event priority matches vanilla sync");
        Assert(EventProgressDisplay.ActiveIcon(false, false, false, -1) == 0, "Invalid invasion icon");
        TestEncounters();
        TestPillarDelivery();
        TestCommands();
        TestAIPatches();
        Console.WriteLine($"PASS: {checks} checks (commands, inverse hints, mapping, boss limits, protected encounters, pillar credits, AI patch JIT validation).");
    }

    /// <summary>
    /// Verifies that refactoring preserves the exact existing assignments across world and grouping options.
    /// </summary>
    // Compare against the versioned mapping fingerprint; intentional pool changes
    // require a new baseline after the focused grouping tests have passed.
    // Invariant tests alone could miss a valid but different permutation that reshuffles existing worlds.
    private static void TestMappingCompatibility()
    {
        int[] candidates = Enumerable.Range(1, NPCID.Count - 1)
            .Where(id => NpcCatalog.CanReplace(new NPC { type = id, netID = id }))
            .Concat(new[] { (int)NPCID.SmallZombie, NPCID.BigCrimslime, NPCID.BigEater })
            .OrderBy(id => id).ToArray();
        var banners = candidates.ToDictionary(id => id,
            id => Item.NPCtoBanner(id == NPCID.SmallZombie ? NPCID.Zombie : id));
        var results = new System.Text.StringBuilder();

        // Six grouping/pool options, both evils, both special-world states, and two seeds.
        // Include full group membership, inverse hints, forced-source fallbacks, and export text.
        for (int options = 0; options < 64; options++)
        foreach (bool crimson in new[] { false, true })
        foreach (bool celebration in new[] { false, true })
        foreach (string seed in new[] { "readability", "123456789" })
        {
            var map = new WorldMapping(seed, crimson, candidates,
                banners: (options & 1) != 0 ? banners : null,
                celebration: celebration,
                combineTwins: (options & 2) != 0,
                separateProgressionPools: (options & 4) != 0,
                groupGoldCritters: (options & 8) != 0,
                groupArmyTiers: (options & 16) != 0,
                includeOppositeEvil: (options & 32) != 0);
            foreach (int source in candidates)
            {
                string identity = source.ToString(System.Globalization.CultureInfo.InvariantCulture);
                results.Append(source).Append(':').Append(map.IsEarlySource(source)).Append(':');
                results.AppendJoin(',', map.Members(source)).Append(':');
                results.Append(map.Replacement(source, identity, true)).Append(':');
                results.Append(map.Replacement(source, identity, false)).Append(':');
                results.Append(map.TryFindSource(source, out int original)).Append(':').Append(original).Append(';');
            }
            results.Append(MappingExport.Render(map, candidates,
                id => id.ToString(System.Globalization.CultureInfo.InvariantCulture), "compatibility"));
        }

        // Preserve the original compatibility fixture across the mod's display-name
        // change. Only normalize the report title; all mappings and other text must match.
        string report = results.ToString().Replace("\r\n", "\n")
            .Replace("Indexed Enemy Randomizer mapping\n", "Enemy Randomizer mapping\n");
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(report);
        string fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        // Version 1.0.5 merges army tiers and updates export descriptions.
        const string expected = "8218435D70C1BE15CD62A4217989D1FFE22177D975636E6E97C8E95755E86690";
        Assert(fingerprint == expected, $"Mapping fingerprint changed: {fingerprint}");
    }

    private static void TestStatueLootSettings()
    {
        var config = new RandomizerConfig();
        Assert(config.IncludeBosses && config.BlockEarlyHardmodeStatueLoot && config.ReduceStatueLoot,
            "Version 1.0.3 options must default on");

        // Test the real vanilla tables, every entry, and repeated toggle changes.
        float[] defaults = (float[])NPCID.Sets.StatueSpawnedDropRarity.Clone();
        bool[] blocks = (bool[])NPCID.Sets.NoEarlymodeLootWhenSpawnedFromStatue.Clone();
        float[] rates = (float[])defaults.Clone();
        bool[] early = (bool[])blocks.Clone();
        foreach (bool block in new[] { true, false, true })
        foreach (bool reduce in new[] { true, false, true })
        {
            StatueLoot.Apply(rates, early, defaults, blocks, block, reduce);
            for (int id = 1; id < NPCID.Count; id++)
            {
                Assert(rates[id] == (reduce && defaults[id] == -1f ? 0.2f : defaults[id]),
                    $"Statue rate changed incorrectly for {id}");
                Assert(early[id] == (blocks[id] || (block && !NpcCatalog.IsPreHardmode(id))),
                    $"Statue tier restriction changed incorrectly for {id}");
            }
            Assert(rates[NPCID.Mimic] == 0f && rates[NPCID.Harpy] == 0.05f,
                "Explicit vanilla rates must survive the fallback");
            Assert(!early[NPCID.Crimera] && !early[NPCID.EaterofSouls],
                "Opposite evil must not turn an early species into a Hardmode loot restriction");
        }
        StatueLoot.Apply(rates, early, defaults, blocks, false, false);
        Assert(rates.SequenceEqual(defaults) && early.SequenceEqual(blocks), "Statue defaults were not restored");

        // Preserve both old preferences, including serializers that populate defaults.
        foreach (bool exclude in new[] { false, true })
        foreach (var handling in new[] { Newtonsoft.Json.DefaultValueHandling.Include,
                     Newtonsoft.Json.DefaultValueHandling.Populate })
        {
            string json = "{\"ExcludeBosses\":" + (exclude ? "true" : "false") + "}";
            var migrated = Newtonsoft.Json.JsonConvert.DeserializeObject<RandomizerConfig>(json,
                new Newtonsoft.Json.JsonSerializerSettings { DefaultValueHandling = handling });
            Assert(migrated.IncludeBosses == !exclude, "Old boss preference was not migrated");
            string saved = Newtonsoft.Json.JsonConvert.SerializeObject(migrated);
            Assert(!saved.Contains("ExcludeBosses"), "Legacy boss option leaked into new saves");
            Assert(Newtonsoft.Json.JsonConvert.DeserializeObject<RandomizerConfig>(saved,
                new Newtonsoft.Json.JsonSerializerSettings { DefaultValueHandling = handling }).IncludeBosses == !exclude,
                "Boss inclusion preference failed to round-trip");
        }
    }

    private static void TestMiscFamilies()
    {
        Assert(new RandomizerConfig().GroupMiscVariants, "Miscellaneous groups must default on");
        int[][] families = [
            [210, 211], [366, 367], [362, 363, 364, 365], [583, 584, 585],
            [671, 672], [674, 675], [595, 596, 597, 598, 599, 600],
            [299, 538], [148, 149], [74, 297, 298]
        ];
        int[] goldIds = [NPCID.GoldDragonfly, NPCID.SquirrelGold, NPCID.GoldBird];
        int[] ids = families.SelectMany(group => group).Concat(goldIds)
            .Concat(new[] { (int)NPCID.BlueSlime, NPCID.Wraith, NPCID.Turtle, NPCID.Raven }).ToArray();
        var banners = ids.ToDictionary(id => id, Item.NPCtoBanner);
        foreach (bool misc in new[] { false, true })
        foreach (bool gold in new[] { false, true })
        foreach (bool banner in new[] { false, true })
        foreach (bool separate in new[] { false, true })
        foreach (bool crimson in new[] { false, true })
        foreach (string seed in new[] { "misc families", "1701534883" })
        {
            var map = new WorldMapping(seed, crimson, ids, banners: banner ? banners : null,
                separateProgressionPools: separate, groupGoldCritters: gold, groupMiscVariants: misc);
            foreach (int[] family in families)
            foreach (int id in family)
            {
                int[] expected;
                if (gold && GoldCritters.Family(id) != 0)
                    expected = ids.Where(member => GoldCritters.Family(member) == GoldCritters.Family(id)).Order().ToArray();
                else if (misc)
                    expected = family;
                else if (id >= 362 && id <= 365)
                    expected = id <= 363 ? [362, 363] : [364, 365];
                else if (banner && (id == 148 || id == 149))
                    expected = [148, 149];
                else
                    expected = [id];
                Assert(map.Members(id).SequenceEqual(expected), $"Incorrect misc membership for {id}");
                Assert(map.TryFindSource(id, out int source) && map.Replacement(source, "source", true) == expected[0],
                    "Misc group inverse hint failed");
                Assert(map.Replacement(id, "id", true) == map.Replacement(expected[0], "representative", false),
                    "Misc family replacements disagree");
            }
            foreach (int id in goldIds)
                Assert(map.IsGoldCritterGroup(id) == gold, "Misc setting changed gold weighting");
            Assert(map.Members(NPCID.Turtle).Count == 1 && map.Members(NPCID.Raven).Count == 1,
                "Misc grouping absorbed unrelated species");
            if (separate)
                foreach (int source in map.EarlySources)
                    Assert(map.Members(map.Replacement(source, "early", true)).All(NpcCatalog.IsPreHardmode),
                        "Misc groups crossed progression pools");
        }
        // Full-catalog permutation coverage with the new option enabled. The
        // existing fingerprint independently verifies old mappings when disabled.
        int[] all = Enumerable.Range(1, NPCID.Count - 1)
            .Where(id => NpcCatalog.CanReplace(new NPC { type = id, netID = id })).ToArray();
        var full = new WorldMapping("misc full catalog", false, all,
            banners: all.ToDictionary(id => id, Item.NPCtoBanner), groupGoldCritters: true,
            groupArmyTiers: true, combineTwins: true, groupMiscVariants: true);
        Assert(full.AllSources.Select(id => full.Replacement(id, "full", true)).Distinct().Count() == full.AllSources.Length,
            "Misc grouping lost full-catalog target coverage");
    }

    private static void TestCritterForms()
    {
        int[][] pairs = [
            [NPCID.Seagull, NPCID.Seagull2], [NPCID.Grebe, NPCID.Grebe2],
            [NPCID.Duck, NPCID.Duck2], [NPCID.DuckWhite, NPCID.DuckWhite2],
            [NPCID.Goldfish, NPCID.GoldfishWalker],
            [NPCID.GoldGoldfish, NPCID.GoldGoldfishWalker]
        ];
        int[] ids = pairs.SelectMany(pair => pair).Append(NPCID.BlueSlime).ToArray();
        var banners = ids.ToDictionary(id => id, Item.NPCtoBanner);
        foreach (bool grouped in new[] { false, true })
        foreach (bool gold in new[] { false, true })
        foreach (bool separate in new[] { false, true })
        foreach (bool crimson in new[] { false, true })
        {
            var map = new WorldMapping("critter forms", crimson, ids,
                banners: grouped ? banners : null, groupGoldCritters: gold,
                separateProgressionPools: separate);
            foreach (int[] pair in pairs)
            {
                int[] expected = gold && GoldCritters.Family(pair[0]) == NPCID.Goldfish
                    ? [NPCID.Goldfish, NPCID.GoldfishWalker, NPCID.GoldGoldfish, NPCID.GoldGoldfishWalker]
                    : pair;
                Assert(map.Members(pair[0]).SequenceEqual(expected), "Critter forms have incorrect membership");
                Assert(map.Members(pair[1]).SequenceEqual(expected), "Alternate form has a separate group");
                Assert(map.Replacement(pair[0], "first", false) == map.Replacement(pair[1], "second", false),
                    "Movement forms map to different replacements");
                Assert(map.TryFindSource(pair[0], out int first) && map.TryFindSource(pair[1], out int second)
                    && first == second, "Movement forms have different inverse hints");
            }
            Assert(map.AllSources.Length == (gold ? 6 : 7), "Critter species merged or split unexpectedly");
        }

        // The equivalent hostile forms already share real banners. Keep that
        // existing setting-controlled behavior and verify they were not missed.
        foreach (var (first, second) in new[] { (164, 165), (163, 238), (239, 240),
                     (236, 237), (530, 531), (158, 159), (195, 196), (198, 199), (348, 349) })
        {
            int[] enemies = [first, second, NPCID.BlueSlime, NPCID.Wraith];
            var enemyBanners = enemies.ToDictionary(id => id, Item.NPCtoBanner);
            Assert(enemyBanners[first] > 0 && enemyBanners[first] == enemyBanners[second],
                $"Alternate hostile forms {first}/{second} need a grouping rule");
            var map = new WorldMapping("hostile forms", false, enemies, banners: enemyBanners);
            Assert(map.Members(first).Contains(second), $"Hostile forms {first}/{second} split despite their banner");
        }
    }

    // HeavySkeleton's negative ID must share the tier and banner of Armored Skeleton,
    // not the early Skeleton variants adjacent to it in NPCID's variant list.
    private static void TestArmoredSkeletonTierAndGrouping()
    {
        Assert(NPCID.HeavySkeleton == -15, "Armored Skeleton variant ID changed");
        Assert(NPCID.FromNetId(NPCID.HeavySkeleton) == NPCID.ArmoredSkeleton, "Heavy Skeleton base species changed");
        Assert(!NpcCatalog.IsPreHardmode(NPCID.HeavySkeleton), "Heavy Skeleton leaked into pre-Hardmode");
        Assert(!NpcCatalog.IsPreHardmode(NPCID.ArmoredSkeleton), "Armored Skeleton leaked into pre-Hardmode");
        Assert(NpcCatalog.IsPreHardmode(NPCID.BigBoned) && NpcCatalog.IsPreHardmode(NPCID.ShortBones),
            "Ordinary Skeleton variants incorrectly moved to Hardmode");

        int[] ids = [NPCID.BlackSlime, NPCID.BlueSlime, NPCID.Zombie, NPCID.ArmoredSkeleton,
            NPCID.HeavySkeleton, NPCID.Wraith, NPCID.Pixie];
        var banners = ids.ToDictionary(id => id, id =>
        {
            var sample = new NPC { netID = id, type = NPCID.FromNetId(id) };
            return Item.NPCtoBanner(sample.BannerID());
        });
        Assert(banners[NPCID.HeavySkeleton] > 0 && banners[NPCID.HeavySkeleton] == banners[NPCID.ArmoredSkeleton],
            "Armored Skeleton forms do not resolve to the same real banner");

        foreach (bool crimson in new[] { false, true })
        foreach (bool grouped in new[] { false, true })
        foreach (bool separate in new[] { false, true })
        foreach (string seed in new[] { "1701534883", "armored regression" })
        {
            var map = new WorldMapping(seed, crimson, ids, banners: grouped ? banners : null,
                separateProgressionPools: separate);
            var expectedMembers = grouped ? new[] { (int)NPCID.ArmoredSkeleton, NPCID.HeavySkeleton }
                : new[] { (int)NPCID.HeavySkeleton };
            Assert(map.Members(NPCID.HeavySkeleton).SequenceEqual(expectedMembers), "Armored Skeleton banner grouping failed");
            if (!separate)
                continue;

            foreach (int source in map.EarlySources)
                Assert(map.Members(map.Replacement(source, source.ToString(), true)).All(id => NPCID.FromNetId(id) != NPCID.ArmoredSkeleton),
                    "An early source can produce an Armored Skeleton");
            Assert(map.TryFindSource(NPCID.HeavySkeleton, out int original) && !map.IsEarlySource(original),
                "Armored Skeleton has an early inverse source");
            Assert(map.Replacement(NPCID.HeavySkeleton, "-15", true) == map.Replacement(NPCID.HeavySkeleton, "-15", false),
                "Forced early Heavy Skeleton spawn lost its fixed Hardmode mapping");
        }
    }

    // Check user-visible hint wording and RNG consumption, which a valid mapping
    // alone does not guarantee. Extra random calls would change later spawn rolls.
    private static void TestHintNotesAndRandomCalls()
    {
        int[] ids = [NPCID.Zombie, NPCID.BaldZombie, NPCID.Bunny, NPCID.GoldBunny,
            NPCID.Wraith, NPCID.Retinazer, NPCID.Spazmatism, NPCID.ScutlixRider,
            NPCID.Scutlix, NPCID.TruffleWorm, NPCID.TruffleWormDigger];
        var banners = ids.ToDictionary(id => id, Item.NPCtoBanner);
        var map = new WorldMapping("hint notes", false, ids, banners: banners,
            combineTwins: true, groupGoldCritters: true);
        Assert(HintCommand.GetVariantNote(map, NPCID.Zombie) == " (random variant)", "Zombie hint lost variant note");
        Assert(HintCommand.GetVariantNote(map, NPCID.Bunny) == " (vanilla critter odds)", "Normal critter hint changed");
        Assert(HintCommand.GetVariantNote(map, NPCID.GoldBunny) == " (vanilla gold chance)", "Gold critter hint changed");
        foreach (int id in new[] { NPCID.Wraith, NPCID.Retinazer, NPCID.ScutlixRider, NPCID.TruffleWorm })
            Assert(HintCommand.GetVariantNote(map, id) == "", "Encounter/singleton hint incorrectly calls it a random variant");

        foreach (int id in new[] { NPCID.Bunny, NPCID.GoldBunny })
            Assert(GoldCritters.Pick([id], _ => throw new Exception("Singleton consumed RNG")) == id,
                "Singleton changed species");

        var bounds = new List<int>();
        int chosen = GoldCritters.Pick([NPCID.Bunny, NPCID.BunnyXmas, NPCID.GoldBunny], maximum =>
        {
            bounds.Add(maximum);
            return 1;
        });
        Assert(chosen == NPCID.BunnyXmas && bounds.SequenceEqual(new[] { NPC.goldCritterChance, 2 }),
            "Normal variant selection changed RNG order or bounds");
        bounds.Clear();
        chosen = GoldCritters.Pick([NPCID.Bunny, NPCID.BunnyXmas, NPCID.GoldBunny], maximum =>
        {
            bounds.Add(maximum);
            return 0;
        });
        Assert(chosen == NPCID.GoldBunny && bounds.SequenceEqual(new[] { NPC.goldCritterChance }),
            "Gold singleton consumed an extra random roll");

        // A luck roll decides rarity; ordinary RNG is reserved for appearance.
        chosen = GoldCritters.Pick([NPCID.Bunny, NPCID.GoldBunny],
            _ => throw new Exception("Unexpected appearance roll"), maximum =>
            {
                Assert(maximum == NPC.goldCritterChance, "Gold roll did not use vanilla's current denominator");
                return 0;
            });
        Assert(chosen == NPCID.GoldBunny, "Player luck roll was ignored");
    }

    /// <summary>
    /// Checks protected encounters, boss-family limits, habitat overrides, and supplemental lunar subtype credit.
    /// </summary>
    private static void TestEncounters()
    {
        foreach (int id in new[] { NPCID.CultistDevote, NPCID.CultistArcherBlue, NPCID.CultistArcherWhite,
                     NPCID.CultistBossClone, NPCID.CultistTablet, NPCID.CultistDragonHead,
                     NPCID.LunarTowerSolar, NPCID.LunarTowerVortex, NPCID.LunarTowerNebula, NPCID.LunarTowerStardust })
        {
            var npc = new NPC { type = id };
            Assert(!NpcCatalog.CanReplace(npc), $"Protected encounter randomized: {id}");
            Assert(!NpcCatalog.KeepActiveAI(npc), $"Protected encounter AI modified: {id}");
        }
        Assert(new RandomizerConfig().IncludeBosses, "Boss inclusion must default on");
        var cultist = new NPC { type = NPCID.CultistBoss, netID = NPCID.CultistBoss, boss = true };
        Assert(NpcCatalog.CanReplace(cultist), "Lunatic Cultist excluded from default pool");
        Assert(!NpcCatalog.CanReplace(cultist, true), "Boss exclusion missed Lunatic Cultist");
        Assert(!NpcCatalog.IsPreHardmode(NPCID.CultistBoss), "Cultist in pre-Hardmode pool");
        Assert(!ProgressionRules.Allows(NPCID.SolarSolenian, NPCID.CultistBoss)
            && !ProgressionRules.Allows(NPCID.MoonLordCore, NPCID.CultistBoss), "Cultist progression cycle");
        foreach (int id in new[] { NPCID.EyeofCthulhu, NPCID.SkeletronHead, NPCID.Retinazer, NPCID.Spazmatism,
            NPCID.TheDestroyer, NPCID.SkeletronPrime, NPCID.Plantera, NPCID.Golem, NPCID.MoonLordCore })
            Assert(!NpcCatalog.CanReplace(new NPC { type = id, boss = true }, true), "Boss source not excluded");
        foreach (int id in new[] { NPCID.Pumpking, NPCID.MourningWood, NPCID.IceQueen, NPCID.PirateShip, NPCID.MartianSaucerCore })
            Assert(!NpcCatalog.CanReplace(new NPC { type = id }, true), "Event boss not excluded");
        Assert(NpcCatalog.CanReplace(new NPC { type = NPCID.Zombie }, true), "Boss exclusion removed normal NPC");
        foreach (bool exclude in new[] { false, true })
        foreach (bool grouped in new[] { false, true })
        {
            NPC[] samples = [cultist, new NPC { type = NPCID.Wraith }, new NPC { type = NPCID.Pixie },
                new NPC { type = NPCID.Pumpking }, new NPC { type = NPCID.SolarSolenian }];
            var ids = samples.Where(n => NpcCatalog.CanReplace(n, exclude)).Select(n => n.type).ToArray();
            // Use real banner metadata for ordinary variants, then deliberately alter it later to test tier isolation.
            var banners = ids.ToDictionary(id => id, Item.NPCtoBanner);
            for (int seed = 0; seed < 30; seed++)
            {
                var map = new WorldMapping(seed.ToString(), false, ids, grouped ? banners : null);
                Assert(map.TryFindSource(NPCID.CultistBoss, out _) == !exclude, "Cultist target eligibility");
                Assert(map.LateSources.Contains(NPCID.CultistBoss) == !exclude, "Cultist source eligibility");
                Assert(map.TryFindSource(NPCID.Pumpking, out _) == !exclude, "Event boss target eligibility");
                foreach (int source in map.LateSources)
                    Assert(!exclude || !samples.Single(n => n.type == map.Replacement(source, "", false)).boss
                        && map.Replacement(source, "", false) != NPCID.Pumpking, "Excluded boss replacement");
            }
        }
        var eye = new NPC { type = NPCID.EyeofCthulhu, boss = true, active = true };
        var secondEye = new NPC { type = NPCID.EyeofCthulhu, boss = true, active = true };
        var skeletron = new NPC { type = NPCID.SkeletronHead, boss = true, active = true };
        Assert(BossRules.AlreadyPresent(secondEye, secondEye, [eye, secondEye], _ => false), "Duplicate Eye allowed");
        Assert(!BossRules.AlreadyPresent(skeletron, skeletron, [eye, skeletron], _ => false), "Distinct boss suppressed");
        Assert(!BossRules.AlreadyPresent(eye, eye, [eye], _ => false), "Boss counted itself");
        Assert(!BossRules.AlreadyPresent(secondEye, secondEye, [eye], _ => true), "Pending randomized source counted as boss");
        eye.active = false;
        Assert(!BossRules.AlreadyPresent(secondEye, secondEye, [eye], _ => false), "Dead boss prevents respawn");
        var eater = new NPC { type = NPCID.EaterofWorldsHead, boss = true };
        var segment = new NPC { type = NPCID.EaterofWorldsBody, active = true };
        Assert(BossRules.AlreadyPresent(eater, eater, [segment], _ => false), "Surviving Eater segments permit second encounter");
        Assert(!BossRules.AlreadyPresent(segment, segment, [eater], _ => false), "Required segment suppressed");
        Assert(!BossRules.Conflicts(NPCID.Retinazer, new NPC { type = NPCID.Spazmatism, active = true }), "Twins cannot coexist");
        Assert(BossRules.Family(NPCID.MoonLordHand) == NPCID.MoonLordCore, "Moon Lord family");
        Assert(BossRules.IsBossRoot(new NPC { type = NPCID.Pumpking }), "Event boss not limited");
        Assert(BossRules.IsBossRoot(new NPC { type = NPCID.DD2DarkMageT1 }), "Dark Mage not limited");
        foreach (int id in new[] { NPCID.BoneSerpentHead, NPCID.GiantWormHead, NPCID.DiggerHead,
                     NPCID.DevourerHead, NPCID.TombCrawlerHead, NPCID.DuneSplicerHead })
            Assert(BossRules.IgnoreBiome(new NPC { type = id }), $"Worm still subject to habitat retreat: {id}");
        foreach (int id in new[] { NPCID.QueenBee, NPCID.Plantera, NPCID.PlanterasTentacle, NPCID.GolemFistLeft })
            Assert(BossRules.IgnoreBiome(new NPC { type = id }), $"Boss/part still subject to habitat: {id}");
        Assert(!BossRules.IgnoreBiome(null), "Protected AI context modified");

        foreach (int id in new[] { NPCID.SolarSolenian, NPCID.SolarCrawltipedeTail, NPCID.VortexSoldier,
                     NPCID.NebulaBrain, NPCID.StardustSoldier })
            Assert(PillarCreditNPC.AdditionalCreditPillar(id) == NPCID.None, "Native pillar kill would be counted twice");
        Assert(PillarCreditNPC.AdditionalCreditPillar(NPCID.VortexLarva) == NPCID.LunarTowerVortex, "Missing Vortex larva credit");
        Assert(PillarCreditNPC.AdditionalCreditPillar(NPCID.StardustCellSmall) == NPCID.LunarTowerStardust, "Missing small cell credit");
        Assert(PillarCreditNPC.AdditionalCreditPillar(NPCID.Zombie) == NPCID.None, "Unrelated enemy receives credit");
    }

    /// <summary>
    /// Patches copies of installed vanilla AI methods and asks the JIT to validate their generated delegates.
    /// </summary>
    private static void TestAIPatches()
    {
        // Compile patched copies of the installed game's real methods, without
        // launching a server, modifying its assembly, or executing game AI.
        foreach (string name in new[] { "VanillaAI_Inner", "AI_006_Worms", "AI_045_Golem",
                     "AI_047_GolemFist", "AI_069_DukeFishron", "AI_120_HallowBoss", "ShouldRunAway" })
        {
            MethodInfo method = typeof(NPC).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var copy = new DynamicMethodDefinition(method);
            using var context = new ILContext(copy.Definition);
            PersistentEnemyAI.PatchRetreatConditions(context, name);
            if (name == "VanillaAI_Inner")
                NpcChildSpawnSources.Patch(context);
            MethodInfo generated = copy.Generate();
            Type[] signature = new[] { typeof(NPC) }.Concat(method.GetParameters().Select(p => p.ParameterType))
                .Append(method.ReturnType).ToArray();
            // Construct the exact signature, including the instance receiver, then JIT-compile without executing game AI.
            Delegate callable = generated.CreateDelegate(Expression.GetDelegateType(signature));
            RuntimeHelpers.PrepareDelegate(callable);
            Assert(true, $"Patched AI failed JIT: {name}");
            Console.WriteLine($"Patched AI compiled: {name}");
        }
    }

    /// <summary>
    /// Checks remote bolt delivery and rejects missing, inactive, out-of-range, or reused pillar slots.
    /// </summary>
    private static void TestPillarDelivery()
    {
        // Use a local NPC-slot array so distant delivery and invalid-slot handling do not require a running world.
        var npcs = new NPC[200];
        foreach (int type in new[] { NPCID.LunarTowerSolar, NPCID.LunarTowerVortex,
                     NPCID.LunarTowerNebula, NPCID.LunarTowerStardust })
        {
            var pillar = new NPC { type = type, active = true, width = 100, height = 200 };
            pillar.Center = new Microsoft.Xna.Framework.Vector2(1000f, 1000f);
            npcs[0] = pillar;
            var bolt = new Projectile { type = ProjectileID.TowerDamageBolt, active = true, width = 8, height = 8 };
            bolt.Center = new Microsoft.Xna.Framework.Vector2(120000f, 20000f);
            bolt.ai[0] = 0;
            PillarCreditProjectile.Deliver(bolt, npcs);
            Assert(bolt.Center == pillar.Center && bolt.active && bolt.netUpdate, "Distant kill not delivered to pillar");
            Assert(bolt.Hitbox.Intersects(pillar.Hitbox), "Vanilla collision will not consume credit");
        }
        // Cover negative, past-end, and empty slots before testing inactive/reused slots.
        foreach (int index in new[] { -1, 200, 1 })
        {
            var absent = new Projectile { type = ProjectileID.TowerDamageBolt, active = true };
            absent.ai[0] = index;
            PillarCreditProjectile.Deliver(absent, npcs);
            Assert(!absent.active, "Invalid pillar index must not reach vanilla Kill");
        }
        var stale = new Projectile { type = ProjectileID.TowerDamageBolt, active = true };
        stale.ai[0] = 0;
        npcs[0].active = false;
        PillarCreditProjectile.Deliver(stale, npcs);
        Assert(!stale.active, "Inactive pillar received credit");
        npcs[0] = new NPC { type = NPCID.Zombie, active = true };
        stale.active = true;
        PillarCreditProjectile.Deliver(stale, npcs);
        Assert(!stale.active, "Reused NPC slot received pillar credit");
    }

    /// <summary>
    /// Verifies banner grouping, inverse hints, deterministic reloads, full coverage, and separation across progression tiers.
    /// </summary>
    private static void TestVariantGroups()
    {
        int[] ids = [NPCID.Zombie, NPCID.BaldZombie, NPCID.PincushionZombie,
            NPCID.ZombieRaincoat, NPCID.Bunny, NPCID.Pixie, NPCID.Wraith];
        var banners = ids.ToDictionary(id => id, Item.NPCtoBanner);
        Assert(banners[NPCID.Zombie] > 0, "Zombie must have a banner");
        Assert(banners[NPCID.Zombie] == banners[NPCID.BaldZombie] &&
            banners[NPCID.Zombie] == banners[NPCID.PincushionZombie], "Zombie variants share banner");
        Assert(banners[NPCID.Zombie] != banners[NPCID.ZombieRaincoat], "Raincoat has distinct banner");
        for (int seed = 0; seed < 100; seed++)
        {
            var map = new WorldMapping(seed.ToString(), false, ids, banners);
            // Reverse enumeration order to model a reload that discovers content in a different order.
            var reload = new WorldMapping(seed.ToString(), false, ids.Reverse(), banners);
            Assert(map.Members(NPCID.Zombie).Count == 3, "Zombie grouping");
            Assert(map.Members(NPCID.ZombieRaincoat).Count == 1, "Raincoat isolation");
            Assert(map.Members(NPCID.Bunny).Count == 1, "Bannerless NPC isolation");
            Assert(map.Replacement(NPCID.Zombie, "", true) == map.Replacement(NPCID.BaldZombie, "", true), "Variants share mapping");
            var reachable = new HashSet<int>();
            foreach (int source in map.EarlySources.Concat(map.LateSources))
            {
                int target = map.Replacement(source, "", NpcCatalog.IsPreHardmode(source));
                Assert(target == reload.Replacement(source, "", NpcCatalog.IsPreHardmode(source)), "Stable grouped mapping");
                foreach (int variant in map.Members(target))
                {
                    reachable.Add(variant);
                    Assert(map.TryFindSource(variant, out int inverse) && inverse == source, "Grouped inverse hint");
                    Assert(NpcCatalog.IsPreHardmode(variant) == NpcCatalog.IsPreHardmode(source), "Grouped progression leak");
                }
            }
            Assert(reachable.SetEquals(ids), "Every grouped variant reachable");
        }
        // Shared banners must never merge the two progression tiers.
        banners[NPCID.Pixie] = banners[NPCID.Zombie];
        var split = new WorldMapping("split", false, ids, banners);
        Assert(!split.Members(NPCID.Zombie).Contains(NPCID.Pixie), "Shared banner across tiers");
        Assert(split.Members(NPCID.Pixie).Count == 1, "Hardmode banner group separated");
    }

    /// <summary>
    /// Checks individual progression dependencies and complete grouped mappings across seeds, evils, and pool modes.
    /// </summary>
    private static void TestProgressionRules()
    {
        // Check source access dependencies directly before testing the repaired full permutations.
        int[] mechs = [NPCID.Retinazer, NPCID.Spazmatism, NPCID.TheDestroyer, NPCID.SkeletronPrime];
        int[] dungeon = [NPCID.AngryBones, NPCID.AngryBonesBig, NPCID.AngryBonesBigMuscle,
            NPCID.AngryBonesBigHelmet, NPCID.DarkCaster, NPCID.CursedSkull, NPCID.DungeonSlime,
            NPCID.BlazingWheel, NPCID.SpikeBall, NPCID.Paladin, NPCID.DungeonSpirit];
        foreach (int source in dungeon)
            Assert(!ProgressionRules.Allows(source, NPCID.SkeletronHead), "Skeletron behind dungeon");
        int[] skeletronGated = Enumerable.Range(NPCID.Scarecrow1, NPCID.HeadlessHorseman - NPCID.Scarecrow1 + 1)
            .Concat(Enumerable.Range(NPCID.MourningWood, NPCID.Poltergeist - NPCID.MourningWood + 1))
            .Concat(Enumerable.Range(NPCID.ZombieElf, NPCID.Flocko - NPCID.ZombieElf + 1))
            .Concat(Enumerable.Range(NPCID.StardustWormHead, NPCID.VortexSoldier - NPCID.StardustWormHead + 1))
            .Concat(new int[] { NPCID.CultistBoss, NPCID.MoonLordCore }).ToArray();
        foreach (int source in skeletronGated)
            Assert(!ProgressionRules.Allows(source, NPCID.SkeletronHead), "Skeletron behind dependent event/boss");
        foreach (int source in new int[] { NPCID.Wraith, NPCID.Plantera, NPCID.Golem, NPCID.MartianWalker })
            Assert(ProgressionRules.Allows(source, NPCID.SkeletronHead), "Unrelated Skeletron source blocked");
        int[] postPlant = [NPCID.Lihzahrd, NPCID.LihzahrdCrawler, NPCID.FlyingSnake, NPCID.Golem,
            NPCID.RustyArmoredBonesAxe, NPCID.SkeletonCommando, NPCID.DungeonSpirit,
            NPCID.Mothron, NPCID.MothronEgg, NPCID.MothronSpawn, NPCID.Butcher, NPCID.DrManFly,
            NPCID.Nailhead, NPCID.Psycho, NPCID.DeadlySphere, NPCID.EmpressButterfly, NPCID.HallowBoss,
            NPCID.Pumpking, NPCID.Scarecrow1, NPCID.Scarecrow10, NPCID.Poltergeist,
            NPCID.ZombieElf, NPCID.NutcrackerSpinning, NPCID.IceQueen, NPCID.Flocko];
        int[] postGolem = [NPCID.MartianProbe, NPCID.BrainScrambler, NPCID.MartianSaucerCore,
            NPCID.MartianWalker, NPCID.DD2Betsy, NPCID.DD2GoblinT3, NPCID.DD2DarkMageT3,
            NPCID.DD2OgreT3, NPCID.DD2LightningBugT3, NPCID.MoonLordCore,
            NPCID.StardustWormHead, NPCID.StardustCellSmall, NPCID.StardustSpiderSmall,
            NPCID.VortexLarva, NPCID.VortexSoldier, NPCID.NebulaBrain, NPCID.SolarSolenian];
        foreach (int source in postPlant.Concat(postGolem))
        {
            Assert(!ProgressionRules.Allows(source, NPCID.Plantera), $"Plantera dependency: {source}");
            foreach (int mech in mechs)
                Assert(!ProgressionRules.Allows(source, mech), $"Mech dependency: {source}");
        }
        foreach (int source in postGolem)
            Assert(!ProgressionRules.Allows(source, NPCID.Golem), $"Golem dependency: {source}");
        foreach (int source in new int[] { NPCID.RedDevil, NPCID.Lavabat, NPCID.DD2OgreT2,
            NPCID.DD2GoblinT2, NPCID.Reaper, NPCID.Frankenstein, NPCID.Plantera, NPCID.Probe }.Concat(mechs))
            foreach (int mech in mechs)
                Assert(!ProgressionRules.Allows(source, mech), "Mech first-kill/circular dependency");
        Assert(!NpcCatalog.IsPreHardmode(NPCID.Lavabat), "Lava Bat leaked into pre-Hardmode");
        Assert(ProgressionRules.Allows(NPCID.Zombie, NPCID.SkeletronHead), "Ordinary source blocked");
        Assert(ProgressionRules.Allows(NPCID.Wraith, NPCID.TheDestroyer), "Early Hardmode source blocked");
        Assert(ProgressionRules.Allows(NPCID.Reaper, NPCID.Plantera), "Pre-Plantera source blocked");
        Assert(ProgressionRules.Allows(NPCID.Lihzahrd, NPCID.Golem), "Temple cannot produce Golem");
        Assert(ProgressionRules.Allows(NPCID.SolarSolenian, NPCID.MoonLordCore), "Final boss unnecessarily blocked");

        // Exercise the complete positive-ID metadata catalog plus explicit negative
        // variants. No Main/ContentSamples initialization or live server is needed.
        int[] candidates = Enumerable.Range(1, NPCID.Count - 1)
            .Where(id => NpcCatalog.CanReplace(new NPC { type = id, netID = id }))
            .Concat(new[] { (int)NPCID.SmallZombie, NPCID.BigCrimslime, NPCID.BigEater }).ToArray();
        var banners = candidates.ToDictionary(id => id, id => Item.NPCtoBanner(id == NPCID.SmallZombie ? NPCID.Zombie : id));
        foreach (bool grouped in new[] { false, true })
        foreach (bool crimson in new[] { false, true })
        foreach (bool separatePools in new[] { false, true })
        for (int seed = 0; seed < 30; seed++)
        {
            var map = new WorldMapping("progression" + seed, crimson, candidates, grouped ? banners : null,
                separateProgressionPools: separatePools);
            var reload = new WorldMapping("progression" + seed, crimson, candidates.Reverse(), grouped ? banners : null,
                separateProgressionPools: separatePools);
            // Track each concrete target variant once so coverage and uniqueness are tested even after grouping.
            var reached = new HashSet<int>();
            foreach (int source in map.EarlySources.Concat(map.LateSources))
            {
                int target = map.Replacement(source, "", NpcCatalog.IsPreHardmode(source));
                Assert(source != target, "Progression repair introduced identity");
                Assert(target == reload.Replacement(source, "", NpcCatalog.IsPreHardmode(source)), "Progression repair nondeterministic");
                foreach (int variant in map.Members(target))
                {
                    Assert(reached.Add(variant), "Progression repair duplicated a target");
                    Assert(map.TryFindSource(variant, out int inverse) && inverse == source, "Repaired inverse hint");
                    foreach (int s in map.Members(source))
                        Assert(ProgressionRules.Allows(s, variant), "Grouped progression violation");
                }
            }
            Assert(reached.SetEquals(candidates.Where(id => SpecialSeedRules.Allowed(id, false))), "Progression repair lost target coverage");
        }

        // A restricted variant cannot hide behind an unrestricted representative.
        int[] mixed = [NPCID.Zombie, NPCID.AngryBones, NPCID.Bunny, NPCID.SkeletronHead, NPCID.BlueSlime];
        var mixedBanners = new Dictionary<int, int> { [NPCID.Zombie] = 123, [NPCID.AngryBones] = 123 };
        for (int seed = 0; seed < 30; seed++)
        {
            var map = new WorldMapping(seed.ToString(), false, mixed, mixedBanners);
            Assert(map.TryFindSource(NPCID.SkeletronHead, out int source) &&
                !map.Members(source).Contains(NPCID.AngryBones), "Restriction checked representative only");
        }
        // Fallback forced sources also obey the rules when absent from the permutation.
        var fallback = new WorldMapping("fallback", false, [NPCID.Wraith, NPCID.Pixie,
            NPCID.TheDestroyer, NPCID.Plantera, NPCID.Golem, NPCID.Mimic]);
        for (int seed = 0; seed < 100; seed++)
            Assert(ProgressionRules.Allows(NPCID.VortexLarva,
                fallback.Replacement(NPCID.VortexLarva, seed.ToString(), false)), "Fallback bypassed progression");
    }

    /// <summary>
    /// Validates event-accounting patches, source normalization, pillar categories, and special-world eligibility.
    /// </summary>
    private static void TestMappedCreditAndSeeds()
    {
        foreach (string name in new[] { "checkDead", "CheckProgressFrostMoon", "CheckProgressPumpkinMoon", "VanillaHitEffect", "DoDeathEvents" })
        {
            MethodInfo method = typeof(NPC).GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;
            using var copy = new DynamicMethodDefinition(method);
            using var context = new ILContext(copy.Definition);
            MappedEventCredit.Patch(context, name);
            if (name == "checkDead")
                NpcChildSpawnSources.Patch(context);
            MethodInfo generated = copy.Generate();
            Type[] signature = new[] { typeof(NPC) }.Concat(method.GetParameters().Select(p => p.ParameterType))
                .Append(method.ReturnType).ToArray();
            RuntimeHelpers.PrepareDelegate(generated.CreateDelegate(Expression.GetDelegateType(signature)));
            Assert(true, $"Event patch JIT {name}");
            Console.WriteLine($"Patched event accounting compiled: {name}");
        }
        Assert(MappedEventCredit.NormalizeEventSource(NPCID.GoblinScout) == NPCID.GoblinScout, "Scout incorrectly promoted to invasion enemy");
        Assert(NPC.GetNPCInvasionGroup(MappedEventCredit.NormalizeEventSource(NPCID.GoblinScout)) == 0,
            "Scout replacement grants invasion credit");
        Assert(NPC.GetNPCInvasionGroup(MappedEventCredit.NormalizeEventSource(NPCID.GoblinPeon)) == 1,
            "Actual army source lost invasion credit");
        Assert(MappedEventCredit.NormalizeEventSource(NPCID.PirateCaptain) == NPCID.PirateCaptain, "Event weight source changed");
        foreach (int id in new[] { NPCID.GoldenSlime, NPCID.BigMimicJungle })
            Assert(!SpecialSeedRules.Allowed(id, false) && SpecialSeedRules.Allowed(id, true), "Special-seed filter");
        Assert(SpecialSeedRules.Allowed(NPCID.ExplosiveBunny, false), "Craftable explosive bunny incorrectly excluded");
        Assert(SpecialSeedRules.Allowed(NPCID.SkeletronPrime, false), "Normal mech excluded with Mechdusa");
        foreach (bool remix in new[] { false, true })
        foreach (bool worthy in new[] { false, true })
            Assert(SpecialSeedRules.MechdusaAllowed(remix, worthy) == (remix && worthy), "Mechdusa seed combination");
        var nativeBolt = new Terraria.DataStructures.EntitySource_Parent(new NPC { type = NPCID.SolarSolenian });
        var mappedBolt = new Terraria.DataStructures.EntitySource_Misc(PillarCreditNPC.CreditContext);
        Assert(PillarCreditProjectile.SuppressNativeBolt(false, nativeBolt), "Native pillar bolt would double count mapped credit");
        Assert(!PillarCreditProjectile.SuppressNativeBolt(true, nativeBolt), "Legacy pillar bolt suppressed");
        Assert(!PillarCreditProjectile.SuppressNativeBolt(false, mappedBolt), "Mapped credit bolt suppressed");
        Assert(!PillarCreditProjectile.SuppressNativeBolt(true, mappedBolt), "Legacy small-form credit suppressed");
        var settings = new RandomizerConfig();
        Assert(!settings.LegacyEventCredit && !settings.LegacyPillarCredit, "New credit modes must default on");
        settings.LegacyEventCredit = true;
        Assert(!settings.LegacyPillarCredit, "Legacy settings coupled");
        settings.LegacyEventCredit = false;
        settings.LegacyPillarCredit = true;
        Assert(!settings.LegacyEventCredit, "Pillar toggle changed event mode");
        foreach (bool grouped in new[] { false, true })
        foreach (bool special in new[] { false, true })
        {
            int[] ids = [NPCID.BlueSlime, NPCID.GoldenSlime, NPCID.GoblinScout, NPCID.GoblinPeon,
                NPCID.Zombie, NPCID.BaldZombie, NPCID.Wraith, NPCID.Pixie, NPCID.BigMimicJungle,
                NPCID.SolarSolenian, NPCID.VortexSoldier, NPCID.NebulaBrain, NPCID.StardustSoldier,
                NPCID.ZombieElf, NPCID.ZombieElfBeard, NPCID.Pumpking, NPCID.PirateCaptain, NPCID.DD2Betsy];
            var banners = ids.ToDictionary(id => id, Item.NPCtoBanner);
            var map = new WorldMapping("credit", false, ids, grouped ? banners : null, special);
            Assert(map.TryFindSource(NPCID.GoldenSlime, out _) == special, "Golden Slime availability");
            Assert(map.TryFindSource(NPCID.BigMimicJungle, out _) == special, "Jungle Mimic availability");
            foreach (int original in ids.Where(id => SpecialSeedRules.Allowed(id, special)))
            {
                int target = map.Replacement(original, "", NpcCatalog.IsPreHardmode(original));
                foreach (int variant in map.Members(target))
                {
                    Assert(map.TryFindSource(variant, out int source) && map.Members(source).Contains(original), "Kill credit did not follow inverse group");
                    int pillar = PillarCreditNPC.CreditPillar(original);
                    if (pillar != NPCID.None)
                        Assert(PillarCreditNPC.CreditPillar(source) == pillar, "Mapped kill credited wrong pillar");
                }
            }
        }
        Assert(PillarCreditNPC.CreditPillar(NPCID.SolarSolenian) == NPCID.LunarTowerSolar, "Solar classification");
        Assert(PillarCreditNPC.CreditPillar(NPCID.VortexLarva) == NPCID.LunarTowerVortex, "Vortex classification");
        Assert(PillarCreditNPC.CreditPillar(NPCID.NebulaHeadcrab) == NPCID.LunarTowerNebula, "Nebula classification");
        Assert(PillarCreditNPC.CreditPillar(NPCID.StardustCellSmall) == NPCID.LunarTowerStardust, "Stardust classification");
        Assert(PillarCreditNPC.CreditPillar(NPCID.Zombie) == NPCID.None, "Unrelated pillar credit");
    }

    /// <summary>
    /// Verifies paired-Twins grouping/credit and the exported representation of complete encounters.
    /// </summary>
    private static void TestCombinedTwinsAndExport()
    {
        Assert(new RandomizerConfig().CombineTwins, "Combined Twins default must be true");
        int[] candidates = [NPCID.Retinazer, NPCID.Spazmatism, NPCID.Wraith, NPCID.Pixie,
            NPCID.Mimic, NPCID.Plantera, NPCID.Golem, NPCID.SolarSolenian,
            NPCID.BlueSlime, NPCID.GoldenSlime, NPCID.Zombie, NPCID.BaldZombie];
        var banners = candidates.ToDictionary(id => id, Item.NPCtoBanner);
        foreach (bool grouped in new[] { false, true })
        foreach (bool combined in new[] { false, true })
        for (int seed = 0; seed < 100; seed++)
        {
            var map = new WorldMapping(seed.ToString(), false, candidates, grouped ? banners : null, false, combined);
            var again = new WorldMapping(seed.ToString(), false, candidates.Reverse(), grouped ? banners : null, false, combined);
            Assert(map.IsTwinsGroup(NPCID.Retinazer) == combined, "Twins grouping coupled to banner setting");
            Assert(map.Members(NPCID.Retinazer).Count == (combined ? 2 : 1), "Incorrect Twin members");
            Assert(map.TryFindSource(NPCID.Retinazer, out int retSource) && map.TryFindSource(NPCID.Spazmatism, out int spazSource)
                && (retSource == spazSource) == combined, "Twins inverse hint grouping");
            Assert((map.Replacement(NPCID.Retinazer, "", false) == map.Replacement(NPCID.Spazmatism, "", false)) == combined,
                "Twins source mapping");
            var reached = new HashSet<int>();
            foreach (int source in map.EarlySources.Concat(map.LateSources))
            {
                int target = map.Replacement(source, "", NpcCatalog.IsPreHardmode(source));
                Assert(target == again.Replacement(source, "", NpcCatalog.IsPreHardmode(source)), "Twins mapping unstable");
                foreach (int t in map.Members(target))
                {
                    Assert(reached.Add(t), "Twins grouping duplicate target");
                    foreach (int s in map.Members(source))
                        Assert(ProgressionRules.Allows(s, t), "Combined Twins progression violation");
                }
            }
            Assert(reached.SetEquals(candidates.Where(id => SpecialSeedRules.Allowed(id, false))), "Twins grouping lost target");
            string report = MappingExport.Render(map, candidates, id => $"NPC [{id}]", "seed=" + seed);
            Assert(report.Contains("seed=" + seed) && report.Contains("PRE-HARDMODE") && report.Contains("HARDMODE"), "Export metadata/tiers");
            Assert(report.Split('\n').Count(line => line.StartsWith("NPC [")) == candidates.Length, "Export missing/duplicate source rows");
            Assert(report.Contains($"NPC [{NPCID.GoldenSlime}] (fallback source) ->"), "Export omitted forced excluded source");
            Assert(report.Contains("The Twins (both):") == combined, "Export misrepresents paired spawn");
        }

        var first = new NPC { type = NPCID.Retinazer, netID = NPCID.Retinazer, active = true, life = 100, target = 0 };
        var second = new NPC { type = NPCID.Spazmatism, netID = NPCID.Spazmatism, active = true, life = 100, target = 0 };
        TwinsEncounter.Link(first, second);
        first.life = 0;
        Assert(!TwinsEncounter.CanCredit(first), "First Twin death credited before encounter completion");
        second.life = 0;
        Assert(TwinsEncounter.CanCredit(second) && TwinsEncounter.CanCredit(second), "Final Twin credit not stable across event reads");
        Assert(!TwinsEncounter.CanCredit(first), "Both Twin deaths credited");
        TwinsEncounter.Link(first, second);
        Assert(TwinsEncounter.CanCredit(first) && !TwinsEncounter.CanCredit(second), "Simultaneous deaths credited twice");
        TwinsEncounter.Link(first, second);
        second.life = 100;
        TwinsEncounter.Forget(second);
        Assert(TwinsEncounter.CanCredit(first), "Reused NPC slot blocks old encounter credit");
        Assert(TwinsEncounter.AlreadyPresent(first, [second]), "Surviving Spazmatism does not block paired encounter");
        RandomizedNPC.RegisterSpawn(second, new Terraria.DataStructures.EntitySource_Misc("test"), 5);
        Assert(!TwinsEncounter.AlreadyPresent(first, [second]), "Pending randomized source blocks Twins");
        Assert(!RandomizedNPC.SuppressPairedSource(first, 4, [first, second]), "Different-tick summon suppressed");
        Assert(RandomizedNPC.SuppressPairedSource(first, 5, [first, second]), "Twin source summon not consolidated");
        Assert(!RandomizedNPC.SuppressPairedSource(first, 5, [first, second]), "Source consolidated repeatedly");
        RandomizedNPC.ClearPendingSpawns();
        RandomizedNPC.RegisterSpawn(second, new DirectNpcSpawnSource(), 5);
        Assert(!RandomizedNPC.SuppressPairedSource(first, 5, [first, second]), "Direct command spawn suppressed");
        RandomizedNPC.ClearPendingSpawns();
        Assert(new DumpMappingCommand().Type == (CommandType.World | CommandType.Console), "Export must use authoritative host mapping");
    }

    /// <summary>
    /// Compares the constrained matcher with brute-force solutions for every four-node allowed-edge graph.
    /// </summary>
    private static void TestConstrainedMatching()
    {
        // Exhaust all allowed-edge graphs on four nodes and compare with brute
        // force. This catches lost targets and greedy repairs that cannot recover.
        int[] ids = [1, 2, 3, 4];
        var edges = ids.SelectMany(s => ids.Where(t => t != s).Select(t => (s, t))).ToArray();
        var permutations = new List<int[]>();
        foreach (int a in ids)
        foreach (int b in ids)
        foreach (int c in ids)
        foreach (int d in ids)
        {
            int[] p = [a, b, c, d];
            if (p.Distinct().Count() == 4 && ids.Where((s, i) => s == p[i]).Count() == 0)
                permutations.Add(p);
        }
        for (int mask = 0; mask < (1 << edges.Length); mask++)
        {
            bool Allowed(int s, int t) => Array.FindIndex(edges, e => e == (s, t)) is int i && i >= 0 && (mask & (1 << i)) != 0;
            bool possible = permutations.Any(p => ids.Select((s, i) => Allowed(s, p[i])).All(x => x));
            try
            {
                var map = SeededSelection.ConstrainedShuffle("test", "constraints", ids, Allowed);
                Assert(possible && map.Count == 4 && map.Values.Distinct().Count() == 4, "Invalid constrained permutation");
                Assert(map.All(p => Allowed(p.Key, p.Value)), "Forbidden edge in matching");
            }
            catch (InvalidOperationException)
            {
                Assert(!possible, "Matching missed valid solution");
            }
        }
        var old = SeededSelection.Shuffle("same", "same", ids);
        var unchanged = SeededSelection.ConstrainedShuffle("same", "same", ids, (_, _) => true);
        Assert(old.All(p => unchanged[p.Key] == p.Value), "Unrestricted shuffle unnecessarily changed");
    }

    /// <summary>
    /// Checks that existing lunar state, surviving pillars, and Moon Lord block duplicate apocalypse triggers.
    /// </summary>
    private static void TestLunarEventGuard()
    {
        RandomizedNPC.ClearPendingSpawns();
        Assert(LunarEventGuard.CanStart(false, false, 0, []), "First Cultist cannot start lunar event");
        Assert(!LunarEventGuard.CanStart(true, false, 0, []), "Repeated Cultist restarts active lunar event");
        Assert(!LunarEventGuard.CanStart(false, true, 0, []), "Tower flags ignored during transition");
        foreach (int countdown in new[] { 1, 60, 3600 })
            Assert(!LunarEventGuard.CanStart(false, false, countdown, []), "Cultist restarts Moon Lord countdown");
        int[] types = [NPCID.LunarTowerSolar, NPCID.LunarTowerVortex, NPCID.LunarTowerNebula,
            NPCID.LunarTowerStardust, NPCID.MoonLordCore];
        foreach (int type in types)
        {
            var npc = new NPC { type = type, active = true, life = 0 };
            Assert(!LunarEventGuard.CanStart(false, false, 0, [null, npc]), "Active/dying lunar entity ignored");
            npc.active = false;
            Assert(LunarEventGuard.CanStart(false, false, 0, [npc]), "Finished event prevents next lunar event");
        }
        // All sixteen combinations of surviving pillars must block a repeat;
        // completing the last pillar remains free to start the normal countdown.
        for (int mask = 0; mask < 16; mask++)
        {
            NPC[] pillars = types.Take(4).Select((type, i) => new NPC
                { type = type, active = (mask & (1 << i)) != 0, life = 1234 }).ToArray();
            Assert(LunarEventGuard.CanStart(false, false, 0, pillars) == (mask == 0), "Partially cleared event restarted");
            Assert(pillars.All(p => p.life == 1234), "Guard modified existing pillars");
        }
        var pending = new NPC { type = NPCID.MoonLordCore, netID = NPCID.MoonLordCore, active = true };
        RandomizedNPC.RegisterSpawn(pending, new Terraria.DataStructures.EntitySource_Misc("test"));
        Assert(LunarEventGuard.CanStart(false, false, 0, [pending]), "Pending random source blocks lunar event");
        RandomizedNPC.ClearPendingSpawns();
        Assert(!LunarEventGuard.CanStart(false, false, 0, [pending]), "Actual Moon Lord must block lunar restart");
        Assert(LunarEventGuard.CanStart(false, false, 0,
            [new NPC { type = NPCID.CultistBoss, active = true }]), "Dying Cultist blocks its own first event");
    }

    /// <summary>
    /// Checks town/shimmer/evil defaults, resident eligibility and tiers, protected exceptions, and resident progression gates.
    /// </summary>
    private static void TestTownAndEvilOptions()
    {
        // Check declared runtime defaults as well as eligibility under both enabled and disabled options.
        var config = new RandomizerConfig();
        Assert(config.IncludeOppositeEvil && config.RandomizeTownShimmer && !config.RandomizeTownNpcs, "Town/evil defaults wrong");
        foreach (int id in new int[] { NPCID.Guide, NPCID.Mechanic, NPCID.GoblinTinkerer, NPCID.Stylist,
                     NPCID.Angler, NPCID.Golfer, NPCID.DD2Bartender, NPCID.BestiaryGirl, NPCID.DyeTrader,
                     NPCID.Painter, NPCID.PartyGirl })
        {
            var npc = new NPC { type = id, townNPC = true };
            Assert(!NpcCatalog.CanReplace(npc), "Town NPC randomized by default");
            Assert(NpcCatalog.CanReplace(npc, includeTownNpcs: true), "Resident missing from optional pool");
            Assert(NpcCatalog.IsPreHardmode(id), "Pre-Hardmode resident put in late pool");
        }
        foreach (int id in new int[] { NPCID.Wizard, NPCID.TaxCollector, NPCID.Cyborg, NPCID.Steampunker,
                     NPCID.Truffle, NPCID.Pirate, NPCID.Princess })
            Assert(!NpcCatalog.IsPreHardmode(id), "Hardmode resident leaked into early pool");
        foreach (int id in new int[] { NPCID.OldMan, NPCID.TravellingMerchant, NPCID.BoundGoblin,
                     NPCID.BoundMechanic, NPCID.SkeletonMerchant, NPCID.TownCat, NPCID.TownDog })
            Assert(!NpcCatalog.CanReplace(new NPC { type = id, townNPC = true }, includeTownNpcs: true), "Protected town entity admitted");
        foreach (bool crimson in new[] { false, true })
        foreach (bool separate in new[] { false, true })
        foreach (bool include in new[] { false, true })
        {
            int[] ids = [NPCID.Zombie, NPCID.Bunny, NPCID.Wraith, NPCID.Pixie, NPCID.Crimera,
                NPCID.EaterofSouls, NPCID.IchorSticker, NPCID.Corruptor];
            var map = new WorldMapping("evil option", crimson, ids, separateProgressionPools: separate, includeOppositeEvil: include);
            foreach (int id in ids)
            {
                bool native = NpcCatalog.AllowedInWorld(id, crimson);
                Assert(map.TryFindSource(id, out _) == (native || include), "Opposite evil toggle ignored");
                if (!native && include && separate)
                    Assert(map.LateSources.Contains(id) && !map.EarlySources.Contains(id), "Opposite evil entered early pool");
            }
        }
        Assert(!ProgressionRules.Allows(NPCID.Mechanic, NPCID.SkeletronHead), "Skeletron locked behind Mechanic");
        Assert(!ProgressionRules.Allows(NPCID.Steampunker, NPCID.SkeletronPrime), "Mech locked behind Steampunker");
        Assert(!ProgressionRules.Allows(NPCID.Cyborg, NPCID.Plantera), "Plantera locked behind Cyborg");
    }

    /// <summary>
    /// Covers every cavern variant, army tier-grouping option combination, and Jungle Mimic special-seed handling.
    /// </summary>
    private static void TestArmyProgression()
    {
        int[][] families = [ [552, 553, 554], [555, 556, 557], [558, 559, 560], [561, 562, 563],
            [564, 565], [568, 569], [570, 571], [572, 573], [574, 575], [576, 577] ];
        int[][] tiers = [ [552, 553, 554], [555, 556, 557], [558, 559, 560], [561, 562, 563],
            [564, 564, 565], [568, 568, 569], [570, 570, 571], [572, 572, 573], [574, 574, 575], [576, 576, 577] ];
        Assert(ArmyFamilies.ProgressionTier(false, false, false) == 1, "Early army tier");
        Assert(ArmyFamilies.ProgressionTier(true, false, false) == 1, "Hardmode alone must not enable tier 2");
        Assert(ArmyFamilies.ProgressionTier(true, true, false) == 2, "Mech army tier");
        Assert(ArmyFamilies.ProgressionTier(true, true, true) == 3, "Golem army tier");
        int[] ids = families.SelectMany(f => f).Concat(new[] { (int)NPCID.BlueSlime, NPCID.Zombie,
            NPCID.SkeletronPrime, NPCID.Golem, NPCID.Wraith }).ToArray();
        foreach (bool separate in new[] { false, true })
        foreach (bool crimson in new[] { false, true })
        {
            var map = new WorldMapping("army persistence", crimson, ids, groupArmyTiers: true,
                separateProgressionPools: separate);
            for (int family = 0; family < families.Length; family++)
            foreach (int member in families[family])
            {
                int first = families[family][0];
                Assert(map.Members(member).SequenceEqual(families[family]), "Army family split between progression pools");
                Assert(map.IsEarlySource(member) == NpcCatalog.IsPreHardmode(first), "Army family uses a later-tier pool");
                Assert(map.Replacement(member, "member", false) == map.Replacement(first, "first", true),
                    "Upgraded army spawn loses the original replacement");
                Assert(map.TryFindSource(member, out int source) && map.TryFindSource(first, out int firstSource)
                    && source == firstSource, "Army tier hint loses its original source");
                for (int tier = 1; tier <= 3; tier++)
                    Assert(ArmyFamilies.ForTier(member, tier) == tiers[family][tier - 1], "Wrong progression-selected army form");
                int target = map.Replacement(member, "member", false);
                Assert(map.Members(target).All(t => ProgressionRules.Allows(first, t)), "Army mapping introduced a progression cycle");
            }
            Assert(map.TryFindSource(NPCID.DD2GoblinT3, out int goblinSource)
                && (!separate || map.IsEarlySource(goblinSource)), "Tier-1 army family moved into the Hardmode pool");
            Assert(HintCommand.GetVariantNote(map, NPCID.DD2OgreT3) == " (tier follows progression)", "Army hint suggests a random tier");
            Assert(MappingExport.Render(map, ids, id => id.ToString(), "test").Contains("Progression-selected army tier:"),
                "Export hides army tier selection");
        }
        // Check the selected IDs against vanilla wave accounting. Replace the
        // difficulty getter in a copied method so this test needs no game window.
        for (int tier = 1; tier <= 3; tier++)
        {
            MethodInfo points = typeof(Terraria.GameContent.Events.DD2Event).GetMethod(
                $"Difficulty_{tier}_GetMonsterPointsWorth", BindingFlags.NonPublic | BindingFlags.Static)!;
            int source = ArmyFamilies.ForTier(NPCID.DD2GoblinT1, tier);
            using var copy = new DynamicMethodDefinition(points);
            foreach (var instruction in copy.Definition.Body.Instructions)
            {
                if (instruction.Operand is Mono.Cecil.MethodReference method && method.Name == "get_expertMode")
                {
                    instruction.OpCode = Mono.Cecil.Cil.OpCodes.Ldc_I4_0;
                    instruction.Operand = null;
                }
            }
            var getPoints = (Func<int, int>)copy.Generate().CreateDelegate(typeof(Func<int, int>));
            Assert(getPoints(source) > 0, "Mapped army source earns no wave credit");
        }
    }

    private static void TestArmyAndCavernFamilies()
    {
        Assert(new RandomizerConfig().GroupOldOnesArmyTiers, "Army grouping default");
        // Inject fixed RNG outputs to visit every family and variant, avoiding statistical or flaky coverage tests.
        var cavern = new HashSet<int>();
        for (int family = 0; family < 3; family++)
        for (int variant = 0; variant < (family == 2 ? 9 : 2); variant++)
        {
            int call = 0;
            cavern.Add(CavernFamilies.Pick(max => call++ == 0 ? family : variant));
        }
        Assert(cavern.SetEquals(Enumerable.Range(NPCID.Crawdad, 13)), "Missing cavern species/variant");
        MethodInfo method = typeof(NPC).GetMethod("SpawnNPC", BindingFlags.Public | BindingFlags.Static)!;
        using (var copy = new DynamicMethodDefinition(method))
        using (var context = new ILContext(copy.Definition))
        {
            CavernFamilies.Patch(context);
            RuntimeHelpers.PrepareDelegate(copy.Generate().CreateDelegate<Action>());
            Assert(true, "Cavern patch JIT");
        }
        // Combine all independent army enemies with progression bosses so unsafe tier-group assignments are observable.
        int[] ids = Enumerable.Range(NPCID.DD2Betsy, NPCID.DD2LightningBugT3 - NPCID.DD2Betsy + 1)
            .Where(id => !NpcCatalog.IsExclusiveHelper(id)).Append(NPCID.SkeletronPrime).Append(NPCID.Golem)
            .Append(NPCID.Wraith).Append(NPCID.Pixie).ToArray();
        foreach (bool enabled in new[] { false, true })
        foreach (bool banners in new[] { false, true })
        foreach (bool separate in new[] { false, true })
        {
            var map = new WorldMapping("army", false, ids, banners ? ids.ToDictionary(id => id, Item.NPCtoBanner) : null,
                separateProgressionPools: separate, groupArmyTiers: enabled);
            foreach (int id in ids.Where(id => ArmyFamilies.Family(id) != 0))
            {
                var expected = enabled ? ids.Where(other => ArmyFamilies.Family(other) == ArmyFamilies.Family(id)) : new[] { id };
                Assert(map.Members(id).ToHashSet().SetEquals(expected), "Army tier grouping wrong");
                int target = map.Replacement(id, id.ToString(), map.IsEarlySource(id));
                Assert(map.Members(target).All(t => ProgressionRules.Allows(map.ProgressionSource(id), t)), "Army grouping allows softlock");
            }
            Assert(!map.TryFindSource(NPCID.DD2SkeletonT1, out _), "Skeleton helper restored");
        }
        foreach (bool special in new[] { false, true })
        {
            var map = new WorldMapping("mimics", false, new int[] { NPCID.Zombie, NPCID.Mimic, NPCID.BigMimicJungle }, celebration: special);
            Assert(map.TryFindSource(NPCID.BigMimicJungle, out _) == special, "Jungle Mimic special seed gate");
            Assert(!map.EarlySources.Contains(NPCID.BigMimicJungle), "Jungle Mimic entered early pool");
            Assert(BossRules.IsMiniBoss(NPCID.BigMimicJungle), "Jungle Mimic missing miniboss rules");
        }
    }

    /// <summary>
    /// Checks independent encounter-limit toggles, gold-family coverage and exact weighting, and tooltip wrapping/IL validity.
    /// </summary>
    private static void TestNewSettings()
    {
        var config = new RandomizerConfig();
        Assert(config.LimitBosses && config.GroupGoldCritters, "New settings must default on");
        var eye = new NPC { type = NPCID.EyeofCthulhu, boss = true, active = true };
        var mimic = new NPC { type = NPCID.BigMimicHallow, active = true };
        foreach (bool bosses in new[] { false, true })
        foreach (bool minis in new[] { false, true })
        {
            Assert(BossRules.AlreadyPresent(eye, null, [eye], _ => false, minis, bosses) == bosses, "Boss toggle depends on miniboss toggle");
            Assert(BossRules.AlreadyPresent(mimic, null, [mimic], _ => false, minis, bosses) == minis, "Miniboss toggle depends on boss toggle");
        }
        int[] ids = Enumerable.Range(1, NPCID.Count - 1).Where(id => GoldCritters.Family(id) != 0).ToArray();
        Assert(NPCID.Sets.GoldCrittersCollection.All(id => ids.Contains(id)), "Missing gold critter family");
        foreach (bool banners in new[] { false, true })
        foreach (bool separate in new[] { false, true })
        {
            var map = new WorldMapping("gold", false, ids,
                banners ? ids.ToDictionary(id => id, Item.NPCtoBanner) : null,
                separateProgressionPools: separate, groupGoldCritters: true);
            foreach (var family in ids.GroupBy(GoldCritters.Family))
            {
                int normal = family.First(id => !GoldCritters.IsGold(id));
                Assert(map.Members(normal).ToHashSet().SetEquals(family), "Critter family split or merged");
                Assert(map.TryFindSource(normal, out int source), "Critter family has no inverse source");
                foreach (int member in family)
                    Assert(map.TryFindSource(member, out int other) && source == other, "Gold source hint differs");
                // Enumerate the vanilla rarity roll; exactly one selects gold before luck adjustments.
                int goldCount = 0;
                for (int roll = 0; roll < NPC.goldCritterChance; roll++)
                {
                    int chosen = GoldCritters.Pick(map.Members(normal), max => max == NPC.goldCritterChance ? roll : 0);
                    if (GoldCritters.IsGold(chosen)) goldCount++;
                    Assert(family.Contains(chosen), "Wrong species in critter selection");
                }
                Assert(goldCount == 1, "Gold probability differs from vanilla");
            }
            var ungrouped = new WorldMapping("gold", false, ids,
                banners ? ids.ToDictionary(id => id, Item.NPCtoBanner) : null,
                separateProgressionPools: separate, groupGoldCritters: false);
            Assert(!ungrouped.IsGoldCritterGroup(NPCID.Bunny), "Gold grouping toggle ignored");
        }
        // Measure in characters for a deterministic wrapping test, then separately validate the actual UI patch.
        string wrapped = ConfigTooltips.Wrap("One two three four five six\nNew paragraph", 12, s => s.Length);
        Assert(wrapped.Split('\n').All(line => line.Length <= 12), "Tooltip exceeds width");
        Assert(wrapped.Replace('\n', ' ') == "One two three four five six New paragraph", "Wrapping loses words");
        MethodInfo draw = ConfigTooltips.DrawMethod();
        using var copy = new DynamicMethodDefinition(draw);
        using var context = new ILContext(copy.Definition);
        ConfigTooltips.Patch(context);
        Type[] signature = new[] { draw.DeclaringType! }.Concat(draw.GetParameters().Select(p => p.ParameterType)).Append(draw.ReturnType).ToArray();
        RuntimeHelpers.PrepareDelegate(copy.Generate().CreateDelegate(Expression.GetDelegateType(signature)));
        Assert(true, "Tooltip patch JIT");
    }

    /// <summary>
    /// Validates announcement-patch IL, the boss-announcement truth table, and concise grouped names.
    /// </summary>
    private static void TestSpawnMessages()
    {
        foreach (string name in new[] { "SpawnBoss", "SpawnOnPlayer", "SpawnSkeletron", "NewNPC" })
        {
            MethodInfo method = typeof(NPC).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;
            using var copy = new DynamicMethodDefinition(method);
            using var context = new ILContext(copy.Definition);
            SpawnMessages.PatchAnnouncements(context, name);
            MethodInfo generated = copy.Generate();
            Type[] signature = method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType).ToArray();
            RuntimeHelpers.PrepareDelegate(generated.CreateDelegate(Expression.GetDelegateType(signature)));
            Assert(true, "Announcement patch failed JIT: " + name);
            Console.WriteLine("Patched spawn announcement compiled: " + name);
        }
        // Cover all source/target boss combinations independently of IL patch validation.
        Assert(SpawnMessages.ShouldAnnounce(true, false), "Boss to ordinary enemy must announce replacement");
        Assert(SpawnMessages.ShouldAnnounce(false, true), "Ordinary enemy to boss must announce");
        Assert(SpawnMessages.ShouldAnnounce(true, true), "Boss to boss must announce");
        Assert(!SpawnMessages.ShouldAnnounce(false, false), "Ordinary replacements spam announcements");
        int[] ids = [NPCID.Zombie, NPCID.BaldZombie, NPCID.SmallZombie, NPCID.Wraith];
        var map = new WorldMapping("short names", false, ids,
            ids.ToDictionary(id => id, id => id == NPCID.Wraith ? 2 : 1));
        foreach (int id in ids.Take(3))
            Assert(SpawnMessages.NameId(map, id) == NPCID.Zombie, "Zombie variants do not share short name");
    }

    /// <summary>
    /// Checks nearest-tile/floating anchors, synchronization, world bounds, slot reuse, and untouched non-vine AI.
    /// </summary>
    private static void TestVineAnchors()
    {
        foreach (int id in new[] { NPCID.ManEater, NPCID.Snatcher, NPCID.Clinger,
                     NPCID.AngryTrapper, NPCID.FungiBulb, NPCID.GiantFungiBulb })
        {
            var npc = new NPC { type = id, aiStyle = NPCAIStyleID.ManEater, width = 32, height = 32,
                position = new Microsoft.Xna.Framework.Vector2(1600, 1600) };
            VineSpawn.Initialize(npc, 500, 200, (x, y) => x == 103 && y == 101 || x == 105 && y == 101);
            Assert(npc.ai[0] == 103 && npc.ai[1] == 101, "Vine did not select nearest local tile");
            Assert(!VineSpawn.AnchorExists(false, npc), "Tile-anchored vine ignores destroyed anchor");
            VineSpawn.Initialize(npc, 500, 200, (_, _) => false);
            Assert(npc.ai[0] == 101 && npc.ai[1] == 101, "Floating vine anchored away from head");
            Assert(VineSpawn.AnchorExists(false, npc), "Floating replacement despawns without terrain");
            // Round-trip the extra-AI flag through the real serialization hooks without starting a network session.
            using (var stream = new System.IO.MemoryStream())
            {
                var hook = new RandomizedNPC();
                var client = new NPC();
                using var writer = new System.IO.BinaryWriter(stream, System.Text.Encoding.UTF8, true);
                using var reader = new System.IO.BinaryReader(stream, System.Text.Encoding.UTF8, true);
                hook.SendExtraAI(npc, null, writer);
                stream.Position = 0;
                hook.ReceiveExtraAI(client, null, reader);
                Assert(VineSpawn.AnchorExists(false, client), "Client lost floating anchor");
                VineSpawn.Forget(npc);
                stream.Position = 0;
                hook.SendExtraAI(npc, null, writer);
                stream.Position = 0;
                hook.ReceiveExtraAI(client, null, reader);
                Assert(!VineSpawn.AnchorExists(false, client), "Client retained stale floating anchor");
            }
            VineSpawn.Forget(npc);
            Assert(!VineSpawn.AnchorExists(false, npc), "Reused NPC retained floating anchor");
            // An extreme position must clamp safely before the injected terrain callback is ever queried.
            npc.position = new Microsoft.Xna.Framework.Vector2(999999, 999999);
            VineSpawn.Initialize(npc, 500, 200, (x, y) =>
            {
                Assert(x > 0 && x < 499 && y > 0 && y < 199, "Vine anchor searched outside world");
                return false;
            });
            Assert(npc.ai[0] == 498 && npc.ai[1] == 198, "Vine anchor not clamped to world dimensions");
        }
        var other = new NPC { type = NPCID.BlueSlime, aiStyle = NPCAIStyleID.Slime };
        other.ai[0] = 7;
        VineSpawn.Initialize(other, 500, 200, (_, _) => throw new Exception("Non-vine scanned terrain"));
        Assert(other.ai[0] == 7, "Non-vine AI changed");
        VineSpawn.Reset();
    }

    /// <summary>
    /// Ensures form changes preserve spawn/encounter state while genuine new spawns in reused slots still randomize.
    /// </summary>
    private static void TestFormTransitions()
    {
        var source = new Terraria.DataStructures.EntitySource_Misc("transition test");
        // Ground/wall spiders, sand spiders, Truffle Worm, and other vanilla
        // transformations must not become new mapping events.
        foreach (var (first, second) in new[] { (239, 240), (164, 165), (163, 238),
                     (236, 237), (530, 531), (374, 375), (195, 196), (390, 382), (406, 405), (428, 427),
                     (602, 603), (608, 609), (362, 363), (364, 365), (55, 230), (592, 593) })
        {
            var npc = new NPC { type = first, netID = first };
            for (int repeat = 0; repeat < 4; repeat++)
            {
                int form = repeat % 2 == 0 ? second : first;
                RandomizedNPC.DuringTransformation(npc, () =>
                {
                    npc.type = npc.netID = form;
                    RandomizedNPC.DuringTransformation(npc, () => RandomizedNPC.RegisterSpawn(npc, source));
                    RandomizedNPC.RegisterSpawn(npc, source);
                });
                Assert(!RandomizedNPC.WillRandomize(npc), $"Form transition {first}/{second} rerolled");
            }
            // Genuine spawn in the reused slot still randomizes.
            npc.type = npc.netID = NPCID.BlueSlime;
            RandomizedNPC.RegisterSpawn(npc, source);
            Assert(RandomizedNPC.WillRandomize(npc), "Transformation guard leaked to later spawn");
            RandomizedNPC.DuringTransformation(npc, () => RandomizedNPC.RegisterSpawn(npc, new DirectNpcSpawnSource()));
            Assert(RandomizedNPC.WillRandomize(npc), "Pre-AI transformation erased initial spawn intent");
        }
        foreach (bool grouped in new[] { false, true })
        foreach (bool separate in new[] { false, true })
        {
            int[] ids = [374, 375, NPCID.Wraith, NPCID.DungeonSpirit];
            var map = new WorldMapping("worm forms", false, ids,
                grouped ? ids.ToDictionary(id => id, _ => 123) : null, separateProgressionPools: separate);
            Assert(map.Members(374).SequenceEqual(new[] { 374, 375 }), "Truffle forms split or merged with unrelated banner");
            Assert(map.Replacement(374, "374", false) == map.Replacement(375, "375", false), "Truffle form mappings differ");
            Assert(map.TryFindSource(374, out int a) && map.TryFindSource(375, out int b) && a == b, "Truffle hints differ");
            Assert(!map.TryFindSource(NPCID.DungeonSpirit, out _), "Dungeon Spirit still mapped");
        }
        RandomizedNPC.ClearPendingSpawns();
    }

    /// <summary>
    /// Verifies mandatory Scutlix pairing, transformed-rider credit, helper exclusions, and retained independent species.
    /// </summary>
    private static void TestExclusiveHelpers()
    {
        foreach (bool grouped in new[] { false, true })
        foreach (bool twins in new[] { false, true })
        foreach (bool separate in new[] { false, true })
        {
            int[] ids = [390, 391, NPCID.Wraith];
            var map = new WorldMapping("scutlix", false, ids,
                grouped ? ids.ToDictionary(id => id, _ => 123) : null,
                combineTwins: twins, separateProgressionPools: separate);
            Assert(map.IsScutlixGroup(390) && map.IsScutlixGroup(391), "Scutlix grouping depends on setting");
            Assert(map.Members(390).SequenceEqual(new[] { 390, 391 }), "Scutlix grouped with unrelated banner");
            Assert(map.Replacement(390, "390", false) == map.Replacement(391, "391", false), "Separate Scutlix source mappings");
            Assert(map.TryFindSource(390, out int first) && map.TryFindSource(391, out int second) && first == second, "Separate Scutlix hints");
            Assert(MappingExport.Render(map, ids, id => id.ToString(), "").Contains("Scutlix and rider (both)"), "Export calls encounter a random variant");
        }
        foreach (bool riderFirst in new[] { false, true })
        {
            var rider = new NPC { type = NPCID.ScutlixRider, netID = NPCID.ScutlixRider, active = true, life = 100, whoAmI = 3 };
            var mount = new NPC { type = NPCID.Scutlix, netID = NPCID.Scutlix, active = true, life = 100, whoAmI = 4 };
            RandomizedNPC.RegisterSpawn(mount, new Terraria.DataStructures.EntitySource_Parent(rider), preserveChildren: false);
            Assert(!RandomizedNPC.WillRandomize(mount), "Mount randomized with preservation disabled");
            Assert(rider.ai[0] == 4 && rider.localAI[0] == 1, "Rider mount link not initialized");
            NPC first = riderFirst ? rider : mount;
            NPC last = riderFirst ? mount : rider;
            first.life = 0;
            Assert(!TwinsEncounter.CanCredit(first), "Partial Scutlix encounter credited");
            first.active = false;
            if (!riderFirst)
            {
                rider.type = NPCID.RayGunner;
                rider.netID = NPCID.RayGunner;
                Assert(TwinsEncounter.CreditTarget(rider) == NPCID.ScutlixRider, "Dismounted rider lost encounter credit identity");
            }
            last.life = 0;
            Assert(TwinsEncounter.CanCredit(last), "Completed Scutlix encounter not credited");
            Assert(!TwinsEncounter.CanCredit(first), "Scutlix credited twice");
            TwinsEncounter.Forget(rider);
            Assert(TwinsEncounter.CreditTarget(rider) == rider.netID, "Reused slot retained encounter identity");
        }
        // Pin explicit helper IDs so accidental changes to catalog grouping cannot make them standalone targets.
        int[] helpers = [5, 535, 139, 372, 373, 658, 659, 660, 619, 478, 479,
            566, 567, 662, 472, 378, 387, 410, 406, 428, 288];
        int[] parents = [NPCID.Mothron, NPCID.GoblinSummoner, NPCID.Clown, NPCID.StardustSpiderBig,
            NPCID.BlueSlime, NPCID.Bee, NPCID.BeeSmall, NPCID.Flocko, NPCID.DD2WyvernT3];
        foreach (int id in helpers)
        {
            var npc = new NPC { type = id, netID = id };
            Assert(!NpcCatalog.CanReplace(npc), $"Exclusive helper {id} eligible");
            RandomizedNPC.RegisterSpawn(npc, new Terraria.DataStructures.EntitySource_Misc("test"), preserveChildren: false);
            Assert(!RandomizedNPC.WillRandomize(npc), $"Exclusive helper {id} randomized with toggle off");
        }
        foreach (bool grouped in new[] { false, true })
        foreach (bool separate in new[] { false, true })
        {
            int[] ids = helpers.Concat(parents).ToArray();
            var map = new WorldMapping("helper audit", false, ids,
                grouped ? ids.ToDictionary(id => id, Item.NPCtoBanner) : null,
                separateProgressionPools: separate);
            Assert(!map.AllSources.SelectMany(id => map.Members(id)).Intersect(helpers).Any(), "Helper leaked into banner groups");
            foreach (int parent in parents)
                Assert(map.TryFindSource(parent, out _), $"Independent NPC {parent} removed");
            foreach (int helper in helpers)
                Assert(!map.TryFindSource(helper, out _), "Helper has inverse mapping");
        }
        RandomizedNPC.ClearPendingSpawns();
    }

    /// <summary>
    /// Checks NPC/projectile child provenance, encounter-controller exceptions, miniboss limits, army mana, and shimmer indices.
    /// </summary>
    private static void TestSpawnBehavior()
    {
        var config = new RandomizerConfig();
        Assert(config.PreserveNpcChildren && config.LimitMiniBosses, "New settings must default on");
        // Compare the same child species under natural and parent sources; provenance, not species alone, controls preservation.
        var natural = new Terraria.DataStructures.EntitySource_Misc("natural test");
        foreach (int parentType in new int[] { NPCID.KingSlime, NPCID.QueenSlimeBoss, NPCID.TheDestroyer,
            NPCID.QueenBee, NPCID.DukeFishron, NPCID.PirateShip, NPCID.DD2Betsy,
            NPCID.MotherSlime, NPCID.CorruptSlime, NPCID.DD2DarkMageT1, NPCID.Mothron })
        {
            var parent = new NPC { type = parentType };
            var source = new Terraria.DataStructures.EntitySource_Parent(parent);
            var child = new NPC { type = NPCID.BlueSlime, netID = NPCID.BlueSlime };
            RandomizedNPC.RegisterSpawn(child, source);
            Assert(!RandomizedNPC.WillRandomize(child), "NPC child randomized with preservation enabled");
            RandomizedNPC.RegisterSpawn(child, source, preserveChildren: false);
            Assert(RandomizedNPC.WillRandomize(child), "NPC child setting cannot be disabled");
            RandomizedNPC.RegisterSpawn(child, natural);
            Assert(RandomizedNPC.WillRandomize(child), "Reused NPC slot inherited child exemption");
            // Exercise a two-projectile ancestry chain, then reuse the last slot to ensure its exemption is cleared.
            var projectile = new Projectile();
            var segment = new Projectile();
            NpcChildProjectiles.Register(projectile, source);
            NpcChildProjectiles.Register(segment, new Terraria.DataStructures.EntitySource_Parent(projectile));
            var chainSource = new Terraria.DataStructures.EntitySource_Parent(segment);
            Assert(NpcChildProjectiles.PreserveSpawn(NPCID.Sharkron, chainSource, true), "Projectile ancestry lost");
            Assert(!NpcChildProjectiles.PreserveSpawn(NPCID.Sharkron, chainSource, false), "Projectile exemption ignored toggle");
            NpcChildProjectiles.Register(segment, natural);
            Assert(!NpcChildProjectiles.PreserveSpawn(NPCID.Sharkron, chainSource, true), "Reused projectile retained ancestry");
        }
        // Event infrastructure is a parent in Terraria terms but must not exempt its ordinary enemy roster.
        foreach (int controller in new int[] { NPCID.CultistTablet, NPCID.DD2LanePortal, NPCID.DD2EterniaCrystal,
            NPCID.LunarTowerSolar, NPCID.LunarTowerVortex, NPCID.LunarTowerNebula, NPCID.LunarTowerStardust })
            Assert(!NpcChildProjectiles.PreserveSpawn(NPCID.CultistBoss,
                new Terraria.DataStructures.EntitySource_Parent(new NPC { type = controller }), true),
                "Event controller suppressed encounter randomization");
        var bossSource = new Terraria.DataStructures.EntitySource_BossSpawn(null);
        Assert(NpcChildProjectiles.PreserveSpawn(NPCID.PirateGhost, bossSource, true), "Captain ghost special source missed");
        Assert(!NpcChildProjectiles.PreserveSpawn(NPCID.KingSlime, bossSource, true), "Normal boss summon suppressed");
        RandomizedNPC.ClearPendingSpawns();

        foreach (int id in Enumerable.Range(1, NPCID.Count - 1).Where(BossRules.IsMiniBoss))
        {
            var candidate = new NPC { type = id, active = true, boss = true };
            var existing = new NPC { type = id, active = true };
            Assert(BossRules.AlreadyPresent(candidate, candidate, [existing], _ => false, true), "Duplicate miniboss allowed");
            Assert(!BossRules.AlreadyPresent(candidate, candidate, [existing], _ => false, false), "Miniboss toggle ignored");
            Assert(!BossRules.AlreadyPresent(candidate, candidate, [existing], _ => true, true), "Pending miniboss wrongly blocks");
            existing.active = false;
            Assert(!BossRules.AlreadyPresent(candidate, candidate, [existing], _ => false, true), "Dead miniboss blocks respawn");
        }
        foreach (int id in new int[] { NPCID.EyeofCthulhu, NPCID.Plantera, NPCID.DD2Betsy })
        {
            var boss = new NPC { type = id, boss = true };
            Assert(BossRules.AlreadyPresent(boss, boss, [new NPC { type = id, active = true }], _ => false, false),
                "Full boss cap disabled with miniboss cap");
        }
        // Different tier IDs still represent one miniboss family for the duplicate limit.
        var ogre = new NPC { type = NPCID.DD2OgreT3 };
        Assert(BossRules.AlreadyPresent(ogre, ogre, [new NPC { type = NPCID.DD2OgreT2, active = true }], _ => false),
            "Different tiers bypass miniboss family limit");

        foreach (int id in Enumerable.Range(NPCID.DD2Betsy, NPCID.DD2LightningBugT3 - NPCID.DD2Betsy + 1))
        {
            Assert(MappedEventCredit.CanDropMappedMana(id, true, false, true), "DD2 source missed mana eligibility");
            Assert(!MappedEventCredit.CanDropMappedMana(id, false, false, true), "Mana allowed outside event");
            Assert(!MappedEventCredit.CanDropMappedMana(id, true, true, true), "Mapped mana duplicates legacy drop");
            Assert(!MappedEventCredit.CanDropMappedMana(id, true, false, false), "Paired encounter could drop twice");
        }
        foreach (int id in new int[] { NPCID.DD2Bartender, NPCID.DD2LanePortal, NPCID.DD2EterniaCrystal, NPCID.Zombie })
            Assert(!MappedEventCredit.CanDropMappedMana(id, true, false, true), "Non-event source awards mana");

        foreach (int id in Enumerable.Range(1, NPCID.Count - 1))
        {
            var npc = new NPC { type = id, townNpcVariationIndex = 4 };
            // Unsupported species must retain their existing variation index rather than being forced into town appearance values.
            bool supported = NPCID.Sets.ShimmerTownTransform[id];
            TownNpcAppearance.SetVariant(npc, true);
            Assert(npc.townNpcVariationIndex == (supported ? 1 : 4), "Shimmer assignment corrupted unsupported NPC variant");
            TownNpcAppearance.SetVariant(npc, false);
            Assert(npc.townNpcVariationIndex == (supported ? 0 : 4), "Normal appearance cannot replace shimmer appearance");
        }
    }

    /// <summary>
    /// Guards against native pre-Hardmode evil variants being classified or grouped into the late pool.
    /// </summary>
    private static void TestNativeEvilVariants()
    {
        int[] crimsonEarly = [NPCID.Crimera, NPCID.BigCrimera, NPCID.LittleCrimera,
            NPCID.FaceMonster, NPCID.BloodCrawler, NPCID.BloodCrawlerWall];
        int[] corruptionEarly = [NPCID.EaterofSouls, NPCID.BigEater, NPCID.LittleEater,
            NPCID.DevourerHead];
        int[] ids = crimsonEarly.Concat(corruptionEarly).Concat(new int[]
            { NPCID.BlueSlime, NPCID.Zombie, NPCID.Derpling, NPCID.MartianWalker }).ToArray();
        var banners = new Dictionary<int, int>
        {
            [NPCID.Crimera] = 1, [NPCID.BigCrimera] = 1, [NPCID.LittleCrimera] = 1,
            [NPCID.EaterofSouls] = 2, [NPCID.BigEater] = 2, [NPCID.LittleEater] = 2
        };
        foreach (bool crimson in new[] { false, true })
        foreach (bool grouped in new[] { false, true })
        for (int seed = 0; seed < 100; seed++)
        {
            var map = new WorldMapping(seed.ToString(), crimson, ids, grouped ? banners : null);
            foreach (int source in crimson ? crimsonEarly : corruptionEarly)
            {
                Assert(map.IsEarlySource(source), "Native pre-Hardmode evil variant assigned to Hardmode");
                int target = map.Replacement(source, "", true);
                Assert(map.Members(target).All(map.IsEarlySource), "Native evil spawned a Hardmode replacement");
                Assert(target == map.Replacement(source, "", false), "Native evil mapping changed on progression");
            }
            foreach (int source in crimson ? corruptionEarly : crimsonEarly)
                Assert(!map.IsEarlySource(source), "Opposite evil must remain in the Hardmode pool");
        }
    }

    /// <summary>
    /// Checks merged-pool coverage and grouping while retaining progression restrictions and deterministic reloads.
    /// </summary>
    private static void TestCombinedPools()
    {
        Assert(new RandomizerConfig().SeparateProgressionPools, "Separate pools must default on");
        int[] ids = [NPCID.BlueSlime, NPCID.Zombie, NPCID.Wraith, NPCID.EaterofSouls,
            NPCID.Crimera, NPCID.SkeletronHead, NPCID.AngryBones, NPCID.TheDestroyer,
            NPCID.Plantera, NPCID.Golem, NPCID.Lihzahrd, NPCID.VortexRifleman,
            NPCID.Retinazer, NPCID.Spazmatism, NPCID.GoldenSlime];
        // Synthetic shared banner checks that combined mode also merges across tiers.
        var banners = new Dictionary<int, int> { [NPCID.Zombie] = 1, [NPCID.Wraith] = 1 };
        bool crossedTiers = false;
        foreach (bool crimson in new[] { false, true })
        foreach (bool grouped in new[] { false, true })
        foreach (bool twins in new[] { false, true })
        for (int seed = 0; seed < 30; seed++)
        {
            var map = new WorldMapping(seed.ToString(), crimson, ids, grouped ? banners : null,
                combineTwins: twins, separateProgressionPools: false);
            var reload = new WorldMapping(seed.ToString(), crimson, ids.Reverse(), grouped ? banners : null,
                combineTwins: twins, separateProgressionPools: false);
            var targets = new HashSet<int>();
            foreach (int source in map.AllSources)
            {
                int target = map.Replacement(source, "", true);
                Assert(targets.Add(target), "Combined targets duplicated");
                Assert(target == reload.Replacement(source, "", false), "Combined map changed on progression/reload");
                Assert(map.TryFindSource(target, out int inverse) && inverse == source, "Combined hint inverse broken");
                Assert(map.Members(source).All(s => map.Members(target).All(t => ProgressionRules.Allows(s, t))),
                    "Combined map bypassed boss progression safeguards");
                crossedTiers |= map.IsEarlySource(source) != map.IsEarlySource(target);
            }
            Assert(targets.SetEquals(map.AllSources), "Combined mapping incomplete");
            Assert(map.IsTwinsGroup(NPCID.Retinazer) == twins, "Combined Twins setting ignored");
            Assert(map.Members(NPCID.Zombie).Contains(NPCID.Wraith) == grouped, "Combined banner grouping failed");
            Assert(map.Replacement(10000, "Test/Modded", true) == map.Replacement(10000, "Test/Modded", false),
                "Combined fallback changed with progression");
            string report = MappingExport.Render(map, ids, id => $"NPC [{id}]", "test");
            Assert(report.Contains("COMBINED POOL") && !report.Contains("PRE-HARDMODE")
                && !report.Contains("HARDMODE (also"), "Combined export has misleading tiers");
        }
        Assert(crossedTiers, "Combined pool never mixed progression tiers");
    }

    /// <summary>
    /// Checks that opposite-evil species enter the late pool without altering the native early permutation.
    /// </summary>
    private static void TestOppositeEvilHardmode()
    {
        Assert(NpcCatalog.CanReplace(new NPC { type = NPCID.SeekerHead }), "World Feeder incorrectly excluded as unused");
        Assert(!NpcCatalog.CanReplace(new NPC { type = NPCID.SeekerBody })
            && !NpcCatalog.CanReplace(new NPC { type = NPCID.SeekerTail }), "Detached World Feeder parts");
        Assert(BossRules.IsBurrowingWorm(NPCID.SeekerHead), "World Feeder missing surface retreat fix");
        int[] ids = [NPCID.Zombie, NPCID.BlueSlime, NPCID.Wraith, NPCID.Mimic,
            NPCID.SeekerHead,
            NPCID.EaterofSouls, NPCID.BigEater, NPCID.LittleEater, NPCID.EaterofWorldsHead,
            NPCID.CorruptBunny, NPCID.CorruptGoldfish, NPCID.CorruptPenguin, NPCID.DevourerHead,
            NPCID.Crimera, NPCID.BigCrimera, NPCID.LittleCrimera, NPCID.BrainofCthulhu,
            NPCID.FaceMonster, NPCID.BloodCrawler, NPCID.BloodCrawlerWall,
            NPCID.CrimsonBunny, NPCID.CrimsonGoldfish, NPCID.CrimsonPenguin,
            NPCID.CorruptSlime, NPCID.BigMimicCorruption, NPCID.Crimslime, NPCID.BigCrimslime,
            NPCID.LittleCrimslime, NPCID.BigMimicCrimson];
        var banners = ids.ToDictionary(id => id, id => Item.NPCtoBanner(id switch
        {
            NPCID.BigEater or NPCID.LittleEater => NPCID.EaterofSouls,
            NPCID.BigCrimera or NPCID.LittleCrimera => NPCID.Crimera,
            NPCID.BigCrimslime or NPCID.LittleCrimslime => NPCID.Crimslime,
            _ => id
        }));
        foreach (bool crimson in new[] { false, true })
        foreach (bool grouped in new[] { false, true })
        for (int seed = 0; seed < 50; seed++)
        {
            var map = new WorldMapping(seed.ToString(), crimson, ids, grouped ? banners : null);
            // The original native-evil pre-Hardmode shuffle must remain unchanged.
            var oldEarly = new WorldMapping(seed.ToString(), crimson,
                ids.Where(id => NpcCatalog.AllowedInWorld(id, crimson)), grouped ? banners : null);
            foreach (int source in map.EarlySources)
                Assert(map.Replacement(source, "", true) == oldEarly.Replacement(source, "", true), "Pre-Hardmode mapping changed");
            foreach (int id in ids.Where(id => !NpcCatalog.AllowedInWorld(id, crimson)))
            {
                Assert(!map.IsEarlySource(id), "Opposite-evil variant not promoted to Hardmode");
                Assert(map.TryFindSource(id, out int source) && !map.IsEarlySource(source), "Opposite-evil target missing from Hardmode");
                int target = map.Replacement(id, "", true);
                Assert(!map.IsEarlySource(target) && target == map.Replacement(id, "", false), "Forced source or progression changed seeded alternative");
            }
            string report = MappingExport.Render(map, ids, id => $"NPC [{id}]", "test");
            string earlySection = report.Split("HARDMODE (also")[0];
            foreach (int id in ids.Where(id => !NpcCatalog.AllowedInWorld(id, crimson)))
            {
                Assert(!earlySection.Contains($"NPC [{id}]"), "Export placed opposite evil in early tier");
                Assert(!report.Contains($"NPC [{id}] (fallback source)"), "Opposite evil still marked excluded in export");
            }
        }
    }

    /// <summary>
    /// Exercises seed stability, tier isolation, inverse lookup, target coverage, and forced-source fallback behavior.
    /// </summary>
    private static void TestWorldMappings()
    {
        int[] candidates = [NPCID.Bunny, NPCID.BlueSlime, NPCID.Zombie, NPCID.ZombieXmas,
            NPCID.ZombieDoctor, NPCID.BunnyXmas, NPCID.EaterofWorldsHead, NPCID.BigEater,
            NPCID.BrainofCthulhu, NPCID.LittleCrimera, NPCID.Wraith, NPCID.TheDestroyer,
            NPCID.ZombieElf, NPCID.HoppinJack, NPCID.Pumpking, NPCID.BigCrimslime,
            NPCID.BigMimicCorruption, NPCID.BigMimicCrimson, NPCID.DarkMummy, NPCID.BloodMummy];
        var signatures = new HashSet<string>();
        for (int seed = 0; seed < 100; seed++)
        foreach (bool crimson in new[] { false, true })
        {
            var map = new WorldMapping(seed.ToString(), crimson, candidates);
            var reloaded = new WorldMapping(seed.ToString(), crimson, candidates.Reverse());
            var expected = candidates.ToHashSet();
            int[] sources = map.EarlySources.Concat(map.LateSources).ToArray();
            Assert(expected.SetEquals(sources), "World pool missing eligible species");
            var results = new HashSet<int>();
            foreach (int source in sources)
            {
                bool early = map.IsEarlySource(source);
                int target = map.Replacement(source, source.ToString(), early);
                Assert(results.Add(target), "Duplicate target in permutation");
                Assert(target != source, "Shuffle left species unchanged");
                Assert(map.IsEarlySource(target) == early, "Shuffle crossed world-specific progression tiers");
                Assert(!early || NpcCatalog.AllowedInWorld(target, crimson), "Opposite evil leaked into pre-Hardmode");
                Assert(target == reloaded.Replacement(source, source.ToString(), early), "Reload/order changed mapping");
                Assert(map.TryFindSource(target, out int inverse) && inverse == source, "Hint returned forward mapping instead of inverse source");
                // Even an early-world hint cannot change a known Hardmode mapping.
                Assert(target == map.Replacement(source, source.ToString(), !early), "Known mapping changed with progression");
            }
            Assert(results.SetEquals(expected), "An eligible target is unreachable in mapping");
            signatures.Add(string.Join(",", sources.Select(id => map.Replacement(id, id.ToString(), NpcCatalog.IsPreHardmode(id)))));

            int wrongEvil = crimson ? NPCID.BigMimicCorruption : NPCID.BigMimicCrimson;
            Assert(map.TryFindSource(wrongEvil, out int evilSource) && !map.IsEarlySource(evilSource), "Opposite-evil hint missing Hardmode source");
            int forcedTarget = map.Replacement(wrongEvil, wrongEvil.ToString(), false);
            Assert(!map.IsEarlySource(forcedTarget),
                "Forced opposite-evil Hardmode source did not get an allowed Hardmode target");
            int modTarget = map.Replacement(10000, "TestMod/Enemy", true);
            Assert(NpcCatalog.IsPreHardmode(modTarget) && NpcCatalog.AllowedInWorld(modTarget, crimson),
                "Modded fallback violates early-world restrictions");
        }
        Assert(signatures.Count > 100, "World seed/evil not reflected in mappings");
        Assert(SeededSelection.Shuffle("seed", "test", []).Count == 0, "Empty shuffle");
        Assert(SeededSelection.Shuffle("seed", "test", [1])[1] == 1, "Single-entry shuffle");

        foreach (int id in new[] { NPCID.EaterofSouls, NPCID.BigEater, NPCID.LittleEater,
                     NPCID.CorruptSlime, NPCID.Slimeling, NPCID.PigronCorruption, NPCID.SandsharkCorrupt,
                     NPCID.CorruptBunny, NPCID.CorruptGoldfish, NPCID.DesertGhoulCorruption })
            Assert(NpcCatalog.AllowedInWorld(id, false) && !NpcCatalog.AllowedInWorld(id, true), $"Corruption classification {id}");
        foreach (int id in new[] { NPCID.Crimera, NPCID.BigCrimera, NPCID.LittleCrimera,
                     NPCID.BigCrimslime, NPCID.LittleCrimslime, NPCID.PigronCrimson, NPCID.SandsharkCrimson,
                     NPCID.CrimsonBunny, NPCID.CrimsonGoldfish, NPCID.DesertGhoulCrimson })
            Assert(NpcCatalog.AllowedInWorld(id, true) && !NpcCatalog.AllowedInWorld(id, false), $"Crimson classification {id}");
    }

    /// <summary>
    /// Checks numeric/name resolution, aliases, ambiguous input, and direct-spawn bypass bookkeeping.
    /// </summary>
    private static void TestCommands()
    {
        NpcNameEntry[] entries = [
            new(3, "Zombie", "Zombie", "Terraria/Zombie"),
            new(-26, "Zombie", "SmallZombie", "Terraria/SmallZombie"),
            new(132, "Zombie", "BaldZombie", "Terraria/BaldZombie"),
            new(4, "Eye of Cthulhu", "EyeofCthulhu", "Terraria/EyeofCthulhu"),
            new(-4, "Pinky", "Pinky", "Terraria/Pinky"),
            new(1000, "Test Creature", "TestCreature", "ExampleMod/TestCreature"),
            new(1001, "Test Creature", "TestCreature", "AnotherMod/TestCreature")
        ];
        foreach (var (query, expected) in new (string, int)[] {
                     ("Zombie", 3), ("zOmBiE", 3), ("3", 3), (" -4 ", -4),
                     ("Eye of Cthulhu", 4), ("\"Eye of Cthulhu\"", 4), ("eyeofcthulhu", 4),
                     ("small zombie", -26), ("cthulhu", 4), ("Terraria/Zombie", 3),
                     ("examplemod/testcreature", 1000), ("Pinky", -4) })
            Assert(NpcNameResolver.TryResolve(query, entries, out int id, out _) && id == expected,
                $"Name/ID resolution failed: {query}");
        foreach (string query in new[] { "", " ", "0", "999999", "unknown NPC", "Test Creature", "zomb" })
            Assert(!NpcNameResolver.TryResolve(query, entries, out _, out string error) && !string.IsNullOrWhiteSpace(error),
                $"Invalid/ambiguous query accepted: {query}");

        var direct = new NPC { type = NPCID.Zombie, netID = NPCID.Zombie };
        var normal = new NPC { type = NPCID.Zombie, netID = NPCID.Zombie };
        RandomizedNPC.RegisterSpawn(direct, new DirectNpcSpawnSource());
        RandomizedNPC.RegisterSpawn(normal, new Terraria.DataStructures.EntitySource_Misc("NaturalSpawnTest"));
        Assert(!RandomizedNPC.WillRandomize(direct), "Direct command spawn still randomized");
        Assert(RandomizedNPC.WillRandomize(normal), "Direct bypass leaked into ordinary spawns");
        direct.type = NPCID.Bunny;
        direct.netID = NPCID.Bunny;
        Assert(!RandomizedNPC.WillRandomize(direct), "Direct bypass lost when defaults change NPC type");
        RandomizedNPC.ClearPendingSpawns();
        Assert(!RandomizedNPC.WillRandomize(normal), "World reset retained pending command data");
        Assert(new SpawnNpcCommand().Type == (CommandType.World | CommandType.Console), "Spawn command routing");
        Assert(new HintCommand().Type == (CommandType.World | CommandType.Console), "Hint command routing");
    }
}

