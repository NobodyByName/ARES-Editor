namespace Ares.Editor.Core.GameData;

/// <summary>
/// A point of interest of a map that gives its containers a loot value (<c>points-of-interest.json</c>), with its volume
/// as shapes to draw. The containers inside it get its value (see <see cref="LootContainer.Area"/>).
/// </summary>
public sealed class LootValueArea
{
    /// <summary>Creates an area.</summary>
    /// <param name="actor">The name of the point of interest's actor in the map.</param>
    /// <param name="name">The localization key of its name, or null.</param>
    /// <param name="value">Its loot value.</param>
    /// <param name="hulls">The convex parts of its volume that bound a closed shape.</param>
    public LootValueArea(string actor, string? name, LootArea value, IReadOnlyList<ConvexShape> hulls)
    {
        Actor = actor ?? throw new ArgumentNullException(nameof(actor));
        Name = name;
        Value = value;
        Hulls = hulls ?? throw new ArgumentNullException(nameof(hulls));
    }

    /// <summary>The name of the point of interest's actor in the map, for example <c>GeneratorControlTower</c>.</summary>
    public string Actor { get; }

    /// <summary>The localization key of the area's name, for example <c>ID_LOCATION_DAM_CONTROLTOWER</c>, or null.</summary>
    public string? Name { get; }

    /// <summary>The loot value of the containers inside it (never <see cref="LootArea.Unknown"/> for a map's areas).</summary>
    public LootArea Value { get; }

    /// <summary>The convex parts of its volume; a point inside any of them is inside the area.</summary>
    public IReadOnlyList<ConvexShape> Hulls { get; }
}
