using System.Collections.Immutable;
using System.Numerics;

namespace Ares.Editor.Core.Geometry;

/// <summary>
/// The triangles the view draws for one collision body, in the body's local space (centimetres): its simple shapes
/// (what players collide with), or its complex triangle meshes when it has no simple shapes.
/// </summary>
/// <remarks>
/// Simple collision outlines walls, floors, stairs and props at about a tenth of the complex meshes' triangles (on the
/// Dam 15.5 M drawn triangles instead of 176 M after placing); the complex meshes add mostly rock and foliage detail.
/// Spheres and capsules are tessellated coarsely; placing scales them per axis.
/// </remarks>
internal sealed class BodyMesh
{
    private const int RingSegments = 12;
    private const int HemisphereRings = 3;

    private readonly List<Vector3> _vertices = [];
    private readonly List<int> _indices = [];

    private BodyMesh()
    {
    }

    /// <summary>Vertices in body space.</summary>
    public IReadOnlyList<Vector3> Vertices => _vertices;

    /// <summary>Three vertex indices per triangle.</summary>
    public IReadOnlyList<int> Indices => _indices;

    /// <summary>Builds the mesh the view draws for a body.</summary>
    /// <param name="body">The body.</param>
    /// <returns>The mesh; empty when the body has nothing to draw.</returns>
    public static BodyMesh Of(CollisionBody body)
    {
        BodyMesh mesh = new();
        bool hasSimple = !body.Boxes.IsEmpty || !body.Spheres.IsEmpty || !body.Capsules.IsEmpty || !body.ConvexHulls.IsEmpty || !body.TriangleMeshes.IsEmpty;
        if (!hasSimple)
        {
            mesh.AddMeshes(body.ComplexMeshes);
            return mesh;
        }

        foreach (CollisionBox box in body.Boxes)
        {
            mesh.AddBox(box);
        }

        foreach (CollisionSphere sphere in body.Spheres)
        {
            mesh.AddCapsule(sphere.Center, Quaternion.Identity, sphere.Radius, 0);
        }

        foreach (CollisionCapsule capsule in body.Capsules)
        {
            mesh.AddCapsule(capsule.Center, capsule.Rotation, capsule.Radius, capsule.HalfLength);
        }

        foreach (ConvexHull hull in body.ConvexHulls)
        {
            mesh.AddHull(hull);
        }

        mesh.AddMeshes(body.TriangleMeshes);
        return mesh;
    }

    private void AddMeshes(ImmutableArray<TriangleMesh> meshes)
    {
        foreach (TriangleMesh source in meshes)
        {
            int first = _vertices.Count;
            _vertices.AddRange(source.Vertices);
            foreach (int index in source.Indices)
            {
                _indices.Add(first + index);
            }
        }
    }

    private void AddHull(ConvexHull hull)
    {
        int first = _vertices.Count;
        _vertices.AddRange(hull.Vertices);
        foreach (HullPolygon polygon in hull.Polygons)
        {
            int start = first + hull.PolygonVertexIndices[polygon.FirstIndex];
            for (int corner = 1; corner < polygon.IndexCount - 1; corner++)
            {
                _indices.Add(start);
                _indices.Add(first + hull.PolygonVertexIndices[polygon.FirstIndex + corner]);
                _indices.Add(first + hull.PolygonVertexIndices[polygon.FirstIndex + corner + 1]);
            }
        }
    }

    // Corner c has the half extents' sign of bit 0 (X), 1 (Y) and 2 (Z); each face is two triangles.
    private void AddBox(CollisionBox box)
    {
        int first = _vertices.Count;
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 offset = new(
                (corner & 1) == 0 ? -box.HalfExtents.X : box.HalfExtents.X,
                (corner & 2) == 0 ? -box.HalfExtents.Y : box.HalfExtents.Y,
                (corner & 4) == 0 ? -box.HalfExtents.Z : box.HalfExtents.Z);
            _vertices.Add(box.Center + Vector3.Transform(offset, box.Rotation));
        }

        ReadOnlySpan<int> faces = [0, 2, 3, 1, 4, 5, 7, 6, 0, 1, 5, 4, 2, 6, 7, 3, 0, 4, 6, 2, 1, 3, 7, 5];
        for (int face = 0; face < faces.Length; face += 4)
        {
            AddQuad(first + faces[face], first + faces[face + 1], first + faces[face + 2], first + faces[face + 3]);
        }
    }

    // A sphere swept along the local Z axis from -halfLength to +halfLength (a sphere when it is 0): rings of RingSegments
    // vertices at HemisphereRings latitudes per cap, from the bottom pole to the top pole.
    private void AddCapsule(Vector3 center, Quaternion rotation, float radius, float halfLength)
    {
        List<(float Z, float Radius)> latitudes = [];
        for (int ring = HemisphereRings - 1; ring >= 0; ring--)
        {
            latitudes.Add(Latitude(ring, -1));
        }

        for (int ring = halfLength > 0 ? 0 : 1; ring < HemisphereRings; ring++)
        {
            latitudes.Add(Latitude(ring, 1));
        }

        int bottom = _vertices.Count;
        _vertices.Add(center + Vector3.Transform(new Vector3(0, 0, -(halfLength + radius)), rotation));
        int firstRing = _vertices.Count;
        foreach ((float z, float ringRadius) in latitudes)
        {
            for (int segment = 0; segment < RingSegments; segment++)
            {
                double angle = 2 * Math.PI * segment / RingSegments;
                Vector3 local = new(ringRadius * (float)Math.Cos(angle), ringRadius * (float)Math.Sin(angle), z);
                _vertices.Add(center + Vector3.Transform(local, rotation));
            }
        }

        int top = _vertices.Count;
        _vertices.Add(center + Vector3.Transform(new Vector3(0, 0, halfLength + radius), rotation));
        int lastRing = firstRing + ((latitudes.Count - 1) * RingSegments);
        for (int segment = 0; segment < RingSegments; segment++)
        {
            int next = (segment + 1) % RingSegments;
            AddTriangle(bottom, firstRing + next, firstRing + segment);
            AddTriangle(top, lastRing + segment, lastRing + next);
            for (int ring = firstRing; ring < lastRing; ring += RingSegments)
            {
                AddQuad(ring + segment, ring + next, ring + RingSegments + next, ring + RingSegments + segment);
            }
        }

        return;

        (float Z, float Radius) Latitude(int ring, int cap)
        {
            double angle = Math.PI / 2 * ring / HemisphereRings;
            return ((cap * halfLength) + (cap * radius * (float)Math.Sin(angle)), radius * (float)Math.Cos(angle));
        }
    }

    private void AddQuad(int a, int b, int c, int d)
    {
        AddTriangle(a, b, c);
        AddTriangle(a, c, d);
    }

    private void AddTriangle(int a, int b, int c)
    {
        _indices.Add(a);
        _indices.Add(b);
        _indices.Add(c);
    }
}
