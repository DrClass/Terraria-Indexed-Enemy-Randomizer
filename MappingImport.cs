using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace IndexedEnemyRandomizer;

/// <summary>Reads exact saved snapshots, without guessing groups from display names.</summary>
internal static class MappingImport
{
    private const string Marker = "Saved mapping data: ";

    internal static void Validate(TagCompound data)
    {
        WorldMapping.Require(data, "Version", "Seed", "Options", "Mapping");
        if (data.GetInt("Version") != 1)
            throw new InvalidDataException("Unsupported mapping version.");
        MappingOptions.Load(data.GetCompound("Options"));
        WorldMapping.Load(data.GetCompound("Mapping"));
        data.GetString("Seed");
    }

    // Keep the human-readable report, followed by an exact portable snapshot.
    // Import uses this snapshot; editing the report's display rows does not edit it.
    internal static string ExportData(TagCompound data)
    {
        using var stream = new MemoryStream();
        TagIO.ToStream(data, stream);
        return "\n" + Marker + Convert.ToBase64String(stream.ToArray()) + "\n";
    }

    internal static TagCompound ReadText(string text)
    {
        string line = text.Split('\n').SingleOrDefault(line => line.StartsWith(Marker, StringComparison.Ordinal));
        if (line == null)
            throw new InvalidDataException("This older text export has no restore data. Use a .dat backup or a new /dumpmapping export.");
        using var stream = new MemoryStream(Convert.FromBase64String(line[Marker.Length..].Trim()));
        return TagIO.FromStream(stream);
    }

    /// <summary>Accepts only a filename in the two mapping folders on the host.</summary>
    internal static string FindFile(string root, string filename)
    {
        if (string.IsNullOrWhiteSpace(filename) || filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || filename.Contains('/') || filename.Contains('\\') || filename.Contains(':')
            || filename is "." or "..")
            throw new InvalidDataException("Use only a filename from MappingBackups or MappingExports.");
        string extension = Path.GetExtension(filename);
        if (!extension.Equals(".dat", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose a .dat backup or .txt mapping export.");
        string[] matches = new[] { "MappingBackups", "MappingExports" }
            .Select(folder => Path.Combine(root, folder, filename)).Where(File.Exists).ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException("File missing or duplicated. Put one copy in the host's MappingBackups or MappingExports folder.");
        return matches[0];
    }
}

/// <summary>Restores a saved mapping while retaining an undo backup of the current one.</summary>
public sealed class ImportMappingCommand : ModCommand
{
    public override string Command => "importmapping";
    public override CommandType Type => CommandType.World | CommandType.Console;
    public override string Usage => "/importmapping <filename.dat or filename.txt>";
    public override string Description => "Restore a mapping backup or importable export from the world host's mapping folders.";
    public override bool IsCaseSensitive => true;

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        if (args.Length == 0)
        {
            caller.Reply(Usage);
            return;
        }
        try
        {
            string path = MappingImport.FindFile(Path.Combine(Main.SavePath, "IndexedEnemyRandomizer"), string.Join(" ", args));
            TagCompound data = Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase)
                ? MappingImport.ReadText(File.ReadAllText(path)) : TagIO.FromFile(path);
            // Validation happens before the previous mapping is backed up or replaced.
            RandomizerSystem.ImportMapping(data);
            caller.Reply("Mapping restored. Previous mapping backed up in IndexedEnemyRandomizer/MappingBackups. Saved with the world; existing NPCs remain unchanged.", Color.LightGreen);
        }
        catch (Exception error)
        {
            Mod.Logger.Error("Could not import mapping.", error);
            // Never echo exception text: filesystem errors can contain private paths.
            caller.Reply("Import failed. Use a valid .dat backup or new /dumpmapping export in the host's MappingBackups or MappingExports folder. Check the host log for details.", Color.OrangeRed);
        }
    }
}
