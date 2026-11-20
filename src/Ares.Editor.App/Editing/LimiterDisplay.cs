using System.Text.RegularExpressions;
using Ares.Editor.Core.GameData;
using Ares.Editor.Core.Matching;

namespace Ares.Editor.App.Editing;

/// <summary>
/// How the editor shows a map's loot placement limiters: names, caps, the sockets each covers, the limiters capping a
/// socket, and the data the 3D view draws.
/// </summary>
public static partial class LimiterDisplay
{
    /// <summary>The colour of the limiters in the view and the lists, <c>#rrggbb</c>.</summary>
    public const string Color = "#ffb020";

    private const string NamePrefix = "LootPlacementLimiter";
    private const string SocketPrefix = "Socket.";

    /// <summary>
    /// A readable name for a limiter: the words of its name after <c>LootPlacementLimiter</c>, with its actor's instance
    /// number when another limiter of the map reads the same.
    /// </summary>
    /// <param name="map">The limiter's map.</param>
    /// <param name="limiter">The limiter.</param>
    /// <returns>The name, for example <c>Electrical Substation</c> or <c>Support Building #24</c>.</returns>
    public static string Name(MapLoot map, LootLimiter limiter)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(limiter);
        string words = Words(limiter);
        return map.Limiters.Count(other => Words(other) == words) > 1 && InstanceNumber().Match(limiter.Id) is { Success: true } number
            ? $"{words} #{number.Groups[1].Value}"
            : words;
    }

    /// <summary>The limiters of a map in list order: by name.</summary>
    /// <param name="map">The map.</param>
    /// <returns>Positions in <see cref="MapLoot.Limiters"/>.</returns>
    public static int[] ListOrder(MapLoot map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return [.. Enumerable.Range(0, map.Limiters.Count).OrderBy(index => Name(map, map.Limiters[index]), StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>A cap in a few words.</summary>
    /// <param name="limit">The entry.</param>
    /// <returns>For example <c>max 2</c>, or <c>no cap</c> for an unlimited entry.</returns>
    public static string Cap(LootLimit limit)
    {
        ArgumentNullException.ThrowIfNull(limit);
        return limit.Unlimited ? "no cap" : $"max {limit.Limit}";
    }

    /// <summary>A tag without the <c>Socket.</c> every socket tag starts with, for narrow lists.</summary>
    /// <param name="tag">The tag.</param>
    /// <returns>For example <c>LootContainer.Electrical</c>.</returns>
    public static string ShortTag(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        return tag.StartsWith(SocketPrefix, StringComparison.OrdinalIgnoreCase) ? tag[SocketPrefix.Length..] : tag;
    }

    /// <summary>A limiter's shape and size in words.</summary>
    /// <param name="limiter">The limiter.</param>
    /// <returns>For example <c>Sphere, 5 m radius</c>.</returns>
    public static string Shape(LootLimiter limiter)
    {
        ArgumentNullException.ThrowIfNull(limiter);
        return limiter.Shape switch
        {
            LootLimiterShape.Sphere => $"Sphere, {Display.Number(limiter.Radius / 100)} m radius",
            LootLimiterShape.Box => $"Box, {Display.Number(limiter.Radius / 100)} m wide",
            LootLimiterShape.Volumes => Display.Count(limiter.Shapes.Count, "volume part"),
            LootLimiterShape.None => "None: covers nothing",
            _ => $"Unknown shape {(int)limiter.Shape}",
        };
    }

    /// <summary>How many sockets a limiter covers whose tag is an entry's tag or below it: container sockets and loose item sockets.</summary>
    /// <param name="map">The limiter's map.</param>
    /// <param name="limiter">The limiter.</param>
    /// <param name="limit">One of its entries.</param>
    /// <returns>The number of sockets.</returns>
    public static int Sockets(MapLoot map, LootLimiter limiter, LootLimit limit)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(limiter);
        ArgumentNullException.ThrowIfNull(limit);
        return map.Containers.Count(container => container.Source == LootContainerSource.Socket && container.Tags.Any(tag => LootMatching.IsTagOrBelow(tag, limit.Tag))
                   && limiter.Covers(container.Location))
            + map.LooseItemSockets.Count(socket => LootMatching.IsTagOrBelow(socket.Tag, limit.Tag) && limiter.Covers(socket.Location));
    }

    /// <summary>The limiters that cap a socket, with the entries that cap it.</summary>
    /// <param name="map">The socket's map.</param>
    /// <param name="location">The socket's location.</param>
    /// <param name="tag">The socket's tag.</param>
    /// <returns>Each capping limiter's position in <see cref="MapLoot.Limiters"/> and entries, in map order.</returns>
    public static (int Index, IReadOnlyList<LootLimit> Limits)[] Capping(MapLoot map, WorldVector location, string tag)
    {
        ArgumentNullException.ThrowIfNull(map);
        return [.. map.Limiters.Select((limiter, index) => (index, limiter.LimitsOn(location, tag))).Where(entry => entry.Item2.Count > 0)];
    }

    /// <summary>What the view draws for a map's limiters, in <see cref="MapLoot.Limiters"/> order.</summary>
    /// <param name="map">The map.</param>
    /// <returns>The descriptors.</returns>
    public static AreaDescriptor[] Descriptors(MapLoot map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return [.. map.Limiters.Select(limiter => new AreaDescriptor(Name(map, limiter), Color, 0, AreaDisplay.Hulls(limiter.Shapes)))];
    }

    private static string Words(LootLimiter limiter)
    {
        string name = limiter.Name.StartsWith(NamePrefix, StringComparison.OrdinalIgnoreCase) ? limiter.Name[NamePrefix.Length..] : limiter.Name;
        string words = AreaDisplay.Words(name);
        return words.Length > 0 ? words : "Limiter";
    }

    [GeneratedRegex(@"_(\d+)$")]
    private static partial Regex InstanceNumber();
}
