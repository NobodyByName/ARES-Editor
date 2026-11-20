using Ares.Editor.Core.Paths;

namespace Ares.Editor.Core.Tests;

/// <summary>Finding a server's folders and remembering them.</summary>
public sealed class PathsTests : IDisposable
{
    private readonly TemporaryFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void Without_a_configuration_the_server_folder_holds_content_and_gamedata()
    {
        ServerLocation location = ServerLocation.Resolve(_folder.Path);

        Assert.Equal(Path.Combine(_folder.Path, ServerLocation.DefaultContentDirectory), location.ContentDirectory);
        Assert.Equal(Path.Combine(_folder.Path, ServerLocation.DefaultGameDataDirectory), location.GameDataDirectory);
        Assert.Null(location.ConfigFile);
    }

    [Fact]
    public void The_configuration_names_the_folders_relative_to_itself()
    {
        string config = _folder.Write(ServerLocation.ConfigFileName, $"""
            # GAME DATA
            {ServerLocation.GameDataDirectoryKey} = "../extract"
            {ServerLocation.ContentDirectoryKey} =
            raid-port = 7777
            """);

        ServerLocation location = ServerLocation.Resolve(_folder.Path);

        Assert.Equal(config, location.ConfigFile);
        Assert.Equal(Path.GetFullPath(Path.Combine(_folder.Path, "..", "extract")), location.GameDataDirectory);
        Assert.Equal(Path.Combine(_folder.Path, "content"), location.ContentDirectory);
    }

    [Fact]
    public void A_malformed_configuration_or_missing_folder_is_refused()
    {
        _folder.Write(ServerLocation.ConfigFileName, "no separator");

        Assert.Throws<EditorDataException>(() => ServerLocation.Resolve(_folder.Path));
        Assert.Throws<EditorDataException>(() => ServerLocation.Resolve(Path.Combine(_folder.Path, "missing")));
    }

    [Fact]
    public void Settings_round_trip_and_name_a_location()
    {
        EditorPaths paths = new(Path.Combine(_folder.Path, "config"));
        Assert.Equal(new EditorSettings(), EditorSettings.Load(paths));
        Assert.Null(new EditorSettings().Location());

        new EditorSettings { ServerDirectory = _folder.Path }.Save(paths);
        EditorSettings server = EditorSettings.Load(paths);
        Assert.Equal(_folder.Path, server.ServerDirectory);
        Assert.Equal(ServerLocation.Resolve(_folder.Path), server.Location());

        EditorSettings explicitFolders = server with { ContentDirectory = "/srv/content", GameDataDirectory = "/srv/gamedata" };
        Assert.Equal(ServerLocation.FromDirectories("/srv/content", "/srv/gamedata"), explicitFolders.Location());
        Assert.EndsWith("settings.json", paths.SettingsFile, StringComparison.Ordinal);
        Assert.EndsWith("ARES-Editor", new EditorPaths().DataDirectory, StringComparison.Ordinal);

        File.WriteAllText(paths.SettingsFile, "{");
        Assert.Throws<EditorDataException>(() => EditorSettings.Load(paths));
    }

    [Fact]
    public void Panel_widths_round_trip_and_older_settings_load_without_them()
    {
        EditorPaths paths = new(Path.Combine(_folder.Path, "config"));
        Directory.CreateDirectory(paths.DataDirectory);
        File.WriteAllText(paths.SettingsFile, "{\"ServerDirectory\": \"/srv/ares\"}");
        EditorSettings older = EditorSettings.Load(paths);
        Assert.Equal("/srv/ares", older.ServerDirectory);
        Assert.Null(older.LeftPanelWidth);
        Assert.Null(older.RightPanelWidth);

        (older with { LeftPanelWidth = 280, RightPanelWidth = 420 }).Save(paths);
        EditorSettings resized = EditorSettings.Load(paths);
        Assert.Equal(280, resized.LeftPanelWidth);
        Assert.Equal(420, resized.RightPanelWidth);
        Assert.Equal("/srv/ares", resized.ServerDirectory);
    }
}
