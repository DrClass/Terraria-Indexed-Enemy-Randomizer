using System;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace EnemyRandomizer;

/// <summary>
/// Patches values read by vanilla AI to prevent time, event, or habitat retreat and environmental enrage.
/// The actual world clock, biome flags, and event state remain untouched.
/// </summary>
internal static class PersistentEnemyAI
{
    /// <summary>
    /// NPC whose vanilla AI is executing on this thread; null disables conditional overrides.
    /// </summary>
    [ThreadStatic]
    private static NPC current;

    /// <summary>
    /// Installs scoped AI hooks and patches retreat-related reads in vanilla AI/helper methods.
    /// </summary>
    internal static void Load()
    {
        On_NPC.VanillaAI += RunVanillaAI;
        On_NPC.ShouldEmpressBeEnraged += NeverEnrageEmpress;
        On_NPC.AI_120_HallowBoss_IsGenuinelyEnraged += EmpressIsNotEnraged;
        // Patch only reads inside vanilla AI and its retreat helpers. Never alter
        // Main's actual clock/event flags, spawning, loot, or event progression.
        foreach (MethodInfo method in typeof(NPC).GetMethods(BindingFlags.Instance |
                     BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (method.Name is "VanillaAI_Inner" or "ShouldRunAway" || method.Name.StartsWith("AI_", StringComparison.Ordinal)
                || method.Name.StartsWith("DespawnEncouragement_", StringComparison.Ordinal))
                MonoModHooks.Modify(method, il => PatchRetreatConditions(il, method.Name));
        }
    }

    /// <summary>
    /// Detaches managed hooks and clears the current context; tModLoader owns IL-hook cleanup.
    /// </summary>
    internal static void Unload()
    {
        On_NPC.VanillaAI -= RunVanillaAI;
        On_NPC.ShouldEmpressBeEnraged -= NeverEnrageEmpress;
        On_NPC.AI_120_HallowBoss_IsGenuinelyEnraged -= EmpressIsNotEnraged;
        current = null;
        // MonoModHooks.Modify hooks are owned and removed by tModLoader on unload.
    }

    /// <summary>
    /// Runs one NPC's AI inside an exception-safe context, restoring any outer context afterward.
    /// Also clears Empress's stored daylight-enrage state while preserving her phase bit.
    /// </summary>
    private static void RunVanillaAI(On_NPC.orig_VanillaAI orig, NPC npc)
    {
        // Clear a previously stored daylight state without resetting phase two.
        if (npc.type == NPCID.HallowBoss && npc.ai[3] is 2f or 3f)
            npc.ai[3] -= 2f;
        // Save the outer context in case another hook runs nested AI; finally restores it on every exit path.
        NPC previous = current;
        current = NpcCatalog.KeepActiveAI(npc) ? npc : null;
        try
        {
            orig(npc);
        }
        finally
        {
            current = previous;
        }
    }

    /// <summary>
    /// Allows nighttime substitution for eligible enemies and selected nighttime critters.
    /// </summary>
    private static bool OverrideTime => current != null &&
        (!NPCID.Sets.CountsAsCritter[current.type] || current.type is NPCID.Owl or NPCID.Firefly or NPCID.LightningBug);

    /// <summary>
    /// Disables the Empress's global daylight-enrage query.
    /// </summary>
    private static bool NeverEnrageEmpress(On_NPC.orig_ShouldEmpressBeEnraged orig) => false;
    /// <summary>
    /// Disables the Empress's per-NPC genuine-enrage query.
    /// </summary>
    private static bool EmpressIsNotEnraged(On_NPC.orig_AI_120_HallowBoss_IsGenuinelyEnraged orig, NPC npc) => false;

    /// <summary>
    /// Checks whether the current AI context belongs to a boss, part, or burrowing worm needing habitat overrides.
    /// </summary>
    private static bool OverrideBiome => BossRules.IgnoreBiome(current);
    /// <summary>
    /// Lists player biome flags whose AI reads may be relaxed for affected encounters.
    /// </summary>
    private static bool HabitatZone(string name) => name is "ZoneJungle" or "ZoneLihzhardTemple"
        or "ZoneCorrupt" or "ZoneCrimson" or "ZoneUndergroundDesert" or "ZoneSnow";

    /// <summary>
    /// Adds delegates after selected field/property reads to substitute AI-only time, event, and habitat values.
    /// Delegates receive the original stack value and retain it when their override does not apply.
    /// </summary>
    internal static void PatchRetreatConditions(ILContext il, string methodName)
    {
        var cursor = new ILCursor(il);
        while (cursor.Next != null)
        {
            // Walk original instructions once; advance first so emitted delegates are not inspected as original game logic.
            Instruction instruction = cursor.Next;
            cursor.Index++;
            if (instruction.OpCode == OpCodes.Ldsfld && instruction.Operand is FieldReference field
                && field.DeclaringType.FullName == typeof(Main).FullName)
            {
                // Each delegate consumes a loaded value and returns its substitute, leaving the surrounding IL stack shape unchanged.
                switch (field.Name)
                {
                    case nameof(Main.dayTime):
                        cursor.EmitDelegate<Func<bool, bool>>(value => OverrideTime ? false : value);
                        break;
                    // Relax depth thresholds for affected creatures; Fishron uses a separate coordinate-based enrage patch.
                    case nameof(Main.worldSurface):
                        cursor.EmitDelegate<Func<double, double>>(value => OverrideBiome && current.type != NPCID.DukeFishron
                            ? -1d : value);
                        break;
                    // Let event creatures believe their own event is active during AI, even after the real event has ended.
                    case nameof(Main.eclipse):
                    case nameof(Main.bloodMoon):
                    case nameof(Main.snowMoon):
                    case nameof(Main.pumpkinMoon):
                        cursor.EmitDelegate<Func<bool, bool>>(value => current != null || value);
                        break;
                    case nameof(Main.invasionType):
                        cursor.EmitDelegate<Func<int, int>>(value => current != null && NPC.GetNPCInvasionGroup(current.type) > 0
                            ? NPC.GetNPCInvasionGroup(current.type) : value);
                        break;
                }
            }
            // Some biome checks are raw fields, while others below are property getters; support both forms.
            else if (instruction.OpCode == OpCodes.Ldfld && instruction.Operand is FieldReference playerField
                     && playerField.DeclaringType.FullName == typeof(Player).FullName && HabitatZone(playerField.Name))
                cursor.EmitDelegate<Func<bool, bool>>(value => OverrideBiome || value);
            else if ((instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
                     && instruction.Operand is MethodReference method)
            {
                if (method.DeclaringType.FullName == typeof(Main).FullName)
                {
                    if (method.Name == nameof(Main.IsItDay))
                        cursor.EmitDelegate<Func<bool, bool>>(value => OverrideTime ? false : value);
                    else if (method.Name == "get_UnderworldLayer")
                        cursor.EmitDelegate<Func<int, int>>(value => OverrideBiome ? 1000000 : value);
                }
                else if (method.DeclaringType.FullName == typeof(Player).FullName
                         && method.Name.StartsWith("get_", StringComparison.Ordinal) && HabitatZone(method.Name[4..]))
                    cursor.EmitDelegate<Func<bool, bool>>(value => OverrideBiome || value);
                else if (method.DeclaringType.FullName == "Terraria.GameContent.Events.DD2Event"
                         && method.Name == "get_EnemiesShouldChasePlayers")
                    cursor.EmitDelegate<Func<bool, bool>>(value => current != null || value);
            }
        }
        // Apply the two specialized patches only to the methods whose layouts they understand.
        if (methodName == "AI_069_DukeFishron")
            DisableFishronEnrage(il);
        if (methodName == "VanillaAI_Inner")
            VineSpawn.Patch(il);
    }

    // Fishron checks raw coordinates instead of ZoneBeach. Locate the local
    // containing that entire condition, rather than changing player positions
    // or world dimensions (both are also used for attacks and movement).
    /// <summary>
    /// Locates Fishron's combined coordinate-based enrage result and forces that local boolean false.
    /// </summary>
    internal static void DisableFishronEnrage(ILContext il)
    {
        var cursor = new ILCursor(il);
        int enrageLocal = -1;
        if (!cursor.TryGotoNext(MoveType.After, i => i.MatchLdcR4(6400f))
            || !cursor.TryGotoNext(MoveType.After, i => i.MatchStloc(out enrageLocal)))
            throw new InvalidOperationException("Cannot locate Duke Fishron's environment-enrage condition in this tModLoader version.");
        // Overwrite only the computed enrage local; attack positions and world dimensions retain their real values.
        cursor.Emit(OpCodes.Ldc_I4_0);
        cursor.Emit(OpCodes.Stloc, enrageLocal);
    }
}
