using System.Globalization;
using Ares.Editor.Core;
using Ares.Editor.Core.GameData;
using Ares.Editor.Core.Geometry;

namespace Ares.Editor.App.Editing;

/// <summary>What changed in a <see cref="GeometrySession"/>.</summary>
[Flags]
public enum GeometryChanges
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>Other geometry is published (or none): the view loads it again.</summary>
    Geometry = 1,

    /// <summary>A data layer was switched.</summary>
    Visibility = 2,

    /// <summary>The build state or the view's loading progress changed.</summary>
    Status = 4,
}

/// <summary>
/// The selected map's collision geometry for the 3D view: built off the UI thread whenever the workspace or the map
/// changes, published to <see cref="GeometryFiles"/>, and the data layers the operator shows. Every change raises
/// <see cref="Changed"/>.
/// </summary>
public sealed class GeometrySession
{
    private readonly EditorSession _session;
    private readonly GeometryFiles _files;
    private CancellationTokenSource? _build;
    private EditorWorkspace? _workspace;
    private string? _mapName;
    private bool[] _layerVisible = [];
    private bool _isBuilding;
    private int _loadedChunks;
    private int _failedChunks;

    /// <summary>Creates the session and builds the geometry of the session's map, if any.</summary>
    /// <param name="session">The editor session whose map is shown.</param>
    /// <param name="files">Where the chunks are served from.</param>
    public GeometrySession(EditorSession session, GeometryFiles files)
    {
        _session = session;
        _files = files;
        session.Changed += OnSessionChanged;
        Refresh();
    }

    /// <summary>Raised after every change, with what changed.</summary>
    public event Action<GeometryChanges>? Changed;

    /// <summary>The published geometry, or null while building or when the map has none.</summary>
    public MapGeometry? Geometry { get; private set; }

    /// <summary>The page-relative folder the published chunks are served from.</summary>
    public string BaseUrl { get; private set; } = string.Empty;

    /// <summary>Why the map has no geometry (no or unreadable collision file), or null.</summary>
    public string? Problem { get; private set; }

    /// <summary>Whether a data layer of <see cref="Geometry"/> is shown.</summary>
    /// <param name="layer">Index in <see cref="MapGeometry.DataLayers"/>.</param>
    /// <returns>Whether it is shown; the base map always is.</returns>
    public bool IsLayerVisible(int layer) => layer >= 0 && layer < _layerVisible.Length && _layerVisible[layer];

    /// <summary>One line on the state of the geometry: building, loading progress, or what is shown.</summary>
    /// <returns>The line, or null when there is nothing to say (no map, or <see cref="Problem"/> says it).</returns>
    public string? StatusLine()
    {
        if (_isBuilding)
        {
            return "Building the map geometry…";
        }

        if (Geometry is not { } geometry)
        {
            return null;
        }

        int total = geometry.Chunks.Count;
        string failed = _failedChunks > 0 ? string.Create(CultureInfo.InvariantCulture, $", {_failedChunks} failed") : string.Empty;
        if (_loadedChunks + _failedChunks < total)
        {
            return string.Create(CultureInfo.InvariantCulture, $"Loading chunk {_loadedChunks} of {total}{failed}…");
        }

        GeometryStatistics statistics = geometry.Statistics;
        double triangles = (statistics.ObjectTriangles + statistics.LandscapeTriangles) / 1e6;
        return string.Create(CultureInfo.InvariantCulture,
            $"{triangles:0.0} M triangles: {statistics.DrawnPlacements:N0} placed bodies and {statistics.HeightFieldTiles} landscape tiles in {total} chunks{failed}.");
    }

    /// <summary>Indices of the shown data layers.</summary>
    /// <returns>The indices, ascending.</returns>
    public int[] VisibleLayers() => [.. Enumerable.Range(0, _layerVisible.Length).Where(IsLayerVisible)];

    /// <summary>Shows or hides a data layer.</summary>
    /// <param name="layer">Index in <see cref="MapGeometry.DataLayers"/>.</param>
    /// <param name="visible">Whether it is shown.</param>
    public void SetLayerVisible(int layer, bool visible)
    {
        if (layer < 0 || layer >= _layerVisible.Length || _layerVisible[layer] == visible)
        {
            return;
        }

        _layerVisible[layer] = visible;
        Raise(GeometryChanges.Visibility);
    }

    /// <summary>Records the view's loading progress for the published geometry.</summary>
    /// <param name="loaded">Chunks loaded.</param>
    /// <param name="failed">Chunks that failed to load.</param>
    public void ReportProgress(int loaded, int failed)
    {
        if (loaded == _loadedChunks && failed == _failedChunks)
        {
            return;
        }

        _loadedChunks = loaded;
        _failedChunks = failed;
        Raise(GeometryChanges.Status);
    }

    private void OnSessionChanged(EditorChanges changes)
    {
        if ((changes & (EditorChanges.Workspace | EditorChanges.Map)) != EditorChanges.None)
        {
            Refresh();
        }
    }

    // Rebuilds when the workspace or the map's name changed; the result of an older build is dropped.
    private void Refresh()
    {
        EditorWorkspace? workspace = _session.Workspace;
        string? mapName = _session.Map?.Name;
        if (ReferenceEquals(workspace, _workspace) && mapName == _mapName)
        {
            return;
        }

        _workspace = workspace;
        _mapName = mapName;
        _build?.Cancel();
        _build?.Dispose();
        _build = null;
        Publish(null, null);
        if (workspace is null || mapName is null)
        {
            return;
        }

        _build = new CancellationTokenSource();
        _ = BuildAsync(workspace.GameData, mapName, _build.Token);
    }

    private async Task BuildAsync(EditorGameData gameData, string mapName, CancellationToken cancellation)
    {
        _isBuilding = true;
        Raise(GeometryChanges.Status);
        MapGeometry? geometry = null;
        string? problem = null;
        try
        {
            geometry = await Task.Run(() => MapGeometry.Load(gameData, mapName, cancellation), cancellation);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (EditorDataException error)
        {
            problem = error.Message;
        }

        if (cancellation.IsCancellationRequested)
        {
            return;
        }

        Publish(geometry, problem);
    }

    private void Publish(MapGeometry? geometry, string? problem)
    {
        Geometry = geometry;
        Problem = problem;
        _isBuilding = false;
        _loadedChunks = 0;
        _failedChunks = 0;
        _layerVisible = new bool[geometry?.DataLayers.Count ?? 0];
        BaseUrl = _files.Publish(geometry);
        Raise(GeometryChanges.Geometry | GeometryChanges.Status);
    }

    private void Raise(GeometryChanges changes) => Changed?.Invoke(changes);
}
