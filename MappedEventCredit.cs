using System;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Terraria;
using Terraria.GameContent.Events;
using Terraria.ID;
using Terraria.ModLoader;

namespace IndexedEnemyRandomizer;

/// <summary>
/// Makes event accounting follow the original species that maps to a slain replacement.
/// Patches only accounting reads so loot and boss defeat flags still belong to the actual NPC.
/// </summary>
public sealed class MappedEventCredit : ModSystem
{
    /// <summary>
    /// Installs targeted patches for invasion/moon progress, Betsy's wave credit, and Etherian Mana drops.
    /// </summary>
    public override void Load()
    {
        // These methods contain distinct accounting paths; patch each copy without replacing general death behavior.
        foreach (string name in new[] { "checkDead", "CheckProgressFrostMoon", "CheckProgressPumpkinMoon", "VanillaHitEffect", "DoDeathEvents" })
        {
            MethodInfo method = typeof(NPC).GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new MissingMethodException(typeof(NPC).FullName, name);
            MonoModHooks.Modify(method, il => Patch(il, name));
        }
    }

    /// <summary>
    /// Keeps the inverse-mapped source unchanged; in particular, Goblin Scouts receive no invasion-enemy substitution.
    /// </summary>
    internal static int NormalizeEventSource(int source) => source;

    /// <summary>
    /// Recognizes the contiguous vanilla Old One's Army NPC range used for mapped mana eligibility.
    /// </summary>
    internal static bool IsOldOnesArmyEnemy(int type) => type >= NPCID.DD2Betsy && type <= NPCID.DD2LightningBugT3;

    /// <summary>
    /// Requires an active army event, mapped accounting, a creditable death, and an army source species.
    /// </summary>
    internal static bool CanDropMappedMana(int source, bool ongoing, bool legacy, bool canCredit) =>
        ongoing && !legacy && canCredit && IsOldOnesArmyEnemy(source);

    /// <summary>
    /// Drops one Etherian Mana for an eligible mapped death when vanilla's event crystal gate allows it.
    /// </summary>
    private static void DropMappedMana(NPC npc)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || !DD2Event.Ongoing
            || ModContent.GetInstance<RandomizerConfig>().LegacyEventCredit)
            return;
        // Use the inverse source and pair-credit owner, then defer the event's crystal timing gate to vanilla.
        int target = TwinsEncounter.CreditTarget(npc);
        int source = SourceForTarget(target);
        bool canCredit = TwinsEncounter.CanCredit(npc);
        bool canDropMana = CanDropMappedMana(source, DD2Event.Ongoing, legacy: false, canCredit: canCredit);
        if (canDropMana && DD2Event.ShouldDropCrystals())
            Item.NewItem(npc.GetSource_Loot(), npc.position, npc.Size, ItemID.DD2EnergyCrystal);
    }

    /// <summary>
    /// Leaves vanilla mana drops enabled only in legacy mode, preventing mapped mode from awarding them twice.
    /// </summary>
    private static bool ShouldDropNativeMana() =>
        ModContent.GetInstance<RandomizerConfig>().LegacyEventCredit && DD2Event.ShouldDropCrystals();

    /// <summary>
    /// Resolves a replacement to its original source type, converting negative variant net IDs to base types.
    /// Excluded bosses keep native credit; targets without an inverse mapping return NPCID.None.
    /// </summary>
    internal static int SourceForTarget(int target)
    {
        // A boss removed from randomization must retain native accounting even though it has no inverse map entry.
        if (RandomizerSystem.BossesExcluded && ContentSamples.NpcsByNetId.TryGetValue(target, out NPC boss)
            && BossRules.IsBossRoot(boss))
            return boss.type;
        // Unmapped helpers and excluded targets cannot impersonate an event source.
        if (!RandomizerSystem.TryFindSource(target, out int source))
            return NPCID.None;
        // The inverse stores the earliest tier. Present accounting with the
        // active event's form so tier-sensitive event behavior stays consistent.
        if (RandomizerSystem.GetMapping().IsArmyGroup(source))
            source = ArmyFamilies.ForTier(source, DD2Event.Ongoing ? DD2Event.OngoingDifficulty : ArmyFamilies.CurrentTier);
        // Negative net IDs are sizes/costumes; event APIs require an NPC type.
        return ContentSamples.NpcsByNetId.TryGetValue(source, out NPC sample) ? sample.type : source;
    }

    /// <summary>
    /// Chooses actual-type legacy credit or inverse-mapped credit, suppressing the first death of a linked pair.
    /// </summary>
    private static int EventType(int original, NPC npc)
    {
        if (ModContent.GetInstance<RandomizerConfig>().LegacyEventCredit)
            return original;
        if (!TwinsEncounter.CanCredit(npc))
            return NPCID.None;

        int target = TwinsEncounter.CreditTarget(npc);
        int source = SourceForTarget(target);
        return NormalizeEventSource(source);
    }

    /// <summary>
    /// Sends normal death credit to the army event, or completes Betsy's final wave with 100 mapped death points.
    /// </summary>
    private static void CreditOldOnesArmy(int source)
    {
        // Vanilla's Betsy wave receives one point per percent of health lost.
        // A mapped replacement instead awards those 100 points on death.
        int awards = 1;
        bool mappedCredit = !ModContent.GetInstance<RandomizerConfig>().LegacyEventCredit;
        bool betsyFinalWave = source == NPCID.DD2Betsy && DD2Event.OngoingDifficulty == 3 && NPC.waveNumber == 7;
        if (mappedCredit && betsyFinalWave)
            awards = 100;
        for (int i = 0; i < awards; i++)
            DD2Event.CheckProgress(source);
    }

    /// <summary>
    /// Rewrites the narrowly scoped IL sites for event-type reads, Betsy credit, and mana checks.
    /// Expected-pattern checks throw on incompatible vanilla layouts rather than applying a partial patch.
    /// </summary>
    internal static void Patch(ILContext il, string name)
    {
        // Each special case has a different purpose and IL layout. Keep their
        // implementations separate from the ordinary event-type substitution.
        if (name == "DoDeathEvents")
        {
            PatchManaDrops(il);
            return;
        }
        if (name == "VanillaHitEffect")
        {
            PatchBetsyHitCredit(il);
            return;
        }

        PatchEventTypeReads(il, name);
    }

    // Replace native mana gates and add one mapped drop attempt at method entry.
    // Actual-type loot and every other death effect remain untouched.
    private static void PatchManaDrops(ILContext il)
    {
        var cursor = new ILCursor(il);
        int checks = 0;
        MethodInfo nativeManaGate = typeof(MappedEventCredit)
            .GetMethod(nameof(ShouldDropNativeMana), BindingFlags.Static | BindingFlags.NonPublic);

        while (cursor.TryGotoNext(MoveType.Before, i => i.MatchCall(typeof(DD2Event), nameof(DD2Event.ShouldDropCrystals))))
        {
            // Retain the instruction itself: switch/branch labels can target it.
            cursor.Next.Operand = il.Method.Module.ImportReference(nativeManaGate);
            cursor.Index++;
            checks++;
        }
        if (checks != 2)
            throw new InvalidOperationException($"Expected two native Etherian Mana checks, found {checks}.");

        cursor.Index = 0;
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.EmitDelegate<Action<NPC>>(DropMappedMana);
    }

    // Betsy normally credits health loss. Mapped mode awards the full wave value
    // when her replacement dies instead, so suppress this native contribution.
    private static void PatchBetsyHitCredit(ILContext il)
    {
        var cursor = new ILCursor(il);
        if (!cursor.TryGotoNext(MoveType.Before, i => i.MatchCall(typeof(DD2Event), nameof(DD2Event.CheckProgress))))
            throw new InvalidOperationException("Betsy progress call not found.");
        cursor.EmitDelegate<Func<int, int>>(id => ModContent.GetInstance<RandomizerConfig>().LegacyEventCredit ? id : NPCID.None);
    }

    // Substitute the source species only inside the method's event accounting.
    // The death method also needs its army-credit call replaced for Betsy's final wave.
    private static void PatchEventTypeReads(ILContext il, string name)
    {
        var cursor = new ILCursor(il);
        if (name == "checkDead")
        {
            // Only the event-accounting tail is patched. Loot, boss defeat flags,
            // death effects and every earlier NPC.type read retain the real type.
            if (!cursor.TryGotoNext(MoveType.Before, i => i.MatchCall(typeof(DD2Event), nameof(DD2Event.CheckProgress)))
                || cursor.Index < 2 || !cursor.Prev.MatchLdfld<NPC>(nameof(NPC.type)))
                throw new InvalidOperationException("NPC death event accounting not found.");
            cursor.Index -= 2;
        }
        // From the selected accounting boundary onward, substitute only reads of this NPC's type.
        int reads = 0;
        while (cursor.Next != null)
        {
            Instruction instruction = cursor.Next;
            if (name == "checkDead" && instruction.MatchCall(typeof(DD2Event), nameof(DD2Event.CheckProgress)))
            {
                cursor.Remove();
                cursor.EmitDelegate<Action<int>>(CreditOldOnesArmy);
                continue;
            }
            cursor.Index++;
            if (instruction.OpCode == OpCodes.Ldfld && instruction.Operand is FieldReference field
                && field.DeclaringType.FullName == typeof(NPC).FullName && field.Name == nameof(NPC.type))
            {
                cursor.Emit(OpCodes.Ldarg_0);
                // The original integer type is already on the stack; pass this NPC too so pair ownership can be checked.
                cursor.EmitDelegate<Func<int, NPC, int>>(EventType);
                reads++;
            }
        }
        if (reads == 0)
            throw new InvalidOperationException($"No event type reads found in {name}.");
    }
}

