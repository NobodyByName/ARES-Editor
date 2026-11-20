namespace Ares.Editor.Core.GameData;

/// <summary>A container type the game registers for one container socket tag (<c>loot-container-types.json</c>).</summary>
/// <param name="Tag">The socket tag (<c>Socket.LootContainer.*</c>); a socket gets the type whose tag equals its own, ignoring case.</param>
/// <param name="ClassPaths">Every class the type may spawn, default classes first, then those of its colour variants, each once.</param>
/// <param name="MaxPerGroup">
/// Most containers of this type per group (one placed prop's sockets of the tag) when group caps apply: the type's own,
/// else the spawn settings' <c>maxPerGroup</c>.
/// </param>
public sealed record LootContainerType(string Tag, IReadOnlyList<string> ClassPaths, int MaxPerGroup);
