namespace Ares.Editor.Core.Paths;

/// <summary>The two folders of an ARES server the editor works on.</summary>
/// <param name="ContentDirectory">Full path of the content folder holding the loot files the editor reads and writes.</param>
/// <param name="GameDataDirectory">Full path of the game data folder ARES-GameDataExtract wrote; the editor only reads it.</param>
/// <param name="ConfigFile">Full path of the <c>ares-host.conf</c> the folders came from, or null when none was used.</param>
public sealed record ServerLocation(string ContentDirectory, string GameDataDirectory, string? ConfigFile)
{
    /// <summary>File name of ARES-Host's configuration, which ARES-Host looks for in its working folder.</summary>
    public const string ConfigFileName = "ares-host.conf";

    /// <summary>Key of the content folder setting in <see cref="ConfigFileName"/>.</summary>
    public const string ContentDirectoryKey = "content-dir";

    /// <summary>Key of the game data folder setting in <see cref="ConfigFileName"/>.</summary>
    public const string GameDataDirectoryKey = "game-data-dir";

    /// <summary>Value ARES-Host uses for <see cref="ContentDirectoryKey"/> when the configuration does not set it.</summary>
    public const string DefaultContentDirectory = "content";

    /// <summary>
    /// Game data folder used when the configuration does not set <see cref="GameDataDirectoryKey"/> (ARES-Host has no
    /// default and requires the setting): <c>gamedata</c>, the folder name the ARES-Server README extracts to.
    /// </summary>
    public const string DefaultGameDataDirectory = "gamedata";

    /// <summary>
    /// Finds the folders of the ARES server in <paramref name="serverDirectory"/> the way ARES-Host does: from the
    /// <see cref="ContentDirectoryKey"/> and <see cref="GameDataDirectoryKey"/> settings of its
    /// <see cref="ConfigFileName"/> (relative paths are relative to that file), with <see cref="DefaultContentDirectory"/>
    /// and <see cref="DefaultGameDataDirectory"/> when a setting or the whole file is missing.
    /// </summary>
    /// <param name="serverDirectory">The folder ARES-Host runs in (the one holding its configuration).</param>
    /// <returns>The location; the folders are not checked to exist.</returns>
    /// <exception cref="EditorDataException">The folder does not exist, or its configuration cannot be read.</exception>
    public static ServerLocation Resolve(string serverDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(serverDirectory);
        string full = Path.GetFullPath(serverDirectory);
        if (!Directory.Exists(full))
        {
            throw new EditorDataException($"The server folder {full} does not exist.");
        }

        string config = Path.Combine(full, ConfigFileName);
        if (!File.Exists(config))
        {
            return new ServerLocation(Path.Combine(full, DefaultContentDirectory), Path.Combine(full, DefaultGameDataDirectory), null);
        }

        HostConfigFile file = HostConfigFile.Load(config);
        return new ServerLocation(file.GetPath(ContentDirectoryKey, DefaultContentDirectory)!, file.GetPath(GameDataDirectoryKey, DefaultGameDataDirectory)!, config);
    }

    /// <summary>A location from folders the operator names directly.</summary>
    /// <param name="contentDirectory">The content folder.</param>
    /// <param name="gameDataDirectory">The game data folder.</param>
    /// <returns>The location with full paths.</returns>
    public static ServerLocation FromDirectories(string contentDirectory, string gameDataDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(contentDirectory);
        ArgumentException.ThrowIfNullOrEmpty(gameDataDirectory);
        return new ServerLocation(Path.GetFullPath(contentDirectory), Path.GetFullPath(gameDataDirectory), null);
    }
}
