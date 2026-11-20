using System.Buffers.Binary;
using System.Numerics;
using Ares.Editor.Core.GameData;
using Ares.Editor.Core.Geometry;

namespace Ares.Editor.Core.Tests;

/// <summary>The collision file reader and the chunks built from it, on a hand-written file and on the real Dam.</summary>
public sealed class MapGeometryTests
{
    [Fact]
    public void The_reader_reads_cells_bodies_placements_and_tiles()
    {
        CollisionMap map = Read(SyntheticCollision.Bytes());

        Assert.Equal(SyntheticCollision.BuildId, map.GameBuildId);
        Assert.Equal("TestMap", map.MapName);
        Assert.Equal(2, map.Cells.Length);
        Assert.Empty(map.Cells[0].DataLayers);
        Assert.Equal<string>([SyntheticCollision.Layer], map.Cells[1].DataLayers);
        Assert.Equal<string>(["Box", "ComplexOnly", "Hull", "Capsule"], map.Bodies.Select(body => body.Name));
        Assert.Equal(new Vector3(100, 100, 100), Assert.Single(map.Bodies[0].Boxes).HalfExtents);
        Assert.Equal(CollisionTraceFlag.SimpleAndComplex, map.Bodies[0].TraceFlag);
        Assert.Single(map.Bodies[0].ComplexMeshes);
        Assert.Equal(CollisionTraceFlag.SimpleAsComplex, map.Bodies[2].TraceFlag);
        Assert.Equal(4, Assert.Single(map.Bodies[2].ConvexHulls).Polygons.Length);
        Assert.Equal(50f, Assert.Single(map.Bodies[3].Capsules).HalfLength);
        Assert.Equal(4, map.Placements.Length);
        Assert.Equal(new WorldVector(1000, 2000, 300), map.Placements[0].Translation);
        Assert.Equal(1, map.Placements[1].CellIndex);
        HeightFieldTile tile = Assert.Single(map.HeightFields);
        Assert.Equal((5, 5, 100f), (tile.CountX, tile.CountY, tile.SpacingX));
        Assert.Equal(HeightFieldCellFlags.LowYTriangleHole, tile.CellFlags[0]);
        Assert.Equal(HeightFieldCellFlags.None, tile.CellFlags[1]);
        Assert.Equal((short)240, tile.Heights[24]);
    }

    [Fact]
    public void The_reader_refuses_other_versions_and_broken_files()
    {
        EditorDataException version = Assert.Throws<EditorDataException>(() => Read(SyntheticCollision.Bytes(version: 2)));
        Assert.Contains("version 2", version.Message, StringComparison.Ordinal);

        byte[] bytes = SyntheticCollision.Bytes();
        Assert.Throws<EditorDataException>(() => Read(bytes[..^5]));
        byte[] magic = [.. bytes];
        magic[0] = (byte)'X';
        Assert.Throws<EditorDataException>(() => Read(magic));
    }

    [Fact]
    public void Chunks_hold_visible_placements_and_the_landscape_in_three_space()
    {
        MapGeometry geometry = MapGeometry.Build(Read(SyntheticCollision.Bytes()), SyntheticCollision.BuildId, TimeSpan.Zero, CancellationToken.None);

        Assert.Null(geometry.BuildWarning);
        Assert.Equal("TestMap", geometry.MapName);
        GeometryLayer layer = Assert.Single(geometry.DataLayers);
        Assert.Equal(new GeometryLayer(SyntheticCollision.Layer, 1), layer);
        Assert.Equal([GeometryKind.Landscape, GeometryKind.Objects, GeometryKind.Objects, GeometryKind.Objects], geometry.Chunks.Select(chunk => chunk.Kind));

        // The 5 × 5 tile: the 2 × 2 block with the hole keeps its 4 cells (7 triangles), the other 3 blocks are 2 each.
        GeometryChunk landscape = geometry.Chunks[0];
        Assert.Equal(13, landscape.TriangleCount);
        Assert.Equal(14, landscape.VertexCount);

        // The box (its simple shape, not its complex triangle) at (1000, 2000, 300) cm: three = (x, z, y) / 100.
        GeometryChunk box = geometry.Chunks[1];
        Assert.Equal(12, box.TriangleCount);
        Assert.Equal(new Vector3(9, 2, 19), box.Minimum);
        Assert.Equal(new Vector3(11, 4, 21), box.Maximum);
        Assert.Empty(box.Layers);

        GeometryChunk complexOnly = geometry.Chunks[2];
        Assert.Equal(2, complexOnly.TriangleCount);
        Assert.Equal<int>([0], complexOnly.Layers);

        // The capsule, scaled by 2: radius 60 cm, segment from 0 to 200 cm.
        GeometryChunk capsule = geometry.Chunks[3];
        Assert.Equal(-0.6f, capsule.Minimum.Y, 3);
        Assert.Equal(2.6f, capsule.Maximum.Y, 3);
        Assert.Equal(299.4f, capsule.Minimum.Z, 3);

        GeometryStatistics statistics = geometry.Statistics;
        Assert.Equal((2, 4, 4, 1, 3), (statistics.Cells, statistics.Bodies, statistics.Placements, statistics.HeightFieldTiles, statistics.DrawnPlacements));
        Assert.Equal(12 + 2 + capsule.TriangleCount, statistics.ObjectTriangles);
        Assert.Equal(13, statistics.LandscapeTriangles);
        Assert.Equal(geometry.Chunks.Sum(chunk => (long)chunk.Data.Length), statistics.Bytes);
    }

    [Fact]
    public void Chunk_data_is_positions_then_short_indices()
    {
        MapGeometry geometry = MapGeometry.Build(Read(SyntheticCollision.Bytes()), SyntheticCollision.BuildId, TimeSpan.Zero, CancellationToken.None);
        GeometryChunk box = geometry.Chunks[1];

        Assert.True(box.ShortIndices);
        ReadOnlySpan<byte> data = box.Data.Span;
        Assert.Equal((box.VertexCount * 12) + (((box.IndexCount * 2) + 3) & ~3), data.Length);
        float firstX = BinaryPrimitives.ReadSingleLittleEndian(data);
        Assert.InRange(firstX, box.Minimum.X, box.Maximum.X);
        ReadOnlySpan<byte> indices = data[(box.VertexCount * 12)..];
        for (int index = 0; index < box.IndexCount; index++)
        {
            Assert.InRange(BinaryPrimitives.ReadUInt16LittleEndian(indices[(index * 2)..]), 0, box.VertexCount - 1);
        }
    }

    [Fact]
    public void Loading_finds_the_map_file_and_compares_builds()
    {
        using TemporaryFolder folder = new();
        SyntheticGameData.Write(folder);
        EditorGameData gameData = EditorGameData.Load(folder.Path, SyntheticGameData.SpawnDefaults);

        EditorDataException missing = Assert.Throws<EditorDataException>(() => MapGeometry.Load(gameData, SyntheticGameData.Map, CancellationToken.None));
        Assert.Contains(".collision", missing.Message, StringComparison.Ordinal);

        string path = MapGeometry.CollisionFile(folder.Path, SyntheticGameData.Map);
        File.WriteAllBytes(path, SyntheticCollision.Bytes());
        MapGeometry geometry = MapGeometry.Load(gameData, SyntheticGameData.Map, CancellationToken.None);
        Assert.Equal(SyntheticCollision.BuildId, gameData.Manifest.GameBuildId);
        Assert.Null(geometry.BuildWarning);
        Assert.Equal(SyntheticCollision.BuildId, geometry.GameBuildId);

        MapGeometry other = MapGeometry.Build(Read(SyntheticCollision.Bytes()), SyntheticCollision.BuildId + 1, TimeSpan.Zero, CancellationToken.None);
        Assert.Contains("124", other.BuildWarning, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dam_geometry_has_plausible_counts()
    {
        RealData.RequireGameData();
        EditorGameData gameData = EditorGameData.Load(RealData.GameDataDirectory!, RealData.SpawnDefaults);
        string dam = Assert.Single(gameData.Manifest.MapNames, name => name.StartsWith("TheDam", StringComparison.Ordinal));
        Assert.SkipWhen(!File.Exists(MapGeometry.CollisionFile(gameData.Directory, dam)), "The game data has no collision file for the Dam.");

        MapGeometry geometry = MapGeometry.Load(gameData, dam, TestContext.Current.CancellationToken);
        GeometryStatistics statistics = geometry.Statistics;
        TestContext.Current.SendDiagnosticMessage(
            $"{dam}: {statistics.Cells} cells, {statistics.Bodies} bodies, {statistics.Placements} placements ({statistics.DrawnPlacements} drawn), " +
            $"{statistics.HeightFieldTiles} tiles; {statistics.ObjectTriangles:N0} object + {statistics.LandscapeTriangles:N0} landscape triangles in " +
            $"{geometry.Chunks.Count} chunks, {statistics.Bytes / 1048576.0:F1} MB; read {statistics.ReadTime.TotalMilliseconds:F0} ms, build {statistics.BuildTime.TotalMilliseconds:F0} ms");

        Assert.Null(geometry.BuildWarning);
        Assert.InRange(statistics.Cells, 100, 10_000);
        Assert.InRange(statistics.Bodies, 1_000, 100_000);
        Assert.InRange(statistics.Placements, 50_000, 1_000_000);
        Assert.InRange(statistics.HeightFieldTiles, 100, 10_000);
        Assert.InRange(statistics.DrawnPlacements, statistics.Placements * 0.95, statistics.Placements);
        Assert.InRange(statistics.ObjectTriangles + statistics.LandscapeTriangles, 5_000_000, 60_000_000);
        Assert.NotEmpty(geometry.DataLayers);
        Assert.Contains(geometry.Chunks, chunk => chunk.Layers.Count > 0);
        Assert.All(geometry.Chunks, chunk => Assert.True(chunk.Minimum.X <= chunk.Maximum.X && chunk.Minimum.Y <= chunk.Maximum.Y && chunk.Minimum.Z <= chunk.Maximum.Z));
    }

    private static CollisionMap Read(byte[] bytes)
    {
        using MemoryStream stream = new(bytes);
        return MapCollisionReader.Read(stream, "test.collision");
    }
}
