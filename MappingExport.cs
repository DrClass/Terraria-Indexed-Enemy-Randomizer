using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace IndexedEnemyRandomizer;

/// <summary>
/// Formats a full mapping as readable text without depending on file-system or chat APIs.
/// </summary>
internal static class MappingExport
{
    /// <summary>
    /// Lists every requested source, its mapped variants/encounter members, and tier/fallback annotations.
    /// The supplied label function controls names while metadata records the world's configuration.
    /// </summary>
    /// <param name="map">World mapping to describe without modifying it.</param>
    /// <param name="sources">Source IDs to list, including optional fallback-only sources.</param>
    /// <param name="label">Formatter for readable NPC names and IDs.</param>
    /// <param name="metadata">Preformatted world/configuration header text.</param>
    /// <returns>The complete plain-text report, ready to save without rolling any variants.</returns>
    internal static string Render(WorldMapping map, IEnumerable<int> sources, Func<int, string> label, string metadata)
    {
        // Write the direction explicitly: these rows answer what a natural source becomes, unlike the inverse hint command.
        var text = new StringBuilder("Indexed Enemy Randomizer mapping\n");
        text.AppendLine(metadata);
        text.AppendLine("Direction: original spawn -> replacement. IDs are in brackets.");
        text.AppendLine("Variants: choose one listed member randomly. Twins and Scutlix encounters: spawn BOTH listed members.");
        text.AppendLine("Grouped source members share the same replacement; each source ID is listed below.");
        text.AppendLine("Fallback sources cannot be targets in this world, but forced spawns still randomize.");
        text.AppendLine("Protected NPCs/encounter parts are not randomized and are not mapping entries.");
        // Sort and deduplicate source IDs for a stable, reviewable report, then put the early tier first.
        foreach (var tier in sources.Distinct().OrderBy(id => id)
            .GroupBy(id => map.SeparateProgressionPools && map.IsEarlySource(id)).OrderByDescending(g => g.Key))
        {
            text.AppendLine();
            if (!map.SeparateProgressionPools)
                text.AppendLine("COMBINED POOL");
            else if (tier.Key)
                text.AppendLine("PRE-HARDMODE");
            else
                text.AppendLine("HARDMODE (also used for forced early spawns)");
            foreach (int source in tier)
            {
                // List the whole target group without performing a random variant roll.
                int target = map.Replacement(source, source.ToString(CultureInfo.InvariantCulture), tier.Key);
                string result = string.Join(" | ", map.Members(target).Select(label));
                // Explain paired encounters and weighted critters explicitly; a plain list would imply the wrong spawn behavior.
                if (map.IsTwinsGroup(target))
                    result = "The Twins (both): " + result;
                else if (map.IsScutlixGroup(target))
                    result = "Scutlix and rider (both): " + result;
                else if (map.IsTruffleWormGroup(target))
                    result = label(Terraria.ID.NPCID.TruffleWorm) + " (normal form; fleeing form shares mapping)";
                else if (map.IsGoldCritterGroup(target))
                    result = "90% normal / 10% gold: " + result;
                else if (map.Members(target).Count > 1)
                    result = "Random variant: " + result;
                // Annotate source-side special cases separately from the target-side spawn instructions.
                string annotation = map.IsTwinsGroup(source) ? " (The Twins source)" : "";
                if (map.IsScutlixGroup(source))
                    annotation = " (Scutlix encounter source)";
                if (!map.TryFindSource(source, out _))
                    annotation += " (fallback source)";
                text.AppendLine($"{label(source)}{annotation} -> {result}");
            }
        }
        return text.ToString();
    }
}

/// <summary>
/// Exports the host's complete mapping and settings to a uniquely named UTF-8 text file.
/// </summary>
public sealed class DumpMappingCommand : ModCommand
{
    /// <summary>
    /// Chat/console command name registered with tModLoader.
    /// </summary>
    public override string Command => "dumpmapping";
    /// <summary>
    /// Runs against the authoritative world from either player chat or the host console.
    /// </summary>
    public override CommandType Type => CommandType.World | CommandType.Console;
    /// <summary>
    /// Short syntax shown when arguments are supplied to this argument-free command.
    /// </summary>
    public override string Usage => "/dumpmapping";
    /// <summary>
    /// Help text describing where mapping exports are written.
    /// </summary>
    public override string Description => "Export the full mapping to a text file on this world's host.";

    /// <summary>
    /// Captures mapping metadata, writes the report, and replies with only the filename to avoid exposing the host username.
    /// </summary>
    public override void Action(CommandCaller caller, string input, string[] args)
    {
        if (args.Length != 0)
        {
            caller.Reply(Usage);
            return;
        }
        // Include the seed and settings that explain the report so it can be reviewed outside the running game.
        var config = ModContent.GetInstance<RandomizerConfig>();
        string metadata = $"World: {Main.worldName}\nSeed: {Main.ActiveWorldFileData.SeedText}\n"
            + $"Mod version: {Mod.Version}\nWorld evil: {(WorldGen.crimson ? "Crimson" : "Corruption")}\n"
            + $"Celebration flag: {Main.tenthAnniversaryWorld}\nGroup variants: {config.GroupNpcVariants}\n"
            + $"Combine Twins: {config.CombineTwins}\nLegacy event credit: {config.LegacyEventCredit}\n"
            + $"Exclude bosses: {config.ExcludeBosses}\n"
            + $"Preserve NPC children: {config.PreserveNpcChildren}\nLimit minibosses: {config.LimitMiniBosses}\n"
            + $"Limit bosses: {config.LimitBosses}\nGroup gold critters: {config.GroupGoldCritters}\n"
            + $"Group Old One's Army tiers: {config.GroupOldOnesArmyTiers}\n"
            + $"Include opposite evil: {config.IncludeOppositeEvil}\nRandomize town NPCs: {config.RandomizeTownNpcs}\n"
            + $"Randomize town shimmer: {config.RandomizeTownShimmer}\n"
            + $"Separate progression pools: {config.SeparateProgressionPools}\n"
            + $"Legacy pillar credit: {config.LegacyPillarCredit}\nExported (UTC): {DateTime.UtcNow:O}";
        string report = MappingExport.Render(RandomizerSystem.GetMapping(), RandomizerSystem.EligibleSources,
            NpcCommandNames.Label, metadata);
        try
        {
            // Write under the host's save folder; timestamp plus GUID avoids collisions between repeated exports.
            string directory = Path.Combine(Main.SavePath, "IndexedEnemyRandomizer", "MappingExports");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"mapping-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.txt");
            File.WriteAllText(path, report, new UTF8Encoding(false));
            // Only reveal the basename and relative save-folder location in chat, never a Windows user-profile path.
            caller.Reply($"Mapping saved: {Path.GetFileName(path)} (world host save folder: IndexedEnemyRandomizer/MappingExports)", Color.LightGreen);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Keep detailed filesystem diagnostics in the host log; chat gets a concise error without private path details.
            Mod.Logger.Error("Could not export mapping", error);
            caller.Reply("Could not export mapping. Check the world host's log for details.", Color.OrangeRed);
        }
    }
}
