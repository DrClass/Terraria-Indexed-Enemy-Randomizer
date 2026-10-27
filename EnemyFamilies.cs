using System;
using System.Reflection;
using Mono.Cecil;
using MonoMod.Cil;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace IndexedEnemyRandomizer;

/// <summary>
/// Explicit size/color families, independent of translated names and banners.
/// </summary>
internal static class MiscFamilies
{
    // Gold forms are absent: GoldCritters runs first and uses vanilla rarity.
    internal static int Family(int id) => id switch
    {
        NPCID.Bee or NPCID.BeeSmall => NPCID.Bee,
        NPCID.Scorpion or NPCID.ScorpionBlack => NPCID.ScorpionBlack,
        NPCID.Duck or NPCID.Duck2 or NPCID.DuckWhite or NPCID.DuckWhite2 => NPCID.Duck,
        NPCID.FairyCritterPink or NPCID.FairyCritterGreen or NPCID.FairyCritterBlue => NPCID.FairyCritterPink,
        NPCID.ScarletMacaw or NPCID.BlueMacaw => NPCID.ScarletMacaw,
        NPCID.YellowCockatiel or NPCID.GrayCockatiel => NPCID.YellowCockatiel,
        >= NPCID.BlackDragonfly and <= NPCID.YellowDragonfly => NPCID.BlackDragonfly,
        NPCID.Squirrel or NPCID.SquirrelRed => NPCID.Squirrel,
        NPCID.Penguin or NPCID.PenguinBlack => NPCID.Penguin,
        NPCID.Bird or NPCID.BirdBlue or NPCID.BirdRed => NPCID.Bird,
        _ => 0
    };
}

/// <summary>
/// Keeps critters' movement forms in one mapping entry, even without banners.
/// </summary>
internal static class CritterForms
{
    // These pairs are one creature changing how it moves, not separate species.
    // Keep white ducks separate from brown ducks, and gold fish separate from
    // normal fish unless the optional gold-critter rule combines them first.
    internal static int Family(int id) => id switch
    {
        NPCID.Seagull or NPCID.Seagull2 => NPCID.Seagull,
        NPCID.Grebe or NPCID.Grebe2 => NPCID.Grebe,
        NPCID.Duck or NPCID.Duck2 => NPCID.Duck,
        NPCID.DuckWhite or NPCID.DuckWhite2 => NPCID.DuckWhite,
        NPCID.Goldfish or NPCID.GoldfishWalker => NPCID.Goldfish,
        NPCID.GoldGoldfish or NPCID.GoldGoldfishWalker => NPCID.GoldGoldfish,
        _ => 0
    };
}

/// <summary>
/// Defines Old One's Army species with multiple tiers; Betsy and exclusive helpers do not form tier groups.
/// </summary>
internal static class ArmyFamilies
{
    /// <summary>
    /// Uses the same world milestones as vanilla's choice of army difficulty.
    /// </summary>
    internal static int ProgressionTier(bool hardmode, bool mechDefeated, bool golemDefeated) =>
        !hardmode ? 1 : golemDefeated ? 3 : mechDefeated ? 2 : 1;

    internal static int CurrentTier => ProgressionTier(Main.hardMode, NPC.downedMechBossAny, NPC.downedGolemBoss);

    /// <summary>
    /// Selects the progression-appropriate form; families without tier 1 use their earliest form.
    /// Dark Mages have no tier 2, so they retain tier 1 until tier 3.
    /// </summary>
    internal static int ForTier(int id, int tier)
    {
        int family = Family(id);
        if (family == 0)
            return id;

        tier = Math.Clamp(tier, 1, 3);
        return family switch
        {
            NPCID.DD2GoblinT1 or NPCID.DD2GoblinBomberT1 or NPCID.DD2WyvernT1 or NPCID.DD2JavelinstT1
                => family + tier - 1,
            _ => tier == 3 ? family + 1 : family
        };
    }

    /// <summary>
    /// Returns a species' earliest tier ID as its grouping key, or zero when no tier grouping applies.
    /// </summary>
    internal static int Family(int id) => id switch
    {
        >= NPCID.DD2GoblinT1 and <= NPCID.DD2GoblinT3 => NPCID.DD2GoblinT1,
        >= NPCID.DD2GoblinBomberT1 and <= NPCID.DD2GoblinBomberT3 => NPCID.DD2GoblinBomberT1,
        >= NPCID.DD2WyvernT1 and <= NPCID.DD2WyvernT3 => NPCID.DD2WyvernT1,
        >= NPCID.DD2JavelinstT1 and <= NPCID.DD2JavelinstT3 => NPCID.DD2JavelinstT1,
        NPCID.DD2DarkMageT1 or NPCID.DD2DarkMageT3 => NPCID.DD2DarkMageT1,
        NPCID.DD2WitherBeastT2 or NPCID.DD2WitherBeastT3 => NPCID.DD2WitherBeastT2,
        NPCID.DD2DrakinT2 or NPCID.DD2DrakinT3 => NPCID.DD2DrakinT2,
        NPCID.DD2KoboldWalkerT2 or NPCID.DD2KoboldWalkerT3 => NPCID.DD2KoboldWalkerT2,
        NPCID.DD2KoboldFlyerT2 or NPCID.DD2KoboldFlyerT3 => NPCID.DD2KoboldFlyerT2,
        NPCID.DD2OgreT2 or NPCID.DD2OgreT3 => NPCID.DD2OgreT2,
        _ => 0
    };
}

/// <summary>
/// Lets Crawdads, Giant Shellies, and Salamanders all participate in a world's normal cavern spawn selection.
/// </summary>
public sealed class CavernFamilies : ModSystem
{
    /// <summary>
    /// Chooses one of the three species uniformly, then one of that species' variants uniformly.
    /// </summary>
    internal static int Pick(Func<int, int> next) => next(3) switch
    {
        0 => NPCID.Crawdad + next(2),
        1 => NPCID.GiantShelly + next(2),
        _ => NPCID.Salamander + next(9)
    };

    /// <summary>
    /// Patches the family choice inside the existing vanilla natural-spawn routine.
    /// </summary>
    public override void Load() => MonoModHooks.Modify(
        typeof(NPC).GetMethod("SpawnNPC", BindingFlags.Public | BindingFlags.Static)!, Patch);

    /// <summary>
    /// Replaces the world-restricted cavern-family array result while retaining vanilla spawn conditions and rate.
    /// </summary>
    internal static void Patch(ILContext il)
    {
        var cursor = new ILCursor(il);
        // Find the native family-table lookup, leaving all conditions that decided to spawn a cavern enemy in place.
        if (!cursor.TryGotoNext(MoveType.After, i => i.MatchLdsfld<NPC>("cavernMonsterType"))
            || !cursor.TryGotoNext(MoveType.After, i => i.Operand is MethodReference m
                && m.Name == "Get" && m.DeclaringType.FullName == "System.Int32[0...,0...]"))
            throw new InvalidOperationException("Cannot locate cavern family spawn selection.");
        // Consume the old restricted-family result and replace only that ID with the unrestricted three-family choice.
        cursor.EmitDelegate<Func<int, int>>(_ => Pick(Main.rand.Next));
    }
}
