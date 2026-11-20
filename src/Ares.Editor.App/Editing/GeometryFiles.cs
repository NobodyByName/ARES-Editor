using System.Globalization;
using System.Runtime.InteropServices;
using Ares.Editor.Core.Geometry;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Ares.Editor.App.Editing;

/// <summary>
/// The window's static files (wwwroot) plus the published map geometry's chunks as virtual files
/// <c>geometry/&lt;generation&gt;/&lt;chunk&gt;.bin</c>, which the page reads with <c>fetch()</c> as binary over the same
/// <c>app://</c> origin, without JSON or text interop. Each publication gets a new generation, so a page never gets a
/// chunk of an older map.
/// </summary>
public sealed class GeometryFiles : IFileProvider
{
    private const string Folder = "geometry/";
    private const string Extension = ".bin";

    private readonly IFileProvider _inner;
    private Publication? _current;
    private int _generation;

    /// <summary>Creates the files over the window's static files.</summary>
    /// <param name="inner">The static files (the app's wwwroot).</param>
    public GeometryFiles(IFileProvider inner)
    {
        _inner = inner;
    }

    /// <summary>Makes a map's chunks the served ones, or serves none.</summary>
    /// <param name="geometry">The geometry, or null.</param>
    /// <returns>The page-relative folder of the chunks (chunk <c>i</c> is <c>&lt;folder&gt;i.bin</c>).</returns>
    public string Publish(MapGeometry? geometry)
    {
        int generation = Interlocked.Increment(ref _generation);
        Volatile.Write(ref _current, geometry is null ? null : new Publication(generation, geometry));
        return string.Create(CultureInfo.InvariantCulture, $"{Folder}{generation}/");
    }

    /// <inheritdoc />
    public IFileInfo GetFileInfo(string subpath)
    {
        ArgumentNullException.ThrowIfNull(subpath);
        string path = subpath.TrimStart('/');
        if (!path.StartsWith(Folder, StringComparison.Ordinal))
        {
            return _inner.GetFileInfo(subpath);
        }

        return Chunk(path[Folder.Length..]) ?? (IFileInfo)new NotFoundFileInfo(path);
    }

    /// <inheritdoc />
    public IDirectoryContents GetDirectoryContents(string subpath) => _inner.GetDirectoryContents(subpath);

    /// <inheritdoc />
    public IChangeToken Watch(string filter) => _inner.Watch(filter);

    // "<generation>/<chunk>.bin" of the current publication.
    private ChunkFile? Chunk(string name)
    {
        Publication? current = Volatile.Read(ref _current);
        int slash = name.IndexOf('/', StringComparison.Ordinal);
        if (current is null || slash < 0 || !name.EndsWith(Extension, StringComparison.Ordinal) ||
            !int.TryParse(name.AsSpan(0, slash), NumberStyles.None, CultureInfo.InvariantCulture, out int generation) || generation != current.Generation ||
            !int.TryParse(name.AsSpan(slash + 1, name.Length - slash - 1 - Extension.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int index) ||
            index >= current.Geometry.Chunks.Count)
        {
            return null;
        }

        return new ChunkFile(name[(slash + 1)..], current.Geometry.Chunks[index].Data);
    }

    private sealed record Publication(int Generation, MapGeometry Geometry);

    private sealed class ChunkFile(string name, ReadOnlyMemory<byte> data) : IFileInfo
    {
        public bool Exists => true;

        public long Length => data.Length;

        public string? PhysicalPath => null;

        public string Name => name;

        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

        public bool IsDirectory => false;

        public Stream CreateReadStream() =>
            MemoryMarshal.TryGetArray(data, out ArraySegment<byte> bytes)
                ? new MemoryStream(bytes.Array!, bytes.Offset, bytes.Count, writable: false)
                : new MemoryStream(data.ToArray(), writable: false);
    }
}
