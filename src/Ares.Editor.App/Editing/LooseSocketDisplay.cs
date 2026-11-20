using Ares.Editor.Core.GameData;
using Ares.Editor.Core.Matching;

namespace Ares.Editor.App.Editing;

/// <summary>How the 3D view and the inspector show loose item sockets: their own colours, apart from the containers' modes.</summary>
public static class LooseSocketDisplay
{
    private static readonly (LooseItemOptions Option, string Name)[] s_optionNames =
    [
        (LooseItemOptions.EnablePhysics, "physics (rests on the surface below)"),
        (LooseItemOptions.UseFixedRotation, "fixed rotation"),
        (LooseItemOptions.IgnoreTiltLimit, "no tilt limit"),
    ];

    /// <summary>The colour of a loose item socket not on nature.</summary>
    public static Rgb PropColor { get; } = Rgb.FromHex("#b9a6ff");

    /// <summary>The colour of a loose item socket on nature.</summary>
    public static Rgb NatureColor { get; } = Rgb.FromHex("#7d9a5c");

    /// <summary>The colour of a socket.</summary>
    /// <param name="socket">The socket.</param>
    /// <returns>Its colour.</returns>
    public static Rgb ColorOf(LooseItemSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        return socket.IsNature ? NatureColor : PropColor;
    }

    /// <summary>The sockets' locations for scene.js <c>setLooseSockets</c>.</summary>
    /// <param name="map">The map.</param>
    /// <returns>Flat <c>[x, y, z, ...]</c> in Unreal units, one triple per socket.</returns>
    public static double[] Positions(MapLoot map)
    {
        ArgumentNullException.ThrowIfNull(map);
        double[] positions = new double[map.LooseItemSockets.Count * 3];
        for (int index = 0; index < map.LooseItemSockets.Count; index++)
        {
            WorldVector location = map.LooseItemSockets[index].Location;
            positions[index * 3] = location.X;
            positions[(index * 3) + 1] = location.Y;
            positions[(index * 3) + 2] = location.Z;
        }

        return positions;
    }

    /// <summary>The sockets' colours for scene.js <c>setLooseSockets</c>.</summary>
    /// <param name="map">The map.</param>
    /// <returns>Flat <c>[r, g, b, ...]</c> sRGB from 0 to 1, one triple per socket.</returns>
    public static double[] Colors(MapLoot map)
    {
        ArgumentNullException.ThrowIfNull(map);
        double[] colors = new double[map.LooseItemSockets.Count * 3];
        for (int index = 0; index < map.LooseItemSockets.Count; index++)
        {
            Rgb color = ColorOf(map.LooseItemSockets[index]);
            colors[index * 3] = color.R;
            colors[(index * 3) + 1] = color.G;
            colors[(index * 3) + 2] = color.B;
        }

        return colors;
    }

    /// <summary>Which sockets are on nature, for scene.js <c>setLooseSockets</c>.</summary>
    /// <param name="map">The map.</param>
    /// <returns>One flag per socket.</returns>
    public static bool[] Nature(MapLoot map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return [.. map.LooseItemSockets.Select(socket => socket.IsNature)];
    }

    /// <summary>How many of the map's sockets are on nature.</summary>
    /// <param name="map">The map.</param>
    /// <returns>The count.</returns>
    public static int NatureCount(MapLoot map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return map.LooseItemSockets.Count(socket => socket.IsNature);
    }

    /// <summary>The placement options of a socket for people.</summary>
    /// <param name="options">The options.</param>
    /// <returns>The text, for example <c>physics, fixed rotation</c>, or <c>none</c>.</returns>
    public static string Options(LooseItemOptions options)
    {
        return options == LooseItemOptions.None
            ? "none"
            : string.Join(", ", s_optionNames.Where(entry => options.HasFlag(entry.Option)).Select(entry => entry.Name));
    }

    /// <summary>The hover line of a socket.</summary>
    /// <param name="socket">The socket.</param>
    /// <param name="match">What the documents decide for it.</param>
    /// <returns>The text: id, tag and chance.</returns>
    public static string Describe(LooseItemSocket socket, LooseItemMatch match)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(match);
        return $"{socket.Id} · {socket.Tag} · loose item chance {Display.Percent(match.Chance)}";
    }
}
