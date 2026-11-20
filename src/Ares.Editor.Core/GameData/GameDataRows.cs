using System.Text.Json.Serialization;

namespace Ares.Editor.Core.GameData;

// The rows of the game data tables the editor reads, as ARES-GameDataExtract writes them (camelCase members). Members
// the editor does not use are left out and skipped when reading.

internal sealed record ManifestRow(int GameBuildId, string ExtractorVersion, List<ManifestFileRow> Files);

internal sealed record ManifestFileRow(string Name);

internal sealed record ClassRow(string Class);

internal sealed record VariantRow(string Material, List<ClassRow> Classes);

internal sealed record ContainerTypeRow(string Tag, List<ClassRow> Classes, List<VariantRow> Variants, int? MaxPerGroup);

internal sealed record SocketRow(
    string Cell,
    string Actor,
    string Component,
    string Socket,
    string ContainerTag,
    WorldVector Location,
    WorldRotation Rotation,
    int Mesh,
    List<string?> OverrideMaterials,
    string? DataLayer);

internal sealed record SocketMeshRow(List<string?> Materials);

internal sealed record PlacedRow(string Cell, string Actor, string Class, WorldVector Location, WorldRotation Rotation, string? DataLayer);

internal sealed record SpawnerClassRow(string Class, double Weight);

internal sealed record SpawnerPointRow(WorldVector Location, WorldRotation Rotation, double Weight);

internal sealed record SpawnerRow(string Cell, string Actor, string? DataLayer, List<SpawnerClassRow> Classes, List<SpawnerPointRow> Points, int? Count);

internal sealed record SelectionRow(string Node, int Min, int Max, int Of);

// Spawners are indices into the map's dynamic-loot-spawners.json.
internal sealed record SpawnerGroupRow(string Id, List<string> Path, List<string> Rounds, int Min, int Max, List<int> Spawners, List<SelectionRow> ParentSelections);

internal sealed record LooseSocketRow(
    string Cell,
    string Actor,
    string Component,
    string Socket,
    string Tag,
    WorldVector Location,
    WorldRotation Rotation,
    LooseItemOptions Options,
    string? DataLayer);

internal sealed record UsableItemRow(
    int UsableIndex,
    string TechnicalName,
    long AssetId,
    int Category,
    int MaxStack,
    bool HasChildSlots,
    List<string>? DropTags = null,
    List<LootPlacementRow>? LootPlacements = null,
    List<string>? LootAvoidGroups = null);

internal sealed record LootPlacementRow(string SocketTag, List<string> ExcludeTags);

internal sealed record RangeRow(int Min, int Max);

internal sealed record TagQueryRow(List<string> TagDictionary, List<int> TokenStream);

internal sealed record ValueTableEntryRow(int QueryType, TagQueryRow? ItemQuery, string? ExactItem, RangeRow StackSize, RangeRow Durability);

internal sealed record ValueTableRow(string Name, List<ValueTableEntryRow> Entries);

internal sealed record ContainerClassRow(ClassRow Container, int SlotCount);

internal sealed record ItemValueRow(long AssetId, string Name);

internal sealed record HullRow(List<WorldPlane> Planes);

internal sealed record AreaRow(string Actor, string? Name, LootArea LootValue, List<HullRow> Hulls);

internal sealed record LimitRow(string Tag, bool? Unlimited, int? Limit);

internal sealed record LimiterVolumeRow(string Actor, List<HullRow> Hulls);

internal sealed record LimiterRow(
    string Cell,
    string Actor,
    string Name,
    string? DataLayer,
    LootLimiterShape? Shape,
    WorldVector Location,
    WorldRotation Rotation,
    double? Radius,
    List<LimiterVolumeRow> Volumes,
    List<LimitRow> Limits);

/// <summary>Source-generated readers of the game data rows: camelCase members, nullable annotations and constructor parameters enforced.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    NumberHandling = JsonNumberHandling.Strict,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(ManifestRow))]
[JsonSerializable(typeof(List<ContainerTypeRow>))]
[JsonSerializable(typeof(List<SocketRow>))]
[JsonSerializable(typeof(List<SocketMeshRow>))]
[JsonSerializable(typeof(List<PlacedRow>))]
[JsonSerializable(typeof(List<SpawnerRow>))]
[JsonSerializable(typeof(List<SpawnerGroupRow>))]
[JsonSerializable(typeof(List<LooseSocketRow>))]
[JsonSerializable(typeof(List<AreaRow>))]
[JsonSerializable(typeof(List<LimiterRow>))]
[JsonSerializable(typeof(List<UsableItemRow>))]
[JsonSerializable(typeof(List<ItemValueRow>))]
[JsonSerializable(typeof(List<ValueTableRow>))]
[JsonSerializable(typeof(List<ContainerClassRow>))]
internal sealed partial class GameDataJson : JsonSerializerContext;
