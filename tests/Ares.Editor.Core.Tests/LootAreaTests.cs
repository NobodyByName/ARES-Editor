using Ares.Editor.Core.GameData;

namespace Ares.Editor.Core.Tests;

/// <summary>The loot value areas as shapes: convex hulls from planes, and the real Dam's areas against the containers' areas.</summary>
public sealed class LootAreaTests
{
    // A face counts a point on its plane as inside, as the server's test (normal·p ≤ distance) does.
    private const double InsideTolerance = 0.01;

    private static readonly Lazy<EditorGameData> s_gameData = new(() => EditorGameData.Load(RealData.GameDataDirectory!, RealData.SpawnDefaults));

    [Fact]
    public void A_box_of_six_planes_has_eight_corners_and_twelve_outward_triangles()
    {
        WorldPlane[] planes =
        [
            new(new WorldVector(1, 0, 0), 300),
            new(new WorldVector(-1, 0, 0), 100),
            new(new WorldVector(0, 2, 0), 400),
            new(new WorldVector(0, -1, 0), 0),
            new(new WorldVector(0, 0, 1), 50),
            new(new WorldVector(0, 0, -1), 50),
        ];

        ConvexShape shape = ConvexShape.FromPlanes(planes)!;

        Assert.Equal(8, shape.Vertices.Count);
        Assert.Contains(shape.Vertices, vertex => Near(vertex, new WorldVector(-100, 0, -50)));
        Assert.Contains(shape.Vertices, vertex => Near(vertex, new WorldVector(300, 200, 50)));
        Assert.Equal(12 * 3, shape.Triangles.Count);
        WorldVector center = new(100, 100, 0);
        for (int start = 0; start < shape.Triangles.Count; start += 3)
        {
            (WorldVector a, WorldVector normal) = Triangle(shape, start);
            Assert.True(Dot(normal, Subtract(a, center)) > 0, $"triangle {start / 3} faces inward");
        }

        Assert.Equal(400.0 * 200 * 100, Volume(shape), 6);
    }

    [Fact]
    public void Planes_that_leave_a_side_open_have_no_shape()
    {
        WorldPlane[] open =
        [
            new(new WorldVector(1, 0, 0), 100),
            new(new WorldVector(-1, 0, 0), 100),
            new(new WorldVector(0, 1, 0), 100),
            new(new WorldVector(0, -1, 0), 100),
            new(new WorldVector(0, 0, -1), 100),
        ];

        Assert.Null(ConvexShape.FromPlanes(open));
        Assert.Null(ConvexShape.FromPlanes(open[..2]));
    }

    [Fact]
    public void The_dam_has_seven_areas_with_outward_hulls()
    {
        MapLoot dam = RealDam();

        Assert.Equal(7, dam.Areas.Count);
        Assert.Equal(4, dam.Areas.Count(area => area.Value == LootArea.High));
        Assert.Equal(3, dam.Areas.Count(area => area.Value == LootArea.Medium));
        foreach (LootValueArea area in dam.Areas)
        {
            Assert.NotEmpty(area.Hulls);
            Assert.False(string.IsNullOrEmpty(area.Actor));
            foreach (ConvexShape hull in area.Hulls)
            {
                Assert.True(hull.Vertices.Count >= 4, $"{area.Actor}: {hull.Vertices.Count} corners");
                Assert.True(hull.Triangles.Count >= 4 * 3, $"{area.Actor}: {hull.Triangles.Count / 3} triangles");
                // Outward winding gives a positive volume; a flat hull would give none (1 m³ = 1e6 cm³).
                Assert.True(Volume(hull) > 1e6, $"{area.Actor}: volume {Volume(hull)} cm³");
                Assert.True(IsClosed(hull), $"{area.Actor}: the surface has a hole");
            }

            TestContext.Current.SendDiagnosticMessage(
                $"{area.Actor} ({area.Name}): {area.Value}, {string.Join(" + ", area.Hulls.Select(hull => $"{hull.Vertices.Count} corners/{hull.Triangles.Count / 3} triangles"))}");
        }
    }

    [Fact]
    public void Every_dam_container_lies_in_the_shapes_of_its_area()
    {
        MapLoot dam = RealDam();
        string[] wrong = [.. dam.Containers
            .Select(container => (Container: container, Drawn: DrawnArea(dam, container.Location)))
            .Where(entry => entry.Drawn != entry.Container.Area)
            .Select(entry => $"{entry.Container.Id}: {entry.Container.Area}, drawn {entry.Drawn}")];

        LootContainer high = dam.Containers.First(container => container.Area == LootArea.High);
        Assert.Contains(dam.Areas, area => area.Value == LootArea.High && area.Hulls.Any(hull => Contains(hull, high.Location)));
        TestContext.Current.SendDiagnosticMessage(
            $"{dam.Containers.Count} containers: {string.Join(", ", dam.Containers.GroupBy(container => container.Area).Select(group => $"{group.Key} {group.Count()}"))}");
        Assert.Empty(wrong);
    }

    private static MapLoot RealDam()
    {
        RealData.RequireGameData();
        return Assert.Single(s_gameData.Value.Maps, map => map.Name.StartsWith("TheDam", StringComparison.Ordinal));
    }

    // The highest loot value among the areas whose drawn shapes contain a point, as the editor assigns areas.
    private static LootArea DrawnArea(MapLoot map, WorldVector point) =>
        map.Areas.Where(area => area.Hulls.Any(hull => Contains(hull, point))).Select(area => area.Value).DefaultIfEmpty(LootArea.Unknown).Max();

    // Whether a point is on the inner side of every face of a convex shape.
    private static bool Contains(ConvexShape shape, WorldVector point)
    {
        for (int start = 0; start < shape.Triangles.Count; start += 3)
        {
            (WorldVector a, WorldVector normal) = Triangle(shape, start);
            if (Dot(normal, Subtract(point, a)) > InsideTolerance * Math.Sqrt(Dot(normal, normal)))
            {
                return false;
            }
        }

        return true;
    }

    // Signed volume from the divergence theorem, taken about the first corner (map coordinates are large): positive when
    // every triangle is wound outward.
    private static double Volume(ConvexShape shape)
    {
        WorldVector origin = shape.Vertices[0];
        double volume = 0;
        for (int start = 0; start < shape.Triangles.Count; start += 3)
        {
            WorldVector a = Subtract(shape.Vertices[shape.Triangles[start]], origin);
            WorldVector b = Subtract(shape.Vertices[shape.Triangles[start + 1]], origin);
            WorldVector c = Subtract(shape.Vertices[shape.Triangles[start + 2]], origin);
            volume += Dot(a, Cross(b, c)) / 6;
        }

        return volume;
    }

    // A closed surface's area vectors (normal times area) cancel out; a hole leaves the missing face's.
    private static bool IsClosed(ConvexShape shape)
    {
        WorldVector sum = default;
        double area = 0;
        for (int start = 0; start < shape.Triangles.Count; start += 3)
        {
            WorldVector normal = Triangle(shape, start).Normal;
            sum = new WorldVector(sum.X + normal.X, sum.Y + normal.Y, sum.Z + normal.Z);
            area += Math.Sqrt(Dot(normal, normal));
        }

        return Math.Sqrt(Dot(sum, sum)) <= 1e-4 * area;
    }

    private static (WorldVector First, WorldVector Normal) Triangle(ConvexShape shape, int start)
    {
        WorldVector a = shape.Vertices[shape.Triangles[start]];
        WorldVector b = shape.Vertices[shape.Triangles[start + 1]];
        WorldVector c = shape.Vertices[shape.Triangles[start + 2]];
        return (a, Cross(Subtract(b, a), Subtract(c, a)));
    }

    private static bool Near(WorldVector a, WorldVector b) => Dot(Subtract(a, b), Subtract(a, b)) < 1e-6;

    private static WorldVector Subtract(WorldVector a, WorldVector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static double Dot(WorldVector a, WorldVector b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    private static WorldVector Cross(WorldVector a, WorldVector b) => new((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));
}
