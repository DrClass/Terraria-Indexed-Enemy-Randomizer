using System;
using System.Collections.Generic;
using System.Linq;

namespace EnemyRandomizer;

// No Main.rand, System.Random, or string.GetHashCode: results must survive reloads
// and be identical on different operating systems and .NET versions.
/// <summary>
/// Deterministic selection algorithms used to build a world's source-to-replacement mapping.
/// These use a fixed hash and PRNG so runtime RNG state cannot reshuffle a world.
/// </summary>
internal static class SeededSelection
{
    /// <summary>
    /// Builds a one-to-one permutation of sorted, distinct IDs using Sattolo's algorithm.
    /// With more than one candidate, no source maps to itself; a singleton necessarily does.
    /// </summary>
    /// <param name="seed">World seed text; treated literally, without culture-dependent conversion.</param>
    /// <param name="group">Stable salt identifying the pool, such as crimson:pre.</param>
    /// <param name="candidates">Candidate net IDs; duplicates are removed and order is canonicalized.</param>
    /// <returns>A source-to-target permutation containing every distinct candidate exactly once on each side.</returns>
    public static Dictionary<int, int> Shuffle(string seed, string group, IEnumerable<int> candidates)
    {
        // Canonical ordering makes the same candidate set produce the same map, regardless of enumeration order.
        int[] sources = candidates.Distinct().OrderBy(id => id).ToArray();
        int[] targets = (int[])sources.Clone();
        uint state = Hash(seed, group);
        // Xorshift cannot escape an all-zero state, so give that rare hash a fixed nonzero starting value.
        if (state == 0)
            state = 0x9e3779b9;

        // Sattolo's shuffle is a permutation with no fixed points (for n > 1).
        // Sorting first makes load order and dictionary enumeration irrelevant.
        for (int i = targets.Length - 1; i > 0; i--)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            int j = (int)(state % (uint)i);
            (targets[i], targets[j]) = (targets[j], targets[i]);
        }
        // Pair each original position with its shuffled target; every target appears exactly once.
        var result = new Dictionary<int, int>();
        for (int i = 0; i < sources.Length; i++)
            result.Add(sources[i], targets[i]);
        return result;
    }

    /// <summary>
    /// Chooses a deterministic fallback for a source outside the main permutation.
    /// Avoids selecting the source itself when another candidate exists; requires a nonempty pool.
    /// </summary>
    /// <param name="seed">World seed text.</param>
    /// <param name="source">Stable source identity, such as a vanilla net ID or qualified mod NPC name.</param>
    /// <param name="pool">Nonempty, deterministically ordered list of legal targets.</param>
    /// <param name="sourceNetId">Original ID to avoid selecting when alternatives exist.</param>
    /// <returns>One deterministic target ID; this fallback is not a one-to-one assignment across sources.</returns>
    /// <exception cref="ArgumentException">The replacement pool is empty.</exception>
    public static int Pick(string seed, string source, IReadOnlyList<int> pool, int sourceNetId)
    {
        if (pool.Count == 0)
            throw new ArgumentException("The replacement pool cannot be empty.", nameof(pool));

        // Fallback selection is stable per source identity and does not consume Terraria's random stream.
        uint hash = Hash(seed, source);

        // Exclude self without giving the next entry twice the probability.
        int self = -1;
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i] == sourceNetId)
                self = i;
        }
        // Treat the source's own slot as absent, then translate the compacted index back into the original list.
        bool excludeSelf = self >= 0 && pool.Count > 1;
        int count = pool.Count;
        if (excludeSelf)
            count--;

        int index = (int)(hash % (uint)count);
        if (excludeSelf && index >= self)
            index++;
        return pool[index];
    }

    // Repair only forbidden edges of the seeded permutation. Augmenting paths
    // move existing assignments when needed, preserving one source per target.
    // Unlike retrying random shuffles, this terminates and finds a perfect
    // matching whenever one exists, or reports an invalid candidate catalog.
    /// <summary>
    /// Repairs progression-forbidden assignments in a seeded permutation while retaining full coverage.
    /// Uses augmenting paths to find a legal one-to-one assignment, or throws if none exists.
    /// </summary>
    /// <param name="seed">World seed text.</param>
    /// <param name="group">Stable salt identifying this permutation.</param>
    /// <param name="candidates">IDs participating on both sides of the permutation.</param>
    /// <param name="allowed">Predicate receiving original source first and replacement target second.</param>
    /// <returns>A complete legal permutation, or an exception if the restrictions make one impossible.</returns>
    /// <exception cref="InvalidOperationException">No complete legal assignment exists for the supplied pool.</exception>
    public static Dictionary<int, int> ConstrainedShuffle(string seed, string group,
        IEnumerable<int> candidates, Func<int, int, bool> allowed)
    {
        // Start with the normal seeded mapping, preserving every assignment that already passes the restrictions.
        var preferred = Shuffle(seed, group, candidates);
        int[] sources = preferred.Keys.OrderBy(id => id).ToArray();
        var result = new Dictionary<int, int>();
        foreach (var pair in preferred)
        {
            if (Valid(pair.Key, pair.Value))
                result.Add(pair.Key, pair.Value);
        }
        // Only targets removed by the restriction filter still need a source; keep a seeded search order for each.
        var assigned = result.Values.ToHashSet();
        var preferences = new Dictionary<int, int[]>();
        foreach (int target in sources)
        {
            if (assigned.Contains(target))
                continue;
            // Each repair gets its own visited set so recursion cannot loop through the same source repeatedly.
            if (!Assign(target, new HashSet<int>()))
                throw new InvalidOperationException($"No progression-safe NPC permutation exists for {group} (target {target}).");
        }
        return result;

        // A multi-entry pool still forbids self-mapping in addition to the caller's progression restrictions.
        bool Valid(int source, int target) => (sources.Length == 1 || source != target) && allowed(source, target);

        // Search for a free source, recursively moving its previous target elsewhere when necessary.
        bool Assign(int target, HashSet<int> visited)
        {
            // Cache this target's legal source preference order so recursive visits make identical choices.
            if (!preferences.TryGetValue(target, out int[] order))
            {
                order = Shuffle(seed, group + ":progression:" + target.ToString(System.Globalization.CultureInfo.InvariantCulture), sources)
                    .OrderBy(pair => pair.Key).Select(pair => pair.Value)
                    .Where(source => Valid(source, target)).ToArray();
                preferences.Add(target, order);
            }
            // Prefer free sources so already-safe mappings change as little as possible.
            foreach (int source in order)
            {
                if (result.ContainsKey(source))
                    continue;
                if (!visited.Add(source))
                    continue;

                result[source] = target;
                return true;
            }
            // If no free source works, reroute an occupied assignment along an augmenting path.
            foreach (int source in order)
            {
                if (!visited.Add(source))
                    continue;

                int previousTarget = result[source];
                if (Assign(previousTarget, visited))
                {
                    result[source] = target;
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Hashes the seed and source identity with explicitly defined UTF-16 byte order and overflow.
    /// The separator distinguishes seed/source boundaries, and the final mixing spreads similar inputs.
    /// </summary>
    private static uint Hash(string seed, string source)
    {
        // Use fixed arithmetic rather than string.GetHashCode, whose output is not stable across processes.
        uint hash = 2166136261;
        foreach (char c in seed + "\0" + source)
        {
            hash = unchecked((hash ^ (byte)c) * 16777619);
            hash = unchecked((hash ^ (byte)(c >> 8)) * 16777619);
        }
        // Avalanche the accumulated bits so nearby seed strings do not produce similarly ordered choices.
        hash ^= hash >> 16;
        hash = unchecked(hash * 0x7feb352d);
        hash ^= hash >> 15;
        hash = unchecked(hash * 0x846ca68b);
        return hash ^ (hash >> 16);
    }
}
