using Ares.Editor.Core.Content;
using Ares.Editor.Core.GameData;

namespace Ares.Editor.Core.Matching;

/// <summary>How the server decides whether a container spawns.</summary>
public enum SpawnDecision
{
    /// <summary>
    /// The game's decision: the chance of the container's tag in <c>loot-spawn.json</c> times its area's multiplier; for a
    /// socket, the group cap and the placement limiters can still stop it.
    /// </summary>
    Default,

    /// <summary>A rule's <c>spawnChance</c>, rolled alone (no group cap or limiter stops a socket).</summary>
    RuleChance,

    /// <summary>A rule's <c>groupFill</c>: a number of the group's sockets drawn by weight get a container.</summary>
    GroupFill,

    /// <summary>
    /// A spawner runs when its dynamic loot group picks it, and places only the spawn settings' spawner classes; its chance is
    /// that of a <see cref="SpawnerChances.DefaultRound"/> raid (see <see cref="SpawnerChances"/>).
    /// </summary>
    SpawnerGroups,

    /// <summary>Never: no container type has the socket's tag, or no container type lists the placed container's class.</summary>
    Never,
}

/// <summary>What the loot settings and rules decide for one container, for display (nothing is rolled).</summary>
/// <param name="CoveringRules">Every rule that covers the container, in file order.</param>
/// <param name="Decision">How its spawning is decided.</param>
/// <param name="SpawnRule">The rule behind <see cref="SpawnDecision.RuleChance"/> or <see cref="SpawnDecision.GroupFill"/>, else null.</param>
/// <param name="ChanceTag">
/// The <c>chances</c> key the default chance comes from (the tag's own entry or its nearest parent's), or null when none
/// applies (chance 0) or the default chance does not apply to the container.
/// </param>
/// <param name="TagChance">The chance of <see cref="ChanceTag"/> (0 without one), or null when the default chance does not apply.</param>
/// <param name="AreaMultiplier">Factor of the container's area loot value from <c>areaMultipliers</c>.</param>
/// <param name="Chance">
/// The chance it spawns from 0 to 1: the rule's chance, or the default chance (<see cref="TagChance"/> times
/// <see cref="AreaMultiplier"/>, at most 1; before group caps and limiters), 0 for <see cref="SpawnDecision.Never"/>, or
/// null when a group fill decides. For the spawner groups, the chance it places a container in a
/// <see cref="SpawnerChances.DefaultRound"/> raid.
/// </param>
/// <param name="LootRule">The rule that sets its contents, or null for the default roll.</param>
/// <param name="RevealDelayRule">The rule that sets its reveal delay, or null for the container class's own.</param>
public sealed record ContainerMatch(
    IReadOnlyList<LootRule> CoveringRules,
    SpawnDecision Decision,
    LootRule? SpawnRule,
    string? ChanceTag,
    double? TagChance,
    double AreaMultiplier,
    double? Chance,
    LootRule? LootRule,
    LootRule? RevealDelayRule);

/// <summary>What the loot settings and rules decide for one loose item socket, for display (nothing is rolled).</summary>
/// <param name="CoveringRules">Every rule that covers the socket, in file order.</param>
/// <param name="SpawnRule">The last covering rule that sets a spawn chance, which replaces the default chance, or null.</param>
/// <param name="ChanceTag">
/// The <c>looseChances</c> key the default chance comes from (the tag's own entry or its nearest parent's), or null when
/// none applies (chance 0) or <see cref="SpawnRule"/> decides.
/// </param>
/// <param name="TagChance">The chance of <see cref="ChanceTag"/> (0 without one), or null when <see cref="SpawnRule"/> decides.</param>
/// <param name="AreaMultiplier">Factor of the socket's area loot value from <c>areaMultipliers</c>.</param>
/// <param name="Chance">
/// The chance it gets an item from 0 to 1: the rule's chance, or the default chance (<see cref="TagChance"/> times
/// <see cref="AreaMultiplier"/>, at most 1; before limiters).
/// </param>
/// <param name="LootRule">The last covering rule that sets a loot list, which gives the socket's one stack, or null for the default roll.</param>
public sealed record LooseItemMatch(
    IReadOnlyList<LootRule> CoveringRules,
    LootRule? SpawnRule,
    string? ChanceTag,
    double? TagChance,
    double AreaMultiplier,
    double Chance,
    LootRule? LootRule);

/// <summary>
/// Which loot settings and rules apply to a container, ported from ARES-Server so the editor shows what the server would
/// use: tag matching from <c>Ares.Common.Game.Loot.LootTags</c>, rule coverage and precedence from
/// <c>Ares.Common.Game.Loot.LootRule</c> / <c>LootRules</c>, tag chances and area multipliers from
/// <c>LootSpawnSettings</c>, and the per-source decision from <c>Ares.Raid.World.Loot.LootCatalog</c>
/// (<c>SocketDecisions</c>, <c>Spawners</c>, <c>Placed</c>, <c>Place</c>), and for loose item sockets from
/// <c>LootSpawnSettings.LooseChance</c> and <c>Ares.Raid.World.Loot.LooseLoot</c>. Keep it in step with those types.
/// </summary>
public static class LootMatching
{
    /// <summary>
    /// Whether a gameplay tag is another tag or below it (<c>A.B.C</c> is below <c>A.B</c>, <c>A.BC</c> is not), ignoring
    /// case (ARES-Server <c>LootTags.IsTagOrBelow</c>).
    /// </summary>
    /// <param name="tag">The tag.</param>
    /// <param name="parent">The other tag.</param>
    /// <returns>True when <paramref name="tag"/> equals <paramref name="parent"/> or lies below it.</returns>
    public static bool IsTagOrBelow(string tag, string parent)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(parent);
        return tag.StartsWith(parent, StringComparison.OrdinalIgnoreCase) && (tag.Length == parent.Length || tag[parent.Length] == '.');
    }

    /// <summary>
    /// Whether a rule covers a container (ARES-Server <c>LootRule.Covers</c>): every container when it lists neither tags
    /// nor containers; else a container whose id it lists (compared exactly), or one of whose tags is one of its tags or
    /// below it.
    /// </summary>
    /// <param name="rule">The rule.</param>
    /// <param name="id">The container's id, or null.</param>
    /// <param name="tags">The container's tags.</param>
    /// <returns>True when the rule applies.</returns>
    public static bool Covers(LootRule rule, string? id, IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(tags);
        IReadOnlyList<string> covered = rule.Tags;
        IReadOnlyList<string> containers = rule.Containers;
        return (covered.Count == 0 && containers.Count == 0)
            || (id is not null && containers.Contains(id, StringComparer.Ordinal))
            || tags.Any(tag => covered.Any(parent => IsTagOrBelow(tag, parent)));
    }

    /// <summary>The positions in <see cref="MapLoot.Containers"/> of the containers one of whose tags is a tag or below it.</summary>
    /// <param name="map">The map.</param>
    /// <param name="tag">The tag.</param>
    /// <returns>The positions, in ascending order.</returns>
    public static int[] IndicesWithTag(MapLoot map, string tag)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(tag);
        return Indices(map, container => container.Tags.Any(own => IsTagOrBelow(own, tag)));
    }

    /// <summary>The positions in <see cref="MapLoot.Containers"/> of the containers a rule covers (see <see cref="Covers"/>).</summary>
    /// <param name="map">The map.</param>
    /// <param name="rule">The rule.</param>
    /// <returns>The positions, in ascending order.</returns>
    public static int[] IndicesCoveredBy(MapLoot map, LootRule rule)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(rule);
        return Indices(map, container => Covers(rule, container.Id, container.Tags));
    }

    /// <summary>The positions in <see cref="MapLoot.LooseItemSockets"/> of the loose item sockets whose tag is a tag or below it.</summary>
    /// <param name="map">The map.</param>
    /// <param name="tag">The tag.</param>
    /// <returns>The positions, in ascending order.</returns>
    public static int[] LooseIndicesWithTag(MapLoot map, string tag)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(tag);
        return LooseIndices(map, socket => IsTagOrBelow(socket.Tag, tag));
    }

    /// <summary>The positions in <see cref="MapLoot.LooseItemSockets"/> of the loose item sockets a rule covers (see <see cref="Covers"/>).</summary>
    /// <param name="map">The map.</param>
    /// <param name="rule">The rule.</param>
    /// <returns>The positions, in ascending order.</returns>
    public static int[] LooseIndicesCoveredBy(MapLoot map, LootRule rule)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(rule);
        return LooseIndices(map, socket => Covers(rule, socket.Id, [socket.Tag]));
    }

    /// <summary>
    /// The <c>chances</c> entry of a tag (ARES-Server <c>LootSpawnSettings.Chance</c>): its own, else its nearest parent's,
    /// keys compared without case.
    /// </summary>
    /// <param name="spawn">The spawn settings.</param>
    /// <param name="tag">The container tag.</param>
    /// <returns>The key and its chance, or null when no entry applies (the chance is then 0).</returns>
    public static KeyValuePair<string, double>? ChanceOf(LootSpawnDocument spawn, string tag)
    {
        ArgumentNullException.ThrowIfNull(spawn);
        ArgumentNullException.ThrowIfNull(tag);
        return Nearest(spawn.Chances, tag);
    }

    // The entry of a tag in a map of chances by tag, else of its nearest parent, keys compared without case.
    private static KeyValuePair<string, double>? Nearest(NumberMap map, string tag)
    {
        KeyValuePair<string, double>[] chances = [.. map];
        for (string current = tag; current.Length > 0; current = current.LastIndexOf('.') is var dot and > 0 ? current[..dot] : string.Empty)
        {
            foreach (KeyValuePair<string, double> entry in chances)
            {
                if (string.Equals(entry.Key, current, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The factor of an area loot value (ARES-Server <c>LootSpawnSettings.Multiplier</c>): its own <c>areaMultipliers</c>
    /// entry, else the one of <see cref="LootArea.Unknown"/>, else 1.
    /// </summary>
    /// <param name="spawn">The spawn settings.</param>
    /// <param name="area">The loot value of the area around a container.</param>
    /// <returns>The factor on a tag's chance.</returns>
    public static double MultiplierOf(LootSpawnDocument spawn, LootArea area)
    {
        ArgumentNullException.ThrowIfNull(spawn);
        return spawn.AreaMultipliers.Get(area.ToString()) ?? spawn.AreaMultipliers.Get(nameof(LootArea.Unknown)) ?? 1;
    }

    /// <summary>
    /// What the settings and rules decide for a container. Rules apply in file order and, for each setting, the last
    /// covering rule that sets it wins. A socket's spawning is decided by the last covering rule that sets a spawn chance
    /// or a group fill (a group fill wins over that rule's own chance); a placed container or a spawner uses the last
    /// covering rule that sets a spawn chance. Contents and reveal delay use the last covering rule that sets them; for
    /// a spawner, a rule covers its containers when it covers any of its class tags, while the server checks each
    /// placed container's own class tag.
    /// </summary>
    /// <param name="container">The container.</param>
    /// <param name="spawn">The spawn settings.</param>
    /// <param name="rules">The loot rules.</param>
    /// <returns>The match.</returns>
    public static ContainerMatch Match(LootContainer container, LootSpawnDocument spawn, LootRulesDocument rules)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(spawn);
        ArgumentNullException.ThrowIfNull(rules);
        LootRule[] covering = [.. rules.Rules.Where(rule => Covers(rule, container.Id, container.Tags))];
        LootRule? loot = Array.FindLast(covering, rule => rule.Loot is not null);
        LootRule? reveal = Array.FindLast(covering, rule => rule.RevealDelaySeconds is not null);
        double multiplier = MultiplierOf(spawn, container.Area);
        bool never = container.Source switch
        {
            LootContainerSource.Socket => container.ClassPaths.Count == 0,
            LootContainerSource.Placed => container.Tags.Count == 0,
            _ => false,
        };
        if (never)
        {
            return new ContainerMatch(covering, SpawnDecision.Never, null, null, null, multiplier, 0, loot, reveal);
        }

        LootRule? spawnRule = container.Source == LootContainerSource.Socket
            ? Array.FindLast(covering, rule => rule.SpawnChance is not null || rule.GroupFill.IsSet)
            : Array.FindLast(covering, rule => rule.SpawnChance is not null);
        if (spawnRule is not null)
        {
            return spawnRule.GroupFill.IsSet && container.Source == LootContainerSource.Socket
                ? new ContainerMatch(covering, SpawnDecision.GroupFill, spawnRule, null, null, multiplier, null, loot, reveal)
                : new ContainerMatch(covering, SpawnDecision.RuleChance, spawnRule, null, null, multiplier, spawnRule.SpawnChance, loot, reveal);
        }

        if (container.Source == LootContainerSource.Spawner)
        {
            return new ContainerMatch(covering, SpawnDecision.SpawnerGroups, null, null, null, multiplier,
                SpawnerChances.Chance(container, spawn, SpawnerChances.DefaultRound), loot, reveal);
        }

        KeyValuePair<string, double>? chance = ChanceOf(spawn, container.Tags[0]);
        double tagChance = chance?.Value ?? 0;
        return new ContainerMatch(covering, SpawnDecision.Default, null, chance?.Key, tagChance, multiplier, Math.Min(1, tagChance * multiplier), loot, reveal);
    }

    /// <summary>
    /// What the settings and rules decide for a loose item socket (ARES-Server <c>LooseLoot.Roll</c>): rules apply in file
    /// order and, for each setting, the last covering rule that sets it wins. A rule's spawn chance replaces the socket's
    /// roll (loose item chance, area value, limiters); a rule's loot list gives its one stack. Group fills and reveal
    /// delays do not apply to loose item sockets.
    /// </summary>
    /// <param name="socket">The loose item socket.</param>
    /// <param name="spawn">The spawn settings.</param>
    /// <param name="rules">The loot rules.</param>
    /// <returns>The match.</returns>
    public static LooseItemMatch MatchLoose(LooseItemSocket socket, LootSpawnDocument spawn, LootRulesDocument rules)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(spawn);
        ArgumentNullException.ThrowIfNull(rules);
        LootRule[] covering = [.. rules.Rules.Where(rule => Covers(rule, socket.Id, [socket.Tag]))];
        LootRule? loot = Array.FindLast(covering, rule => rule.Loot is not null);
        double multiplier = MultiplierOf(spawn, socket.Area);
        if (Array.FindLast(covering, rule => rule.SpawnChance is not null) is { SpawnChance: { } ruleChance } spawnRule)
        {
            return new LooseItemMatch(covering, spawnRule, null, null, multiplier, ruleChance, loot);
        }

        // The looseChances entry of the tag, else of its nearest parent (ARES-Server LootSpawnSettings.LooseChance).
        KeyValuePair<string, double>? chance = Nearest(spawn.LooseChances, socket.Tag);
        double tagChance = chance?.Value ?? 0;
        return new LooseItemMatch(covering, null, chance?.Key, tagChance, multiplier, Math.Min(1, tagChance * multiplier), loot);
    }

    private static int[] LooseIndices(MapLoot map, Func<LooseItemSocket, bool> include)
    {
        List<int> indices = [];
        for (int index = 0; index < map.LooseItemSockets.Count; index++)
        {
            if (include(map.LooseItemSockets[index]))
            {
                indices.Add(index);
            }
        }

        return [.. indices];
    }

    private static int[] Indices(MapLoot map, Func<LootContainer, bool> include)
    {
        List<int> indices = [];
        for (int index = 0; index < map.Containers.Count; index++)
        {
            if (include(map.Containers[index]))
            {
                indices.Add(index);
            }
        }

        return [.. indices];
    }
}
