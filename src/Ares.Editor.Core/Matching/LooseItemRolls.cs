using System.Collections.Concurrent;
using Ares.Editor.Core.Content;
using Ares.Editor.Core.GameData;

namespace Ares.Editor.Core.Matching;

/// <summary>
/// The stack of a loose item socket, ported from ARES-Server <c>Ares.Raid.World.Loot.LooseItemRolls</c> so the editor can
/// estimate what the server puts there: a weighted choice between the table items (an item of the
/// <c>looseItems</c> tables whose placement list accepts the socket's tag, by tier weight) and each entry of the
/// socket's pool. The contents profiles of the socket's tag chain (<see cref="ContentProfiles"/>) add, filter and weight
/// the items and scale the tiers. An operator rule's list gives one of its items that can lie in the world instead.
/// </summary>
public sealed class LooseItemRolls
{
    private const string TableGroupPrefix = "Table: ";

    private readonly RollData _data;
    private readonly LootContentsDocument _contents;
    private readonly LootLooseItems? _settings;
    private readonly RollItem[] _allowed;
    private readonly (RollItem Item, int Tier)[] _tableItems;
    private readonly Dictionary<LootTypeEntry, RollItem[]> _entryItems = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, RollItem> _pickups;
    private readonly List<string> _problems = [];

    // What each tag draws from, worked out once per tag (thread-safe: the editor estimates odds off its UI thread).
    private readonly ConcurrentDictionary<string, Prepared> _prepared = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Prepares the table items and pool entries from the game data and the contents file.</summary>
    /// <param name="data">What the roll reads from the game data.</param>
    /// <param name="contents">The contents file.</param>
    public LooseItemRolls(RollData data, LootContentsDocument contents)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _contents = contents ?? throw new ArgumentNullException(nameof(contents));
        _settings = contents.LooseItems;
        _pickups = data.Items.ToDictionary(item => item.TechnicalName, StringComparer.Ordinal);
        _allowed = ContainerContentRolls.Allowed(data.Items, contents.ExcludedDropTags);
        if (_settings is null)
        {
            _tableItems = [];
            _problems.Add("loot-contents.json has no looseItems; the server refuses to start.");
            return;
        }

        foreach (string name in _settings.Tables.Where(name => !data.ValueTables.Any(table => string.Equals(table.Name, name, StringComparison.OrdinalIgnoreCase))))
        {
            _problems.Add($"looseItems names the unknown table {name}; the server refuses to start.");
        }

        _problems.AddRange(ContentProfiles.Validate(contents, data).Select(problem => problem.ToString()));
        ValueTableEntry[] entries = [.. data.ValueTables.Where(table => _settings.Tables.Contains(table.Name, StringComparer.OrdinalIgnoreCase)).SelectMany(table => table.Entries)];
        _tableItems = [.. _allowed.Where(item => entries.Any(entry => entry.Selects(item)))
            .Select(item => (item, ContainerContentRolls.TierIndex(contents, item))).Where(entry => entry.Item2 >= 0)];
        foreach (LootTypeEntry entry in _settings.Pools.SelectMany(pool => pool.Entries))
        {
            _entryItems.TryAdd(entry, [.. _allowed.Where(item => ContainerContentRolls.Matches(item, entry))]);
        }
    }

    /// <summary>What the server would refuse in the contents file, such as unknown value loot tables.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>The pool of a loose item tag: the one listing the tag, else its nearest parent tag.</summary>
    /// <param name="tag">The socket's tag.</param>
    /// <returns>The pool, or null.</returns>
    public LootLoosePool? Pool(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        if (_settings is null)
        {
            return null;
        }

        for (string current = tag; current.Length > 0; current = current.LastIndexOf('.') is var dot and > 0 ? current[..dot] : string.Empty)
        {
            if (_settings.Pools.FirstOrDefault(pool => pool.Tags.Contains(current, StringComparer.OrdinalIgnoreCase)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Whether a rule's list has an item that can lie in the world, so it decides the socket's stack.</summary>
    /// <param name="list">The rule's list.</param>
    /// <returns>True when it has one.</returns>
    public bool CanHold(LootList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        return list.Items.Any(entry => _pickups.ContainsKey(entry.Item));
    }

    /// <summary>Draws a socket's stack from an operator rule's list.</summary>
    /// <param name="list">The rule's list.</param>
    /// <param name="random">Random numbers.</param>
    /// <returns>The stack, or null when the list has no item that can lie in the world.</returns>
    public RolledStack? Roll(LootList list, Random random)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(random);
        LootItem[] items = [.. list.Items.Where(entry => _pickups.ContainsKey(entry.Item) && entry.Weight > 0)];
        if (items.Length == 0)
        {
            return null;
        }

        double roll = random.NextDouble() * items.Sum(entry => entry.Weight);
        LootItem chosen = items[^1];
        foreach (LootItem entry in items)
        {
            if (roll < entry.Weight)
            {
                chosen = entry;
                break;
            }

            roll -= entry.Weight;
        }

        RollItem item = _pickups[chosen.Item];
        CountRange amount = chosen.Amount is { } range ? new CountRange(range.Min, range.Max) : new CountRange(1, 1);
        return new RolledStack(item.TechnicalName, item.Label, ContainerContentRolls.Amount(item, amount, random), "Rule list");
    }

    /// <summary>Draws a socket's default stack.</summary>
    /// <param name="tag">The socket's tag.</param>
    /// <param name="area">Loot value of the area around the socket.</param>
    /// <param name="random">Random numbers.</param>
    /// <returns>The stack, or null when nothing can lie there.</returns>
    public RolledStack? Roll(string tag, LootArea area, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (_settings is null)
        {
            return null;
        }

        Prepared prepared = Prepare(tag);
        LootLoosePool? pool = Pool(tag);
        List<(PreparedEntry? Entry, double Weight)> sources = [];
        double tableWeight = pool?.TableWeight ?? 1;
        if (prepared.Candidates.Length > 0 && tableWeight > 0)
        {
            sources.Add((null, tableWeight));
        }

        sources.AddRange(prepared.Entries.Where(entry => entry.Entry.Weight > 0 && entry.Items.Length > 0).Select(entry => ((PreparedEntry?)entry, entry.Entry.Weight)));
        if (sources.Count == 0)
        {
            return null;
        }

        double roll = random.NextDouble() * sources.Sum(source => source.Weight);
        PreparedEntry? chosen = sources[^1].Entry;
        foreach ((PreparedEntry? entry, double weight) in sources)
        {
            if (roll < weight)
            {
                chosen = entry;
                break;
            }

            roll -= weight;
        }

        if (chosen is not null)
        {
            WeightedItem[] items = chosen.Items;
            RollItem item = items[ContainerContentRolls.Pick(items, entry => entry.Weight, random)].Item;
            return new RolledStack(item.TechnicalName, item.Label,
                ContainerContentRolls.Amount(item, new CountRange(chosen.Entry.Amount.Min, chosen.Entry.Amount.Max), random), $"{pool!.Name}: {chosen.Entry.DropTag}");
        }

        int tier = ContainerContentRolls.PickTier(_contents, prepared.Profile, [.. prepared.Candidates.Select(entry => entry.Tier).Distinct().Order()], area, random);
        if (tier < 0)
        {
            return null;
        }

        Candidate[] inTier = [.. prepared.Candidates.Where(entry => entry.Tier == tier)];
        Candidate picked = inTier[ContainerContentRolls.Pick(inTier, entry => entry.Weight, random)];
        CountRange amount = picked.Amount ?? new CountRange(_settings.Amount.Min, _settings.Amount.Max);
        return new RolledStack(picked.Item.TechnicalName, picked.Item.Label, ContainerContentRolls.Amount(picked.Item, amount, random),
            TableGroupPrefix + _contents.Tiers[tier].Name, picked.Amount is not null);
    }

    /// <summary>The items the profiles of a socket add although their placement list does not accept its tag, one line per addition.</summary>
    /// <param name="tag">The socket's tag.</param>
    /// <returns>The warnings.</returns>
    public IReadOnlyList<string> PlacementWarnings(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        return ContainerContentRolls.PlacementWarnings(Prepare(tag).Profile, tag, _data.Items, _allowed);
    }

    // The table items the tag accepts and the items its profiles add (an added item taking the amount and tier of the
    // first, nearest, addition that brings it), filtered and weighted by the profiles; and its pool's entries, the same.
    private Prepared Prepare(string tag) => _prepared.GetOrAdd(tag, key =>
    {
        MergedProfile profile = ContentProfiles.Resolve(_contents, key, null);
        Candidate[] candidates =
        [
            .. _tableItems.Where(entry => ContainerContentRolls.MatchesContainerTag(entry.Item, key)).Select(entry => new Candidate(entry.Item, entry.Tier, 1, null)),
        ];
        IEnumerable<LootTypeEntry> poolEntries = Pool(key)?.Entries ?? (IEnumerable<LootTypeEntry>)[];
        PreparedEntry[] entries = [.. poolEntries.Select(entry => new PreparedEntry(entry, [.. _entryItems[entry].Select(item => new WeightedItem(item, 1))]))];
        if (profile.KeepsItems)
        {
            return new Prepared(profile, candidates, entries);
        }

        Dictionary<RollItem, Candidate> added = new(ReferenceEqualityComparer.Instance);
        foreach (ProfileValue<LootAddition> addition in profile.Additions)
        {
            foreach (RollItem item in ContentProfiles.Brings(addition.Value, _data.Items, _allowed))
            {
                int tier = addition.Value.Tier is { } name ? _contents.Tiers.ToList().FindIndex(entry => entry.Name == name) : ContainerContentRolls.TierIndex(_contents, item);
                if (tier >= 0)
                {
                    added.TryAdd(item, new Candidate(item, tier, 1, new CountRange(addition.Value.Amount.Min, addition.Value.Amount.Max)));
                }
            }
        }

        candidates =
        [
            .. candidates.Where(entry => !added.ContainsKey(entry.Item)).Concat(added.Values).Where(entry => profile.Keeps(entry.Item))
                .Select(entry => entry with { Weight = profile.Multiplier(entry.Item) }),
        ];
        entries =
        [
            .. entries.Select(entry => entry with
            {
                Items = [.. entry.Items.Where(item => profile.Keeps(item.Item)).Select(item => item with { Weight = profile.Multiplier(item.Item) })],
            }),
        ];
        return new Prepared(profile, candidates, entries);
    });

    // A table item of a tag, with the stack size of the addition that brought it (null: the looseItems amount).
    private sealed record Candidate(RollItem Item, int Tier, double Weight, CountRange? Amount);

    private sealed record WeightedItem(RollItem Item, double Weight);

    private sealed record PreparedEntry(LootTypeEntry Entry, WeightedItem[] Items);

    private sealed record Prepared(MergedProfile Profile, Candidate[] Candidates, PreparedEntry[] Entries);
}
