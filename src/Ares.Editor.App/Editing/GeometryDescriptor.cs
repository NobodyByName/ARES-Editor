using Ares.Editor.Core.Geometry;

namespace Ares.Editor.App.Editing;

/// <summary>
/// What the 3D view needs to load published map geometry (wwwroot/js/map-geometry.js): where the chunk files are, the
/// data layers with the shown ones, and per chunk its kind, layers, sizes and bounds. Serialised to JSON by the JS
/// interop (camelCase).
/// </summary>
/// <param name="BaseUrl">Page-relative folder of the chunk files; chunk <c>i</c> is <c>&lt;BaseUrl&gt;i.bin</c>.</param>
/// <param name="Layers">Names of the data layers.</param>
/// <param name="VisibleLayers">Indices of the shown data layers.</param>
/// <param name="Chunks">The chunks.</param>
public sealed record GeometryDescriptor(string BaseUrl, string[] Layers, int[] VisibleLayers, ChunkDescriptor[] Chunks)
{
    /// <summary>The descriptor of a session's published geometry.</summary>
    /// <param name="session">The session.</param>
    /// <returns>The descriptor, or null when nothing is published.</returns>
    public static GeometryDescriptor? Of(GeometrySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Geometry is not { } geometry)
        {
            return null;
        }

        return new GeometryDescriptor(
            session.BaseUrl,
            [.. geometry.DataLayers.Select(layer => layer.Name)],
            session.VisibleLayers(),
            [.. geometry.Chunks.Select(chunk => new ChunkDescriptor(
                (int)chunk.Kind,
                [.. chunk.Layers],
                chunk.VertexCount,
                chunk.IndexCount,
                chunk.ShortIndices,
                [chunk.Minimum.X, chunk.Minimum.Y, chunk.Minimum.Z],
                [chunk.Maximum.X, chunk.Maximum.Y, chunk.Maximum.Z]))]);
    }
}

/// <summary>One chunk of a <see cref="GeometryDescriptor"/> (see <see cref="GeometryChunk"/>).</summary>
/// <param name="Kind">The <see cref="GeometryKind"/> as a number.</param>
/// <param name="Layers">Indices of the chunk's data layers; empty for the base map.</param>
/// <param name="VertexCount">Number of vertices.</param>
/// <param name="IndexCount">Number of indices.</param>
/// <param name="ShortIndices">Whether the indices are UInt16 (else UInt32).</param>
/// <param name="Min">Smallest corner of the bounds, three.js space.</param>
/// <param name="Max">Largest corner of the bounds, three.js space.</param>
public sealed record ChunkDescriptor(int Kind, int[] Layers, int VertexCount, int IndexCount, bool ShortIndices, float[] Min, float[] Max);
