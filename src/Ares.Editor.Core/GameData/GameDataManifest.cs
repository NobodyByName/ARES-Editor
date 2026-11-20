namespace Ares.Editor.Core.GameData;

/// <summary>The build stamp of a game data folder (<c>manifest.json</c>) and the maps it holds.</summary>
/// <param name="GameBuildId">Build id of the game the data was extracted from.</param>
/// <param name="ExtractorVersion">ARES version of the ARES-GameDataExtract that wrote it.</param>
/// <param name="MapNames">World names of the maps with a <c>maps/&lt;map&gt;/world.json</c>, ordered by name (ordinal).</param>
public sealed record GameDataManifest(int GameBuildId, string ExtractorVersion, IReadOnlyList<string> MapNames);
