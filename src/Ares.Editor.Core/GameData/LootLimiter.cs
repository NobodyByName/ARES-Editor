using Ares.Editor.Core.Matching;

namespace Ares.Editor.Core.GameData;

/// <summary>Which points a loot placement limiter covers. The numbers are the ones the game data stores (ARES-Server <c>LootLimiterShape</c>).</summary>
public enum LootLimiterShape
{
    /// <summary>No point.</summary>
    None = 0,

    /// <summary>Points within <see cref="LootLimiter.Radius"/> of <see cref="LootLimiter.Location"/>.</summary>
    Sphere = 1,

    /// <summary>
    /// Points inside a cube centred on <see cref="LootLimiter.Location"/>, turned by <see cref="LootLimiter.Rotation"/>, reaching
    /// <see cref="LootLimiter.Radius"/> / 2 from the centre along each of its axes.
    /// </summary>
    Box = 2,

    /// <summary>Points inside any convex part of the limiter's volumes.</summary>
    Volumes = 3,
}

/// <summary>A limiter's cap on the spawns of one gameplay tag (ARES-Server <c>LootLimit</c>).</summary>
/// <param name="Tag">The tag; a socket counts when its tag is this tag or below it, ignoring case.</param>
/// <param name="Unlimited">True when the entry caps nothing.</param>
/// <param name="Limit">How many spawns the limiter allows on its sockets of the tag.</param>
public sealed record LootLimit(string Tag, bool Unlimited, int Limit)
{
    /// <summary>Whether the entry caps a socket tag: it is not unlimited and the tag is its tag or below it (ARES-Server <c>LootLimiterRules.Matches</c>).</summary>
    /// <param name="socketTag">The socket's tag.</param>
    /// <returns>True when the entry caps sockets of the tag.</returns>
    public bool Caps(string socketTag) => !Unlimited && LootMatching.IsTagOrBelow(socketTag, Tag);
}

/// <summary>
/// A loot placement limiter of a map: an area that caps, per gameplay tag, how many sockets inside it get loot (container
/// sockets and loose item sockets; ARES-Server <c>LootLimiter</c> and <c>LootLimiterRules</c>). The spawn settings'
/// <c>limiters</c> switch turns all of them on or off.
/// </summary>
public sealed class LootLimiter
{
    // The polyhedron that draws a sphere: planes tangent to it at this many longitudes on each of this many latitudes,
    // and at the poles.
    private const int SphereLongitudes = 16;
    private const int SphereLatitudes = 7;

    private readonly IReadOnlyList<IReadOnlyList<WorldPlane>> _hulls;

    /// <summary>Creates a limiter.</summary>
    /// <param name="id">Its id: <c>cell/actor</c>.</param>
    /// <param name="name">The name the map gives it (usually <c>LootPlacementLimiter_</c> and the area it covers).</param>
    /// <param name="dataLayer">The data layer that streams it, or null when it is always present.</param>
    /// <param name="shape">Which points it covers.</param>
    /// <param name="location">The centre of its sphere or box.</param>
    /// <param name="rotation">The rotation of its box.</param>
    /// <param name="radius">The sphere's radius, or twice the box's half extent.</param>
    /// <param name="hulls">For <see cref="LootLimiterShape.Volumes"/>: the planes of every convex part of its volumes.</param>
    /// <param name="limits">Its caps, in table order.</param>
    public LootLimiter(string id, string name, string? dataLayer, LootLimiterShape shape, WorldVector location, WorldRotation rotation, double radius,
        IReadOnlyList<IReadOnlyList<WorldPlane>> hulls, IReadOnlyList<LootLimit> limits)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        DataLayer = dataLayer;
        Shape = shape;
        Location = location;
        Rotation = rotation;
        Radius = radius;
        _hulls = hulls ?? throw new ArgumentNullException(nameof(hulls));
        Limits = limits ?? throw new ArgumentNullException(nameof(limits));
        Shapes = [.. DrawnHulls().Select(ConvexShape.FromPlanes).OfType<ConvexShape>()];
    }

    /// <summary>The id: <c>cell/actor</c>.</summary>
    public string Id { get; }

    /// <summary>The name the map gives it.</summary>
    public string Name { get; }

    /// <summary>The data layer that streams it, or null when it is always present; it caps only while that layer is active.</summary>
    public string? DataLayer { get; }

    /// <summary>Which points it covers.</summary>
    public LootLimiterShape Shape { get; }

    /// <summary>The centre of its sphere or box.</summary>
    public WorldVector Location { get; }

    /// <summary>The rotation of its box.</summary>
    public WorldRotation Rotation { get; }

    /// <summary>The sphere's radius, or twice the box's half extent, in centimetres.</summary>
    public double Radius { get; }

    /// <summary>Its caps, in table order; an unlimited entry caps nothing.</summary>
    public IReadOnlyList<LootLimit> Limits { get; }

    /// <summary>The covered space for drawing: the convex parts of its volumes, its box, or a many-sided solid around its sphere.</summary>
    public IReadOnlyList<ConvexShape> Shapes { get; }

    /// <summary>
    /// Whether it covers a point (ARES-Server <c>LootLimiterRules.IsPointInside</c>): within its sphere, inside its box or
    /// inside a convex part of its volumes. A point with a coordinate that is not finite is never covered.
    /// </summary>
    /// <param name="point">The world location.</param>
    /// <returns>True when the limiter covers the point.</returns>
    public bool Covers(WorldVector point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Z))
        {
            return false;
        }

        WorldVector offset = new(point.X - Location.X, point.Y - Location.Y, point.Z - Location.Z);
        switch (Shape)
        {
            case LootLimiterShape.Sphere:
                return Dot(offset, offset) <= Radius * Radius;
            case LootLimiterShape.Box:
                double half = Radius / 2;
                return Math.Abs(Dot(offset, Rotation.Rotate(new WorldVector(1, 0, 0)))) <= half
                    && Math.Abs(Dot(offset, Rotation.Rotate(new WorldVector(0, 1, 0)))) <= half
                    && Math.Abs(Dot(offset, Rotation.Rotate(new WorldVector(0, 0, 1)))) <= half;
            case LootLimiterShape.Volumes:
                return _hulls.Any(hull => hull.All(plane => Dot(plane.Normal, point) <= plane.Distance));
            default:
                return false;
        }
    }

    /// <summary>The entries that cap a socket: none when the limiter does not cover it, else those that cap its tag (ARES-Server <c>LootSpawnSelection</c>).</summary>
    /// <param name="location">The socket's location.</param>
    /// <param name="tag">The socket's tag.</param>
    /// <returns>The entries, in table order.</returns>
    public IReadOnlyList<LootLimit> LimitsOn(WorldVector location, string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        LootLimit[] caps = [.. Limits.Where(limit => limit.Caps(tag))];
        return caps.Length > 0 && Covers(location) ? caps : [];
    }

    private IEnumerable<IReadOnlyList<WorldPlane>> DrawnHulls()
    {
        switch (Shape)
        {
            case LootLimiterShape.Volumes:
                return _hulls;
            case LootLimiterShape.Box:
                double half = Radius / 2;
                WorldPlane[] box = [.. new[] { new WorldVector(1, 0, 0), new WorldVector(0, 1, 0), new WorldVector(0, 0, 1) }
                    .Select(Rotation.Rotate)
                    .SelectMany(axis => new[] { axis, new WorldVector(-axis.X, -axis.Y, -axis.Z) })
                    .Select(normal => new WorldPlane(normal, Dot(normal, Location) + half))];
                return [box];
            case LootLimiterShape.Sphere:
                List<WorldVector> normals = [new(0, 0, 1), new(0, 0, -1)];
                for (int latitude = 1; latitude <= SphereLatitudes; latitude++)
                {
                    (double sinLatitude, double cosLatitude) = Math.SinCos((-0.5 + (latitude / (SphereLatitudes + 1.0))) * Math.PI);
                    for (int longitude = 0; longitude < SphereLongitudes; longitude++)
                    {
                        (double sinLongitude, double cosLongitude) = Math.SinCos(2 * Math.PI * longitude / SphereLongitudes);
                        normals.Add(new WorldVector(cosLatitude * cosLongitude, cosLatitude * sinLongitude, sinLatitude));
                    }
                }

                return [[.. normals.Select(normal => new WorldPlane(normal, Dot(normal, Location) + Radius))]];
            default:
                return [];
        }
    }

    private static double Dot(WorldVector a, WorldVector b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);
}
