using Ares.Editor.Core.Content;
using Ares.Editor.Core.GameData;

namespace Ares.Editor.Core.Matching;

/// <summary>
/// The chance a dynamic loot spawner places a container in a raid, from ARES-Server's <c>DynamicLootRuns</c> (<c>ActiveGroups</c>,
/// <c>Pick</c>, <c>Run</c>) and the spawner class filter in <c>LootCatalog</c>. Keep it in step with those.
/// </summary>
/// <remarks>
/// Groups are treated as independent: two groups of one round that share a choice above them are in fact correlated, so a
/// spawner in several groups of a round gets an approximate chance. Data layers are ignored, as for the other containers.
/// </remarks>
public static class SpawnerChances
{
    /// <summary>The round configuration whose chance stands for a spawner's spawn chance: a normal raid.</summary>
    public const string DefaultRound = "Standard";

    /// <summary>
    /// The chance a group picks a given one of its spawners when its round runs: the chance every choice above it keeps its
    /// branch, times the expected share of its spawners it picks (a uniform count in [Min, Max], at most its spawner count).
    /// </summary>
    /// <param name="group">The group.</param>
    /// <returns>The chance from 0 to 1.</returns>
    public static double PickChance(SpawnerGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return group.Choices.Aggregate(Share(group.Min, group.Max, group.Spawners), (chance, choice) => chance * Share(choice.Min, choice.Max, choice.Of));
    }

    /// <summary>The chance at least one of a spawner's groups of a round picks it (a spawner picked twice runs once).</summary>
    /// <param name="container">The spawner.</param>
    /// <param name="round">The round configuration.</param>
    /// <returns>The chance from 0 to 1; 0 when no group of the round lists it.</returns>
    public static double RunChance(LootContainer container, string round)
    {
        ArgumentNullException.ThrowIfNull(container);
        double missed = container.SpawnerGroups.Where(group => group.Rounds.Contains(round, StringComparer.Ordinal))
            .Aggregate(1d, (missed, group) => missed * (1 - PickChance(group)));
        return 1 - missed;
    }

    /// <summary>
    /// The chance one container a run places is kept: the weight share of its classes the spawn settings enable and a
    /// container type lists (a drawn class that is disabled or unservable places nothing).
    /// </summary>
    /// <param name="container">The spawner.</param>
    /// <param name="spawn">The spawn settings.</param>
    /// <returns>The chance from 0 to 1.</returns>
    public static double ClassChance(LootContainer container, LootSpawnDocument spawn)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(spawn);
        if (container.SpawnerClasses.Count == 0)
        {
            return 0;
        }

        // As the server's weighted draw: negative weights count as 0, and all classes are equal when no weight is positive.
        double total = container.SpawnerClasses.Sum(entry => Math.Max(0, entry.Weight));
        return total > 0
            ? container.SpawnerClasses.Where(Kept).Sum(entry => Math.Max(0, entry.Weight)) / total
            : (double)container.SpawnerClasses.Count(Kept) / container.SpawnerClasses.Count;

        bool Kept(SpawnerClass entry) => entry.Tag is not null && spawn.SpawnerClasses.Contains(entry.ClassPath, StringComparer.Ordinal);
    }

    /// <summary>The chance a spawner places at least one container in a raid of a round.</summary>
    /// <param name="container">The spawner.</param>
    /// <param name="spawn">The spawn settings.</param>
    /// <param name="round">The round configuration.</param>
    /// <returns>The chance from 0 to 1.</returns>
    public static double Chance(LootContainer container, LootSpawnDocument spawn, string round)
    {
        int placed = Math.Min(container.SpawnerCount, container.Points.Count);
        return RunChance(container, round) * (1 - Math.Pow(1 - ClassChance(container, spawn), placed));
    }

    /// <summary>The rounds any of a spawner's groups runs in, the default round first, then by name.</summary>
    /// <param name="container">The spawner.</param>
    /// <returns>The round configurations.</returns>
    public static IReadOnlyList<string> Rounds(LootContainer container) =>
        [.. container.SpawnerGroups.SelectMany(group => group.Rounds).Distinct(StringComparer.Ordinal)
            .OrderBy(round => round == DefaultRound ? 0 : 1).ThenBy(round => round, StringComparer.Ordinal)];

    // The expected share of n items a uniform count in [min, max] (at most n) keeps.
    private static double Share(int min, int max, int n)
    {
        if (n <= 0)
        {
            return 0;
        }

        int low = Math.Max(0, min);
        int high = Math.Max(low, max);
        double kept = 0;
        for (int count = low; count <= high; count++)
        {
            kept += Math.Min(count, n);
        }

        return kept / (high - low + 1) / n;
    }
}
