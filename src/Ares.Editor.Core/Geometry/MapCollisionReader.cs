using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Ares.Editor.Core.GameData;

namespace Ares.Editor.Core.Geometry;

/// <summary>
/// Reads the parts of a map-collision file (<c>maps/&lt;map&gt;/&lt;map&gt;.collision</c>) the 3D view draws. The file is
/// ARES-Server's own <c>ARESCOLL</c> format, version <see cref="FormatVersion"/>, written by ARES-GameDataExtract; this
/// reader mirrors ARES-Server's <c>Ares.Common.Maps.MapCollisionFile</c> (format version 3) field by field, so a format
/// change there means changing this reader too.
/// </summary>
/// <remarks>
/// All values are little-endian; strings are an int32 byte count and UTF-8. The header is <c>ARESCOLL</c>, the format
/// version, the game build id and the map name; four sections follow (<c>CELL</c>, <c>BODY</c>, <c>PLAC</c>,
/// <c>HFLD</c>), each a tag, an int64 payload length and the payload. Materials, surface types, hull planes and stored
/// bounds are read past; everything the view uses is checked (counts, indices, lengths), and a payload must be
/// consumed exactly.
/// </remarks>
internal static class MapCollisionReader
{
    // The format version this reader understands.
    private const int FormatVersion = 3;

    private const int MaximumNameLength = 4096;
    private const int CellFlagMask = 0b111;

    private static readonly UTF8Encoding s_utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static ReadOnlySpan<byte> Magic => "ARESCOLL"u8;

    /// <summary>Reads a collision file.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The map.</returns>
    /// <exception cref="EditorDataException">The file cannot be read, is not a version-3 collision file or is malformed.</exception>
    public static CollisionMap Read(string path)
    {
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1 << 16);
            return Read(stream, path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new EditorDataException($"Cannot read the collision file {path}: {error.Message}", error);
        }
    }

    /// <summary>Reads a collision file from a stream.</summary>
    /// <param name="stream">The stream, positioned at the file's start.</param>
    /// <param name="name">The file's name for messages.</param>
    /// <returns>The map.</returns>
    /// <exception cref="EditorDataException">The stream does not hold a version-3 collision file or it is malformed.</exception>
    public static CollisionMap Read(Stream stream, string name)
    {
        if (!BitConverter.IsLittleEndian)
        {
            throw new PlatformNotSupportedException("Collision files need a little-endian machine.");
        }

        Span<byte> header = stackalloc byte[Magic.Length + (3 * sizeof(int))];
        ReadExactly(stream, header, name, "header");
        if (!header[..Magic.Length].SequenceEqual(Magic))
        {
            throw new EditorDataException($"{name} is not an ARES collision file.");
        }

        int version = BinaryPrimitives.ReadInt32LittleEndian(header[Magic.Length..]);
        if (version != FormatVersion)
        {
            throw new EditorDataException(
                $"{name} is collision format version {version}; this editor reads version {FormatVersion}. Use the ARES-GameDataExtract and ARES-Editor of the same ARES release.");
        }

        int gameBuildId = BinaryPrimitives.ReadInt32LittleEndian(header[(Magic.Length + sizeof(int))..]);
        int nameLength = BinaryPrimitives.ReadInt32LittleEndian(header[(Magic.Length + (2 * sizeof(int)))..]);
        if (nameLength is <= 0 or > MaximumNameLength)
        {
            throw Malformed(name, "header", $"invalid map name length {nameLength}");
        }

        byte[] mapName = new byte[nameLength];
        ReadExactly(stream, mapName, name, "header");
        string map = Decode(mapName, name, "header");

        ImmutableArray<CollisionCell> cells = ReadCells(new PayloadReader(ReadSection(stream, "CELL"u8, name, "cell"), name, "cell"));
        ImmutableArray<CollisionBody> bodies = ReadBodies(new PayloadReader(ReadSection(stream, "BODY"u8, name, "body"), name, "body"));
        ImmutableArray<BodyPlacement> placements = ReadPlacements(new PayloadReader(ReadSection(stream, "PLAC"u8, name, "placement"), name, "placement"), bodies.Length, cells.Length);
        ImmutableArray<HeightFieldTile> tiles = ReadHeightFields(new PayloadReader(ReadSection(stream, "HFLD"u8, name, "height-field"), name, "height-field"), cells.Length);
        return new CollisionMap(gameBuildId, map, cells, bodies, placements, tiles);
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer, string name, string part)
    {
        try
        {
            stream.ReadExactly(buffer);
        }
        catch (EndOfStreamException error)
        {
            throw new EditorDataException($"{name} ends early in its {part}.", error);
        }
    }

    private static byte[] ReadSection(Stream stream, ReadOnlySpan<byte> tag, string name, string section)
    {
        Span<byte> header = stackalloc byte[4 + sizeof(long)];
        ReadExactly(stream, header, name, section + " section header");
        if (!header[..4].SequenceEqual(tag))
        {
            throw Malformed(name, section + " section", "unexpected section tag");
        }

        long length = BinaryPrimitives.ReadInt64LittleEndian(header[4..]);
        if (length < 0 || length > Array.MaxLength || (stream.CanSeek && length > stream.Length - stream.Position))
        {
            throw Malformed(name, section + " section", $"invalid length {length}");
        }

        byte[] payload = GC.AllocateUninitializedArray<byte>((int)length);
        ReadExactly(stream, payload, name, section + " section");
        return payload;
    }

    private static ImmutableArray<CollisionCell> ReadCells(PayloadReader reader)
    {
        CollisionCell[] cells = new CollisionCell[reader.ReadCount(8)];
        for (int cell = 0; cell < cells.Length; cell++)
        {
            string packageName = reader.ReadString();
            string[] layers = new string[reader.ReadCount(4)];
            for (int layer = 0; layer < layers.Length; layer++)
            {
                layers[layer] = reader.ReadString();
            }

            cells[cell] = new CollisionCell(packageName, ImmutableCollectionsMarshal.AsImmutableArray(layers));
        }

        reader.ExpectEnd();
        return ImmutableCollectionsMarshal.AsImmutableArray(cells);
    }

    private static ImmutableArray<CollisionBody> ReadBodies(PayloadReader reader)
    {
        CollisionBody[] bodies = new CollisionBody[reader.ReadCount(24)];
        for (int body = 0; body < bodies.Length; body++)
        {
            string name = reader.ReadString();
            CollisionBox[] boxes = new CollisionBox[reader.ReadCount(40)];
            for (int box = 0; box < boxes.Length; box++)
            {
                boxes[box] = new CollisionBox(reader.ReadVector(), reader.ReadQuaternion(), reader.ReadVector());
            }

            CollisionSphere[] spheres = new CollisionSphere[reader.ReadCount(16)];
            for (int sphere = 0; sphere < spheres.Length; sphere++)
            {
                spheres[sphere] = new CollisionSphere(reader.ReadVector(), reader.ReadSingle());
            }

            CollisionCapsule[] capsules = new CollisionCapsule[reader.ReadCount(36)];
            for (int capsule = 0; capsule < capsules.Length; capsule++)
            {
                capsules[capsule] = new CollisionCapsule(reader.ReadVector(), reader.ReadQuaternion(), reader.ReadSingle(), reader.ReadSingle());
            }

            ConvexHull[] hulls = new ConvexHull[reader.ReadCount(36)];
            for (int hull = 0; hull < hulls.Length; hull++)
            {
                hulls[hull] = ReadHull(ref reader, body);
            }

            ImmutableArray<TriangleMesh> meshes = ReadMeshes(ref reader, body);
            byte traceFlag = reader.ReadByte();
            if (traceFlag is < (byte)CollisionTraceFlag.SimpleAndComplex or > (byte)CollisionTraceFlag.ComplexAsSimple)
            {
                throw reader.Malformed($"body {body} has an invalid trace flag {traceFlag}");
            }

            ImmutableArray<TriangleMesh> complexMeshes = ReadMeshes(ref reader, body);
            bodies[body] = new CollisionBody(
                name,
                ImmutableCollectionsMarshal.AsImmutableArray(boxes),
                ImmutableCollectionsMarshal.AsImmutableArray(spheres),
                ImmutableCollectionsMarshal.AsImmutableArray(capsules),
                ImmutableCollectionsMarshal.AsImmutableArray(hulls),
                meshes,
                (CollisionTraceFlag)traceFlag,
                complexMeshes);
        }

        reader.ExpectEnd();
        return ImmutableCollectionsMarshal.AsImmutableArray(bodies);
    }

    // Vertices, polygons (plane normal and distance, read past; first index; index count), byte indices, bounds (read past).
    private static ConvexHull ReadHull(ref PayloadReader reader, int body)
    {
        Vector3[] vertices = reader.ReadArray<Vector3>();
        HullPolygon[] polygons = new HullPolygon[reader.ReadCount(24)];
        for (int polygon = 0; polygon < polygons.Length; polygon++)
        {
            reader.Skip(4 * sizeof(float));
            polygons[polygon] = new HullPolygon(reader.ReadInt32(), reader.ReadInt32());
        }

        byte[] indices = reader.ReadArray<byte>();
        reader.Skip(6 * sizeof(float));
        if (polygons.Any(polygon => polygon.IndexCount < 3 || polygon.FirstIndex < 0 || polygon.FirstIndex > indices.Length - polygon.IndexCount))
        {
            throw reader.Malformed($"body {body} has a convex hull with an invalid polygon");
        }

        if (indices.Any(index => index >= vertices.Length))
        {
            throw reader.Malformed($"body {body} has a convex hull with a vertex index out of range");
        }

        return new ConvexHull(
            ImmutableCollectionsMarshal.AsImmutableArray(vertices),
            ImmutableCollectionsMarshal.AsImmutableArray(polygons),
            ImmutableCollectionsMarshal.AsImmutableArray(indices));
    }

    // Per mesh: vertices, int32 indices, uint16 material indices (read past), bounds (read past).
    private static ImmutableArray<TriangleMesh> ReadMeshes(ref PayloadReader reader, int body)
    {
        TriangleMesh[] meshes = new TriangleMesh[reader.ReadCount(32)];
        for (int mesh = 0; mesh < meshes.Length; mesh++)
        {
            Vector3[] vertices = reader.ReadArray<Vector3>();
            int[] indices = reader.ReadArray<int>();
            reader.Skip(reader.ReadCount(sizeof(ushort)) * sizeof(ushort));
            reader.Skip(6 * sizeof(float));
            if (indices.Length % 3 != 0 || Array.Exists(indices, index => (uint)index >= (uint)vertices.Length))
            {
                throw reader.Malformed($"body {body} has a triangle mesh with invalid indices");
            }

            meshes[mesh] = new TriangleMesh(ImmutableCollectionsMarshal.AsImmutableArray(vertices), ImmutableCollectionsMarshal.AsImmutableArray(indices));
        }

        return ImmutableCollectionsMarshal.AsImmutableArray(meshes);
    }

    // Per placement: body, cell, blocked channels, translation (doubles), rotation, scale, surface type and material slot
    // surface types (read past).
    private static ImmutableArray<BodyPlacement> ReadPlacements(PayloadReader reader, int bodyCount, int cellCount)
    {
        BodyPlacement[] placements = new BodyPlacement[reader.ReadCount(69)];
        for (int placement = 0; placement < placements.Length; placement++)
        {
            int body = reader.ReadInt32();
            int cell = reader.ReadInt32();
            uint channels = reader.ReadUInt32();
            WorldVector translation = reader.ReadWorldVector();
            Quaternion rotation = reader.ReadQuaternion();
            Vector3 scale = reader.ReadVector();
            reader.Skip(1);
            reader.Skip(reader.ReadCount(1));
            if ((uint)body >= (uint)bodyCount || (uint)cell >= (uint)cellCount)
            {
                throw reader.Malformed($"placement {placement} refers to body {body} of {bodyCount} or cell {cell} of {cellCount}");
            }

            placements[placement] = new BodyPlacement(body, cell, channels, translation, rotation, scale);
        }

        reader.ExpectEnd();
        return ImmutableCollectionsMarshal.AsImmutableArray(placements);
    }

    // Per tile: cell, origin (doubles), spacings and height scale, sample counts, int16 heights, the surface type palette
    // (read past) and one byte per grid cell whose low three bits are the cell flags.
    private static ImmutableArray<HeightFieldTile> ReadHeightFields(PayloadReader reader, int cellCount)
    {
        HeightFieldTile[] tiles = new HeightFieldTile[reader.ReadCount(56)];
        for (int tile = 0; tile < tiles.Length; tile++)
        {
            int cell = reader.ReadInt32();
            WorldVector origin = reader.ReadWorldVector();
            float spacingX = reader.ReadSingle();
            float spacingY = reader.ReadSingle();
            float heightScale = reader.ReadSingle();
            int countX = reader.ReadInt32();
            int countY = reader.ReadInt32();
            if ((uint)cell >= (uint)cellCount || countX < 2 || countY < 2 || (long)countX * countY > reader.Remaining / sizeof(short))
            {
                throw reader.Malformed($"tile {tile} has an invalid cell {cell} or size {countX} × {countY}");
            }

            short[] heights = reader.ReadArray<short>(countX * countY);
            reader.Skip(reader.ReadCount(1));
            byte[] flags = reader.ReadArray<byte>((countX - 1) * (countY - 1));
            for (int index = 0; index < flags.Length; index++)
            {
                flags[index] = (byte)(flags[index] & CellFlagMask);
            }

            tiles[tile] = new HeightFieldTile(
                cell,
                origin,
                spacingX,
                spacingY,
                heightScale,
                countX,
                countY,
                ImmutableCollectionsMarshal.AsImmutableArray(heights),
                [.. MemoryMarshal.Cast<byte, HeightFieldCellFlags>(flags)]);
        }

        reader.ExpectEnd();
        return ImmutableCollectionsMarshal.AsImmutableArray(tiles);
    }

    private static string Decode(ReadOnlySpan<byte> bytes, string name, string part)
    {
        try
        {
            return s_utf8.GetString(bytes);
        }
        catch (DecoderFallbackException error)
        {
            throw new EditorDataException($"{name} is malformed in its {part}: invalid UTF-8 text.", error);
        }
    }

    private static EditorDataException Malformed(string name, string part, string problem) => new($"{name} is malformed in its {part}: {problem}.");

    /// <summary>Reads one section payload; running past its end is a malformed file.</summary>
    private ref struct PayloadReader(ReadOnlySpan<byte> data, string name, string section)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _at;

        public readonly int Remaining => _data.Length - _at;

        public int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(Take(sizeof(int)));

        public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(sizeof(uint)));

        public byte ReadByte() => Take(1)[0];

        public float ReadSingle() => BinaryPrimitives.ReadSingleLittleEndian(Take(sizeof(float)));

        public Vector3 ReadVector() => new(ReadSingle(), ReadSingle(), ReadSingle());

        public Quaternion ReadQuaternion() => new(ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());

        public WorldVector ReadWorldVector() =>
            new(
                BinaryPrimitives.ReadDoubleLittleEndian(Take(sizeof(double))),
                BinaryPrimitives.ReadDoubleLittleEndian(Take(sizeof(double))),
                BinaryPrimitives.ReadDoubleLittleEndian(Take(sizeof(double))));

        public string ReadString()
        {
            int length = ReadInt32();
            if (length <= 0 || length > Remaining)
            {
                throw Malformed($"invalid string length {length}");
            }

            return Decode(Take(length), name, section + " section");
        }

        /// <summary>Reads a count of elements that each take at least <paramref name="minimumSize"/> bytes.</summary>
        public int ReadCount(int minimumSize)
        {
            int count = ReadInt32();
            if (count < 0 || (long)count * minimumSize > Remaining)
            {
                throw Malformed($"invalid count {count}");
            }

            return count;
        }

        /// <summary>Reads a count followed by that many elements.</summary>
        public T[] ReadArray<T>()
            where T : unmanaged =>
            ReadArray<T>(ReadCount(Unsafe.SizeOf<T>()));

        public T[] ReadArray<T>(int count)
            where T : unmanaged =>
            [.. MemoryMarshal.Cast<byte, T>(Take(count * Unsafe.SizeOf<T>()))];

        public void Skip(int count) => Take(count);

        public readonly void ExpectEnd()
        {
            if (_at != _data.Length)
            {
                throw Malformed($"{_data.Length - _at} bytes left over");
            }
        }

        public readonly EditorDataException Malformed(string problem) => MapCollisionReader.Malformed(name, section + " section", problem);

        private ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || count > _data.Length - _at)
            {
                throw Malformed("data ends early");
            }

            ReadOnlySpan<byte> bytes = _data.Slice(_at, count);
            _at += count;
            return bytes;
        }
    }
}
