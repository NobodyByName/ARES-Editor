namespace Ares.Editor.Core.GameData;

/// <summary>
/// What the spawn settings (<c>loot-spawn.json</c>) give the members the game data leaves unset, as ARES-Server applies
/// them (<c>LootSpawnSettings.MaxPerGroup</c>, <c>LootLimiterDefaults</c>, <c>LootSpawnSettings.SpawnerCount</c>).
/// </summary>
/// <param name="MaxPerGroup">Most containers per group of a container type that sets none (<c>maxPerGroup</c>).</param>
/// <param name="LimiterShape">Shape of a placement limiter that sets none (<c>limiterDefaults.shape</c>).</param>
/// <param name="LimiterRadius">Radius of a placement limiter that sets none, in centimetres (<c>limiterDefaults.radius</c>).</param>
/// <param name="LimitUnlimited">Whether a limit entry that does not say caps nothing (<c>limiterDefaults.unlimited</c>).</param>
/// <param name="Limit">Spawns a limit entry that sets no number allows (<c>limiterDefaults.limit</c>).</param>
/// <param name="SpawnerCount">Containers a dynamic loot spawner that sets no number places per run (<c>spawnerCount</c>).</param>
public sealed record LootSpawnDefaults(int MaxPerGroup, LootLimiterShape LimiterShape, double LimiterRadius, bool LimitUnlimited, int Limit, int SpawnerCount);
