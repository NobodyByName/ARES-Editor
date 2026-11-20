using System.Collections.Concurrent;
using Ares.Editor.Core.Content;
using Ares.Editor.Core.GameData;

namespace Ares.Editor.Core.Matching;

/// <summary>One stack a roll gave, for the odds.</summary>
/// <param name="Item">The item's technical name.</param>
/// <param name="Label">The item's in-game name, else its technical name.</param>
/// <param name="Amount">Items in the stack.</param>
/// <param name="Group">What it came from: the tier, the type pool entry's drop tag, the extra stack or the rule list.</param>
/// <param name="Added">Whether a contents profile's <c>add</c> brought the item.</param>
public sealed record RolledStack(string Item, string Label, int Amount, string Group, bool Added = false);

/// <summary>What one roll gave a container.</summary>
/// <param name="Pool">What it drew from (as the server's logs name it), or null when nothing covers the container.</param>
/// <param name="Stacks">The stacks.</param>
public sealed record RolledContents(string? Pool, IReadOnlyList<RolledStack> Stacks);

/// <summary>
/// The default contents roll of a loot container, ported from ARES-Server <c>Ares.Raid.World.Loot.LootContentRolls</c>
/// so the editor can estimate what the server puts in a container: a type pool draws its stacks from its entries; a
/// table pool draws 2 or 3 stacks (or its own range) from the items its value tables select whose placement list accepts
/// the container's tag, one of the first stack's tier first, then by tier weight scaled by the area's loot value, never
/// an item twice nor next to an item of a group either avoids; then maybe the extra stack. The contents profiles of the
/// container's tag chain and class (<see cref="ContentProfiles"/>) add, filter and weight the items and change the
/// number of stacks, the tiers and the extra stack. It reads the current (possibly unsaved) <c>loot-contents.json</c>.
/// </summary>
public sealed class ContainerContentRolls
{
    // The group name of the extra stack's stacks, before the entry's drop tag.
    private const string ExtraGroup = "Extra stack";

    // The group name of a type pool's stacks that a profile's add gave, before the match.
    private const string AddedGroup = "Added";

    private readonly RollData _data;
    private readonly LootContentsDocument _contents;
    private readonly RollItem[] _items;
    private readonly TargetStacks[] _targets;
    private readonly int _firstTier;
    private readonly Dictionary<RollItem, HashSet<string>> _dropTagGroups;
    private readonly Dictionary<string, PreparedPool> _tablePools = new(StringComparer.OrdinalIgnoreCase);
    // What a container of a tag and class draws from, worked out once per pair (thread-safe: the editor estimates odds
    // off its UI thread).
    private readonly ConcurrentDictionary<(string Tag, string Class), Prepared> _prepared = new(TagClassComparer.Instance);
    private readonly List<string> _problems = [];

    /// <summary>Prepares the pools from the game data and the contents file.</summary>
    /// <param name="data">What the roll reads from the game data.</param>
    /// <param name="contents">The contents file.</param>
    public ContainerContentRolls(RollData data, LootContentsDocument contents)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _contents = contents ?? throw new ArgumentNullException(nameof(contents));
        List<LootTier> tiers = [.. contents.Tiers];
        _firstTier = contents.FirstStackTier is { } first ? tiers.FindIndex(tier => tier.Name == first) : -1;
        _targets = [.. contents.TargetStacks.Select(target => new TargetStacks(target.Target, target.Weight))];
        _items = Allowed(data.Items, contents.ExcludedDropTags);
        _dropTagGroups = _items.ToDictionary<RollItem, RollItem, HashSet<string>>(item => item, item => new HashSet<string>(item.DropTags.SelectMany(TagAndParents), StringComparer.OrdinalIgnoreCase),
            ReferenceEqualityComparer.Instance);
        foreach (LootTablePool pool in contents.TablePools)
        {
            _tablePools.TryAdd(pool.Tag, Prepare(pool));
        }

        if (_targets.Length == 0 || Array.Exists(_targets, target => target.Target < 1 || !(double.IsFinite(target.Weight) && target.Weight >= 0)))
        {
            _problems.Add("targetStacks must list at least one target of 1 or more stacks with a weight of 0 or more; the server refuses to start.");
        }

        _problems.AddRange(ContentProfiles.Validate(contents, data).Select(problem => problem.ToString()));
    }

    /// <summary>What the server would refuse in the contents file and the roll skips, such as unknown value loot tables.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>The items a list of drop tags leaves in (ARES-Server: <c>excludedDropTags</c>).</summary>
    internal static RollItem[] Allowed(IEnumerable<RollItem> items, IReadOnlyList<string> excluded) =>
        [.. items.Where(item => !item.DropTags.Any(tag => excluded.Any(parent => LootMatching.IsTagOrBelow(tag, parent))))];

    /// <summary>Whether an item's placement list accepts a container or loose item tag (ARES-Server <c>LootContentRolls.MatchesContainerTag</c>).</summary>
    /// <param name="item">The item.</param>
    /// <param name="tag">The container's tag.</param>
    /// <returns>True when the item may spawn there.</returns>
    public static bool MatchesContainerTag(RollItem item, string tag)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(tag);
        return item.Placements.Any(placement => placement.SocketTag.Length > 0 && LootMatching.IsTagOrBelow(tag, placement.SocketTag)
            && !placement.ExcludeTags.Any(excluded => LootMatching.IsTagOrBelow(tag, excluded)));
    }

    /// <summary>Draws a container's contents from an operator rule's list, as the server does.</summary>
    /// <param name="rule">Name of the rule.</param>
    /// <param name="list">The rule's contents.</param>
    /// <param name="labels">In-game names by technical name.</param>
    /// <param name="slotCount">Slots of the container.</param>
    /// <param name="random">Random numbers.</param>
    /// <returns>The contents.</returns>
    public RolledContents Roll(string rule, LootList list, IReadOnlyDictionary<string, string?> labels, int slotCount, Random random)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(random);
        int target = list.Stacks is { } stacks ? Uniform(new CountRange(Math.Min(stacks.Min, slotCount), Math.Min(stacks.Max, slotCount)), random)
            : Math.Min(Target(random), slotCount);
        LootItem[] items = [.. list.Items.Where(entry => entry.Weight > 0)];
        double total = items.Sum(entry => entry.Weight);
        List<RolledStack> drawn = [];
        while (items.Length > 0 && drawn.Count < target)
        {
            double roll = random.NextDouble() * total;
            LootItem picked = items[^1];
            foreach (LootItem entry in items)
            {
                if (roll < entry.Weight)
                {
                    picked = entry;
                    break;
                }

                roll -= entry.Weight;
            }

            CountRange amount = picked.Amount is { } range ? new CountRange(range.Min, range.Max) : new CountRange(1, 1);
            drawn.Add(new RolledStack(picked.Item, labels.GetValueOrDefault(picked.Item) ?? picked.Item, Math.Max(1, Uniform(amount, random)), "Rule list"));
        }

        return new RolledContents($"rule {rule}", drawn);
    }

    /// <summary>Draws a container's default contents, as the server does.</summary>
    /// <param name="containerTag">The container's tag, or null when it has none.</param>
    /// <param name="containerClass">The container's class path.</param>
    /// <param name="slotCount">Slots of the container.</param>
    /// <param name="area">Loot value of the area around the container.</param>
    /// <param name="random">Random numbers.</param>
    /// <returns>The contents.</returns>
    public RolledContents Roll(string? containerTag, string containerClass, int slotCount, LootArea area, Random random)
    {
        ArgumentNullException.ThrowIfNull(containerClass);
        ArgumentNullException.ThrowIfNull(random);
        Prepared prepared = Prepare(containerTag, containerClass);
        if (prepared.TypePool is { } type)
        {
            List<RolledStack> stacks = [];
            List<RollItem> present = [];
            CountRange range = prepared.Profile.Stacks?.Value ?? new CountRange(type.Stacks.Min, type.Stacks.Max);
            int target = Math.Min(Uniform(range, random), slotCount);
            while (stacks.Count < target && Draw(prepared.Entries, present, false, random) is { } stack)
            {
                stacks.Add(stack.Stack);
                present.Add(stack.Item);
            }

            if (prepared.Profile.SetsExtraStack && stacks.Count < slotCount && random.NextDouble() < prepared.ExtraChance
                && Draw(prepared.ExtraEntries, present, true, random) is { } extra)
            {
                stacks.Add(extra.Stack with { Group = $"{ExtraGroup}: {extra.Stack.Group}" });
            }

            return new RolledContents(type.Name, stacks);
        }

        if (prepared.Pool is not { } pool)
        {
            return new RolledContents(null, []);
        }

        if (prepared.Candidates.Length == 0)
        {
            return new RolledContents(pool.Pool.Name, []);
        }

        List<RolledStack> drawn = [];
        List<RollItem> held = [];
        List<PoolItem> available = [.. prepared.Candidates];
        CountRange? stackRange = prepared.Profile.Stacks?.Value ?? (pool.Pool.Stacks is { } poolStacks ? new CountRange(poolStacks.Min, poolStacks.Max) : null);
        int stackTarget = Math.Min(stackRange is { } chosenRange ? Uniform(chosenRange, random) : Target(random), slotCount);
        while (drawn.Count < stackTarget && available.Count > 0)
        {
            int tier = drawn.Count == 0 && prepared.FirstTier >= 0 && available.Exists(entry => entry.Tier == prepared.FirstTier)
                ? prepared.FirstTier
                : PickTier(_contents, prepared.Profile, [.. available.Select(entry => entry.Tier).Distinct().Order()], area, random);
            if (tier < 0)
            {
                break;
            }

            PoolItem[] inTier = [.. available.Where(entry => entry.Tier == tier)];
            PoolItem picked = inTier[Pick(inTier, entry => entry.Weight, random)];
            drawn.Add(new RolledStack(picked.Item.TechnicalName, picked.Item.Label, Amount(picked.Item, picked.StackSize, random), _contents.Tiers[tier].Name, picked.Added));
            held.Add(picked.Item);
            available.RemoveAll(entry => !Fits(entry.Item, picked.Item));
        }

        if ((pool.Pool.ExtraStack || prepared.Profile.SetsExtraStack) && drawn.Count < slotCount && random.NextDouble() < prepared.ExtraChance
            && Draw(prepared.ExtraEntries, held, true, random) is { } extraStack)
        {
            drawn.Add(extraStack.Stack with { Group = $"{ExtraGroup}: {extraStack.Stack.Group}" });
        }

        return new RolledContents(pool.Pool.Name, drawn);
    }

    /// <summary>
    /// The items the profiles of a container add although their placement list does not accept its tag (the server adds them
    /// anyway), one line per addition.
    /// </summary>
    /// <param name="containerTag">The container's tag, or null when it has none (then there is nothing to check).</param>
    /// <param name="containerClass">The container's class path.</param>
    /// <returns>The warnings.</returns>
    public IReadOnlyList<string> PlacementWarnings(string? containerTag, string containerClass)
    {
        ArgumentNullException.ThrowIfNull(containerClass);
        return containerTag is null ? [] : PlacementWarnings(Prepare(containerTag, containerClass).Profile, containerTag, _data.Items, _items);
    }

    /// <summary>The additions of a merged profile whose items' placement lists do not accept a tag, one line per addition.</summary>
    internal static IReadOnlyList<string> PlacementWarnings(MergedProfile profile, string tag, IReadOnlyList<RollItem> items, IReadOnlyList<RollItem> allowed) =>
    [
        .. profile.Additions
            .Select(addition => (Addition: addition, Refused: ContentProfiles.Brings(addition.Value, items, allowed).Where(item => !MatchesContainerTag(item, tag)).Select(item => item.Label).ToArray()))
            .Where(entry => entry.Refused.Length > 0)
            .Select(entry => $"Profile {entry.Addition.Profile.Name} adds {Listed(entry.Refused)} ({entry.Addition.Value.Text}), whose placement list does not accept {tag}."),
    ];

    // Up to five names, then how many more.
    private static string Listed(string[] names) => names.Length <= 5 ? string.Join(", ", names) : $"{string.Join(", ", names.Take(5))} and {names.Length - 5} more";

    /// <summary>The type pool of a container: the one listing its class, else one listing its tag or a parent of it.</summary>
    private LootTypePool? TypePool(string? containerTag, string containerClass) =>
        _contents.TypePools.FirstOrDefault(pool => pool.Classes.Contains(containerClass, StringComparer.Ordinal))
        ?? (containerTag is null ? null : _contents.TypePools.FirstOrDefault(pool => pool.Tags.Any(tag => LootMatching.IsTagOrBelow(containerTag, tag))));

    // One stack from weighted entries: an entry that still has an item not in the container, then one of its items by
    // the profiles' multiplier. Null when no entry has one left.
    private (RolledStack Stack, RollItem Item)? Draw(IReadOnlyList<DrawEntry> entries, List<RollItem> present, bool avoidGroups, Random random)
    {
        (DrawEntry Entry, WeightedItem[] Items)[] open =
        [
            .. entries.Select(entry => (Entry: entry, Items: entry.Items.Where(item => avoidGroups ? present.TrueForAll(other => Fits(item.Item, other))
                    : present.TrueForAll(other => !ReferenceEquals(other, item.Item))).ToArray()))
                .Where(entry => entry.Items.Length > 0 && entry.Entry.Weight > 0),
        ];
        if (open.Length == 0)
        {
            return null;
        }

        double roll = random.NextDouble() * open.Sum(entry => entry.Entry.Weight);
        (DrawEntry chosen, WeightedItem[] items) = open[^1];
        foreach ((DrawEntry Entry, WeightedItem[] Items) entry in open)
        {
            if (roll < entry.Entry.Weight)
            {
                (chosen, items) = entry;
                break;
            }

            roll -= entry.Entry.Weight;
        }

        RollItem item = items[Pick(items, entry => entry.Weight, random)].Item;
        return (new RolledStack(item.TechnicalName, item.Label, Amount(item, chosen.AmountRange, random), chosen.Group, chosen.Added), item);
    }

    /// <summary>
    /// A position by weight; when every weight is the same, an even draw (so contents without profile weights draw as they
    /// always did).
    /// </summary>
    internal static int Pick<T>(IReadOnlyList<T> entries, Func<T, double> weight, Random random)
    {
        double first = weight(entries[0]);
        bool even = true;
        double total = 0;
        foreach (T entry in entries)
        {
            double value = weight(entry);
            even &= value.Equals(first);
            total += value;
        }

        if (even)
        {
            return random.Next(entries.Count);
        }

        double roll = random.NextDouble() * total;
        for (int index = 0; index < entries.Count; index++)
        {
            if (roll < weight(entries[index]))
            {
                return index;
            }

            roll -= weight(entries[index]);
        }

        return entries.Count - 1;
    }

    // A tier (by position) by weight among those present; the area's loot value scales the weights.
    // The profiles' tier multipliers scale the weights too.
    // Tiers weighing 0 are never picked; -1 when no tier present weighs more than 0.
    internal static int PickTier(LootContentsDocument contents, MergedProfile profile, IReadOnlyList<int> tiers, LootArea area, Random random)
    {
        double[] weights =
        [
            .. tiers.Select(tier => contents.Tiers[tier].Weight * profile.TierMultiplier(contents.Tiers[tier].Name) * (contents.Tiers[tier].AreaMultipliers.Get(area.ToString()) ?? 1)),
        ];
        double total = weights.Sum();
        if (total <= 0)
        {
            return -1;
        }

        double roll = random.NextDouble() * total;
        for (int index = 0; index < tiers.Count; index++)
        {
            if (roll < weights[index])
            {
                return tiers[index];
            }

            roll -= weights[index];
        }

        return tiers[Array.FindLastIndex(weights, weight => weight > 0)];
    }

    // Position of an item's tier: the first tier whose drop tag it carries, else the tier without a drop tag (-1 when
    // there is none, which the server refuses).
    internal static int TierIndex(LootContentsDocument contents, RollItem item)
    {
        for (int index = 0; index < contents.Tiers.Count; index++)
        {
            if (contents.Tiers[index].DropTag is { } tag && item.DropTags.Any(drop => LootMatching.IsTagOrBelow(drop, tag)))
            {
                return index;
            }
        }

        return contents.Tiers.ToList().FindIndex(tier => tier.DropTag is null);
    }

    // The weighted target number of stacks of the contents file (ARES-Server LootContentRolls.Target); none without targets.
    private int Target(Random random)
    {
        double roll = random.NextDouble() * _targets.Sum(target => target.Weight);
        foreach (TargetStacks target in _targets)
        {
            if (roll < target.Weight)
            {
                return target.Target;
            }

            roll -= target.Weight;
        }

        return _targets.Length > 0 ? _targets[^1].Target : 0;
    }

    // Whether an item may join another in a container: not the same, and no avoid group of either covers the other.
    private bool Fits(RollItem item, RollItem other) => !ReferenceEquals(item, other) && !Avoids(item, other) && !Avoids(other, item);

    private bool Avoids(RollItem item, RollItem other) => item.AvoidGroups.Any(_dropTagGroups[other].Contains);

    private static IEnumerable<string> TagAndParents(string tag)
    {
        for (string current = tag; current.Length > 0; current = current.LastIndexOf('.') is var dot and > 0 ? current[..dot] : string.Empty)
        {
            yield return current;
        }
    }

    internal static int Amount(RollItem item, CountRange range, Random random) => Math.Max(1, Math.Min(Uniform(range, random), item.MaxStack));

    private static int Uniform(CountRange range, Random random) => range.Max <= range.Min ? range.Min : random.Next(range.Min, range.Max + 1);

    internal static bool Matches(RollItem item, LootTypeEntry entry) =>
        item.DropTags.Any(tag => LootMatching.IsTagOrBelow(tag, entry.DropTag))
        && !item.DropTags.Any(tag => entry.ExcludedDropTags.Any(excluded => LootMatching.IsTagOrBelow(tag, excluded)));

    private Prepared Prepare(string? containerTag, string containerClass) => _prepared.GetOrAdd((containerTag ?? string.Empty, containerClass), key =>
    {
        string? tag = key.Tag.Length > 0 ? key.Tag : null;
        MergedProfile profile = ContentProfiles.Resolve(_contents, tag, key.Class);
        int firstTier = profile.FirstStackTier is { } first
            ? first.Value is { } name ? _contents.Tiers.ToList().FindIndex(tier => tier.Name == name) : -1
            : _firstTier;
        double extraChance = profile.ExtraChance?.Value ?? _contents.ExtraStack.Chance;
        DrawEntry[] extraEntries = [.. (profile.ExtraEntries?.Value ?? _contents.ExtraStack.Entries).Select(entry => EntryOf(entry, profile))];
        if (TypePool(tag, key.Class) is { } type)
        {
            DrawEntry[] entries =
            [
                .. type.Entries.Select(entry => EntryOf(entry, profile)),
                .. profile.Additions.Select(addition => new DrawEntry($"{AddedGroup}: {addition.Value.Text}", 1,
                    new CountRange(addition.Value.Amount.Min, addition.Value.Amount.Max),
                    [.. ContentProfiles.Brings(addition.Value, _data.Items, _items).Where(profile.Keeps).Select(item => new WeightedItem(item, profile.Multiplier(item)))], true)),
            ];
            return new Prepared(profile, type, entries, null, [], firstTier, extraChance, extraEntries);
        }

        if (tag is null || FindTablePool(tag) is not { } pool)
        {
            return new Prepared(profile, null, [], null, [], firstTier, extraChance, extraEntries);
        }

        PoolItem[] candidates = [.. pool.Items.Where(entry => MatchesContainerTag(entry.Item, tag))];
        if (!profile.KeepsItems)
        {
            candidates = [.. WithAdditions(candidates, profile).Where(entry => profile.Keeps(entry.Item)).Select(entry => entry with { Weight = profile.Multiplier(entry.Item) })];
        }

        return new Prepared(profile, null, [], pool, candidates, firstTier, extraChance, extraEntries);
    });

    // The source candidates and the items the profiles add, an added item taking the amount and tier of the first (nearest)
    // addition that brings it.
    private IEnumerable<PoolItem> WithAdditions(PoolItem[] source, MergedProfile profile)
    {
        Dictionary<RollItem, PoolItem> added = new(ReferenceEqualityComparer.Instance);
        foreach (ProfileValue<LootAddition> addition in profile.Additions)
        {
            foreach (RollItem item in ContentProfiles.Brings(addition.Value, _data.Items, _items))
            {
                int tier = addition.Value.Tier is { } name ? _contents.Tiers.ToList().FindIndex(entry => entry.Name == name) : TierIndex(_contents, item);
                if (tier >= 0)
                {
                    added.TryAdd(item, new PoolItem(item, tier, new CountRange(addition.Value.Amount.Min, addition.Value.Amount.Max), 1, true));
                }
            }
        }

        return source.Where(entry => !added.ContainsKey(entry.Item)).Concat(added.Values);
    }

    // A type pool or extra stack entry's items, filtered and weighted by the profiles.
    private DrawEntry EntryOf(LootTypeEntry entry, MergedProfile profile) =>
        new(entry.DropTag, entry.Weight, new CountRange(entry.Amount.Min, entry.Amount.Max),
            [.. _items.Where(item => Matches(item, entry) && profile.Keeps(item)).Select(item => new WeightedItem(item, profile.Multiplier(item)))], false);

    // The pool of the tag itself, else of its nearest parent tag.
    private PreparedPool? FindTablePool(string containerTag)
    {
        for (string current = containerTag; current.Length > 0; current = current.LastIndexOf('.') is var dot and > 0 ? current[..dot] : string.Empty)
        {
            if (_tablePools.TryGetValue(current, out PreparedPool? pool))
            {
                return pool;
            }
        }

        return null;
    }

    // The items a pool's tables select, each with the stack size of the first entry that selects it.
    private PreparedPool Prepare(LootTablePool pool)
    {
        List<PoolItem> items = [];
        HashSet<RollItem> seen = new(ReferenceEqualityComparer.Instance);
        foreach (string name in pool.Tables)
        {
            ValueTable[] tables = [.. _data.ValueTables.Where(table => string.Equals(table.Name, name, StringComparison.OrdinalIgnoreCase))];
            if (tables.Length == 0)
            {
                _problems.Add($"Pool {pool.Name} names the unknown table {name}; the server refuses to start.");
                continue;
            }

            items.AddRange(tables.SelectMany(table => table.Entries)
                .SelectMany(entry => _items.Where(entry.Selects).Select(item => new PoolItem(item, TierIndex(_contents, item), entry.StackSize, 1, false)))
                .Where(entry => entry.Tier >= 0 && seen.Add(entry.Item)));
        }

        return new PreparedPool(pool, items);
    }

    private sealed record PoolItem(RollItem Item, int Tier, CountRange StackSize, double Weight, bool Added);

    private sealed record PreparedPool(LootTablePool Pool, IReadOnlyList<PoolItem> Items);

    private sealed record WeightedItem(RollItem Item, double Weight);

    private sealed record DrawEntry(string Group, double Weight, CountRange AmountRange, WeightedItem[] Items, bool Added);

    // What a container of one tag and class draws from: its merged profile, then its type pool's entries or its table
    // pool's candidates, its forced first tier and its extra stack.
    private sealed record Prepared(
        MergedProfile Profile,
        LootTypePool? TypePool,
        DrawEntry[] Entries,
        PreparedPool? Pool,
        PoolItem[] Candidates,
        int FirstTier,
        double ExtraChance,
        DrawEntry[] ExtraEntries);

    // Tags compare without case, class paths exactly.
    private sealed class TagClassComparer : IEqualityComparer<(string Tag, string Class)>
    {
        public static readonly TagClassComparer Instance = new();

        public bool Equals((string Tag, string Class) x, (string Tag, string Class) y) =>
            string.Equals(x.Tag, y.Tag, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Class, y.Class, StringComparison.Ordinal);

        public int GetHashCode((string Tag, string Class) obj) => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Tag), StringComparer.Ordinal.GetHashCode(obj.Class));
    }
}
