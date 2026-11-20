using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Ares.Editor.Core.GameData;

/// <summary>
/// The parts of a game data folder (written by ARES-GameDataExtract) the editor shows, read-only: the build stamp, the
/// container types and their tag hierarchy, the items a container can hold, and each map's loot containers, loot value areas, loose item sockets and loot placement limiters. Table layouts and meanings follow
/// ARES-Server's <c>Ares.Common.GameData</c> readers; what the game data leaves unset takes the spawn settings' defaults
/// (<see cref="LootSpawnDefaults"/>), as ARES-Server applies them when it loads.
/// </summary>
public sealed class EditorGameData
{
    private const string ManifestFile = "manifest.json";
    private const string ContainerTypesFile = "loot-container-types.json";
    private const string UsableItemsFile = "usable-items.json";
    private const string ItemValuesFile = "item-values.json";
    private const string ValueTablesFile = "static-value-loot-tables.json";
    private const string ContainerClassesFile = "loot-container-classes.json";
    private const string MapsDirectory = "maps";
    private const string WorldFile = "world.json";

    // The prop's first material slot is the one the colour variants are keyed by (ARES-Server LootSocketClasses).
    private const int VariantMaterialSlot = 0;

    private readonly Dictionary<string, ContainerTypeRow> _typesByTag = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _tagsByClass = new(StringComparer.Ordinal);
    private readonly LootSpawnDefaults _defaults;

    private EditorGameData(string directory, GameDataManifest manifest, List<ContainerTypeRow> types, List<ContainerItem> items, RollData roll, LootSpawnDefaults defaults)
    {
        Directory = directory;
        _defaults = defaults;
        Manifest = manifest;
        foreach (ContainerTypeRow type in types)
        {
            _typesByTag.TryAdd(type.Tag, type);
            foreach (ClassRow entry in type.Classes.Concat(type.Variants.SelectMany(variant => variant.Classes)))
            {
                _tagsByClass.TryAdd(entry.Class, type.Tag);
            }
        }

        ContainerTypes = [.. types.Select(type => new LootContainerType(type.Tag,
            [.. type.Classes.Concat(type.Variants.SelectMany(variant => variant.Classes)).Select(entry => entry.Class).Distinct(StringComparer.Ordinal)], type.MaxPerGroup ?? defaults.MaxPerGroup))];
        TagTree = new LootTagTree(ContainerTypes);
        ContainerItems = items;
        Roll = roll;
        Maps = [.. manifest.MapNames.Select(ReadMap)];
    }

    /// <summary>Full path of the game data folder.</summary>
    public string Directory { get; }

    /// <summary>The folder's build stamp and maps.</summary>
    public GameDataManifest Manifest { get; }

    /// <summary>The container types, in table order (ordered by tag).</summary>
    public IReadOnlyList<LootContainerType> ContainerTypes { get; }

    /// <summary>The hierarchy of the container types' tags.</summary>
    public LootTagTree TagTree { get; }

    /// <summary>The items a loot rule can put in a container, by usable index.</summary>
    public IReadOnlyList<ContainerItem> ContainerItems { get; }

    /// <summary>What the default loot roll reads: the plain items, value loot tables and slot counts.</summary>
    public RollData Roll { get; }

    /// <summary>The loot containers of each map the manifest lists, ordered by map name.</summary>
    public IReadOnlyList<MapLoot> Maps { get; }

    /// <summary>Whether a loose item socket of any map has a tag or one below it (the server accepts such a rule tag).</summary>
    /// <param name="tag">The tag.</param>
    /// <returns>True when a loose item socket has the tag or one below it.</returns>
    public bool HasLooseItemTag(string tag) => Maps.Any(map => map.HasLooseItemTag(tag));

    /// <summary>Reads a game data folder.</summary>
    /// <param name="directory">The folder ARES-GameDataExtract wrote.</param>
    /// <param name="defaults">What the spawn settings give the members the game data leaves unset.</param>
    /// <returns>The data.</returns>
    /// <exception cref="EditorDataException">The folder, its manifest or a table the editor reads is missing or malformed.</exception>
    public static EditorGameData Load(string directory, LootSpawnDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        ArgumentException.ThrowIfNullOrEmpty(directory);
        string full = Path.GetFullPath(directory);
        if (!System.IO.Directory.Exists(full))
        {
            throw new EditorDataException($"The game data folder {full} does not exist. Extract it with ARES-GameDataExtract and point the editor at its output folder.");
        }

        ManifestRow manifest = Read(full, ManifestFile, GameDataJson.Default.ManifestRow);
        string[] maps = [.. manifest.Files.Select(file => file.Name.Split('/'))
            .Where(segments => segments is [MapsDirectory, _, WorldFile])
            .Select(segments => segments[1])
            .Order(StringComparer.Ordinal)];
        List<UsableItemRow> catalogue = Read(full, UsableItemsFile, GameDataJson.Default.ListUsableItemRow);
        List<ItemValueRow> values = Read(full, ItemValuesFile, GameDataJson.Default.ListItemValueRow);
        return new EditorGameData(full, new GameDataManifest(manifest.GameBuildId, manifest.ExtractorVersion, maps), Read(full, ContainerTypesFile, GameDataJson.Default.ListContainerTypeRow),
            ContainerItem.Select(catalogue, values), ReadRoll(full, catalogue, values), defaults);
    }

    private static RollData ReadRoll(string directory, List<UsableItemRow> catalogue, List<ItemValueRow> values)
    {
        List<ValueTableRow> tables = Read(directory, ValueTablesFile, GameDataJson.Default.ListValueTableRow);
        Dictionary<string, int> slots = new(StringComparer.Ordinal);
        foreach (ContainerClassRow row in Read(directory, ContainerClassesFile, GameDataJson.Default.ListContainerClassRow))
        {
            slots.TryAdd(row.Container.Class, row.SlotCount);
        }

        return new RollData(ContainerItem.SelectPlain(catalogue, values),
            [.. tables.Select(table => new ValueTable(table.Name, [.. table.Entries.Select(entry => Entry(table.Name, entry))]))], slots);

        static ValueTableEntry Entry(string table, ValueTableEntryRow row)
        {
            TagQuery? query;
            try
            {
                query = row.ItemQuery is { } item ? TagQuery.Parse(item.TagDictionary, item.TokenStream) : null;
            }
            catch (FormatException error)
            {
                throw new EditorDataException($"Value loot table {table} has a malformed item query: {error.Message}", error);
            }

            // ARES-Server LootItemQueryType: 0 names an exact item, 1 selects by query.
            return new ValueTableEntry(row.QueryType == 0 ? row.ExactItem : null, row.QueryType == 0 ? null : query,
                new CountRange(row.StackSize.Min, row.StackSize.Max), new CountRange(row.Durability.Min, row.Durability.Max));
        }
    }

    /// <summary>
    /// The container tag of a container class, as ARES-Server's <c>LootSocketClasses.ContainerTag</c> finds it: the tag of the
    /// first container type (in table order) that lists the class, by default or in a colour variant.
    /// </summary>
    /// <param name="classPath">The class path.</param>
    /// <returns>The tag, or null when no container type lists the class.</returns>
    public string? ContainerTagOf(string classPath) => _tagsByClass.GetValueOrDefault(classPath);

    private static T Read<T>(string directory, string name, JsonTypeInfo<T> type)
    {
        string path = Path.Combine(directory, name.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            throw new EditorDataException($"The game data folder {directory} has no {name}. Extract it again with the ARES-GameDataExtract of the server's version.");
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, type) ?? throw new EditorDataException($"Game data file {path} is empty (null).");
        }
        catch (JsonException error)
        {
            throw new EditorDataException($"Game data file {path} is malformed: {error.Message}", error);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new EditorDataException($"Cannot read game data file {path}: {error.Message}", error);
        }
    }

    private MapLoot ReadMap(string map)
    {
        string folder = $"{MapsDirectory}/{map}/";
        List<AreaRow> areaRows = Read(Directory, folder + "points-of-interest.json", GameDataJson.Default.ListAreaRow);
        LootValueAreas areas = new(areaRows);
        List<SocketMeshRow> meshes = Read(Directory, folder + "loot-socket-meshes.json", GameDataJson.Default.ListSocketMeshRow);
        List<LooseSocketRow> loose = Read(Directory, folder + "loose-item-sockets.json", GameDataJson.Default.ListLooseSocketRow);
        List<SpawnerRow> spawners = Read(Directory, folder + "dynamic-loot-spawners.json", GameDataJson.Default.ListSpawnerRow);
        List<SpawnerGroup>[] groups = [.. spawners.Select(_ => new List<SpawnerGroup>())];
        foreach (SpawnerGroupRow row in Read(Directory, folder + "dynamic-loot-groups.json", GameDataJson.Default.ListSpawnerGroupRow))
        {
            SpawnerGroup group = new(row.Id, row.Path, row.Rounds, row.Min, row.Max, row.Spawners.Count,
                [.. row.ParentSelections.Select(choice => new SpawnerGroupChoice(choice.Node, choice.Min, choice.Max, choice.Of))]);
            foreach (int index in row.Spawners.Where(index => index >= 0 && index < groups.Length).Distinct())
            {
                groups[index].Add(group);
            }
        }

        return new MapLoot(map,
        [
            .. Read(Directory, folder + "loot-sockets.json", GameDataJson.Default.ListSocketRow).Select(socket => Socket(socket, meshes, areas)),
            .. Read(Directory, folder + "placed-loot.json", GameDataJson.Default.ListPlacedRow).Select(placed => Placed(placed, areas)),
            .. spawners.Select((spawner, index) => Spawner(spawner, areas, groups[index])),
        ],
        [.. areaRows.Where(row => row.LootValue != LootArea.Unknown).Select(Area)],
        [.. loose.Select(socket => new LooseItemSocket($"{socket.Cell}/{socket.Actor}/{socket.Component}/{socket.Socket}", socket.Tag, socket.Location, socket.Rotation,
            socket.Options, socket.DataLayer, areas.At(socket.Location)))],
        [.. Read(Directory, folder + "loot-limiters.json", GameDataJson.Default.ListLimiterRow).Select(Limiter)]);
    }

    private LootLimiter Limiter(LimiterRow row) =>
        new($"{row.Cell}/{row.Actor}", row.Name, row.DataLayer, row.Shape ?? _defaults.LimiterShape, row.Location, row.Rotation, row.Radius ?? _defaults.LimiterRadius,
            [.. row.Volumes.SelectMany(volume => volume.Hulls).Select(hull => hull.Planes)],
            [.. row.Limits.Select(limit => new LootLimit(limit.Tag, limit.Unlimited ?? _defaults.LimitUnlimited, limit.Limit ?? _defaults.Limit))]);

    // An area's volume for drawing: the hulls that bound a closed shape.
    private static LootValueArea Area(AreaRow row) =>
        new(row.Actor, row.Name, row.LootValue, [.. row.Hulls.Select(hull => ConvexShape.FromPlanes(hull.Planes)).OfType<ConvexShape>()]);

    private LootContainer Socket(SocketRow socket, List<SocketMeshRow> meshes, LootValueAreas areas) => new()
    {
        Id = $"{socket.Cell}/{socket.Actor}/{socket.Component}/{socket.Socket}",
        Prop = $"{socket.Cell}/{socket.Actor}/{socket.Component}",
        Source = LootContainerSource.Socket,
        Location = socket.Location,
        Rotation = socket.Rotation,
        Tags = [socket.ContainerTag],
        Area = areas.At(socket.Location),
        ClassPaths = SocketClasses(socket, meshes),
        DataLayer = socket.DataLayer,
    };

    private LootContainer Placed(PlacedRow placed, LootValueAreas areas) => new()
    {
        Id = $"{placed.Cell}/{placed.Actor}",
        Source = LootContainerSource.Placed,
        Location = placed.Location,
        Rotation = placed.Rotation,
        Tags = ContainerTagOf(placed.Class) is { } tag ? [tag] : [],
        Area = areas.At(placed.Location),
        ClassPaths = [placed.Class],
        DataLayer = placed.DataLayer,
    };

    private LootContainer Spawner(SpawnerRow spawner, LootValueAreas areas, List<SpawnerGroup> groups)
    {
        SpawnerPoint[] points = [.. spawner.Points.Select(point => new SpawnerPoint(point.Location, point.Rotation, point.Weight, areas.At(point.Location)))];
        SpawnerClass[] classes = [.. spawner.Classes.Select(entry => new SpawnerClass(entry.Class, entry.Weight, ContainerTagOf(entry.Class)))];
        return new LootContainer
        {
            Id = $"{spawner.Cell}/{spawner.Actor}",
            Source = LootContainerSource.Spawner,
            Location = points.Length > 0 ? points[0].Location : default,
            Rotation = points.Length > 0 ? points[0].Rotation : default,
            Tags = [.. classes.Select(entry => entry.Tag).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)],
            Area = points.Length > 0 ? points[0].Area : LootArea.Unknown,
            ClassPaths = [.. classes.Select(entry => entry.ClassPath)],
            DataLayer = spawner.DataLayer,
            Points = points,
            SpawnerClasses = classes,
            SpawnerCount = spawner.Count ?? _defaults.SpawnerCount,
            SpawnerGroups = groups,
        };
    }

    // The classes the game picks from at a socket (ARES-Server LootSocketClasses.Candidates): the colour variant of the
    // prop's material (the component's override for the first slot, else the mesh's own), else the type's classes.
    private string[] SocketClasses(SocketRow socket, List<SocketMeshRow> meshes)
    {
        if (!_typesByTag.TryGetValue(socket.ContainerTag, out ContainerTypeRow? type))
        {
            return [];
        }

        string? material = socket.OverrideMaterials.Count > VariantMaterialSlot && !string.IsNullOrEmpty(socket.OverrideMaterials[VariantMaterialSlot])
            ? socket.OverrideMaterials[VariantMaterialSlot]
            : socket.Mesh >= 0 && socket.Mesh < meshes.Count && meshes[socket.Mesh].Materials.Count > VariantMaterialSlot ? meshes[socket.Mesh].Materials[VariantMaterialSlot] : null;
        VariantRow? variant = material is null ? null : type.Variants.Find(candidate => string.Equals(candidate.Material, material, StringComparison.OrdinalIgnoreCase));
        return [.. (variant?.Classes ?? type.Classes).Select(entry => entry.Class)];
    }
}
