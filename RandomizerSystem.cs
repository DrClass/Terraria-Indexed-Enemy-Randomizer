using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace IndexedEnemyRandomizer;

/// <summary>
/// Owns the world's saved mapping. Only first load or an explicit command can
/// generate assignments; config changes and mod updates never rebuild them.
/// </summary>
public sealed class RandomizerSystem : ModSystem
{
    private static int[] eligible = [];
    private static Dictionary<int, int> banners = new();
    private static WorldMapping mapping;
    private static TagCompound savedData;
    private static byte[] networkData;
    private static readonly WorldMapping Empty = new("", false, Array.Empty<int>());
    internal static MappingOptions ActiveOptions { get; private set; }
    internal static string MappingSeed { get; private set; }
    internal static bool LoadFailed { get; private set; }

    internal static bool TownNpcsIncluded => ActiveOptions?.TownNpcs ?? false;
    internal static bool BossesExcluded => !(ActiveOptions?.IncludeBosses ?? true);

    // Presence in the saved map replaces live eligibility checks. New NPCs and
    // missing targets are left untouched, without patching the saved assignments.
    internal static bool IsAvailable(int id) => id != 0 && id < NPCID.Count
        && ContentSamples.NpcsByNetId.TryGetValue(id, out NPC sample) && sample.type > NPCID.None && sample.ModNPC == null;
    internal static bool CanRandomize(NPC npc) => npc.ModNPC == null
        && GetMapping().AvailableTargets(npc.netID, IsAvailable).Length > 0;

    public override void PostSetupContent()
    {
        eligible = ContentSamples.NpcsByNetId
            .Where(pair => pair.Key < NPCID.Count && NpcCatalog.CanReplace(pair.Value, includeTownNpcs: true))
            .Select(pair => pair.Key).OrderBy(id => id).ToArray();
        banners = eligible.ToDictionary(id => id,
            id => Item.NPCtoBanner(ContentSamples.NpcsByNetId[id].BannerID()));
    }

    public override void OnWorldLoad() => ResetMapping();
    public override void OnWorldUnload() => ResetMapping();
    public override void ClearWorld() => ResetMapping();
    public override void Unload()
    {
        ResetMapping();
        eligible = [];
        banners.Clear();
    }

    private static void ResetMapping()
    {
        mapping = null;
        savedData = null;
        networkData = null;
        ActiveOptions = null;
        MappingSeed = null;
        LoadFailed = false;
        unreadableContainer = null;
        RandomizedNPC.ClearPendingSpawns();
    }

    // This hook runs after all saved world data is loaded, on the host only.
    // A missing entry is a first load; an invalid entry must never regenerate.
    public override void PostWorldLoad()
    {
        EnsureMapping();
        if (LoadFailed)
            Mod.Logger.Error("Saved randomizer mapping could not be loaded. Randomization is disabled; restore a backup or run regeneratemapping.");
    }

    public override void PostWorldGen() => EnsureMapping();

    private static void EnsureMapping()
    {
        if (mapping != null || LoadFailed || Main.netMode == NetmodeID.MultiplayerClient || Main.ActiveWorldFileData == null)
            return;
        Generate(Main.ActiveWorldFileData.SeedText);
    }

    internal static WorldMapping GetMapping()
    {
        // Clients wait for the host's map instead of constructing their own.
        EnsureMapping();
        return mapping ?? Empty;
    }

    public override void SaveWorldData(TagCompound tag)
    {
        EnsureMapping();
        // Keep invalid/unsupported data verbatim so saving cannot destroy the
        // only copy. Regeneration is the explicit recovery path.
        if (unreadableContainer != null)
            tag["SavedMapping"] = unreadableContainer["SavedMapping"];
        else if (savedData != null)
            tag["SavedMapping"] = savedData;
    }

    public override void LoadWorldData(TagCompound tag)
    {
        if (tag.ContainsKey("SavedMapping"))
        {
            try { Restore(tag.GetCompound("SavedMapping")); }
            catch (Exception error)
            {
                LoadFailed = true;
                mapping = null;
                Mod.Logger.Error("Could not read saved mapping container.", error);
                // Save the complete original payload, including malformed type data.
                unreadableContainer = tag;
            }
        }
    }

    private static TagCompound unreadableContainer;

    private static void Restore(TagCompound data)
    {
        savedData = data;
        mapping = null;
        ActiveOptions = null;
        MappingSeed = null;
        networkData = null;
        try
        {
            WorldMapping.Require(data, "Version", "Options", "Mapping", "Seed");
            if (data.GetInt("Version") != 1)
                throw new InvalidDataException("Unsupported world mapping format.");
            var options = MappingOptions.Load(data.GetCompound("Options"));
            var restored = WorldMapping.Load(data.GetCompound("Mapping"));
            ActiveOptions = options;
            MappingSeed = data.GetString("Seed");
            mapping = restored;
            LoadFailed = false;
        }
        catch (Exception)
        {
            // Do not silently reshuffle a damaged save. All NPCs spawn normally.
            LoadFailed = true;
        }
    }

    private static void Generate(string seed)
    {
        var options = MappingOptions.FromConfig(ModContent.GetInstance<RandomizerConfig>());
        int[] candidates = eligible.Where(id => NpcCatalog.CanReplace(ContentSamples.NpcsByNetId[id],
            !options.IncludeBosses, options.TownNpcs)).ToArray();
        var generated = new WorldMapping(seed, WorldGen.crimson, candidates,
            banners: options.Banners ? banners : null, celebration: Main.tenthAnniversaryWorld,
            combineTwins: options.Twins, separateProgressionPools: options.Separate,
            groupGoldCritters: options.Gold, groupArmyTiers: options.Army,
            includeOppositeEvil: options.OppositeEvil, groupMiscVariants: options.Misc);
        // Freeze generated data through the same reader used on future loads.
        // Nothing below depends on the next release's catalogs or group rules.
        var data = new TagCompound
        {
            ["Version"] = 1, ["Seed"] = seed,
            ["Options"] = options.Save(), ["Mapping"] = generated.Save()
        };
        WorldMapping.Load(data.GetCompound("Mapping"));
        Restore(data);
        if (LoadFailed)
            throw new InvalidDataException("Generated mapping failed validation.");
        unreadableContainer = null;
    }

    internal static void Regenerate(string seed)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
            throw new InvalidOperationException("Only the world host can regenerate mappings.");
        // Back up before replacing anything. If the backup cannot be written,
        // abort instead of losing the previous map. Never expose this full path in chat.
        BackupCurrentMapping();
        Generate(seed);
        // Existing NPCs and their encounter links remain intact. Future spawns
        // use the new map, and clients receive the host's exact assignments.
        if (Main.netMode == NetmodeID.Server)
            NetMessage.SendData(MessageID.WorldData);
    }

    private static void BackupCurrentMapping()
    {
        TagCompound backup = unreadableContainer ?? savedData;
        if (backup != null)
        {
            string directory = Path.Combine(Main.SavePath, "IndexedEnemyRandomizer", "MappingBackups");
            Directory.CreateDirectory(directory);
            TagIO.ToFile(backup, Path.Combine(directory, $"mapping-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.dat"));
        }
    }

    /// <summary>Returns the complete saved snapshot for an importable export.</summary>
    internal static TagCompound ExportSnapshot()
    {
        EnsureMapping();
        if (LoadFailed || savedData == null)
            throw new InvalidDataException("No readable mapping to export.");
        return savedData;
    }

    /// <summary>Validates before backing up or changing any active world state.</summary>
    internal static void ImportMapping(TagCompound data)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
            throw new InvalidOperationException("Only the world host can import mappings.");
        MappingImport.Validate(data);
        BackupCurrentMapping();
        Restore(data);
        unreadableContainer = null;
        if (Main.netMode == NetmodeID.Server)
            NetMessage.SendData(MessageID.WorldData);
    }

    public override void NetSend(BinaryWriter writer)
    {
        EnsureMapping();
        writer.Write(LoadFailed);
        if (LoadFailed)
            return;
        if (networkData == null)
        {
            using var stream = new MemoryStream();
            TagIO.ToStream(savedData, stream);
            networkData = stream.ToArray();
        }
        writer.Write(networkData.Length);
        writer.Write(networkData);
    }

    public override void NetReceive(BinaryReader reader)
    {
        if (reader.ReadBoolean())
        {
            mapping = null;
            ActiveOptions = null;
            LoadFailed = true;
            return;
        }
        int length = reader.ReadInt32();
        if (length < 0 || length > 1000000)
            throw new InvalidDataException("Invalid mapping packet length.");
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException();
        using var stream = new MemoryStream(bytes);
        Restore(TagIO.FromStream(stream));
    }


    /// <summary>Chooses an available member of the saved target without inventing a fallback.</summary>
    internal static int Replacement(NPC npc)
    {
        WorldMapping current = GetMapping();
        int target = current.Replacement(npc.netID, "", false);
        IReadOnlyList<int> variants = current.AvailableTargets(npc.netID, IsAvailable);
        if (variants.Count == 0)
            return npc.netID;
        // Paired encounters start with a known root; PreAI will create its required partner after initialization.
        if (current.IsTwinsGroup(target))
            return NPCID.Retinazer;
        if (current.IsScutlixGroup(target))
            return NPCID.ScutlixRider;
        if (current.IsTruffleWormGroup(target))
            return variants.Contains(NPCID.TruffleWorm) ? NPCID.TruffleWorm : variants[0];
        if (current.IsArmyGroup(target))
        {
            int tier = ArmyFamilies.ForTier(target, ArmyFamilies.CurrentTier);
            return variants.Contains(tier) ? tier : npc.netID;
        }
        // Only the member roll varies from spawn to spawn. The selected family itself remains fixed by the seed.
        if (current.IsGoldCritterGroup(target))
        {
            // Vanilla natural spawns roll the nearby player's luck. For replacements
            // use the closest living player, with an ordinary roll when none exists.
            int player = Player.FindClosest(npc.position, npc.width, npc.height);
            if (player >= 0 && player < Main.maxPlayers && Main.player[player].active && !Main.player[player].dead)
                return GoldCritters.Pick(variants, Main.rand.Next, Main.player[player].RollLuck);
            return GoldCritters.Pick(variants, Main.rand.Next);
        }
        return variants.Count == 1 ? variants[0] : variants[Main.rand.Next(variants.Count)];
    }

    /// <summary>
    /// Looks up the original spawn replaced by the requested target in the current mapping.
    /// </summary>
    internal static bool TryFindSource(int targetNetId, out int sourceNetId)
    {
        var current = GetMapping();
        if (!IsAvailable(targetNetId) || !current.TryFindSource(targetNetId, out sourceNetId))
        {
            sourceNetId = 0;
            return false;
        }
        sourceNetId = current.Members(sourceNetId).FirstOrDefault(IsAvailable);
        return sourceNetId != 0;
    }

    /// <summary>
    /// Exposes the current mapping's variants for commands and other presentation code.
    /// </summary>
    internal static IReadOnlyList<int> GroupMembers(int id) => GetMapping().Members(id);

    /// <summary>
    /// Lists saved source IDs for exports, including entries whose NPCs were removed.
    /// </summary>
    internal static int[] EligibleSources => GetMapping().KnownIds;
}



