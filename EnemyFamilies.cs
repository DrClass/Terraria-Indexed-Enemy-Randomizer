using System;
using System.Reflection;
using Mono.Cecil;
using MonoMod.Cil;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace IndexedEnemyRandomizer;

/// <summary>
/// Defines Old One's Army species with multiple tiers; Betsy and exclusive helpers do not form tier groups.
/// </summary>
internal static class ArmyFamilies
{
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
