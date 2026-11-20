using Ares.Editor.Core.GameData;

namespace Ares.Editor.Core.Tests;

/// <summary>The loot placement limiters: reading them, which sockets they cover and cap, their drawn shapes, and the real Dam's.</summary>
public sealed class LootLimiterTests : IDisposable
{
    private static readonly Lazy<EditorGameData> s_realGameData = new(() => EditorGameData.Load(RealData.GameDataDirectory!, RealData.SpawnDefaults));

    private readonly TemporaryFolder _folder = new();

    public LootLimiterTests()
    {
        SyntheticGameData.Write(_folder);
    }

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void A_map_reads_its_limiters_with_their_shape_layer_and_caps()
    {
        MapLoot map = EditorGameData.Load(_folder.Path, SyntheticGameData.SpawnDefaults).Maps[0];

        Assert.Equal(["Cell1/LootPlacementLimiter_1", "/LootPlacementLimiter_2"], map.Limiters.Select(limiter => limiter.Id));
        LootLimiter office = map.Limiters[0];
        Assert.Equal("LootPlacementLimiter_Office", office.Name);
        Assert.Equal(LootLimiterShape.Volumes, office.Shape);
        Assert.Null(office.DataLayer);
        Assert.Equal([new LootLimit("Socket.LootContainer.Tech", false, 2), new LootLimit("Socket.Loot", false, 1), new LootLimit("Socket.LootContainer.Tech.Drawer", true, 0)],
            office.Limits);
        Assert.Equal(8, Assert.Single(office.Shapes).Vertices.Count);
        LootLimiter field = map.Limiters[1];
        Assert.Equal(LootLimiterShape.Sphere, field.Shape);
        Assert.Equal("DataLayer_A", field.DataLayer);
        Assert.Single(field.Shapes);
    }

    [Fact]
    public void A_socket_is_capped_by_the_limited_entries_of_its_tag_or_above_in_the_limiters_covering_it()
    {
        MapLoot map = EditorGameData.Load(_folder.Path, SyntheticGameData.SpawnDefaults).Maps[0];
        LootLimiter office = map.Limiters[0];
        LootLimiter field = map.Limiters[1];
        LootContainer desk = map.Find(SyntheticGameData.SocketId)!;
        LootContainer farDesk = map.Find(SyntheticGameData.VariantSocketId)!;
        LootContainer untyped = map.Find(SyntheticGameData.UntypedSocketId)!;
        LooseItemSocket sofa = map.FindLooseItemSocket(SyntheticGameData.LooseId)!;

        // The unlimited drawer entry caps nothing, and Socket.Loot is not above Socket.LootContainer.
        Assert.Equal([office.Limits[0]], office.LimitsOn(desk.Location, desk.Tags[0]));
        Assert.Equal([office.Limits[1]], office.LimitsOn(sofa.Location, sofa.Tag));
        Assert.Empty(office.LimitsOn(farDesk.Location, farDesk.Tags[0]));
        Assert.Empty(field.LimitsOn(desk.Location, desk.Tags[0]));
        Assert.Equal([field.Limits[0]], field.LimitsOn(untyped.Location, untyped.Tags[0]));
        Assert.False(office.Covers(new WorldVector(double.NaN, 0, 0)));
    }

    [Fact]
    public void A_box_turns_with_its_rotation_and_reaches_half_its_radius()
    {
        LootLimiter box = new("/Box", "Box", null, LootLimiterShape.Box, new WorldVector(1000, 0, 0), new WorldRotation(0, 45, 0), 200, [], []);

        // Turned by 45°, the box's corner points along X: 141 cm out is inside, a point 90 cm along both X and Y is not.
        Assert.True(box.Covers(new WorldVector(1140, 0, 0)));
        Assert.False(box.Covers(new WorldVector(1090, 90, 0)));
        Assert.True(box.Covers(new WorldVector(1000, 0, 99)));
        Assert.False(box.Covers(new WorldVector(1000, 0, 101)));
        ConvexShape shape = Assert.Single(box.Shapes);
        Assert.Equal(8, shape.Vertices.Count);
        Assert.All(shape.Vertices, corner => Assert.Equal(Math.Sqrt(3) * 100, Distance(corner, box.Location), 6));
    }

    [Fact]
    public void A_sphere_covers_its_radius_and_is_drawn_just_around_it()
    {
        LootLimiter sphere = new("/Sphere", "Sphere", null, LootLimiterShape.Sphere, new WorldVector(0, 0, 500), default, 300, [], []);

        Assert.True(sphere.Covers(new WorldVector(0, 299, 500)));
        Assert.False(sphere.Covers(new WorldVector(0, 301, 500)));
        ConvexShape shape = Assert.Single(sphere.Shapes);
        Assert.All(shape.Vertices, corner => Assert.InRange(Distance(corner, sphere.Location), 300 - 1e-6, 330));
    }

    [Fact]
    public void A_rotation_turns_vectors_like_the_engine()
    {
        WorldVector turned = new WorldRotation(0, 90, 0).Rotate(new WorldVector(1, 0, 0));
        WorldVector raised = new WorldRotation(90, 0, 0).Rotate(new WorldVector(1, 0, 0));

        Assert.Equal(0, turned.X, 12);
        Assert.Equal(1, turned.Y, 12);
        Assert.Equal(1, raised.Z, 12);
    }

    [Fact]
    public void The_dams_limiters_all_have_a_shape_and_cover_sockets()
    {
        RealData.RequireGameData();
        MapLoot dam = Assert.Single(s_realGameData.Value.Maps, map => map.Name.StartsWith("TheDam", StringComparison.Ordinal));

        Assert.Equal(34, dam.Limiters.Count);
        Assert.Equal(31, dam.Limiters.Count(limiter => limiter.Shape == LootLimiterShape.Volumes));
        foreach (LootLimiter limiter in dam.Limiters)
        {
            Assert.NotEmpty(limiter.Shapes);
            int capped = dam.Containers.Count(container => container.Source == LootContainerSource.Socket && limiter.LimitsOn(container.Location, container.Tags[0]).Count > 0)
                + dam.LooseItemSockets.Count(socket => limiter.LimitsOn(socket.Location, socket.Tag).Count > 0);
            TestContext.Current.SendDiagnosticMessage($"{limiter.Id} ({limiter.Name}, {limiter.Shape}): {limiter.Shapes.Count} parts, {capped} capped sockets");
        }

        Assert.True(dam.Limiters.Count(limiter => dam.Containers.Any(container => container.Source == LootContainerSource.Socket && limiter.Covers(container.Location))) > 30);
    }

    private static double Distance(WorldVector a, WorldVector b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)) + ((a.Z - b.Z) * (a.Z - b.Z)));
}
