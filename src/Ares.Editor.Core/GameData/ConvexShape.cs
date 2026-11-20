namespace Ares.Editor.Core.GameData;

/// <summary>
/// A convex polyhedron in Unreal units (centimetres, Z up) as a triangle mesh, built from the planes of a game hull to
/// draw it. Each triangle <c>(a, b, c)</c> is wound so that <c>(b − a) × (c − a)</c>, taken on the raw coordinates, points
/// out of the shape.
/// </summary>
/// <remarks>
/// Each plane's face is a large square on the plane clipped by the inner side of every other plane, which leaves the
/// face's corners in order around its outward normal; the face is then fan-triangulated. Clipping needs no tolerance to
/// decide which corners belong to a face, so the game's nearly coplanar planes (often a fraction of a centimetre apart)
/// give thin faces rather than overlapping ones.
/// </remarks>
public sealed class ConvexShape
{
    // Half the side (cm) of the square each face starts as: 1000 km, far beyond any map. A face that still reaches half
    // of it belongs to a hull that is open on that side.
    private const double FaceExtent = 1e8;

    // Corners of different faces this close (cm) are one vertex; a triangle with less area (cm²) is dropped.
    private const double MergeDistance = 0.01;
    private const double MinimumArea = 1e-6;

    // Two planes this close in normal (1 − cos) and offset (cm) are one repeated plane, and only the first gives a face.
    // Planes that differ more, however little, clip each other and share the face between them.
    private const double SameNormal = 1e-12;
    private const double SameDistance = 1e-6;

    private ConvexShape(WorldVector[] vertices, int[] triangles)
    {
        Vertices = vertices;
        Triangles = triangles;
    }

    /// <summary>The corners of the shape (the corners of its faces, merged).</summary>
    public IReadOnlyList<WorldVector> Vertices { get; }

    /// <summary>The faces as triangles: three indices into <see cref="Vertices"/> each, wound outward.</summary>
    public IReadOnlyList<int> Triangles { get; }

    /// <summary>The shape of a hull: the points on the inner side of every plane (<c>Normal·p ≤ Distance</c>).</summary>
    /// <param name="planes">The hull's planes.</param>
    /// <returns>The shape, or null when the planes bound no volume (open on a side, or empty).</returns>
    public static ConvexShape? FromPlanes(IReadOnlyList<WorldPlane> planes)
    {
        ArgumentNullException.ThrowIfNull(planes);
        WorldPlane[] unit = [.. planes.Where(plane => Length(plane.Normal) > 0).Select(Normalize)];
        List<WorldVector> vertices = [];
        List<int> triangles = [];
        for (int index = 0; index < unit.Length; index++)
        {
            List<WorldVector> face = Face(unit, index);
            if (face.Count < 3)
            {
                continue;
            }

            WorldVector origin = Scale(unit[index].Normal, unit[index].Distance);
            if (face.Exists(corner => Length(Subtract(corner, origin)) > FaceExtent / 2))
            {
                return null;
            }

            for (int corner = 1; corner < face.Count - 1; corner++)
            {
                if (Length(Cross(Subtract(face[corner], face[0]), Subtract(face[corner + 1], face[0]))) / 2 >= MinimumArea)
                {
                    triangles.AddRange([VertexOf(vertices, face[0]), VertexOf(vertices, face[corner]), VertexOf(vertices, face[corner + 1])]);
                }
            }
        }

        return triangles.Count >= 4 * 3 ? new ConvexShape([.. vertices], [.. triangles]) : null;
    }

    // The face on a plane: a square around the point of the plane nearest the origin, counter-clockwise around the
    // normal, clipped by every other plane. Empty when the plane does not bound the shape or repeats an earlier plane.
    private static List<WorldVector> Face(WorldPlane[] planes, int index)
    {
        WorldPlane plane = planes[index];
        WorldVector u = Unit(Cross(plane.Normal, SmallestAxis(plane.Normal)));
        WorldVector v = Cross(plane.Normal, u);
        WorldVector origin = Scale(plane.Normal, plane.Distance);
        List<WorldVector> face =
        [
            Add(origin, Add(Scale(u, -FaceExtent), Scale(v, -FaceExtent))),
            Add(origin, Add(Scale(u, FaceExtent), Scale(v, -FaceExtent))),
            Add(origin, Add(Scale(u, FaceExtent), Scale(v, FaceExtent))),
            Add(origin, Add(Scale(u, -FaceExtent), Scale(v, FaceExtent))),
        ];
        for (int other = 0; other < planes.Length && face.Count >= 3; other++)
        {
            if (other == index)
            {
                continue;
            }

            if (1 - Dot(planes[other].Normal, plane.Normal) <= SameNormal && Math.Abs(planes[other].Distance - plane.Distance) <= SameDistance)
            {
                if (other < index)
                {
                    return [];
                }

                continue;
            }

            face = Clip(face, planes[other]);
        }

        return face;
    }

    // The part of a convex polygon on the inner side of a plane (Sutherland–Hodgman), keeping the corners' order.
    private static List<WorldVector> Clip(List<WorldVector> polygon, WorldPlane plane)
    {
        List<WorldVector> clipped = [];
        for (int corner = 0; corner < polygon.Count; corner++)
        {
            WorldVector current = polygon[corner];
            WorldVector next = polygon[(corner + 1) % polygon.Count];
            double currentHeight = Dot(plane.Normal, current) - plane.Distance;
            double nextHeight = Dot(plane.Normal, next) - plane.Distance;
            if (currentHeight <= 0)
            {
                clipped.Add(current);
            }

            if ((currentHeight < 0 && nextHeight > 0) || (currentHeight > 0 && nextHeight < 0))
            {
                clipped.Add(Add(current, Scale(Subtract(next, current), currentHeight / (currentHeight - nextHeight))));
            }
        }

        return clipped;
    }

    // The index of a corner among the vertices, adding it unless one is within MergeDistance.
    private static int VertexOf(List<WorldVector> vertices, WorldVector corner)
    {
        int index = vertices.FindIndex(vertex => Length(Subtract(vertex, corner)) <= MergeDistance);
        if (index >= 0)
        {
            return index;
        }

        vertices.Add(corner);
        return vertices.Count - 1;
    }

    // The unit axis least aligned with a direction, to build a basis on a plane.
    private static WorldVector SmallestAxis(WorldVector direction)
    {
        double x = Math.Abs(direction.X);
        double y = Math.Abs(direction.Y);
        double z = Math.Abs(direction.Z);
        if (x <= y && x <= z)
        {
            return new WorldVector(1, 0, 0);
        }

        return y <= z ? new WorldVector(0, 1, 0) : new WorldVector(0, 0, 1);
    }

    private static WorldPlane Normalize(WorldPlane plane)
    {
        double length = Length(plane.Normal);
        return new WorldPlane(Scale(plane.Normal, 1 / length), plane.Distance / length);
    }

    private static WorldVector Unit(WorldVector vector) => Scale(vector, 1 / Length(vector));

    private static WorldVector Add(WorldVector a, WorldVector b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    private static WorldVector Subtract(WorldVector a, WorldVector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static WorldVector Scale(WorldVector vector, double factor) => new(vector.X * factor, vector.Y * factor, vector.Z * factor);

    private static double Dot(WorldVector a, WorldVector b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    private static WorldVector Cross(WorldVector a, WorldVector b) => new((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));

    private static double Length(WorldVector vector) => Math.Sqrt(Dot(vector, vector));
}
