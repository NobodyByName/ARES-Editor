namespace Ares.Editor.Core.GameData;

/// <summary>A point where a dynamic loot spawner may put a container.</summary>
/// <param name="Location">World location.</param>
/// <param name="Rotation">World rotation (the spawner may randomise the yaw instead).</param>
/// <param name="Weight">Relative chance of the point among the spawner's points.</param>
/// <param name="Area">Loot value of the area around the point.</param>
public sealed record SpawnerPoint(WorldVector Location, WorldRotation Rotation, double Weight, LootArea Area);

/// <summary>A container class a dynamic loot spawner may place.</summary>
/// <param name="ClassPath">Container class path (<c>/Game/.../BP_X.BP_X_C</c>).</param>
/// <param name="Weight">Relative chance of the class among the spawner's classes.</param>
/// <param name="Tag">Container tag of the class (see <see cref="EditorGameData.ContainerTagOf"/>), or null when no container type lists it.</param>
public sealed record SpawnerClass(string ClassPath, double Weight, string? Tag);

/// <summary>A random choice of activity subnodes above a dynamic loot group: it keeps a number of the node's children.</summary>
/// <param name="Node">The activity node.</param>
/// <param name="Min">Fewest children kept.</param>
/// <param name="Max">Most children kept.</param>
/// <param name="Of">Children the node has.</param>
public sealed record SpawnerGroupChoice(string Node, int Min, int Max, int Of);

/// <summary>A dynamic loot group a spawner belongs to: when its round runs, it picks some of its spawners at random.</summary>
/// <param name="Id">Activity node and position of the group within it.</param>
/// <param name="Path">Titles of the activity nodes from the root down to the group's node.</param>
/// <param name="Rounds">Round configurations in which the group runs.</param>
/// <param name="Min">Fewest spawners it picks.</param>
/// <param name="Max">Most spawners it picks.</param>
/// <param name="Spawners">Spawners in the group.</param>
/// <param name="Choices">Random choices of activity nodes above the group that decide whether it runs at all.</param>
public sealed record SpawnerGroup(string Id, IReadOnlyList<string> Path, IReadOnlyList<string> Rounds, int Min, int Max, int Spawners, IReadOnlyList<SpawnerGroupChoice> Choices);

/// <summary>
/// One loot container of a map as the server's loot rules see it: its id, where it stands, its container tags and the
/// loot value of its area.
/// </summary>
public sealed class LootContainer
{
    /// <summary>
    /// The id loot rules name it by (<c>containers</c> in <c>loot-rules.json</c>), as ARES-Server's
    /// <c>LootContainerIds</c> builds it: <c>cell/actor/component/socket</c> for a socket, <c>cell/actor</c> for a placed
    /// container or a spawner. The cell is empty for the map's persistent level, so such an id starts with <c>/</c>.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// For a socket, the placed prop that holds it as <c>cell/actor/component</c> (its <see cref="Id"/> without the socket
    /// name); null for other sources.
    /// </summary>
    public string? Prop { get; init; }

    /// <summary>Which game data table the container comes from.</summary>
    public required LootContainerSource Source { get; init; }

    /// <summary>World location: the socket, the placed actor's root, or a spawner's first point.</summary>
    public required WorldVector Location { get; init; }

    /// <summary>World rotation at <see cref="Location"/>.</summary>
    public required WorldRotation Rotation { get; init; }

    /// <summary>
    /// The container tags rules and chances match: a socket's own tag (spelled as in the mesh), a placed container's
    /// class tag, every distinct class tag of a spawner. Empty when no container type lists a placed container's class.
    /// Tags compare without case.
    /// </summary>
    public required IReadOnlyList<string> Tags { get; init; }

    /// <summary>Loot value of the area around <see cref="Location"/>: the highest among the map's areas that contain it.</summary>
    public required LootArea Area { get; init; }

    /// <summary>
    /// Container classes that may stand here: a socket's candidates (its type's classes, or those of the colour variant of
    /// the prop's material; empty when no container type has its tag), a placed container's class, a spawner's classes.
    /// </summary>
    public required IReadOnlyList<string> ClassPaths { get; init; }

    /// <summary>Data layer that streams the container, or null when it is always present.</summary>
    public required string? DataLayer { get; init; }

    /// <summary>A spawner's candidate points, in table order; empty for other sources.</summary>
    public IReadOnlyList<SpawnerPoint> Points { get; init; } = [];

    /// <summary>A spawner's classes with their weights and tags; empty for other sources.</summary>
    public IReadOnlyList<SpawnerClass> SpawnerClasses { get; init; } = [];

    /// <summary>Containers one run of a spawner places; 0 for other sources.</summary>
    public int SpawnerCount { get; init; }

    /// <summary>
    /// The dynamic loot groups that can pick a spawner, in table order; empty for other sources. A spawner in no group never
    /// runs.
    /// </summary>
    public IReadOnlyList<SpawnerGroup> SpawnerGroups { get; init; } = [];
}
