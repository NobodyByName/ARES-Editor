using Ares.Editor.Core.Matching;

namespace Ares.Editor.Core.GameData;

/// <summary>
/// How the item on a loose item socket is placed (the socket's <c>Socket.LootOptions.*</c> gameplay tags; ARES-Server
/// <c>Ares.Structs.Game.World.LooseItemOptions</c>). The numbers are the ones the game data stores.
/// </summary>
[Flags]
public enum LooseItemOptions
{
    /// <summary>No option: the item stays where the socket is.</summary>
    None = 0,

    /// <summary>The item simulates physics and comes to rest below the socket.</summary>
    EnablePhysics = 1,

    /// <summary>The item keeps the socket's rotation.</summary>
    UseFixedRotation = 2,

    /// <summary>The socket's tilt is not limited.</summary>
    IgnoreTiltLimit = 4,
}

/// <summary>
/// A loose item socket of a map: a spot where the server may put one item stack lying in the world. Loot rules can cover
/// it by id or tag.
/// </summary>
/// <param name="Id">Its id in the loot rules: <c>cell/actor/component/socket</c>, as for a container socket.</param>
/// <param name="Tag">Its <c>Socket.Loot.*</c> gameplay tag.</param>
/// <param name="Location">World location.</param>
/// <param name="Rotation">World rotation.</param>
/// <param name="Options">How the item is placed.</param>
/// <param name="DataLayer">Data layer that streams the socket's prop, or null when it is always present.</param>
/// <param name="Area">Loot value of the area around <paramref name="Location"/>: the highest among the map's areas that contain it.</param>
public sealed record LooseItemSocket(string Id, string Tag, WorldVector Location, WorldRotation Rotation, LooseItemOptions Options, string? DataLayer, LootArea Area)
{
    /// <summary>The tag of the sockets on trees, rocks and other plants (<c>Socket.Loot.Nature</c> and below).</summary>
    public const string NatureTag = "Socket.Loot.Nature";

    /// <summary>Whether the socket is on nature (its tag is <see cref="NatureTag"/> or below it, ignoring case).</summary>
    public bool IsNature => LootMatching.IsTagOrBelow(Tag, NatureTag);
}
