using Ares.Editor.Core.Content;
using Ares.Editor.Core.GameData;

namespace Ares.Editor.Core.Matching;

/// <summary>How often an item comes up in a container or on a socket that spawns.</summary>
/// <param name="Item">The item's technical name.</param>
/// <param name="Label">The item's in-game name, else its technical name.</param>
/// <param name="Group">What it came from (tier, pool entry, extra stack, rule list).</param>
/// <param name="Chance">Share of rolls with at least one stack of it from this group.</param>
/// <param name="MinAmount">Smallest stack seen.</param>
/// <param name="MaxAmount">Largest stack seen.</param>
/// <param name="Added">Whether a contents profile's <c>add</c> brought it.</param>
public sealed record ItemOdds(string Item, string Label, string Group, double Chance, int MinAmount, int MaxAmount, bool Added = false);

/// <summary>How often a group (tier, pool entry, extra stack, rule list) gives a stack.</summary>
/// <param name="Group">The group.</param>
/// <param name="Chance">Share of rolls with at least one stack from it.</param>
/// <param name="ExpectedStacks">Average stacks from it per roll.</param>
/// <param name="Items">Its items, most frequent first.</param>
public sealed record GroupOdds(string Group, double Chance, double ExpectedStacks, IReadOnlyList<ItemOdds> Items);

/// <summary>A share of the rolls.</summary>
/// <param name="Name">What the share is of.</param>
/// <param name="Share">The share, from 0 to 1.</param>
public sealed record OddsShare(string Name, double Share);

/// <summary>
/// What a container or loose item socket holds when it spawns, estimated by drawing its contents many times with the
/// server's roll (<see cref="ContainerContentRolls"/>, <see cref="LooseItemRolls"/>) from a fixed seed, so the numbers stay
/// put between views. Spawning itself (its chance, group fills, limiters) is left out: the shares are of the rolls.
/// </summary>
/// <param name="Rolls">How many rolls.</param>
/// <param name="Pools">What the rolls drew from, by share.</param>
/// <param name="StackCounts">Number of stacks, by share (from 0).</param>
/// <param name="Groups">The groups, most frequent first.</param>
/// <param name="Note">Why nothing is rolled, or null.</param>
/// <param name="Problems">What the server would refuse in the contents file.</param>
/// <param name="Warnings">What the server accepts but may not be meant, such as profile additions the items' placement lists refuse.</param>
public sealed record ContentOdds(
    int Rolls,
    IReadOnlyList<OddsShare> Pools,
    IReadOnlyList<OddsShare> StackCounts,
    IReadOnlyList<GroupOdds> Groups,
    string? Note,
    IReadOnlyList<string> Problems,
    IReadOnlyList<string> Warnings)
{
    // Rolls per estimate: ±1 percentage point at worst (95 %).
    private const int DefaultRolls = 10_000;

    private const int Seed = 20_261_119;

    /// <summary>Estimates a container's contents when it spawns.</summary>
    /// <param name="gameData">The game data.</param>
    /// <param name="spawn">The spawn settings (which spawner classes are enabled).</param>
    /// <param name="rolls">The container roll of the current contents file.</param>
    /// <param name="container">The container.</param>
    /// <param name="match">What the rules decide for it.</param>
    /// <param name="count">How many rolls.</param>
    /// <returns>The odds.</returns>
    public static ContentOdds ForContainer(EditorGameData gameData, LootSpawnDocument spawn, ContainerContentRolls rolls, LootContainer container, ContainerMatch match,
        int count = DefaultRolls)
    {
        ArgumentNullException.ThrowIfNull(gameData);
        ArgumentNullException.ThrowIfNull(spawn);
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(match);
        RollData data = gameData.Roll;

        // The classes it may be, with their tag, slots and weight, as the server picks them.
        (string Class, string? Tag, int Slots, double Weight)[] classes = container.Source switch
        {
            LootContainerSource.Spawner => [.. container.SpawnerClasses
                .Where(entry => spawn.SpawnerClasses.Contains(entry.ClassPath, StringComparer.Ordinal) && entry.Tag is not null && data.ServedSlots(entry.ClassPath) is not null)
                .Select(entry => (entry.ClassPath, entry.Tag, data.ServedSlots(entry.ClassPath)!.Value, Math.Max(0, entry.Weight)))],
            _ => [.. container.ClassPaths.Where(path => data.ServedSlots(path) is not null)
                .Select(path => (path, container.Tags.Count > 0 ? container.Tags[0] : null, data.ServedSlots(path)!.Value, 1.0))],
        };
        if (classes.Length == 0)
        {
            return Empty(container.Source == LootContainerSource.Spawner
                ? "No enabled spawner class is a container the server serves, so nothing is placed."
                : "No class of it is a container the server serves.", rolls.Problems);
        }

        if (classes.All(entry => entry.Weight <= 0))
        {
            classes = [.. classes.Select(entry => entry with { Weight = 1 })];
        }

        (LootArea Area, double Weight)[] areas = container is { Source: LootContainerSource.Spawner, Points.Count: > 0 }
            ? [.. container.Points.Select(point => (point.Area, Math.Max(0, point.Weight)))]
            : [(container.Area, 1)];
        if (areas.All(entry => entry.Weight <= 0))
        {
            areas = [.. areas.Select(entry => entry with { Weight = 1 })];
        }

        Dictionary<string, string?> labels = gameData.ContainerItems.ToDictionary(item => item.TechnicalName, item => item.DisplayName, StringComparer.Ordinal);
        Random random = new(Seed);
        string ruleName = match.LootRule is { } rule ? rule.Name ?? "(unnamed)" : string.Empty;
        LootList? list = match.LootRule?.Loot;
        RolledContents[] samples = new RolledContents[count];
        for (int index = 0; index < count; index++)
        {
            (string path, string? tag, int slots, _) = Pick(classes, entry => entry.Weight, random);
            samples[index] = list is not null
                ? rolls.Roll(ruleName, list, labels, slots, random)
                : rolls.Roll(tag, path, slots, Pick(areas, entry => entry.Weight, random).Area, random);
        }

        string[] warnings = list is not null ? [] : [.. classes.SelectMany(entry => rolls.PlacementWarnings(entry.Tag, entry.Class)).Distinct(StringComparer.Ordinal)];
        return Of(samples, rolls.Problems, warnings);
    }

    /// <summary>Estimates a loose item socket's stack when it spawns.</summary>
    /// <param name="rolls">The loose item roll of the current contents file.</param>
    /// <param name="socket">The socket.</param>
    /// <param name="match">What the rules decide for it.</param>
    /// <param name="count">How many rolls.</param>
    /// <returns>The odds.</returns>
    public static ContentOdds ForLoose(LooseItemRolls rolls, LooseItemSocket socket, LooseItemMatch match, int count = DefaultRolls)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(match);
        LootList? list = match.LootRule?.Loot is { } listed && rolls.CanHold(listed) ? listed : null;
        Random random = new(Seed);
        RolledContents[] samples = new RolledContents[count];
        string pool = list is not null ? $"rule {match.LootRule!.Name ?? "(unnamed)"}" : rolls.Pool(socket.Tag)?.Name ?? "tables";
        for (int index = 0; index < count; index++)
        {
            RolledStack? stack = list is not null ? rolls.Roll(list, random) : rolls.Roll(socket.Tag, socket.Area, random);
            samples[index] = new RolledContents(pool, stack is null ? [] : [stack]);
        }

        IReadOnlyList<string> warnings = list is not null ? [] : rolls.PlacementWarnings(socket.Tag);
        return samples.All(sample => sample.Stacks.Count == 0)
            ? Empty("No item can lie on a socket of this tag, so the server leaves it out.", rolls.Problems, warnings)
            : Of(samples, rolls.Problems, warnings);
    }

    private static ContentOdds Empty(string note, IReadOnlyList<string> problems, IReadOnlyList<string>? warnings = null) => new(0, [], [], [], note, problems, warnings ?? []);

    // Tallies the rolls.
    private static ContentOdds Of(RolledContents[] samples, IReadOnlyList<string> problems, IReadOnlyList<string> warnings)
    {
        double total = samples.Length;
        Dictionary<(string Group, string Item), (string Label, int Rolls, int Min, int Max, bool Added)> items = [];
        Dictionary<string, (int Rolls, int Stacks)> groups = new(StringComparer.Ordinal);
        foreach (RolledContents sample in samples)
        {
            foreach (IGrouping<string, RolledStack> group in sample.Stacks.GroupBy(stack => stack.Group, StringComparer.Ordinal))
            {
                (int rolls, int stacks) = groups.GetValueOrDefault(group.Key);
                groups[group.Key] = (rolls + 1, stacks + group.Count());
                foreach (IGrouping<string, RolledStack> item in group.GroupBy(stack => stack.Item, StringComparer.Ordinal))
                {
                    (string Group, string Item) key = (group.Key, item.Key);
                    int min = item.Min(stack => stack.Amount);
                    int max = item.Max(stack => stack.Amount);
                    bool added = item.Any(stack => stack.Added);
                    items[key] = items.TryGetValue(key, out (string Label, int Rolls, int Min, int Max, bool Added) seen)
                        ? (seen.Label, seen.Rolls + 1, Math.Min(seen.Min, min), Math.Max(seen.Max, max), seen.Added || added)
                        : (item.First().Label, 1, min, max, added);
                }
            }
        }

        return new ContentOdds(samples.Length,
            [.. samples.GroupBy(sample => sample.Pool ?? "nothing").Select(group => new OddsShare(group.Key, group.Count() / total)).OrderByDescending(share => share.Share)],
            [.. samples.GroupBy(sample => sample.Stacks.Count).OrderBy(group => group.Key).Select(group => new OddsShare(group.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), group.Count() / total))],
            [.. groups.Select(group => new GroupOdds(group.Key, group.Value.Rolls / total, group.Value.Stacks / total,
                    [.. items.Where(item => item.Key.Group == group.Key)
                        .Select(item => new ItemOdds(item.Key.Item, item.Value.Label, group.Key, item.Value.Rolls / total, item.Value.Min, item.Value.Max, item.Value.Added))
                        .OrderByDescending(item => item.Chance).ThenBy(item => item.Label, StringComparer.Ordinal)]))
                .OrderByDescending(group => group.ExpectedStacks).ThenBy(group => group.Group, StringComparer.Ordinal)],
            null, problems, warnings);
    }

    // A weighted draw, as the server's.
    private static T Pick<T>(T[] entries, Func<T, double> weight, Random random)
    {
        if (entries.Length == 1)
        {
            return entries[0];
        }

        double roll = random.NextDouble() * entries.Sum(weight);
        foreach (T entry in entries)
        {
            if (roll < weight(entry))
            {
                return entry;
            }

            roll -= weight(entry);
        }

        return entries[^1];
    }
}
