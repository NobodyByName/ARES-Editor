using Ares.Editor.Core.Content;
using Ares.Editor.Core.GameData;
using Ares.Editor.Core.Matching;
using Ares.Editor.Core.Paths;

namespace Ares.Editor.Core;

/// <summary>
/// Everything the editor works on for one ARES server: its game data (read-only) and its three loot content files,
/// which edits change in memory until they are saved.
/// </summary>
public sealed class EditorWorkspace
{
    private EditorWorkspace(ServerLocation location, EditorGameData gameData, LootRulesDocument rules, LootSpawnDocument spawn, LootContentsDocument contents)
    {
        Location = location;
        GameData = gameData;
        Rules = rules;
        Spawn = spawn;
        Contents = contents;
    }

    /// <summary>The server's folders.</summary>
    public ServerLocation Location { get; }

    /// <summary>The server's game data: build stamp, container types and tag hierarchy, maps and their containers.</summary>
    public EditorGameData GameData { get; }

    /// <summary><c>loot-rules.json</c>.</summary>
    public LootRulesDocument Rules { get; }

    /// <summary><c>loot-spawn.json</c>.</summary>
    public LootSpawnDocument Spawn { get; }

    /// <summary><c>loot-contents.json</c>.</summary>
    public LootContentsDocument Contents { get; }

    /// <summary>Reads the game data and the loot content files of a server.</summary>
    /// <param name="location">The server's folders.</param>
    /// <returns>The workspace.</returns>
    /// <exception cref="EditorDataException">A folder or file is missing, unreadable or malformed.</exception>
    public static EditorWorkspace Open(ServerLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (!Directory.Exists(location.ContentDirectory))
        {
            throw new EditorDataException($"The content folder {location.ContentDirectory} does not exist.");
        }

        // The game data's unset members take the spawn file's defaults, as on the server.
        LootSpawnDocument spawn = LootSpawnDocument.Load(Path.Combine(location.ContentDirectory, LootSpawnDocument.FileName));
        EditorGameData gameData = EditorGameData.Load(location.GameDataDirectory, spawn.Defaults);
        return new EditorWorkspace(location, gameData,
            LootRulesDocument.Load(Path.Combine(location.ContentDirectory, LootRulesDocument.FileName)),
            spawn,
            LootContentsDocument.Load(Path.Combine(location.ContentDirectory, LootContentsDocument.FileName)));
    }

    /// <summary>What the current (possibly unsaved) spawn settings and rules decide for a container.</summary>
    /// <param name="container">A container of one of <see cref="EditorGameData.Maps"/>.</param>
    /// <returns>The match.</returns>
    public ContainerMatch Match(LootContainer container) => LootMatching.Match(container, Spawn, Rules);

    /// <summary>What the current (possibly unsaved) spawn settings and rules decide for a loose item socket.</summary>
    /// <param name="socket">A loose item socket of one of <see cref="EditorGameData.Maps"/>.</param>
    /// <returns>The match.</returns>
    public LooseItemMatch Match(LooseItemSocket socket) => LootMatching.MatchLoose(socket, Spawn, Rules);

    /// <summary>Saves the three content files.</summary>
    /// <exception cref="EditorDataException">A file cannot be written.</exception>
    public void SaveAll()
    {
        Rules.Save();
        Spawn.Save();
        Contents.Save();
    }
}
