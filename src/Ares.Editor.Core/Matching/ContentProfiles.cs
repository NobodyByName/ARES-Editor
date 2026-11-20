using Ares.Editor.Core.Content;
using Ares.Editor.Core.GameData;

namespace Ares.Editor.Core.Matching;

/// <summary>A merged profile value and the profile it comes from.</summary>
/// <typeparam name="T">The value's type.</typeparam>
/// <param name="Value">The value.</param>
/// <param name="Profile">The nearest profile that sets it.</param>
public sealed record ProfileValue<T>(T Value, LootProfile Profile);

/// <summary>
/// The contents profiles that apply to a container or loose item socket, merged (ARES-Server
/// <c>Ares.Raid.World.Loot.MergedLootProfile.Resolve</c> resolves them the same way): the profiles of the tag's chain from
/// the top down to the tag itself, then the profile of the container's class, a later one overriding. Single values come
/// from the nearest profile that sets them, tier multipliers per tier name, weights and additions per match key; null or
/// empty members fall back to the file's defaults.
/// </summary>
public sealed class MergedProfile
{
    // A merge of no profile: the file's defaults apply.
    private static readonly MergedProfile s_none = new([]);

    private MergedProfile(IReadOnlyList<LootProfile> chain)
    {
        Chain = chain;
        Dictionary<string, ProfileValue<LootWeight>> weights = new(StringComparer.Ordinal);
        Dictionary<string, ProfileValue<LootAddition>> additions = new(StringComparer.Ordinal);
        Dictionary<string, ProfileValue<double>> tiers = new(StringComparer.Ordinal);

        // Nearest first: the first profile that sets a value wins, and keyed entries come out nearest first.
        foreach (LootProfile profile in chain.Reverse())
        {
            if (Only is null && profile.Only is { } only)
            {
                Only = new ProfileValue<IReadOnlyList<LootMatch>>([.. only], profile);
            }

            if (Stacks is null && profile.Stacks is { } stacks)
            {
                Stacks = new ProfileValue<CountRange>(new CountRange(stacks.Min, stacks.Max), profile);
            }

            if (FirstStackTier is null && profile.SetsFirstStackTier)
            {
                FirstStackTier = new ProfileValue<string?>(profile.FirstStackTier, profile);
            }

            if (profile.ExtraStack is { } extra)
            {
                SetsExtraStack = true;
                if (ExtraChance is null && extra.Chance is { } chance)
                {
                    ExtraChance = new ProfileValue<double>(chance, profile);
                }

                if (ExtraEntries is null && extra.Entries is { } entries)
                {
                    ExtraEntries = new ProfileValue<IReadOnlyList<LootTypeEntry>>([.. entries], profile);
                }
            }

            foreach (KeyValuePair<string, double> tier in profile.Tiers)
            {
                tiers.TryAdd(tier.Key, new ProfileValue<double>(tier.Value, profile));
            }

            foreach (LootWeight weight in profile.Weights ?? (IEnumerable<LootWeight>)[])
            {
                weights.TryAdd(weight.Key, new ProfileValue<LootWeight>(weight, profile));
            }

            foreach (LootAddition addition in profile.Additions ?? (IEnumerable<LootAddition>)[])
            {
                additions.TryAdd(addition.Key, new ProfileValue<LootAddition>(addition, profile));
            }
        }

        Weights = [.. weights.Values];
        Additions = [.. additions.Values];
        Tiers = tiers;
    }

    /// <summary>The profiles that apply, farthest first.</summary>
    public IReadOnlyList<LootProfile> Chain { get; }

    /// <summary>The <c>only</c> matches, or null when no profile sets them.</summary>
    public ProfileValue<IReadOnlyList<LootMatch>>? Only { get; }

    /// <summary>The weights, one per match key, nearest profile first.</summary>
    public IReadOnlyList<ProfileValue<LootWeight>> Weights { get; }

    /// <summary>The additions, one per match key, nearest profile first.</summary>
    public IReadOnlyList<ProfileValue<LootAddition>> Additions { get; }

    /// <summary>Tier multipliers by tier name; a tier without an entry keeps its weight.</summary>
    public IReadOnlyDictionary<string, ProfileValue<double>> Tiers { get; }

    /// <summary>The first stack's tier (null value: none), or null when no profile sets it.</summary>
    public ProfileValue<string?>? FirstStackTier { get; }

    /// <summary>The number of stacks, or null when no profile sets it.</summary>
    public ProfileValue<CountRange>? Stacks { get; }

    /// <summary>Whether a profile of the chain has an <c>extraStack</c>, which gives the extra stack to any container.</summary>
    public bool SetsExtraStack { get; }

    /// <summary>The extra stack's chance, or null when no profile sets it.</summary>
    public ProfileValue<double>? ExtraChance { get; }

    /// <summary>The extra stack's entries, or null when no profile sets them.</summary>
    public ProfileValue<IReadOnlyList<LootTypeEntry>>? ExtraEntries { get; }

    /// <summary>Whether no weight, <c>only</c> or addition changes which items come up or how often.</summary>
    public bool KeepsItems => Only is null && Weights.Count == 0 && Additions.Count == 0;

    /// <summary>Merges profiles.</summary>
    /// <param name="chain">The profiles, farthest first.</param>
    /// <returns>The merge.</returns>
    public static MergedProfile Of(IReadOnlyList<LootProfile> chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        return chain.Count == 0 ? s_none : new MergedProfile(chain);
    }

    /// <summary>An item's pick weight multiplier: the product of every weight that matches it (1 without one).</summary>
    /// <param name="item">The item.</param>
    /// <returns>The multiplier.</returns>
    public double Multiplier(RollItem item) =>
        Weights.Where(weight => ContentProfiles.Matches(weight.Value, item)).Aggregate(1.0, (product, weight) => product * weight.Value.Multiplier);

    /// <summary>Whether an item stays a candidate: it matches <see cref="Only"/> when set, and its multiplier is above 0.</summary>
    /// <param name="item">The item.</param>
    /// <returns>True when it stays.</returns>
    public bool Keeps(RollItem item) => (Only is null || Only.Value.Any(match => ContentProfiles.Matches(match, item))) && Multiplier(item) > 0;

    /// <summary>A tier's weight multiplier (1 without an entry).</summary>
    /// <param name="tier">The tier's name.</param>
    /// <returns>The multiplier.</returns>
    public double TierMultiplier(string tier) => Tiers.TryGetValue(tier, out ProfileValue<double>? multiplier) ? multiplier.Value : 1;
}

/// <summary>A profile the server refuses, and why.</summary>
/// <param name="Profile">The profile.</param>
/// <param name="Message">What is wrong.</param>
public sealed record ProfileProblem(LootProfile Profile, string Message)
{
    /// <inheritdoc />
    public override string ToString() => $"Profile {Profile.Name}: {Message}; the server refuses to start.";
}

/// <summary>
/// Finds and checks the contents profiles of <c>loot-contents.json</c>, as ARES-Server
/// <c>Ares.Common.Game.Content.LootSettingsContent</c> does.
/// </summary>
public static class ContentProfiles
{
    /// <summary>The tag at or below which container profiles go.</summary>
    public const string ContainerRoot = "Socket.LootContainer";

    /// <summary>The tag at or below which loose item socket profiles go.</summary>
    public const string LooseRoot = "Socket.Loot";

    /// <summary>Whether a tag is a loose item socket tag (<c>Socket.Loot</c> or below), whose profiles set no container-only member.</summary>
    /// <param name="tag">The tag.</param>
    /// <returns>True for a loose item tag.</returns>
    public static bool IsLooseTag(string tag) => LootMatching.IsTagOrBelow(tag, LooseRoot);

    /// <summary>
    /// The profiles that apply to a container or loose item socket, farthest first: those of the tag and the tags above it
    /// (ignoring case), from the top down, then the class's.
    /// </summary>
    /// <param name="contents">The contents file.</param>
    /// <param name="tag">The container or socket tag, or null when it has none.</param>
    /// <param name="containerClass">The container's class path, or null for a loose item socket.</param>
    /// <returns>The chain.</returns>
    public static IReadOnlyList<LootProfile> ChainOf(LootContentsDocument contents, string? tag, string? containerClass)
    {
        ArgumentNullException.ThrowIfNull(contents);
        List<LootProfile> chain = [];
        if (contents.Profiles.Count == 0)
        {
            return chain;
        }

        if (tag is not null)
        {
            string[] parts = tag.Split('.');
            for (int depth = 1; depth <= parts.Length; depth++)
            {
                string prefix = string.Join('.', parts, 0, depth);
                if (contents.Profiles.FirstOrDefault(profile => profile.Tag is { } own && string.Equals(own, prefix, StringComparison.OrdinalIgnoreCase)) is { } found)
                {
                    chain.Add(found);
                }
            }
        }

        if (containerClass is not null && contents.Profiles.FirstOrDefault(profile => string.Equals(profile.Class, containerClass, StringComparison.Ordinal)) is { } byClass)
        {
            chain.Add(byClass);
        }

        return chain;
    }

    /// <summary>The merged profile of a container or loose item socket (see <see cref="ChainOf"/>).</summary>
    /// <param name="contents">The contents file.</param>
    /// <param name="tag">The tag, or null.</param>
    /// <param name="containerClass">The container's class path, or null for a loose item socket.</param>
    /// <returns>The merge.</returns>
    public static MergedProfile Resolve(LootContentsDocument contents, string? tag, string? containerClass) => MergedProfile.Of(ChainOf(contents, tag, containerClass));

    /// <summary>Whether a match selects an item: it carries the drop tag or one below it (ignoring case), or it is the named item.</summary>
    /// <param name="match">The match.</param>
    /// <param name="item">The item.</param>
    /// <returns>True when it matches.</returns>
    public static bool Matches(LootMatch match, RollItem item)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(item);
        return match.DropTag is { } dropTag ? item.DropTags.Any(tag => LootMatching.IsTagOrBelow(tag, dropTag))
            : match.Item is { } name && string.Equals(item.TechnicalName, name, StringComparison.Ordinal);
    }

    /// <summary>
    /// The items an addition brings: the named item (any plain item), or every item carrying the drop tag that the file's
    /// <c>excludedDropTags</c> leave in.
    /// </summary>
    /// <param name="addition">The addition.</param>
    /// <param name="items">Every plain item.</param>
    /// <param name="allowed">The items <c>excludedDropTags</c> leave in.</param>
    /// <returns>The items.</returns>
    public static IEnumerable<RollItem> Brings(LootMatch addition, IEnumerable<RollItem> items, IEnumerable<RollItem> allowed)
    {
        ArgumentNullException.ThrowIfNull(addition);
        return addition.DropTag is not null ? allowed.Where(item => Matches(addition, item)) : items.Where(item => Matches(addition, item));
    }

    /// <summary>What the server refuses in the profiles of a contents file.</summary>
    /// <param name="contents">The contents file.</param>
    /// <param name="data">What the roll reads from the game data.</param>
    /// <returns>The problems, in file order.</returns>
    public static IReadOnlyList<ProfileProblem> Validate(LootContentsDocument contents, RollData data)
    {
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentNullException.ThrowIfNull(data);
        List<ProfileProblem> problems = [];
        if (contents.Profiles.Count == 0)
        {
            return problems;
        }

        HashSet<string> items = new(data.Items.Select(item => item.TechnicalName), StringComparer.Ordinal);
        HashSet<string> tiers = new(contents.Tiers.Select(tier => tier.Name), StringComparer.Ordinal);
        HashSet<string> names = new(StringComparer.Ordinal);
        HashSet<string> tags = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> classes = new(StringComparer.Ordinal);
        foreach (LootProfile profile in contents.Profiles)
        {
            if (!names.Add(profile.Name))
            {
                Add("another profile has the same name");
            }

            if ((profile.Tag is null) == (profile.Class is null))
            {
                Add("it needs exactly one of tag and class");
            }

            if (profile.Tag is { } tag)
            {
                if (!LootMatching.IsTagOrBelow(tag, ContainerRoot) && !IsLooseTag(tag))
                {
                    Add($"its tag {tag} is not {ContainerRoot} or {LooseRoot} or below");
                }

                if (!tags.Add(tag))
                {
                    Add($"another profile has the tag {tag}");
                }

                if (IsLooseTag(tag) && (profile.SetsFirstStackTier || profile.Stacks is not null || profile.ExtraStack is not null))
                {
                    Add("a loose item profile sets no firstStackTier, stacks or extraStack");
                }
            }

            if (profile.Class is { } containerClass)
            {
                if (data.ServedSlots(containerClass) is null)
                {
                    Add($"its class {containerClass} is not a container class the server serves");
                }

                if (!classes.Add(containerClass))
                {
                    Add($"another profile has the class {containerClass}");
                }
            }

            foreach (LootMatch match in (profile.Only ?? (IEnumerable<LootMatch>)[]).Concat(profile.Weights ?? (IEnumerable<LootMatch>)[])
                .Concat(profile.Additions ?? (IEnumerable<LootMatch>)[]))
            {
                if ((match.DropTag is null) == (match.Item is null))
                {
                    Add("a match needs exactly one of dropTag and item");
                }
                else if (match.Item is { } item && !items.Contains(item))
                {
                    Add($"no item the roll draws from is named {item}");
                }
                else if (match.DropTag is { } dropTag && !data.Items.Any(entry => entry.DropTags.Any(carried => LootMatching.IsTagOrBelow(carried, dropTag))))
                {
                    Add($"no item carries the drop tag {dropTag}");
                }
            }

            foreach (LootWeight weight in profile.Weights ?? (IEnumerable<LootWeight>)[])
            {
                if (weight.Multiplier < 0)
                {
                    Add($"the weight of {weight.Text} is below 0");
                }
            }

            foreach (LootAddition addition in profile.Additions ?? (IEnumerable<LootAddition>)[])
            {
                CheckRange(addition.Amount, 1, $"the amount of {addition.Text}", Add);
                if (addition.Tier is { } tier && !tiers.Contains(tier))
                {
                    Add($"{addition.Text} names the unknown tier {tier}");
                }
            }

            foreach (KeyValuePair<string, double> tier in profile.Tiers)
            {
                if (!tiers.Contains(tier.Key))
                {
                    Add($"tiers names the unknown tier {tier.Key}");
                }

                if (tier.Value < 0)
                {
                    Add($"the multiplier of tier {tier.Key} is below 0");
                }
            }

            if (profile.FirstStackTier is { } first && !tiers.Contains(first))
            {
                Add($"firstStackTier names the unknown tier {first}");
            }

            if (profile.Stacks is { } stacks)
            {
                CheckRange(stacks, 0, "stacks", Add);
            }

            if (profile.ExtraStack is not { } extra)
            {
                continue;
            }

            if (extra.Chance is < 0 or > 1)
            {
                Add("the extra stack's chance is not between 0 and 1");
            }

            foreach (LootTypeEntry entry in extra.Entries ?? (IEnumerable<LootTypeEntry>)[])
            {
                CheckRange(entry.Amount, 1, $"the amount of extra stack entry {entry.DropTag}", Add);
                if (entry.Weight < 0)
                {
                    Add($"the weight of extra stack entry {entry.DropTag} is below 0");
                }
            }

            continue;

            void Add(string message) => problems.Add(new ProfileProblem(profile, message));
        }

        return problems;
    }

    private static void CheckRange(LootCount range, int lowest, string what, Action<string> add)
    {
        if (range.Min < lowest)
        {
            add($"{what} starts below {lowest}");
        }

        if (range.Min > range.Max)
        {
            add($"{what} has its min above its max");
        }
    }
}
