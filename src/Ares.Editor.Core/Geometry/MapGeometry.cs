using System.Diagnostics;
using System.Numerics;
using Ares.Editor.Core.GameData;

namespace Ares.Editor.Core.Geometry;

/// <summary>What a <see cref="GeometryChunk"/> holds.</summary>
public enum GeometryKind
{
    /// <summary>Landscape height-field tiles.</summary>
    Landscape,

    /// <summary>Placed collision bodies: buildings, props, rocks, vegetation.</summary>
    Objects,
}

/// <summary>
/// The map's collision geometry ready for the 3D view: world-space triangle chunks of about 250 × 250 m, each of one
/// kind and one set of data layers, built from
/// the map's collision file (<see cref="MapCollisionReader"/>) and held in memory while the map is shown.
/// </summary>
/// <remarks>
/// Objects are the placements that block the Visibility channel, which sight traces use, so blockers nobody sees
/// (invisible walls, water planes that only stop bullets, trigger-only parts) are left out. Each drawn body shows its
/// simple collision, or its complex meshes when it has no simple shapes (<see cref="BodyMesh"/>); the landscape is drawn
/// at half resolution (<see cref="LandscapeMesh"/>). A cell is shown when it belongs to the base map or any of its data
/// layers is switched on, as ARES-Raid's collision world decides.
/// </remarks>
public sealed class MapGeometry
{
    // Side of the square world areas chunks are grouped by, in centimetres.
    private const double ChunkSize = 25_000;

    // ECollisionChannel Visibility (bit n of the placement's blocked channels is channel n).
    private const uint VisibilityChannel = 1u << 3;

    private MapGeometry(string mapName, int gameBuildId, string? buildWarning, IReadOnlyList<GeometryLayer> dataLayers, IReadOnlyList<GeometryChunk> chunks,
        GeometryStatistics statistics)
    {
        MapName = mapName;
        GameBuildId = gameBuildId;
        BuildWarning = buildWarning;
        DataLayers = dataLayers;
        Chunks = chunks;
        Statistics = statistics;
    }

    /// <summary>The map's world name.</summary>
    public string MapName { get; }

    /// <summary>Build id of the game the collision file was extracted from.</summary>
    public int GameBuildId { get; }

    /// <summary>Why the collision file may not match the game data (another game build), or null.</summary>
    public string? BuildWarning { get; }

    /// <summary>The data layers some drawn cell belongs to, ordered by name.</summary>
    public IReadOnlyList<GeometryLayer> DataLayers { get; }

    /// <summary>The chunks, landscape first, each kind ordered by world area.</summary>
    public IReadOnlyList<GeometryChunk> Chunks { get; }

    /// <summary>What the file holds and what the build made of it.</summary>
    public GeometryStatistics Statistics { get; }

    /// <summary>The collision file of a map in a game data folder: <c>maps/&lt;map&gt;/&lt;map&gt;.collision</c>.</summary>
    /// <param name="gameDataDirectory">The game data folder.</param>
    /// <param name="mapName">The map's world name.</param>
    /// <returns>The path.</returns>
    public static string CollisionFile(string gameDataDirectory, string mapName) => Path.Combine(gameDataDirectory, "maps", mapName, mapName + ".collision");

    /// <summary>Reads a map's collision file and builds its chunks.</summary>
    /// <param name="gameData">The game data the map belongs to; its build is compared with the file's.</param>
    /// <param name="mapName">The map's world name.</param>
    /// <param name="cancellation">Stops the build.</param>
    /// <returns>The geometry.</returns>
    /// <exception cref="EditorDataException">The file is missing, unreadable, of another format version or malformed.</exception>
    /// <exception cref="OperationCanceledException">The build was cancelled.</exception>
    public static MapGeometry Load(EditorGameData gameData, string mapName, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(gameData);
        string path = CollisionFile(gameData.Directory, mapName);
        if (!File.Exists(path))
        {
            throw new EditorDataException($"No collision file {path}: the view shows the containers without the map. Extract the game data again with ARES-GameDataExtract to get it.");
        }

        Stopwatch watch = Stopwatch.StartNew();
        CollisionMap map = MapCollisionReader.Read(path);
        TimeSpan readTime = watch.Elapsed;
        return Build(map, gameData.Manifest.GameBuildId, readTime, cancellation);
    }

    /// <summary>Builds the chunks of a map read from its collision file.</summary>
    /// <param name="map">The map.</param>
    /// <param name="expectedGameBuildId">Build of the game data the map is shown with.</param>
    /// <param name="readTime">How long reading the file took.</param>
    /// <param name="cancellation">Stops the build.</param>
    /// <returns>The geometry.</returns>
    internal static MapGeometry Build(CollisionMap map, int expectedGameBuildId, TimeSpan readTime, CancellationToken cancellation)
    {
        Stopwatch watch = Stopwatch.StartNew();
        string[] layerNames = [.. map.Cells.SelectMany(cell => cell.DataLayers).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        Dictionary<string, int> layerIndex = layerNames.Select((name, index) => (name, index)).ToDictionary(entry => entry.name, entry => entry.index, StringComparer.Ordinal);
        // Each cell's set of data layers, numbered by first appearance.
        List<int[]> layerSets = [];
        Dictionary<string, int> setIndex = new(StringComparer.Ordinal);
        int[] cellSets = new int[map.Cells.Length];
        for (int cell = 0; cell < cellSets.Length; cell++)
        {
            int[] set = [.. map.Cells[cell].DataLayers.Select(name => layerIndex[name]).Distinct().Order()];
            string key = string.Join(',', set);
            if (!setIndex.TryGetValue(key, out int number))
            {
                number = layerSets.Count;
                setIndex.Add(key, number);
                layerSets.Add(set);
            }

            cellSets[cell] = number;
        }

        int[] layerPlacements = new int[layerNames.Length];

        BodyMesh?[] meshes = new BodyMesh?[map.Bodies.Length];
        Dictionary<GroupKey, List<int>> objectGroups = [];
        for (int index = 0; index < map.Placements.Length; index++)
        {
            BodyPlacement placement = map.Placements[index];
            if ((placement.BlockedChannels & VisibilityChannel) == 0)
            {
                continue;
            }

            meshes[placement.BodyIndex] ??= BodyMesh.Of(map.Bodies[placement.BodyIndex]);
            foreach (int layer in layerSets[cellSets[placement.CellIndex]])
            {
                layerPlacements[layer]++;
            }

            Add(objectGroups, GroupKey.Of(placement.Translation.X, placement.Translation.Y, cellSets[placement.CellIndex]), index);
        }

        Dictionary<GroupKey, List<int>> landscapeGroups = [];
        for (int index = 0; index < map.HeightFields.Length; index++)
        {
            HeightFieldTile tile = map.HeightFields[index];
            double centerX = tile.Origin.X + ((tile.CountX - 1) * (double)tile.SpacingX / 2);
            double centerY = tile.Origin.Y + ((tile.CountY - 1) * (double)tile.SpacingY / 2);
            Add(landscapeGroups, GroupKey.Of(centerX, centerY, cellSets[tile.CellIndex]), index);
        }

        List<GeometryChunk> chunks =
        [
            .. BuildGroups(landscapeGroups, GeometryKind.Landscape, layerSets, (writer, index) => LandscapeMesh.Add(writer, map.HeightFields[index]), cancellation),
            .. BuildGroups(objectGroups, GeometryKind.Objects, layerSets, (writer, index) =>
            {
                BodyPlacement placement = map.Placements[index];
                writer.AddPlaced(meshes[placement.BodyIndex]!, placement);
            }, cancellation),
        ];

        int drawnPlacements = objectGroups.Values.Sum(group => group.Count);
        GeometryStatistics statistics = new(
            map.Cells.Length,
            map.Bodies.Length,
            map.Placements.Length,
            map.HeightFields.Length,
            drawnPlacements,
            chunks.Where(chunk => chunk.Kind == GeometryKind.Objects).Sum(chunk => (long)chunk.TriangleCount),
            chunks.Where(chunk => chunk.Kind == GeometryKind.Landscape).Sum(chunk => (long)chunk.TriangleCount),
            chunks.Sum(chunk => (long)chunk.Data.Length),
            readTime,
            watch.Elapsed);
        string? warning = map.GameBuildId == expectedGameBuildId
            ? null
            : $"The collision file is from game build {map.GameBuildId}, the game data from build {expectedGameBuildId}; the map may not match. Extract the game data again.";
        GeometryLayer[] layers = [.. layerNames.Select((name, index) => new GeometryLayer(name, layerPlacements[index]))];
        return new MapGeometry(map.MapName, map.GameBuildId, warning, layers, chunks, statistics);
    }

    private static void Add(Dictionary<GroupKey, List<int>> groups, GroupKey key, int index)
    {
        if (!groups.TryGetValue(key, out List<int>? members))
        {
            members = [];
            groups.Add(key, members);
        }

        members.Add(index);
    }

    // Builds each group's chunks in parallel and returns them ordered by group key.
    private static IEnumerable<GeometryChunk> BuildGroups(Dictionary<GroupKey, List<int>> groups, GeometryKind kind, List<int[]> layerSets,
        Action<ChunkWriter, int> add, CancellationToken cancellation)
    {
        KeyValuePair<GroupKey, List<int>>[] ordered = [.. groups.OrderBy(group => group.Key)];
        IReadOnlyList<GeometryChunk>[] built = new IReadOnlyList<GeometryChunk>[ordered.Length];
        ParallelOptions options = new() { CancellationToken = cancellation };
        Parallel.For(0, ordered.Length, options, group =>
        {
            ChunkWriter writer = new(kind, layerSets[ordered[group].Key.LayerSet]);
            foreach (int index in ordered[group].Value)
            {
                add(writer, index);
            }

            built[group] = writer.Finish();
        });
        return built.SelectMany(group => group);
    }

    // A world area and a set of data layers (an index into the build's layer sets).
    private readonly record struct GroupKey(int AreaX, int AreaY, int LayerSet) : IComparable<GroupKey>
    {
        public static GroupKey Of(double x, double y, int layerSet) => new((int)Math.Floor(x / ChunkSize), (int)Math.Floor(y / ChunkSize), layerSet);

        public int CompareTo(GroupKey other)
        {
            int byY = AreaY.CompareTo(other.AreaY);
            int byX = byY != 0 ? byY : AreaX.CompareTo(other.AreaX);
            return byX != 0 ? byX : LayerSet.CompareTo(other.LayerSet);
        }
    }
}

/// <summary>A data layer of the map, which the operator can show or hide.</summary>
/// <param name="Name">The layer's name in the collision file (the game's data-layer instance name).</param>
/// <param name="PlacementCount">How many drawn placements belong to it.</param>
public sealed record GeometryLayer(string Name, int PlacementCount);

/// <summary>Counts and timings of a <see cref="MapGeometry"/> build.</summary>
/// <param name="Cells">Streaming cells in the file.</param>
/// <param name="Bodies">Collision bodies in the file.</param>
/// <param name="Placements">Body placements in the file.</param>
/// <param name="HeightFieldTiles">Landscape tiles in the file.</param>
/// <param name="DrawnPlacements">Placements drawn (those that block the Visibility channel).</param>
/// <param name="ObjectTriangles">Triangles of the drawn placements, after placing every one.</param>
/// <param name="LandscapeTriangles">Triangles of the landscape as drawn.</param>
/// <param name="Bytes">Size of all chunks' data.</param>
/// <param name="ReadTime">Time to read the file.</param>
/// <param name="BuildTime">Time to build the chunks.</param>
public sealed record GeometryStatistics(
    int Cells,
    int Bodies,
    int Placements,
    int HeightFieldTiles,
    int DrawnPlacements,
    long ObjectTriangles,
    long LandscapeTriangles,
    long Bytes,
    TimeSpan ReadTime,
    TimeSpan BuildTime);

/// <summary>
/// One piece of the map the view uploads as a mesh. <see cref="Data"/> holds <see cref="VertexCount"/> × 3 Float32
/// positions in three.js space (metres, Y up: <c>(ue.x, ue.z, ue.y) × 0.01</c>), then <see cref="IndexCount"/> indices,
/// three per triangle: UInt16 when <see cref="ShortIndices"/> (padded to a multiple of 4 bytes), else UInt32; all
/// little-endian.
/// </summary>
public sealed class GeometryChunk
{
    internal GeometryChunk(GeometryKind kind, IReadOnlyList<int> layers, int vertexCount, int indexCount, Vector3 minimum, Vector3 maximum, byte[] data)
    {
        Kind = kind;
        Layers = layers;
        VertexCount = vertexCount;
        IndexCount = indexCount;
        Minimum = minimum;
        Maximum = maximum;
        Data = data;
    }

    /// <summary>What the chunk holds.</summary>
    public GeometryKind Kind { get; }

    /// <summary>Indices in <see cref="MapGeometry.DataLayers"/> of the layers the chunk's cells belong to; empty for the base map.</summary>
    public IReadOnlyList<int> Layers { get; }

    /// <summary>Number of vertices.</summary>
    public int VertexCount { get; }

    /// <summary>Number of indices (three per triangle).</summary>
    public int IndexCount { get; }

    /// <summary>Number of triangles.</summary>
    public int TriangleCount => IndexCount / 3;

    /// <summary>Whether the indices are UInt16 (else UInt32).</summary>
    public bool ShortIndices => VertexCount <= ChunkWriter.ShortIndexVertexLimit;

    /// <summary>Smallest corner of the chunk's bounds, in three.js space.</summary>
    public Vector3 Minimum { get; }

    /// <summary>Largest corner of the chunk's bounds, in three.js space.</summary>
    public Vector3 Maximum { get; }

    /// <summary>The positions and indices.</summary>
    public ReadOnlyMemory<byte> Data { get; }
}
