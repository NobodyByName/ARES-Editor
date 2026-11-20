namespace Ares.Editor.Core.GameData;

/// <summary>A world location or direction in Unreal units: centimetres, Z up.</summary>
/// <param name="X">X (forward in Unreal's left-handed frame).</param>
/// <param name="Y">Y (right).</param>
/// <param name="Z">Z (up).</param>
public readonly record struct WorldVector(double X, double Y, double Z);

/// <summary>A world plane in Unreal units: the points <c>p</c> with <c>Normal·p = Distance</c>; its inner side is <c>Normal·p ≤ Distance</c>.</summary>
/// <param name="Normal">The normal, pointing to the outer side.</param>
/// <param name="Distance">The plane's offset along the normal, in centimetres times the normal's length.</param>
public readonly record struct WorldPlane(WorldVector Normal, double Distance);

/// <summary>A world rotation in degrees, as Unreal's rotator stores it.</summary>
/// <param name="Pitch">Rotation about the right axis (Y).</param>
/// <param name="Yaw">Rotation about the up axis (Z).</param>
/// <param name="Roll">Rotation about the forward axis (X).</param>
public readonly record struct WorldRotation(double Pitch, double Yaw, double Roll)
{
    /// <summary>Rotates a vector like the engine's rotation matrix of this rotator (ARES-Server <c>Rotator.Rotate</c>).</summary>
    /// <param name="vector">The vector.</param>
    /// <returns>The rotated vector.</returns>
    public WorldVector Rotate(WorldVector vector)
    {
        (double sp, double cp) = Math.SinCos(Pitch * Math.PI / 180);
        (double sy, double cy) = Math.SinCos(Yaw * Math.PI / 180);
        (double sr, double cr) = Math.SinCos(Roll * Math.PI / 180);
        WorldVector x = new(cp * cy, cp * sy, sp);
        WorldVector y = new((sr * sp * cy) - (cr * sy), (sr * sp * sy) + (cr * cy), -sr * cp);
        WorldVector z = new(-((cr * sp * cy) + (sr * sy)), (cy * sr) - (cr * sp * sy), cr * cp);
        return new WorldVector(
            (vector.X * x.X) + (vector.Y * y.X) + (vector.Z * z.X),
            (vector.X * x.Y) + (vector.Y * y.Y) + (vector.Z * z.Y),
            (vector.X * x.Z) + (vector.Y * y.Z) + (vector.Z * z.Z));
    }
}

/// <summary>
/// The loot value of the map area around a container (the game's point-of-interest loot value), the key of
/// <c>areaMultipliers</c> in <c>loot-spawn.json</c> and <c>loot-contents.json</c>. The numbers are the ones the game data
/// stores.
/// </summary>
public enum LootArea
{
    /// <summary>Outside every area with a loot value.</summary>
    Unknown = 0,

    /// <summary>An area of low loot value.</summary>
    Low = 1,

    /// <summary>An area of medium loot value.</summary>
    Medium = 2,

    /// <summary>An area of high loot value.</summary>
    High = 3,
}

/// <summary>Where a loot container of a map comes from in the game data.</summary>
public enum LootContainerSource
{
    /// <summary>A loot socket of a placed prop's mesh (<c>loot-sockets.json</c>), where the server may spawn a container.</summary>
    Socket,

    /// <summary>A container actor the map places itself (<c>placed-loot.json</c>).</summary>
    Placed,

    /// <summary>A dynamic loot spawner (<c>dynamic-loot-spawners.json</c>) that places containers on some of its points.</summary>
    Spawner,
}
