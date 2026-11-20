using Ares.Editor.Core;
using Ares.Editor.Core.Content;
using Ares.Editor.Core.GameData;
using Ares.Editor.Core.Matching;
using Ares.Editor.Core.Paths;

namespace Ares.Editor.App.Editing;

/// <summary>
/// The editor's state for its one window: the open server, the selected map with what the current (unsaved) loot files
/// decide for each of its containers, and what the operator selected. Panels change the documents of
/// <see cref="Workspace"/> directly and then call <see cref="Edited"/>; every change raises <see cref="Changed"/>.
/// </summary>
public sealed class EditorSession
{
    private readonly EditorPaths _paths;
    private ContainerMatch[] _matches = [];
    private readonly Lock _rollsLock = new();
    private (EditorWorkspace Workspace, ContainerContentRolls Containers, LooseItemRolls Loose)? _rolls;

    /// <summary>Creates a session with nothing open.</summary>
    /// <param name="paths">Where the editor's settings live.</param>
    public EditorSession(EditorPaths paths)
    {
        _paths = paths;
    }

    /// <summary>Raised after every change, with what changed.</summary>
    public event Action<EditorChanges>? Changed;

    /// <summary>The open server, or null before one is opened.</summary>
    public EditorWorkspace? Workspace { get; private set; }

    /// <summary>The selected map of <see cref="Workspace"/>, or null.</summary>
    public MapLoot? Map { get; private set; }

    /// <summary>What the current documents decide for each container of <see cref="Map"/>, by container position.</summary>
    public IReadOnlyList<ContainerMatch> Matches => _matches;

    /// <summary>The groups of sockets of <see cref="Map"/> that the current documents' group fills decide.</summary>
    public LootFillGroups FillGroups { get; private set; } = LootFillGroups.Empty;

    /// <summary>How many containers of <see cref="Map"/> are at or below each tag (see <see cref="MapLoot.CountByTag"/>).</summary>
    public IReadOnlyDictionary<string, int> TagCounts { get; private set; } = new Dictionary<string, int>();

    /// <summary>Counts the recomputations of the matches, so views can tell when what they derived from the content is stale.</summary>
    public int Revision { get; private set; }

    /// <summary>
    /// The server's contents rolls of the current (possibly unsaved) contents file, built on first use after each change;
    /// null without a workspace. Safe to read off the UI thread.
    /// </summary>
    public (ContainerContentRolls Containers, LooseItemRolls Loose)? Rolls
    {
        get
        {
            lock (_rollsLock)
            {
                if (Workspace is not { } workspace)
                {
                    return null;
                }

                if (_rolls is { } cached && ReferenceEquals(cached.Workspace, workspace))
                {
                    return (cached.Containers, cached.Loose);
                }

                (EditorWorkspace Workspace, ContainerContentRolls Containers, LooseItemRolls Loose) built =
                    (workspace, new ContainerContentRolls(workspace.GameData.Roll, workspace.Contents), new LooseItemRolls(workspace.GameData.Roll, workspace.Contents));
                _rolls = built;
                return (built.Containers, built.Loose);
            }
        }
    }

    /// <summary>Position in <see cref="MapLoot.Containers"/> of the selected container, or -1.</summary>
    public int SelectedIndex { get; private set; } = -1;

    /// <summary>The selected container, or null.</summary>
    public LootContainer? SelectedContainer => Map is not null && SelectedIndex >= 0 ? Map.Containers[SelectedIndex] : null;

    /// <summary>Position in <see cref="MapLoot.LooseItemSockets"/> of the selected loose item socket, or -1; never set with <see cref="SelectedIndex"/>.</summary>
    public int SelectedLooseIndex { get; private set; } = -1;

    /// <summary>The selected loose item socket, or null.</summary>
    public LooseItemSocket? SelectedLooseSocket => Map is not null && SelectedLooseIndex >= 0 ? Map.LooseItemSockets[SelectedLooseIndex] : null;

    /// <summary>The id of the selected container or loose item socket, or null.</summary>
    public string? SelectedId => SelectedContainer?.Id ?? SelectedLooseSocket?.Id;

    /// <summary>The rule open in the inspector, or null.</summary>
    public LootRule? SelectedRule { get; private set; }

    /// <summary>The selected node of the tag tree, or null.</summary>
    public string? SelectedTag { get; private set; }

    /// <summary>The container class whose contents profile is open, or null (the selected tag's profile shows instead).</summary>
    public string? SelectedClass { get; private set; }

    /// <summary>Whether the view highlights the selected rule's containers (else the selected tag's), whichever was picked last.</summary>
    public bool HighlightsRule { get; private set; }

    /// <summary>How the view colours containers.</summary>
    public ColorMode ColorMode { get; private set; } = ColorMode.SpawnChance;

    /// <summary>Whether the documents have edits that are not saved.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>The last error to show the operator (a folder or file that cannot be read or written), or null.</summary>
    public string? Error { get; private set; }

    /// <summary>A short note about the last save, or null.</summary>
    public string? Notice { get; private set; }

    /// <summary>Whether a server is being opened or reloaded.</summary>
    public bool IsOpening { get; private set; }

    /// <summary>
    /// The containers the view highlights: those the selected rule covers, or those at or below the selected tag; empty
    /// when neither is selected.
    /// </summary>
    /// <returns>Positions in <see cref="MapLoot.Containers"/>.</returns>
    public int[] HighlightedIndices()
    {
        if (Map is null)
        {
            return [];
        }

        if (HighlightsRule && SelectedRule is not null)
        {
            return LootMatching.IndicesCoveredBy(Map, SelectedRule);
        }

        return SelectedTag is null ? [] : LootMatching.IndicesWithTag(Map, SelectedTag);
    }

    /// <summary>The loose item sockets the view highlights, chosen as in <see cref="HighlightedIndices"/>.</summary>
    /// <returns>Positions in <see cref="MapLoot.LooseItemSockets"/>.</returns>
    public int[] HighlightedLooseIndices()
    {
        if (Map is null)
        {
            return [];
        }

        if (HighlightsRule && SelectedRule is not null)
        {
            return LootMatching.LooseIndicesCoveredBy(Map, SelectedRule);
        }

        return SelectedTag is null ? [] : LootMatching.LooseIndicesWithTag(Map, SelectedTag);
    }

    /// <summary>Reopens the server the settings remember, if any; errors are shown, not thrown.</summary>
    /// <returns>A task that completes once the server is open or the error is shown.</returns>
    public async Task OpenRememberedAsync()
    {
        ServerLocation? location;
        try
        {
            location = EditorSettings.Load(_paths).Location();
        }
        catch (EditorDataException error)
        {
            ShowError(error.Message);
            return;
        }

        if (location is not null)
        {
            await OpenAsync(location, null);
        }
    }

    /// <summary>Opens the ARES-Server folder (the one ARES-Host runs in) and remembers it; errors are shown, not thrown.</summary>
    /// <param name="serverDirectory">The folder.</param>
    /// <returns>A task that completes once the server is open or the error is shown.</returns>
    public async Task OpenServerAsync(string serverDirectory)
    {
        ServerLocation location;
        try
        {
            location = ServerLocation.Resolve(serverDirectory);
        }
        catch (EditorDataException error)
        {
            ShowError(error.Message);
            return;
        }

        await OpenAsync(location, new EditorSettings { ServerDirectory = Path.GetFullPath(serverDirectory) });
    }

    /// <summary>Opens a content folder and a game data folder given directly, and remembers them; errors are shown, not thrown.</summary>
    /// <param name="contentDirectory">The content folder.</param>
    /// <param name="gameDataDirectory">The game data folder.</param>
    /// <returns>A task that completes once the server is open or the error is shown.</returns>
    public Task OpenDirectoriesAsync(string contentDirectory, string gameDataDirectory)
    {
        ServerLocation location = ServerLocation.FromDirectories(contentDirectory, gameDataDirectory);
        return OpenAsync(location, new EditorSettings { ContentDirectory = location.ContentDirectory, GameDataDirectory = location.GameDataDirectory });
    }

    /// <summary>The settings file's contents, or empty settings when there is none or it cannot be read.</summary>
    /// <returns>The settings.</returns>
    public EditorSettings RememberedSettings()
    {
        try
        {
            return EditorSettings.Load(_paths);
        }
        catch (EditorDataException)
        {
            return new EditorSettings();
        }
    }

    /// <summary>Remembers the side panel widths, keeping the remembered folders; errors are shown, not thrown.</summary>
    /// <param name="leftWidth">The left panel's width in pixels, or null for its default width.</param>
    /// <param name="rightWidth">The right panel's width in pixels, or null for its default width.</param>
    public void RememberPanelWidths(int? leftWidth, int? rightWidth)
    {
        try
        {
            (RememberedSettings() with { LeftPanelWidth = leftWidth, RightPanelWidth = rightWidth }).Save(_paths);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ShowError($"Cannot write the editor settings {_paths.SettingsFile}: {error.Message}");
        }
    }

    /// <summary>Discards every edit and reads the server's files again.</summary>
    /// <returns>A task that completes once the files are read or the error is shown.</returns>
    public Task ReloadAsync() => Workspace is null ? Task.CompletedTask : OpenAsync(Workspace.Location, null);

    /// <summary>Saves the three content files.</summary>
    public void Save()
    {
        if (Workspace is null)
        {
            return;
        }

        try
        {
            Workspace.SaveAll();
            IsDirty = false;
            Error = null;
            Notice = $"Saved at {DateTime.Now:HH:mm:ss}";
            Raise(EditorChanges.Status);
        }
        catch (EditorDataException error)
        {
            ShowError(error.Message);
        }
    }

    /// <summary>Selects a map of the workspace by name.</summary>
    /// <param name="name">The map's world name.</param>
    public void SelectMap(string name)
    {
        if (Workspace?.GameData.Maps.FirstOrDefault(candidate => candidate.Name == name) is not { } map || map == Map)
        {
            return;
        }

        Map = map;
        SelectedIndex = -1;
        SelectedLooseIndex = -1;
        TagCounts = map.CountByTag();
        Recompute();
        Raise(EditorChanges.Map);
    }

    /// <summary>Selects a container, or none; either way no loose item socket stays selected.</summary>
    /// <param name="index">Its position in <see cref="MapLoot.Containers"/>, or -1.</param>
    public void SelectContainer(int index)
    {
        int selected = Map is not null && index >= 0 && index < Map.Containers.Count ? index : -1;
        if (selected == SelectedIndex && SelectedLooseIndex < 0)
        {
            return;
        }

        SelectedIndex = selected;
        SelectedLooseIndex = -1;
        Raise(EditorChanges.Selection);
    }

    /// <summary>Selects a loose item socket, or none; either way no container stays selected.</summary>
    /// <param name="index">Its position in <see cref="MapLoot.LooseItemSockets"/>, or -1.</param>
    public void SelectLooseSocket(int index)
    {
        int selected = Map is not null && index >= 0 && index < Map.LooseItemSockets.Count ? index : -1;
        if (selected == SelectedLooseIndex && SelectedIndex < 0)
        {
            return;
        }

        SelectedLooseIndex = selected;
        SelectedIndex = -1;
        Raise(EditorChanges.Selection);
    }

    /// <summary>
    /// Selects the container or loose item socket with an id on the current map and flies the view to it; nothing when
    /// the map has none.
    /// </summary>
    /// <param name="id">The container or loose item socket id.</param>
    public void FocusContainer(string id)
    {
        if (Map is null)
        {
            return;
        }

        if (Map.Find(id) is { } container)
        {
            SelectContainer(IndexOf(Map, container));
        }
        else
        {
            int loose = Map.IndexOfLooseItemSocket(id);
            if (loose < 0)
            {
                return;
            }

            SelectLooseSocket(loose);
        }

        RequestFocus();
    }

    /// <summary>Asks the view to fly to the selected container or loose item socket.</summary>
    public void RequestFocus()
    {
        if (SelectedIndex >= 0 || SelectedLooseIndex >= 0)
        {
            Raise(EditorChanges.Focus);
        }
    }

    /// <summary>Opens a rule in the inspector and highlights its containers, or closes the rule.</summary>
    /// <param name="rule">The rule, or null.</param>
    public void SelectRule(LootRule? rule)
    {
        SelectedRule = rule;
        HighlightsRule = rule is not null;
        Raise(EditorChanges.Selection);
    }

    /// <summary>Selects a tag of the tag tree and highlights its containers, or clears it.</summary>
    /// <param name="tag">The tag, or null.</param>
    public void SelectTag(string? tag)
    {
        SelectedTag = tag;
        SelectedClass = null;
        HighlightsRule = tag is null && SelectedRule is not null;
        Raise(EditorChanges.Selection);
    }

    /// <summary>Opens the contents profile of a container class, or closes it.</summary>
    /// <param name="containerClass">The class path, or null.</param>
    public void SelectClass(string? containerClass)
    {
        SelectedClass = containerClass;
        Raise(EditorChanges.Selection);
    }

    /// <summary>Stops highlighting a tag or a rule's coverage; the selected rule stays open in the inspector.</summary>
    public void ClearHighlight()
    {
        SelectedTag = null;
        HighlightsRule = false;
        Raise(EditorChanges.Selection);
    }

    /// <summary>Changes how the view colours containers.</summary>
    /// <param name="mode">The mode.</param>
    public void SetColorMode(ColorMode mode)
    {
        if (mode == ColorMode)
        {
            return;
        }

        ColorMode = mode;
        Raise(EditorChanges.Colors);
    }

    /// <summary>Call after changing a document: recomputes every match and marks the session dirty.</summary>
    public void Edited()
    {
        if (SelectedRule is not null && Workspace?.Rules.Rules.IndexOf(SelectedRule) < 0)
        {
            SelectedRule = null;
            HighlightsRule = false;
        }

        IsDirty = true;
        Notice = null;
        Recompute();
        Raise(EditorChanges.Content);
    }

    /// <summary>Hides the error and the save note.</summary>
    public void DismissMessages()
    {
        Error = null;
        Notice = null;
        Raise(EditorChanges.Status);
    }

    private void ShowError(string message)
    {
        Error = message;
        Notice = null;
        Raise(EditorChanges.Status);
    }

    // Opens a location (reading the files off the UI thread), keeping the current workspace when it fails; on success
    // remembers the settings given (keeping the remembered panel widths) and keeps the map, container, loose item socket and tag selections
    // when they still exist.
    private async Task OpenAsync(ServerLocation location, EditorSettings? remember)
    {
        if (IsOpening)
        {
            return;
        }

        IsOpening = true;
        Raise(EditorChanges.Status);
        EditorChanges changes = EditorChanges.Status;
        try
        {
            EditorWorkspace workspace = await Task.Run(() => EditorWorkspace.Open(location));
            string? mapName = Map?.Name;
            string? containerId = SelectedContainer?.Id;
            string? looseId = SelectedLooseSocket?.Id;
            Workspace = workspace;
            Map = workspace.GameData.Maps.FirstOrDefault(map => map.Name == mapName) ?? (workspace.GameData.Maps.Count > 0 ? workspace.GameData.Maps[0] : null);
            TagCounts = Map?.CountByTag() ?? new Dictionary<string, int>();
            SelectedIndex = containerId is not null && Map?.Find(containerId) is { } kept ? IndexOf(Map, kept) : -1;
            SelectedLooseIndex = looseId is not null && Map is not null ? Map.IndexOfLooseItemSocket(looseId) : -1;
            SelectedRule = null;
            HighlightsRule = false;
            SelectedClass = null;
            if (SelectedTag is not null && workspace.GameData.TagTree.Find(SelectedTag) is null && !workspace.GameData.HasLooseItemTag(SelectedTag))
            {
                SelectedTag = null;
            }

            IsDirty = false;
            Error = null;
            Notice = null;
            Recompute();
            changes |= EditorChanges.Workspace;
            if (remember is not null)
            {
                EditorSettings stored = RememberedSettings();
                (remember with { LeftPanelWidth = stored.LeftPanelWidth, RightPanelWidth = stored.RightPanelWidth }).Save(_paths);
            }
        }
        catch (EditorDataException error)
        {
            Error = error.Message;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Error = $"Cannot write the editor settings {_paths.SettingsFile}: {error.Message}";
        }
        finally
        {
            IsOpening = false;
        }

        Raise(changes);
    }

    private void Recompute()
    {
        Revision++;
        lock (_rollsLock)
        {
            _rolls = null;
        }

        if (Workspace is null || Map is null)
        {
            _matches = [];
            FillGroups = LootFillGroups.Empty;
            return;
        }

        ContainerMatch[] matches = new ContainerMatch[Map.Containers.Count];
        for (int index = 0; index < matches.Length; index++)
        {
            matches[index] = Workspace.Match(Map.Containers[index]);
        }

        _matches = matches;
        FillGroups = LootFillGroups.Of(Map, matches);
    }

    private static int IndexOf(MapLoot map, LootContainer container)
    {
        for (int index = 0; index < map.Containers.Count; index++)
        {
            if (ReferenceEquals(map.Containers[index], container))
            {
                return index;
            }
        }

        return -1;
    }

    private void Raise(EditorChanges changes) => Changed?.Invoke(changes);
}
