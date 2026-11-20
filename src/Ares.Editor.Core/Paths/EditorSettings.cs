using System.Text.Json;

namespace Ares.Editor.Core.Paths;

/// <summary>
/// The editor's remembered choices: the last server folder, or the last content and game data folders the operator gave
/// explicitly, and the side panel widths. Empty values mean nothing was chosen yet.
/// </summary>
public sealed record EditorSettings
{
    private static readonly JsonSerializerOptions s_indented = new() { WriteIndented = true };

    /// <summary>The ARES-Server folder last opened with <see cref="ServerLocation.Resolve"/>, or null.</summary>
    public string? ServerDirectory { get; init; }

    /// <summary>The content folder last given explicitly, or null to derive it from <see cref="ServerDirectory"/>.</summary>
    public string? ContentDirectory { get; init; }

    /// <summary>The game data folder last given explicitly, or null to derive it from <see cref="ServerDirectory"/>.</summary>
    public string? GameDataDirectory { get; init; }

    /// <summary>The left side panel's width in pixels as the operator last set it, or null for its default width.</summary>
    public int? LeftPanelWidth { get; init; }

    /// <summary>The right side panel's (the inspector's) width in pixels as the operator last set it, or null for its default width.</summary>
    public int? RightPanelWidth { get; init; }

    /// <summary>Reads the settings file, or returns empty settings when there is none.</summary>
    /// <param name="paths">Where the settings live.</param>
    /// <returns>The settings.</returns>
    /// <exception cref="EditorDataException">The file exists but cannot be read or is not valid JSON.</exception>
    public static EditorSettings Load(EditorPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (!File.Exists(paths.SettingsFile))
        {
            return new EditorSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(paths.SettingsFile)) ?? new EditorSettings();
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new EditorDataException($"Cannot read the editor settings {paths.SettingsFile}: {error.Message}", error);
        }
    }

    /// <summary>Writes the settings file, creating its folder.</summary>
    /// <param name="paths">Where the settings live.</param>
    public void Save(EditorPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Directory.CreateDirectory(paths.DataDirectory);
        File.WriteAllText(paths.SettingsFile, JsonSerializer.Serialize(this, s_indented));
    }

    /// <summary>
    /// The server location these settings describe: the explicit folders when both are set, else the ones resolved from
    /// <see cref="ServerDirectory"/>, else null.
    /// </summary>
    /// <returns>The location, or null when the settings name none.</returns>
    /// <exception cref="EditorDataException">The server folder's <c>ares-host.conf</c> cannot be read.</exception>
    public ServerLocation? Location()
    {
        if (ContentDirectory is not null && GameDataDirectory is not null)
        {
            return ServerLocation.FromDirectories(ContentDirectory, GameDataDirectory);
        }

        return ServerDirectory is null ? null : ServerLocation.Resolve(ServerDirectory);
    }
}
