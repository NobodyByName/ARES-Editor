using System.Text;

namespace Ares.Editor.Core.Tests;

/// <summary>
/// A small map-collision file (ARES <c>ARESCOLL</c> version 3) written byte by byte, following ARES-Server's
/// <c>MapCollisionFile</c> layout: two cells (the base map and one data layer), four bodies (a box whose complex mesh is
/// not drawn, a complex-only quad, a hull placed only as an invisible blocker, a capsule) and one 5 × 5 landscape tile
/// with a hole.
/// </summary>
internal static class SyntheticCollision
{
    public const int BuildId = 123;
    public const string Layer = "DataLayer_Test";

    // ECollisionChannel bits: WorldStatic, Visibility, and the player pawn's game channel.
    private const uint WorldStatic = 1u << 0;
    private const uint Visibility = 1u << 3;
    private const uint PlayerPawn = 1u << 16;

    /// <summary>The file's bytes.</summary>
    /// <param name="version">The format version to write in the header.</param>
    public static byte[] Bytes(int version = 3)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write("ARESCOLL"u8);
        writer.Write(version);
        writer.Write(BuildId);
        WriteString(writer, "TestMap");
        Section(writer, "CELL", Cells);
        Section(writer, "BODY", Bodies);
        Section(writer, "PLAC", Placements);
        Section(writer, "HFLD", HeightFields);
        writer.Flush();
        return stream.ToArray();
    }

    private static void Section(BinaryWriter writer, string tag, Action<BinaryWriter> payload)
    {
        using MemoryStream body = new();
        using (BinaryWriter bodyWriter = new(body, Encoding.UTF8, leaveOpen: true))
        {
            payload(bodyWriter);
        }

        writer.Write(Encoding.ASCII.GetBytes(tag));
        writer.Write(body.Length);
        writer.Write(body.ToArray());
    }

    private static void Cells(BinaryWriter writer)
    {
        writer.Write(2);
        WriteString(writer, "/Game/Cell0");
        writer.Write(0);
        WriteString(writer, "/Game/Cell1");
        writer.Write(1);
        WriteString(writer, Layer);
    }

    // Per body: name, boxes, spheres, capsules, hulls, simple meshes, trace flag, complex meshes.
    private static void Bodies(BinaryWriter writer)
    {
        writer.Write(4);

        // 0: a 2 m box with a one-triangle complex mesh (SimpleAndComplex): the box is drawn.
        WriteString(writer, "Box");
        writer.Write(1);
        WriteVector(writer, 0, 0, 0);
        WriteQuaternion(writer, 0, 0, 0, 1);
        WriteVector(writer, 100, 100, 100);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write((byte)1);
        writer.Write(1);
        WriteMesh(writer, [0, 0, 0, 1, 0, 0, 0, 1, 0], [0, 1, 2], withMaterials: true);

        // 1: complex-only quad (no simple shapes): the complex mesh is drawn.
        WriteString(writer, "ComplexOnly");
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write((byte)1);
        writer.Write(1);
        WriteMesh(writer, [0, 0, 0, 100, 0, 0, 100, 100, 0, 0, 100, 0], [0, 1, 2, 0, 2, 3], withMaterials: false);

        // 2: a tetrahedron hull (SimpleAsComplex).
        WriteString(writer, "Hull");
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(1);
        writer.Write(4);
        WriteVector(writer, 0, 0, 0);
        WriteVector(writer, 100, 0, 0);
        WriteVector(writer, 0, 100, 0);
        WriteVector(writer, 0, 0, 100);
        writer.Write(4);
        for (int polygon = 0; polygon < 4; polygon++)
        {
            WriteVector(writer, 0, 0, 1);
            writer.Write(0f);
            writer.Write(polygon * 3);
            writer.Write(3);
        }

        writer.Write(12);
        writer.Write(new byte[] { 0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3 });
        WriteVector(writer, 0, 0, 0);
        WriteVector(writer, 100, 100, 100);
        writer.Write(0);
        writer.Write((byte)2);
        writer.Write(0);

        // 3: a capsule (SimpleAsComplex).
        WriteString(writer, "Capsule");
        writer.Write(0);
        writer.Write(0);
        writer.Write(1);
        WriteVector(writer, 0, 0, 50);
        WriteQuaternion(writer, 0, 0, 0, 1);
        writer.Write(30f);
        writer.Write(50f);
        writer.Write(0);
        writer.Write(0);
        writer.Write((byte)2);
        writer.Write(0);
    }

    // Per placement: body, cell, blocked channels, translation (doubles), rotation, scale, surface, material slots.
    private static void Placements(BinaryWriter writer)
    {
        writer.Write(4);
        Placement(writer, 0, 0, WorldStatic | Visibility, 1000, 2000, 300, scale: 1);
        Placement(writer, 1, 1, Visibility, 30000, 0, 0, scale: 1);
        Placement(writer, 2, 0, PlayerPawn, 1000, 2000, 300, scale: 1);
        Placement(writer, 3, 0, Visibility, 0, 30000, 0, scale: 2);
    }

    private static void Placement(BinaryWriter writer, int body, int cell, uint channels, double x, double y, double z, float scale)
    {
        writer.Write(body);
        writer.Write(cell);
        writer.Write(channels);
        writer.Write(x);
        writer.Write(y);
        writer.Write(z);
        WriteQuaternion(writer, 0, 0, 0, 1);
        WriteVector(writer, scale, scale, scale);
        writer.Write((byte)0);
        writer.Write(1);
        writer.Write((byte)0);
    }

    // One 5 × 5 tile at the origin, 1 m spacing; grid cell (0, 0) has its low-Y triangle as a hole.
    private static void HeightFields(BinaryWriter writer)
    {
        writer.Write(1);
        writer.Write(0);
        writer.Write(0.0);
        writer.Write(0.0);
        writer.Write(0.0);
        writer.Write(100f);
        writer.Write(100f);
        writer.Write(1f);
        writer.Write(5);
        writer.Write(5);
        for (int sample = 0; sample < 25; sample++)
        {
            writer.Write((short)(sample * 10));
        }

        writer.Write(1);
        writer.Write((byte)3);
        for (int cell = 0; cell < 16; cell++)
        {
            writer.Write(cell == 0 ? (byte)2 : (byte)0);
        }
    }

    private static void WriteMesh(BinaryWriter writer, float[] vertices, int[] indices, bool withMaterials)
    {
        writer.Write(vertices.Length / 3);
        foreach (float value in vertices)
        {
            writer.Write(value);
        }

        writer.Write(indices.Length);
        foreach (int index in indices)
        {
            writer.Write(index);
        }

        writer.Write(withMaterials ? indices.Length / 3 : 0);
        for (int triangle = 0; withMaterials && triangle < indices.Length / 3; triangle++)
        {
            writer.Write((ushort)0);
        }

        WriteVector(writer, 0, 0, 0);
        WriteVector(writer, 100, 100, 100);
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static void WriteVector(BinaryWriter writer, float x, float y, float z)
    {
        writer.Write(x);
        writer.Write(y);
        writer.Write(z);
    }

    private static void WriteQuaternion(BinaryWriter writer, float x, float y, float z, float w)
    {
        WriteVector(writer, x, y, z);
        writer.Write(w);
    }
}
