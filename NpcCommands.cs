using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace EnemyRandomizer;

// Mark only this NPC spawn. Children created later by its AI still obey the
// normal randomizer/part rules; no global bypass flag can leak to other spawns.
/// <summary>
/// Per-spawn marker that bypasses randomization for an explicitly requested NPC or required companion.
/// Unlike a global flag, it cannot accidentally bypass unrelated simultaneous spawns.
/// </summary>
internal sealed class DirectNpcSpawnSource : IEntitySource
{
    /// <summary>
    /// Diagnostic source context used to identify direct command/companion spawns.
    /// </summary>
    public string Context => "EnemyRandomizer:spawnnpc";
}

/// <summary>
/// Adapts loaded NPC metadata and localized names to the pure command-name resolver.
/// </summary>
internal static class NpcCommandNames
{
    /// <summary>
    /// Canonical reflected NPCID names, choosing one name when multiple constants alias the same numeric ID.
    /// </summary>
    private static readonly Dictionary<int, string> VanillaNames = typeof(NPCID)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.FieldType == typeof(short))
        .GroupBy(field => (int)(short)field.GetRawConstantValue())
        .ToDictionary(group => group.Key, group => group.First().Name);

    /// <summary>
    /// Formats a localized NPC name with its numeric net ID for unambiguous command/export output.
    /// </summary>
    internal static string Label(int id) => $"{Lang.GetNPCNameValue(id)} [{id}]";

    /// <summary>
    /// Resolves joined command arguments against loaded vanilla/modded NPC names and replies with concise errors.
    /// </summary>
    internal static bool TryResolve(CommandCaller caller, string[] args, out int id)
    {
        // Build aliases from loaded samples so localized, internal, qualified mod names, and negative variant IDs all work.
        var entries = ContentSamples.NpcsByNetId.Where(pair => pair.Value.type > NPCID.None)
            .Select(pair =>
            {
                string name = pair.Value.ModNPC?.Name
                    ?? VanillaNames.GetValueOrDefault(pair.Key, pair.Key.ToString(CultureInfo.InvariantCulture));
                return new NpcNameEntry(pair.Key, Lang.GetNPCNameValue(pair.Key), name,
                    pair.Value.ModNPC?.FullName ?? "Terraria/" + name);
            });
        if (NpcNameResolver.TryResolve(string.Join(" ", args), entries, out id, out string error))
            return true;
        caller.Reply(error, Color.OrangeRed);
        return false;
    }
}

/// <summary>
/// Spawns the exact requested NPC without applying its mapping, while retaining configured encounter limits.
/// </summary>
public sealed class SpawnNpcCommand : ModCommand
{
    /// <summary>
    /// Registers the direct-spawn command as spawnnpc.
    /// </summary>
    public override string Command => "spawnnpc";
    /// <summary>
    /// Allows authoritative execution from player chat and the server console.
    /// </summary>
    public override CommandType Type => CommandType.World | CommandType.Console;
    /// <summary>
    /// Documents the accepted name-or-ID argument.
    /// </summary>
    public override string Usage => "/spawnnpc <NPC name or ID>";
    /// <summary>
    /// Help text explaining the command's per-spawn randomization bypass.
    /// </summary>
    public override string Description => "Spawn the requested NPC directly, without randomizing it.";
    /// <summary>
    /// Preserves input casing for diagnostics; the custom name resolver handles case-insensitive matching itself.
    /// </summary>
    public override bool IsCaseSensitive => true;

    /// <summary>
    /// Resolves an NPC, checks duplicate limits and slot availability, then creates it near the caller or world spawn.
    /// </summary>
    public override void Action(CommandCaller caller, string input, string[] args)
    {
        if (!NpcCommandNames.TryResolve(caller, args, out int id))
            return;
        // Direct spawning bypasses the map, but the configured encounter limit still applies to the requested species.
        NPC sample = ContentSamples.NpcsByNetId[id];
        if (BossRules.AlreadyPresent(sample, null, Main.npc, RandomizedNPC.WillRandomize,
            ModContent.GetInstance<RandomizerConfig>().LimitMiniBosses, ModContent.GetInstance<RandomizerConfig>().LimitBosses))
        {
            caller.Reply($"{NpcCommandNames.Label(id)} is already alive. The one-boss limit still applies.", Color.OrangeRed);
            return;
        }

        // Console callers have no player; use world spawn instead of dereferencing a missing player object.
        Player player = caller.Player;
        Vector2 bottom = player != null
            ? player.Bottom + new Vector2(player.direction * 80f, 0f)
            : new Vector2(Main.spawnTileX * 16f, Main.spawnTileY * 16f);
        // Keep the requested NPC's full dimensions inside the world even when the caller is near its edge.
        bottom.X = MathHelper.Clamp(bottom.X, 32f + sample.width / 2f, Main.maxTilesX * 16f - 32f - sample.width / 2f);
        bottom.Y = MathHelper.Clamp(bottom.Y, sample.height + 32f, Main.maxTilesY * 16f - 32f);
        // Attach the bypass to this creation only, preserving normal rules for unrelated and later child spawns.
        int index = NPC.NewNPC(new DirectNpcSpawnSource(), (int)bottom.X, (int)bottom.Y,
            id, Target: player?.whoAmI ?? 255);
        if (index < 0 || index >= Main.maxNPCs || !Main.npc[index].active)
        {
            caller.Reply("No free NPC slot. Wait for an NPC to die or despawn, then try again.", Color.OrangeRed);
            return;
        }

        NPC spawned = Main.npc[index];
        // Special seeds can turn bunnies into explosive bunnies inside NewNPC.
        // Restore the exact requested ID; our source marker survives SetDefaults.
        if (spawned.netID != id)
            spawned.SetDefaults(id);
        spawned.Bottom = bottom;
        spawned.target = player?.whoAmI ?? 255;
        spawned.netUpdate = true;
        if (Main.netMode == NetmodeID.Server)
            NetMessage.SendData(MessageID.SyncNPC, number: index);
        caller.Reply($"Spawned {NpcCommandNames.Label(id)} {(player != null ? "beside you" : "at world spawn")} (not randomized).", Color.LightGreen);
    }
}

/// <summary>
/// Answers the inverse question: which original NPC spawn produces the enemy the player wants to find?
/// </summary>
public sealed class HintCommand : ModCommand
{
    /// <summary>
    /// Registers the inverse-mapping lookup as hint.
    /// </summary>
    public override string Command => "hint";
    /// <summary>
    /// Allows hints from player chat or the host console using the world's authoritative mapping.
    /// </summary>
    public override CommandType Type => CommandType.World | CommandType.Console;
    /// <summary>
    /// Documents the requested target name-or-ID argument.
    /// </summary>
    public override string Usage => "/hint <NPC name or ID>";
    /// <summary>
    /// Help text describing the source-to-look-for meaning of the result.
    /// </summary>
    public override string Description => "Find which original NPC spawn produces the NPC you want.";
    /// <summary>
    /// Preserves the original argument text before custom name normalization.
    /// </summary>
    public override bool IsCaseSensitive => true;

    /// <summary>
    /// Resolves a desired target, explains exclusions, and reports one concise source-group name and variant note.
    /// </summary>
    public override void Action(CommandCaller caller, string input, string[] args)
    {
        int id;
        // Allow the encounter name as a shortcut when the two eyes form one mapping entry.
        if (TwinsEncounter.Enabled && string.Equals(string.Join(" ", args), "twins", System.StringComparison.OrdinalIgnoreCase))
            id = NPCID.Retinazer;
        else if (!NpcCommandNames.TryResolve(caller, args, out id))
            return;
        // Choose a short encounter label; do not dump every member of a banner group into chat.
        var map = RandomizerSystem.GetMapping();
        string target = GetHintName(map, id, describeVariants: false);
        NPC sample = ContentSamples.NpcsByNetId[id];
        // Explain why a target cannot be found before attempting an inverse lookup.
        if (NpcCatalog.IsProtected(sample) && !(RandomizerSystem.TownNpcsIncluded && NpcCatalog.IsTownPoolCandidate(sample)))
        {
            caller.Reply($"{target}: not randomized.");
            return;
        }
        if (RandomizerSystem.BossesExcluded && BossRules.IsBossRoot(sample))
        {
            caller.Reply($"{target}: boss randomization disabled.");
            return;
        }
        if (!NpcCatalog.CanReplace(sample, includeTownNpcs: RandomizerSystem.TownNpcsIncluded))
        {
            caller.Reply($"{target}: not a standalone replacement.");
            return;
        }
        // A forced-spawn fallback can randomize without ever being a target in the world's complete permutation.
        if (!RandomizerSystem.TryFindSource(id, out int source))
        {
            if (!SpecialSeedRules.Allowed(id, Main.tenthAnniversaryWorld))
            {
                caller.Reply($"{target}: requires a Celebration/Zenith world.");
                return;
            }
            caller.Reply($"{target}: not in the replacement pool.");
            return;
        }

        // The hint points backward from desired replacement to original source; group notes explain per-spawn variety.
        string sourceName = GetHintName(map, source, describeVariants: true);
        string note = GetVariantNote(map, id);
        caller.Reply($"{target} replaces {sourceName}{note}.", Color.LightGreen);
    }

    // Encounter names describe the whole group; ordinary sources add a short
    // reminder that any variant can be the spawn the player should look for.
    private static string GetHintName(WorldMapping map, int id, bool describeVariants)
    {
        if (map.IsTwinsGroup(id))
            return "The Twins";
        if (map.IsScutlixGroup(id))
            return "Scutlix and rider";
        if (map.IsTruffleWormGroup(id))
            return "Truffle Worm";

        string name = Lang.GetNPCNameValue(id);
        if (describeVariants && map.Members(id).Count > 1)
            name += " variants";
        return name;
    }

    // Paired encounters and alternate lifecycle forms are not random variant
    // choices. Only ordinary variant families need that qualification.
    internal static string GetVariantNote(WorldMapping map, int id)
    {
        if (map.IsGoldCritterGroup(id))
        {
            if (GoldCritters.IsGold(id))
                return " (10% gold chance)";
            return " (90% normal chance)";
        }

        if (map.Members(id).Count <= 1)
            return "";
        if (map.IsTwinsGroup(id) || map.IsScutlixGroup(id) || map.IsTruffleWormGroup(id))
            return "";

        return " (random variant)";
    }
}
