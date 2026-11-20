using Ares.Editor.Core.Matching;

namespace Ares.Editor.Core.GameData;

/// <summary>The loot containers of one map, the areas that give them a loot value, its loose item sockets and its loot placement limiters.</summary>
public sealed class MapLoot
{
    private readonly Dictionary<string, LootContainer> _byId;
    private readonly Dictionary<string, LooseItemSocket> _looseById;
    private readonly string[] _looseTags;

    /// <summary>Creates the map's loot.</summary>
    /// <param name="name">The map's world name.</param>
    /// <param name="containers">Its containers: sockets, then placed containers, then spawners, each in table order.</param>
    /// <param name="areas">Its areas with a loot value, in table order.</param>
    /// <param name="looseItemSockets">Its loose item sockets, in table order (none when null).</param>
    /// <param name="limiters">Its loot placement limiters, in table order (none when null).</param>
    public MapLoot(string name, IReadOnlyList<LootContainer> containers, IReadOnlyList<LootValueArea> areas, IReadOnlyList<LooseItemSocket>? looseItemSockets = null,
        IReadOnlyList<LootLimiter>? limiters = null)
    {
        ArgumentNullException.ThrowIfNull(containers);
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Containers = containers;
        Areas = areas ?? throw new ArgumentNullException(nameof(areas));
        _byId = new Dictionary<string, LootContainer>(StringComparer.Ordinal);
        foreach (LootContainer container in containers)
        {
            _byId.TryAdd(container.Id, container);
        }

        LooseItemSockets = looseItemSockets ?? [];
        _looseById = new Dictionary<string, LooseItemSocket>(StringComparer.Ordinal);
        foreach (LooseItemSocket socket in LooseItemSockets)
        {
            _looseById.TryAdd(socket.Id, socket);
        }

        Limiters = limiters ?? [];
        _looseTags = [.. LooseItemSockets.Select(socket => socket.Tag).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The map's world name, for example <c>TheDam_02_P</c>.</summary>
    public string Name { get; }

    /// <summary>Every container: sockets, then placed containers, then spawners, each in table order.</summary>
    public IReadOnlyList<LootContainer> Containers { get; }

    /// <summary>The points of interest with a loot value, in table order; outside them a container's area is <see cref="LootArea.Unknown"/>.</summary>
    public IReadOnlyList<LootValueArea> Areas { get; }

    /// <summary>The loose item sockets, in table order.</summary>
    public IReadOnlyList<LooseItemSocket> LooseItemSockets { get; }

    /// <summary>The loot placement limiters, in table order (ordered by cell and actor).</summary>
    public IReadOnlyList<LootLimiter> Limiters { get; }

    /// <summary>The distinct tags of <see cref="LooseItemSockets"/> (ignoring case), spelled as the first socket with each spells it, in table order.</summary>
    public IReadOnlyList<string> LooseItemTags => _looseTags;

    /// <summary>The loose item socket with an id (compared exactly, as the server does), or null.</summary>
    /// <param name="id">The socket id.</param>
    /// <returns>The first loose item socket with the id, or null.</returns>
    public LooseItemSocket? FindLooseItemSocket(string id) => _looseById.GetValueOrDefault(id);

    /// <summary>The position in <see cref="LooseItemSockets"/> of the loose item socket with an id (compared exactly), or -1.</summary>
    /// <param name="id">The socket id.</param>
    /// <returns>The position of the first loose item socket with the id, or -1.</returns>
    public int IndexOfLooseItemSocket(string id)
    {
        if (FindLooseItemSocket(id) is not { } socket)
        {
            return -1;
        }

        for (int index = 0; index < LooseItemSockets.Count; index++)
        {
            if (ReferenceEquals(LooseItemSockets[index], socket))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Whether a loose item socket's tag is a tag or below it (ignoring case, as the server compares tags).</summary>
    /// <param name="tag">The tag.</param>
    /// <returns>True when a loose item socket of the map has the tag or one below it.</returns>
    public bool HasLooseItemTag(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        return Array.Exists(_looseTags, own => LootMatching.IsTagOrBelow(own, tag));
    }

    /// <summary>The container with an id (compared exactly, as the server does), or null.</summary>
    /// <param name="id">The container id.</param>
    /// <returns>The first container with the id, or null.</returns>
    public LootContainer? Find(string id) => _byId.GetValueOrDefault(id);

    /// <summary>
    /// How many containers are at or below each tag: a container counts once for each of its tags and every tag above
    /// them (<c>A.B.C</c> counts for <c>A.B.C</c>, <c>A.B</c> and <c>A</c>).
    /// </summary>
    /// <returns>The counts by tag, keys compared without case; a tag no container is at or below has no entry.</returns>
    public IReadOnlyDictionary<string, int> CountByTag()
    {
        Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> counted = new(StringComparer.OrdinalIgnoreCase);
        foreach (LootContainer container in Containers)
        {
            counted.Clear();
            foreach (string tag in container.Tags)
            {
                for (string current = tag; current.Length > 0; current = current.LastIndexOf('.') is var dot and > 0 ? current[..dot] : string.Empty)
                {
                    if (counted.Add(current))
                    {
                        counts[current] = counts.GetValueOrDefault(current) + 1;
                    }
                }
            }
        }

        return counts;
    }
}
