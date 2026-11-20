using Ares.Editor.Core.Content;
using Ares.Editor.Core.GameData;

namespace Ares.Editor.Core.Matching;

/// <summary>One group of sockets whose number of filled sockets a rule's group fill draws together.</summary>
public sealed class LootFillGroup
{
    private readonly List<int> _members = [];

    internal LootFillGroup(LootRule rule, string key, string tag, LootArea area, double areaMultiplier)
    {
        Rule = rule;
        Key = key;
        Tag = tag;
        Area = area;
        AreaMultiplier = areaMultiplier;
    }

    /// <summary>The rule whose group fill decides the group.</summary>
    public LootRule Rule { get; }

    /// <summary>
    /// The group's key within its rule, as ARES-Server keys it: empty for the containers the rule lists, else the prop and
    /// tag (<c>cell/actor/component/TAG</c>, the tag in upper case).
    /// </summary>
    public string Key { get; }

    /// <summary>Whether the group is the containers the rule lists by id (else one placed prop's sockets of a tag).</summary>
    public bool IsListed => Key.Length == 0;

    /// <summary>The members' tag when they all have the same one (compared without case), else null.</summary>
    public string? Tag { get; private set; }

    /// <summary>The members: positions in <see cref="MapLoot.Containers"/>, in map order.</summary>
    public IReadOnlyList<int> Members => _members;

    /// <summary>How many sockets the group has.</summary>
    public int Size => _members.Count;

    /// <summary>The loot value of the area around its first socket, which ARES-Server uses for the whole group.</summary>
    public LootArea Area { get; }

    /// <summary>The factor of <see cref="Area"/> from <c>areaMultipliers</c>, which scales the group's fill (see <see cref="GroupFillShares.InArea"/>).</summary>
    public double AreaMultiplier { get; }

    /// <summary>The chances of each number of filled sockets for this group, in its area.</summary>
    public IReadOnlyList<FillShare> Chances => GroupFillShares.Of(Rule.GroupFill).InArea(Size, AreaMultiplier);

    /// <summary>How many of its sockets get a container on average, in its area (see <see cref="GroupFillShares.ExpectedFilled"/>).</summary>
    public double ExpectedFilled => GroupFillShares.Of(Rule.GroupFill).ExpectedFilled(Size, AreaMultiplier);

    internal void Add(int index, string tag)
    {
        _members.Add(index);
        if (Tag is not null && !string.Equals(Tag, tag, StringComparison.OrdinalIgnoreCase))
        {
            Tag = null;
        }
    }
}

/// <summary>What a rule's group fill covers on a map.</summary>
/// <param name="Groups">How many groups.</param>
/// <param name="Containers">How many sockets in all.</param>
/// <param name="Sizes">How many groups have each size, by size in ascending order.</param>
/// <param name="Tag">The tag every member has (compared without case), or null when they differ or there is none.</param>
/// <param name="ExpectedFilled">How many of the sockets get a container per raid on average, each group in its area.</param>
public sealed record LootFillSummary(int Groups, int Containers, IReadOnlyList<KeyValuePair<int, int>> Sizes, string? Tag, double ExpectedFilled);

/// <summary>
/// The groups of a map's sockets that group fills decide, ported from ARES-Server's
/// <c>Ares.Raid.World.Loot.LootCatalog.SocketDecisions</c>: a socket joins a group when its deciding spawn rule (see
/// <see cref="LootMatching.Match"/>) sets a group fill. The sockets the rule lists by id form one group; every other
/// socket groups with the sockets of the same placed prop and tag (key: rule position, then <c>""</c> or
/// <c>cell/actor/component/TAG</c>). Each group draws its number of filled sockets (at most its size, its weights scaled
/// by the area multiplier of its first socket, <c>Ares.Common.Game.Loot.LootFill.Draw</c>), then that many of its
/// sockets at random. Groups are in the order of
/// their first socket. Keep it in step with those members.
/// </summary>
public sealed class LootFillGroups
{
    private readonly LootFillGroup[] _groups;
    private readonly int[] _groupOf;

    private LootFillGroups(LootFillGroup[] groups, int[] groupOf)
    {
        _groups = groups;
        _groupOf = groupOf;
    }

    /// <summary>No groups.</summary>
    public static LootFillGroups Empty { get; } = new([], []);

    /// <summary>Every group, in the order of its first socket.</summary>
    public IReadOnlyList<LootFillGroup> Groups => _groups;

    /// <summary>Finds the groups of a map from what the current documents decide for its containers.</summary>
    /// <param name="map">The map.</param>
    /// <param name="matches">The match of each container of <paramref name="map"/>, by position (<see cref="LootMatching.Match"/>).</param>
    /// <returns>The groups.</returns>
    public static LootFillGroups Of(MapLoot map, IReadOnlyList<ContainerMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(matches);
        if (matches.Count != map.Containers.Count)
        {
            throw new ArgumentException("One match per container is needed.", nameof(matches));
        }

        List<LootFillGroup> groups = [];
        int[] groupOf = new int[map.Containers.Count];
        Array.Fill(groupOf, -1);
        Dictionary<LootRule, (HashSet<string> Listed, Dictionary<string, int> Groups)> byRule = new(ReferenceEqualityComparer.Instance);
        for (int index = 0; index < groupOf.Length; index++)
        {
            ContainerMatch match = matches[index];
            LootContainer container = map.Containers[index];
            if (match is not { Decision: SpawnDecision.GroupFill, SpawnRule: { } rule } || container.Source != LootContainerSource.Socket)
            {
                continue;
            }

            if (!byRule.TryGetValue(rule, out (HashSet<string> Listed, Dictionary<string, int> Groups) ofRule))
            {
                byRule[rule] = ofRule = (new HashSet<string>(rule.Containers, StringComparer.Ordinal), new Dictionary<string, int>(StringComparer.Ordinal));
            }

            string tag = container.Tags[0];
            string key = ofRule.Listed.Contains(container.Id) ? string.Empty : $"{container.Prop}/{tag.ToUpperInvariant()}";
            if (!ofRule.Groups.TryGetValue(key, out int group))
            {
                ofRule.Groups[key] = group = groups.Count;
                groups.Add(new LootFillGroup(rule, key, tag, container.Area, match.AreaMultiplier));
            }

            groups[group].Add(index, tag);
            groupOf[index] = group;
        }

        return new LootFillGroups([.. groups], groupOf);
    }

    /// <summary>The group of a container, or null when no group fill decides it.</summary>
    /// <param name="index">The container's position in <see cref="MapLoot.Containers"/>.</param>
    /// <returns>The group, or null.</returns>
    public LootFillGroup? GroupOf(int index) => index >= 0 && index < _groupOf.Length && _groupOf[index] >= 0 ? _groups[_groupOf[index]] : null;

    /// <summary>The groups a rule decides, in the order of their first socket.</summary>
    /// <param name="rule">The rule.</param>
    /// <returns>The groups.</returns>
    public LootFillGroup[] Of(LootRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return Array.FindAll(_groups, group => ReferenceEquals(group.Rule, rule));
    }

    /// <summary>What a rule's group fill covers: its groups, their sizes and the containers it fills per raid on average.</summary>
    /// <param name="rule">The rule.</param>
    /// <returns>The summary.</returns>
    public LootFillSummary Summarize(LootRule rule)
    {
        LootFillGroup[] groups = Of(rule);
        GroupFillShares shares = GroupFillShares.Of(rule.GroupFill);
        string? tag = groups.Length > 0 && groups.All(group => group.Tag is not null && string.Equals(group.Tag, groups[0].Tag, StringComparison.OrdinalIgnoreCase))
            ? groups[0].Tag
            : null;
        return new LootFillSummary(
            groups.Length,
            groups.Sum(group => group.Size),
            [.. groups.GroupBy(group => group.Size).OrderBy(sizes => sizes.Key).Select(sizes => new KeyValuePair<int, int>(sizes.Key, sizes.Count()))],
            tag,
            groups.Sum(group => shares.ExpectedFilled(group.Size, group.AreaMultiplier)));
    }
}
