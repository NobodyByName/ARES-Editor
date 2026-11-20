namespace Ares.Editor.Core.GameData;

/// <summary>
/// The areas of a map that show a loot value, and the loot value at a point. Mirrors ARES-Server's
/// <c>Ares.Raid.World.Loot.LootValueAreas</c> and <c>LootVolumeRules</c>: the highest loot value among the areas with
/// one whose volume (any convex hull: on the inner side of every plane) contains the point, else
/// <see cref="LootArea.Unknown"/>.
/// </summary>
internal sealed class LootValueAreas
{
    private readonly AreaRow[] _areas;

    public LootValueAreas(IEnumerable<AreaRow> areas)
    {
        _areas = [.. areas.Where(area => area.LootValue != LootArea.Unknown).OrderByDescending(area => area.LootValue)];
    }

    public LootArea At(WorldVector point)
    {
        if (!(double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z)))
        {
            return LootArea.Unknown;
        }

        return Array.Find(_areas, area => area.Hulls.Exists(hull => hull.Planes.TrueForAll(plane => Inside(plane, point))))?.LootValue ?? LootArea.Unknown;
    }

    private static bool Inside(WorldPlane plane, WorldVector point) =>
        (plane.Normal.X * point.X) + (plane.Normal.Y * point.Y) + (plane.Normal.Z * point.Z) <= plane.Distance;
}
