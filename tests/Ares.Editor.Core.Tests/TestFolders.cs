using Ares.Editor.Core.Content;
using Ares.Editor.Core.GameData;

namespace Ares.Editor.Core.Tests;

/// <summary>A temporary folder deleted with the test.</summary>
internal sealed class TemporaryFolder : IDisposable
{
    public TemporaryFolder()
    {
        Directory.CreateDirectory(Path);
    }

    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ares-editor-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>Writes a file below the folder, creating its folders, and returns its full path.</summary>
    public string Write(string name, string content)
    {
        string path = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

/// <summary>
/// The real data of the workspace, when it is there: the content folder ARES-Server ships (found next to this repository,
/// or named by <c>ARES_CONTENT_DIR</c>) and an extracted game data folder (named by <c>ARES_GAMEDATA_DIR</c>, as for
/// ARES-Server's real-data tests).
/// </summary>
internal static class RealData
{
    public static string? ContentDirectory { get; } = Environment.GetEnvironmentVariable("ARES_CONTENT_DIR") is { Length: > 0 } set ? set : FindServerContent();

    public static string? GameDataDirectory { get; } = Environment.GetEnvironmentVariable("ARES_GAMEDATA_DIR") is { Length: > 0 } set ? set : null;

    /// <summary>The path of a real content file, or skips the test when it is not there.</summary>
    public static string ContentFile(string name)
    {
        string? path = ContentDirectory is null ? null : Path.Combine(ContentDirectory, name);
        Assert.SkipWhen(path is null || !File.Exists(path), $"No ARES-Server content/{name} next to ARES-Editor (or set ARES_CONTENT_DIR).");
        return path;
    }

    /// <summary>The defaults of the real <c>loot-spawn.json</c> for the game data's unset members, or skips the test without it.</summary>
    public static LootSpawnDefaults SpawnDefaults => LootSpawnDocument.Load(ContentFile(LootSpawnDocument.FileName)).Defaults;

    /// <summary>Skips the test without a real game data folder.</summary>
    public static void RequireGameData()
    {
        Assert.SkipWhen(GameDataDirectory is null || !File.Exists(Path.Combine(GameDataDirectory, "manifest.json")), "Set ARES_GAMEDATA_DIR to an extracted game data folder.");
    }

    private static string? FindServerContent()
    {
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            string candidate = Path.Combine(folder.FullName, "ARES-Server", "content");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
