using System.Collections.Immutable;
using System.Numerics;
using Ares.Editor.Core.GameData;

namespace Ares.Editor.Core.Geometry;

/// <summary>
/// The parts of a map's collision file (<see cref="MapCollisionReader"/>) the 3D view draws: its streaming cells, its
/// bodies' shapes, where each body is placed and the landscape height-field tiles. Positions are Unreal units
/// (centimetres, Z up).
/// </summary>
/// <param name="GameBuildId">Build id of the game the file was extracted from.</param>
/// <param name="MapName">The map's world name.</param>
/// <param name="Cells">The streaming cells.</param>
/// <param name="Bodies">The collision bodies, each in its own local space.</param>
/// <param name="Placements">The bodies placed in the world.</param>
/// <param name="HeightFields">The landscape tiles.</param>
internal sealed record CollisionMap(
    int GameBuildId,
    string MapName,
    ImmutableArray<CollisionCell> Cells,
    ImmutableArray<CollisionBody> Bodies,
    ImmutableArray<BodyPlacement> Placements,
    ImmutableArray<HeightFieldTile> HeightFields);

/// <summary>A streaming cell, which owns placements and height-field tiles (server <c>MapCell</c>).</summary>
/// <param name="PackageName">The cell's package name.</param>
/// <param name="DataLayers">The data layers the cell belongs to; empty for the base map, which is always loaded.</param>
internal sealed record CollisionCell(string PackageName, ImmutableArray<string> DataLayers);

/// <summary>Which collision a body offers to simple and complex queries (server <c>CollisionTraceFlag</c>).</summary>
internal enum CollisionTraceFlag : byte
{
    /// <summary>Simple queries hit the simple shapes, complex queries the complex meshes.</summary>
    SimpleAndComplex = 1,

    /// <summary>Both kinds of query hit the simple shapes.</summary>
    SimpleAsComplex = 2,

    /// <summary>The complex triangle mesh is stored as a simple shape; both kinds of query hit it.</summary>
    ComplexAsSimple = 3,
}

/// <summary>
/// A collision body in its own local space (server <c>CollisionBody</c>): the simple shapes (boxes, spheres, capsules,
/// convex hulls, triangle meshes) and the complex triangle meshes.
/// </summary>
/// <param name="Name">Identifies the body's source; unique within a map.</param>
/// <param name="Boxes">Oriented boxes.</param>
/// <param name="Spheres">Spheres.</param>
/// <param name="Capsules">Capsules.</param>
/// <param name="ConvexHulls">Convex hulls.</param>
/// <param name="TriangleMeshes">Triangle meshes of the simple collision.</param>
/// <param name="TraceFlag">Which collision complex queries hit.</param>
/// <param name="ComplexMeshes">Triangle meshes only complex queries hit.</param>
internal sealed record CollisionBody(
    string Name,
    ImmutableArray<CollisionBox> Boxes,
    ImmutableArray<CollisionSphere> Spheres,
    ImmutableArray<CollisionCapsule> Capsules,
    ImmutableArray<ConvexHull> ConvexHulls,
    ImmutableArray<TriangleMesh> TriangleMeshes,
    CollisionTraceFlag TraceFlag,
    ImmutableArray<TriangleMesh> ComplexMeshes);

/// <summary>An oriented box in body space.</summary>
/// <param name="Center">Box centre.</param>
/// <param name="Rotation">Box orientation.</param>
/// <param name="HalfExtents">Half the box size along its own axes.</param>
internal readonly record struct CollisionBox(Vector3 Center, Quaternion Rotation, Vector3 HalfExtents);

/// <summary>A sphere in body space.</summary>
/// <param name="Center">Sphere centre.</param>
/// <param name="Radius">Sphere radius.</param>
internal readonly record struct CollisionSphere(Vector3 Center, float Radius);

/// <summary>A capsule in body space: a segment along the capsule's local Z axis, swept by a sphere.</summary>
/// <param name="Center">Centre of the segment.</param>
/// <param name="Rotation">Capsule orientation.</param>
/// <param name="Radius">Radius of the swept sphere.</param>
/// <param name="HalfLength">Distance from the centre to each cap's centre.</param>
internal readonly record struct CollisionCapsule(Vector3 Center, Quaternion Rotation, float Radius, float HalfLength);

/// <summary>A convex hull in body space: its vertices and its faces, each a polygon of vertex indices.</summary>
/// <param name="Vertices">The hull's vertices.</param>
/// <param name="Polygons">The faces, as ranges of <paramref name="PolygonVertexIndices"/>.</param>
/// <param name="PolygonVertexIndices">Vertex indices of every face, face after face.</param>
internal sealed record ConvexHull(ImmutableArray<Vector3> Vertices, ImmutableArray<HullPolygon> Polygons, ImmutableArray<byte> PolygonVertexIndices);

/// <summary>A face of a convex hull: a range of the hull's polygon vertex indices.</summary>
/// <param name="FirstIndex">Position of the face's first vertex index.</param>
/// <param name="IndexCount">Number of vertices of the face (at least 3).</param>
internal readonly record struct HullPolygon(int FirstIndex, int IndexCount);

/// <summary>A triangle mesh in body space.</summary>
/// <param name="Vertices">The vertices.</param>
/// <param name="Indices">Three vertex indices per triangle.</param>
internal sealed record TriangleMesh(ImmutableArray<Vector3> Vertices, ImmutableArray<int> Indices);

/// <summary>
/// One body placed in the world (server <c>BodyPlacement</c>): a body-space point <c>p</c> lies at
/// <c>Translation + Rotation · (Scale ∘ p)</c>.
/// </summary>
/// <param name="BodyIndex">Index in <see cref="CollisionMap.Bodies"/>.</param>
/// <param name="CellIndex">Index in <see cref="CollisionMap.Cells"/>.</param>
/// <param name="BlockedChannels">The collision channels the placement blocks, bit <c>n</c> for channel <c>n</c>.</param>
/// <param name="Translation">World position of the body's origin.</param>
/// <param name="Rotation">World rotation (unit quaternion).</param>
/// <param name="Scale">World scale per body axis; may be negative or non-uniform.</param>
internal readonly record struct BodyPlacement(int BodyIndex, int CellIndex, uint BlockedChannels, WorldVector Translation, Quaternion Rotation, Vector3 Scale);

/// <summary>
/// One landscape tile (server <c>HeightFieldTile</c>): an axis-aligned grid of samples; sample (x, y) lies at
/// <c>Origin + (x · SpacingX, y · SpacingY, height · HeightScale)</c>.
/// </summary>
/// <param name="CellIndex">Index in <see cref="CollisionMap.Cells"/>.</param>
/// <param name="Origin">World position of sample (0, 0) at height 0.</param>
/// <param name="SpacingX">Distance between samples along world X.</param>
/// <param name="SpacingY">Distance between samples along world Y.</param>
/// <param name="HeightScale">World Z per height unit.</param>
/// <param name="CountX">Number of samples along X (at least 2).</param>
/// <param name="CountY">Number of samples along Y (at least 2).</param>
/// <param name="Heights">Sample heights; sample (x, y) is at <c>y · CountX + x</c>.</param>
/// <param name="CellFlags">Grid cell flags (<see cref="HeightFieldCellFlags"/>); cell (x, y) is at <c>y · (CountX − 1) + x</c>.</param>
internal sealed record HeightFieldTile(
    int CellIndex,
    WorldVector Origin,
    float SpacingX,
    float SpacingY,
    float HeightScale,
    int CountX,
    int CountY,
    ImmutableArray<short> Heights,
    ImmutableArray<HeightFieldCellFlags> CellFlags);

/// <summary>
/// How a height-field grid cell is split into two triangles and which of them collide (server
/// <c>HeightFieldCellFlags</c>). Grid cell (x, y) has the corners (x, y), (x + 1, y), (x, y + 1) and (x + 1, y + 1); its
/// low-Y triangle holds the edge (x, y)–(x + 1, y).
/// </summary>
[Flags]
internal enum HeightFieldCellFlags : byte
{
    /// <summary>Diagonal from (x + 1, y) to (x, y + 1); both triangles collide.</summary>
    None = 0,

    /// <summary>The diagonal runs from (x, y) to (x + 1, y + 1) instead.</summary>
    DiagonalFromLowCorner = 1,

    /// <summary>The low-Y triangle is a hole.</summary>
    LowYTriangleHole = 2,

    /// <summary>The high-Y triangle is a hole.</summary>
    HighYTriangleHole = 4,
}
