using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace EnemyRandomizer;

/// <summary>
/// Immutable name aliases and net ID for one NPC, independent of Terraria's live content tables.
/// </summary>
internal sealed record NpcNameEntry(int NetId, string DisplayName, string InternalName, string FullName);

/// <summary>
/// Resolves IDs and human-readable NPC names with deterministic precedence and useful ambiguity errors.
/// </summary>
internal static class NpcNameResolver
{
    /// <summary>
    /// Tries numeric ID, full name, internal name, display name, then substring matches in that order.
    /// Returns false with a user-facing error rather than silently choosing among ambiguous candidates.
    /// </summary>
    /// <param name="input">Name or numeric ID supplied by the caller.</param>
    /// <param name="entries">Available IDs and name aliases.</param>
    /// <param name="netId">Resolved ID on success; zero on failure.</param>
    /// <param name="error">Null on success, or a concise explanation on failure.</param>
    /// <returns>True only when the input resolves to exactly one available ID.</returns>
    internal static bool TryResolve(string input, IEnumerable<NpcNameEntry> entries,
        out int netId, out string error)
    {
        netId = 0;
        error = null;
        // Accept surrounding quotes for multi-word names without requiring the command parser to strip them first.
        string query = input.Trim().Trim('"', '\'').Trim();
        NpcNameEntry[] all = entries.ToArray();
        // An explicit numeric ID is authoritative; do not reinterpret an unknown number as a partial name.
        if (int.TryParse(query, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
        {
            if (all.Any(entry => entry.NetId == id))
            {
                netId = id;
                return true;
            }
            error = $"Unknown NPC ID: {id}.";
            return false;
        }

        // Normalize once for comparison while keeping the original query intact for readable error messages.
        string key = Normalize(query);
        if (key.Length == 0)
        {
            error = "Enter an NPC name or ID, for example: Zombie, Eye of Cthulhu, or 3.";
            return false;
        }

        // Internal names take priority: many size/costume variants share the
        // display name 'Zombie', but NPCID.Zombie must resolve to the base NPC.
        NpcNameEntry[] matches = all.Where(entry => Normalize(entry.FullName) == key).ToArray();
        if (matches.Length == 0)
            matches = all.Where(entry => Normalize(entry.InternalName) == key).ToArray();
        if (matches.Length == 0)
            matches = all.Where(entry => Normalize(entry.DisplayName) == key).ToArray();
        if (matches.Length == 0)
            matches = all.Where(entry => Normalize(entry.DisplayName).Contains(key)
                || Normalize(entry.InternalName).Contains(key) || Normalize(entry.FullName).Contains(key)).ToArray();
        // Aliases may converge on one ID; deduplicate before deciding whether the result is ambiguous.
        matches = matches.DistinctBy(entry => entry.NetId).OrderBy(entry => entry.NetId).ToArray();
        if (matches.Length == 1)
        {
            netId = matches[0].NetId;
            return true;
        }
        // Cap suggestions at eight so broad partial-name searches cannot flood chat.
        if (matches.Length == 0)
        {
            error = $"No NPC matches '{query}'. Try its full name or ID.";
            return false;
        }

        string suggestions = string.Join(", ", matches.Take(8)
            .Select(entry => $"{entry.FullName} [{entry.NetId}]"));
        error = "Several NPCs match. Use an ID or full name: " + suggestions;
        if (matches.Length > 8)
            error += $" (+{matches.Length - 8} more)";
        return false;
    }

    /// <summary>
    /// Removes punctuation/spacing and lowercases invariantly so equivalent name spellings compare equally.
    /// </summary>
    private static string Normalize(string value) =>
        new((value ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
