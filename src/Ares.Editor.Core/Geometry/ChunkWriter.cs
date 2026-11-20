using System.Buffers.Binary;
using System.Numerics;
using Ares.Editor.Core.GameData;

namespace Ares.Editor.Core.Geometry;

/// <summary>
/// Collects world-space triangles of one spatial group into <see cref="GeometryChunk"/>s that the view uploads as they
/// are: Float32 positions in three.js space and 16-bit indices, starting a new chunk before one would pass 65,536
/// vertices (a single part larger than that gets a chunk of its own with 32-bit indices).
/// </summary>
internal sealed class ChunkWriter
{
    /// <summary>Most vertices a chunk with 16-bit indices holds.</summary>
    public const int ShortIndexVertexLimit = ushort.MaxValue + 1;

    // Unreal centimetres, Z up, left-handed -> three.js metres, Y up, right-handed: three = (ue.x, ue.z, ue.y) * 0.01.
    private const double UnrealToMetres = 0.01;

    private readonly GeometryKind _kind;
    private readonly IReadOnlyList<int> _layers;
    private readonly List<GeometryChunk> _chunks = [];
    private float[] _positions = new float[3 * 1024];
    private int[] _indices = new int[3 * 1024];
    private int _vertexCount;
    private int _indexCount;
    private Vector3 _minimum = new(float.PositiveInfinity);
    private Vector3 _maximum = new(float.NegativeInfinity);

    /// <summary>Creates a writer for one group.</summary>
    /// <param name="kind">What the group's triangles are.</param>
    /// <param name="layers">The data layers of the group's cells (indices into <see cref="MapGeometry.DataLayers"/>).</param>
    public ChunkWriter(GeometryKind kind, IReadOnlyList<int> layers)
    {
        _kind = kind;
        _layers = layers;
    }

    /// <summary>Makes room for a part of up to <paramref name="vertexCount"/> vertices, finishing the current chunk if needed.</summary>
    /// <param name="vertexCount">Most vertices the part adds.</param>
    /// <returns>The index the part's first vertex will get.</returns>
    public int BeginPart(int vertexCount)
    {
        if (_vertexCount > 0 && _vertexCount + vertexCount > ShortIndexVertexLimit)
        {
            Flush();
        }

        return _vertexCount;
    }

    /// <summary>Adds a vertex given in Unreal world units.</summary>
    /// <param name="x">Unreal X (cm).</param>
    /// <param name="y">Unreal Y (cm).</param>
    /// <param name="z">Unreal Z (cm, up).</param>
    /// <returns>The vertex's index in the chunk.</returns>
    public int AddVertex(double x, double y, double z)
    {
        if ((_vertexCount + 1) * 3 > _positions.Length)
        {
            Array.Resize(ref _positions, _positions.Length * 2);
        }

        Vector3 three = new((float)(x * UnrealToMetres), (float)(z * UnrealToMetres), (float)(y * UnrealToMetres));
        _positions[_vertexCount * 3] = three.X;
        _positions[(_vertexCount * 3) + 1] = three.Y;
        _positions[(_vertexCount * 3) + 2] = three.Z;
        _minimum = Vector3.Min(_minimum, three);
        _maximum = Vector3.Max(_maximum, three);
        return _vertexCount++;
    }

    /// <summary>Adds a triangle of vertices of the chunk being written.</summary>
    /// <param name="a">First vertex index.</param>
    /// <param name="b">Second vertex index.</param>
    /// <param name="c">Third vertex index.</param>
    public void AddTriangle(int a, int b, int c)
    {
        if (_indexCount + 3 > _indices.Length)
        {
            Array.Resize(ref _indices, _indices.Length * 2);
        }

        _indices[_indexCount++] = a;
        _indices[_indexCount++] = b;
        _indices[_indexCount++] = c;
    }

    /// <summary>Adds a body mesh as placed: a body-space point <c>p</c> lies at <c>Translation + Rotation · (Scale ∘ p)</c>.</summary>
    /// <param name="mesh">The body's mesh.</param>
    /// <param name="placement">The placement.</param>
    public void AddPlaced(BodyMesh mesh, in BodyPlacement placement)
    {
        int first = BeginPart(mesh.Vertices.Count);
        WorldVector origin = placement.Translation;
        foreach (Vector3 vertex in mesh.Vertices)
        {
            Vector3 offset = Vector3.Transform(vertex * placement.Scale, placement.Rotation);
            AddVertex(origin.X + offset.X, origin.Y + offset.Y, origin.Z + offset.Z);
        }

        IReadOnlyList<int> indices = mesh.Indices;
        for (int index = 0; index < indices.Count; index += 3)
        {
            AddTriangle(first + indices[index], first + indices[index + 1], first + indices[index + 2]);
        }
    }

    /// <summary>Finishes the chunk being written and returns every chunk of the group.</summary>
    /// <returns>The chunks, in the order they were written.</returns>
    public IReadOnlyList<GeometryChunk> Finish()
    {
        Flush();
        return _chunks;
    }

    // Layout: VertexCount × 3 Float32 positions, then IndexCount indices (UInt16, padded to 4 bytes, or UInt32).
    private void Flush()
    {
        if (_indexCount == 0)
        {
            _vertexCount = 0;
            ResetBounds();
            return;
        }

        bool shortIndices = _vertexCount <= ShortIndexVertexLimit;
        int positionBytes = _vertexCount * 3 * sizeof(float);
        int indexBytes = shortIndices ? ((_indexCount * sizeof(ushort)) + 3) & ~3 : _indexCount * sizeof(uint);
        byte[] data = new byte[positionBytes + indexBytes];
        Span<byte> target = data;
        for (int value = 0; value < _vertexCount * 3; value++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(target[(value * sizeof(float))..], _positions[value]);
        }

        Span<byte> indexTarget = target[positionBytes..];
        for (int index = 0; index < _indexCount; index++)
        {
            if (shortIndices)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(indexTarget[(index * sizeof(ushort))..], (ushort)_indices[index]);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(indexTarget[(index * sizeof(uint))..], (uint)_indices[index]);
            }
        }

        _chunks.Add(new GeometryChunk(_kind, _layers, _vertexCount, _indexCount, _minimum, _maximum, data));
        _vertexCount = 0;
        _indexCount = 0;
        ResetBounds();
    }

    private void ResetBounds()
    {
        _minimum = new Vector3(float.PositiveInfinity);
        _maximum = new Vector3(float.NegativeInfinity);
    }
}
