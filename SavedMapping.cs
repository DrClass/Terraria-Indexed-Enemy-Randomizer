using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria.ModLoader.IO;

namespace IndexedEnemyRandomizer;

/// <summary>
/// The saved assignments are authoritative. Loading never runs current grouping,
/// catalog, progression, or shuffle rules against them.
/// </summary>
internal sealed partial class WorldMapping
{
    private readonly HashSet<int> savedEarlyIds;
    private readonly Dictionary<int, int> savedKinds;

    internal int[] KnownIds => representatives.Keys.OrderBy(id => id).ToArray();
    internal bool ContainsSource(int id) => representatives.ContainsKey(id);
    private bool? SavedKind(int id, int kind) => savedKinds == null ? null
        : savedKinds.GetValueOrDefault(Representative(id), -1) == kind;

    /// <summary>Retains how a group spawns, not just the NPC IDs in it.</summary>
    private int Kind(int id) => IsTwinsGroup(id) ? 1 : IsScutlixGroup(id) ? 2
        : IsTruffleWormGroup(id) ? 3 : IsGoldCritterGroup(id) ? 4 : IsArmyGroup(id) ? 5 : 0;

    internal TagCompound Save() => new()
    {
        ["Version"] = 1,
        ["Seed"] = seed,
        ["Crimson"] = crimson,
        ["Separate"] = SeparateProgressionPools,
        ["Gold"] = GroupGoldCritters,
        ["Army"] = GroupArmyTiers,
        ["Groups"] = AllSources.Select(id => new TagCompound
        {
            ["Source"] = id, ["Target"] = mapping[id], ["Members"] = members[id].ToArray(),
            ["EarlyMembers"] = members[id].Where(IsEarlySource).ToArray(), ["Kind"] = Kind(id)
        }).ToList()
    };

    internal static WorldMapping Load(TagCompound tag) => new(tag);

    private WorldMapping(TagCompound tag)
    {
        Require(tag, "Version", "Seed", "Crimson", "Separate", "Gold", "Army", "Groups");
        if (tag.GetInt("Version") != 1)
            throw new InvalidDataException("Unsupported saved mapping version.");
        seed = tag.GetString("Seed");
        crimson = tag.GetBool("Crimson");
        SeparateProgressionPools = tag.GetBool("Separate");
        GroupGoldCritters = tag.GetBool("Gold");
        GroupArmyTiers = tag.GetBool("Army");
        mapping = new();
        savedEarlyIds = new();
        savedKinds = new();
        var groups = tag.GetList<TagCompound>("Groups");
        if (groups.Count > 10000)
            throw new InvalidDataException("Saved mapping has too many groups.");
        foreach (TagCompound group in groups)
        {
            Require(group, "Source", "Target", "Members", "EarlyMembers", "Kind");
            int source = group.GetInt("Source");
            int[] ids = group.GetIntArray("Members");
            int kind = group.GetInt("Kind");
            int[] earlyIds = group.GetIntArray("EarlyMembers");
            if (ids.Length == 0 || ids.Length > 10000 || ids[0] != source || ids.Contains(0) || kind < 0 || kind > 5)
                throw new InvalidDataException("Invalid saved NPC group.");
            if (earlyIds.Any(id => !ids.Contains(id)))
                throw new InvalidDataException("Saved progression IDs are outside their group.");
            // Do not validate against the installed catalog. Removed IDs remain
            // stored, while runtime availability decides whether they can spawn.
            members.Add(source, ids.ToArray());
            mapping.Add(source, group.GetInt("Target"));
            savedKinds.Add(source, kind);
            foreach (int id in ids)
            {
                representatives.Add(id, source);
                if (earlyIds.Contains(id))
                    savedEarlyIds.Add(id);
            }
        }
        if (mapping.Values.Any(target => !members.ContainsKey(target)) || mapping.Values.Distinct().Count() != mapping.Count)
            throw new InvalidDataException("Saved assignments are not a complete permutation.");
        sourcesByTarget = mapping.ToDictionary(pair => pair.Value, pair => pair.Key);
        AllSources = members.Keys.OrderBy(id => id).ToArray();
        EarlySources = AllSources.Where(savedEarlyIds.Contains).ToArray();
        LateSources = AllSources.Where(id => !savedEarlyIds.Contains(id)).ToArray();
    }

    internal static void Require(TagCompound tag, params string[] keys)
    {
        if (keys.Any(key => !tag.ContainsKey(key)))
            throw new InvalidDataException("Saved mapping data is incomplete.");
    }

    /// <summary>
    /// Filters missing variants without changing the saved group. Paired
    /// encounters require every member; a partial encounter cannot be spawned.
    /// </summary>
    internal int[] AvailableTargets(int source, Func<int, bool> available)
    {
        if (!ContainsSource(source) || !available(source))
            return [];
        int target = Replacement(source, "", false);
        int[] present = Members(target).Where(available).ToArray();
        if ((IsTwinsGroup(target) || IsScutlixGroup(target)) && present.Length != Members(target).Count)
            return [];
        return present;
    }
}

/// <summary>Only generation options are frozen; gameplay options stay live.</summary>
internal sealed record MappingOptions(bool IncludeBosses, bool TownNpcs, bool Banners,
    bool Twins, bool Separate, bool Gold, bool Army, bool OppositeEvil, bool Misc)
{
    internal static MappingOptions FromConfig(RandomizerConfig config) => new(config.IncludeBosses,
        config.RandomizeTownNpcs, config.GroupNpcVariants, config.CombineTwins, config.SeparateProgressionPools,
        config.GroupGoldCritters, config.GroupOldOnesArmyTiers, config.IncludeOppositeEvil, config.GroupMiscVariants);

    internal TagCompound Save() => new()
    {
        ["IncludeBosses"] = IncludeBosses, ["TownNpcs"] = TownNpcs, ["Banners"] = Banners,
        ["Twins"] = Twins, ["Separate"] = Separate, ["Gold"] = Gold,
        ["Army"] = Army, ["OppositeEvil"] = OppositeEvil, ["Misc"] = Misc
    };

    internal static MappingOptions Load(TagCompound tag)
    {
        WorldMapping.Require(tag, "IncludeBosses", "TownNpcs", "Banners", "Twins", "Separate", "Gold", "Army", "OppositeEvil", "Misc");
        return new(tag.GetBool("IncludeBosses"), tag.GetBool("TownNpcs"), tag.GetBool("Banners"),
            tag.GetBool("Twins"), tag.GetBool("Separate"), tag.GetBool("Gold"), tag.GetBool("Army"),
            tag.GetBool("OppositeEvil"), tag.GetBool("Misc"));
    }
}
