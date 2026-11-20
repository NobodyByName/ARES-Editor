using System.Text.RegularExpressions;
using Ares.Editor.Core.Content;
using Ares.Editor.Core.GameData;
using Ares.Editor.Core.Matching;

namespace Ares.Editor.App.Editing;

/// <summary>How the editor shows a map's loot value areas: names, colours, labels with the current multipliers, and the data the 3D view draws.</summary>
public static partial class AreaDisplay
{
    private const string LocationPrefix = "ID_LOCATION_";

    /// <summary>The colour of an area of a loot value (for <see cref="LootArea.Unknown"/>: everywhere else).</summary>
    /// <param name="value">The loot value.</param>
    /// <returns>The colour as <c>#rrggbb</c>.</returns>
    public static string Color(LootArea value) => value switch
    {
        LootArea.High => "#ff6b57",
        LootArea.Medium => "#b48cff",
        LootArea.Low => "#4fd1c5",
        _ => "#6f7683",
    };

    /// <summary>
    /// A readable name for an area: the words of its name's localization key without <c>ID_LOCATION_</c>, else its actor's
    /// name in words without Unreal's instance number.
    /// </summary>
    /// <param name="area">The area.</param>
    /// <returns>
    /// The name, for example <c>Dam Watertreatment Main</c> for <c>ID_LOCATION_DAM_WATERTREATMENT_MAIN</c>, or
    /// <c>Gate Control House</c> for an actor <c>GateControlHouse_121</c> without a key.
    /// </returns>
    public static string Name(LootValueArea area)
    {
        ArgumentNullException.ThrowIfNull(area);
        if (area.Name is not { Length: > 0 } key)
        {
            return Words(area.Actor);
        }

        string words = key.StartsWith(LocationPrefix, StringComparison.OrdinalIgnoreCase) ? key[LocationPrefix.Length..] : key;
        string[] parts = words.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0
            ? string.Join(' ', parts.Select(part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()))
            : Words(area.Actor);
    }

    /// <summary>An actor or object name in words, without Unreal's instance number.</summary>
    /// <param name="name">The name, for example <c>GateControlHouse_121</c>.</param>
    /// <returns>The words, for example <c>Gate Control House</c>.</returns>
    public static string Words(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return WordBreak().Replace(InstanceNumber().Replace(name, string.Empty).Replace('_', ' '), " ").Trim();
    }

    /// <summary>The multiplier the spawn settings give containers of a loot value.</summary>
    /// <param name="spawn">The spawn settings.</param>
    /// <param name="value">The loot value.</param>
    /// <returns>The text, for example <c>×3.4</c>.</returns>
    public static string Multiplier(LootSpawnDocument spawn, LootArea value) => "×" + Display.Number(LootMatching.MultiplierOf(spawn, value));

    // The label the view shows over an area, for example "Generator Control Tower · High ×3.4".
    private static string Label(LootValueArea area, LootSpawnDocument spawn) => $"{Name(area)} · {area.Value} {Multiplier(spawn, area.Value)}";

    /// <summary>The areas of a map in list order: highest loot value first, then by name.</summary>
    /// <param name="map">The map.</param>
    /// <returns>Positions in <see cref="MapLoot.Areas"/>.</returns>
    public static int[] ListOrder(MapLoot map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return [.. Enumerable.Range(0, map.Areas.Count)
            .OrderByDescending(index => map.Areas[index].Value)
            .ThenBy(index => Name(map.Areas[index]), StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The labels of a map's areas, in <see cref="MapLoot.Areas"/> order.</summary>
    /// <param name="map">The map.</param>
    /// <param name="spawn">The spawn settings.</param>
    /// <returns>The labels.</returns>
    public static string[] Labels(MapLoot map, LootSpawnDocument spawn)
    {
        ArgumentNullException.ThrowIfNull(map);
        return [.. map.Areas.Select(area => Label(area, spawn))];
    }

    /// <summary>What the view draws for a map's areas, in <see cref="MapLoot.Areas"/> order.</summary>
    /// <param name="map">The map.</param>
    /// <param name="spawn">The spawn settings.</param>
    /// <returns>The descriptors.</returns>
    public static AreaDescriptor[] Descriptors(MapLoot map, LootSpawnDocument spawn)
    {
        ArgumentNullException.ThrowIfNull(map);
        return [.. map.Areas.Select(area => new AreaDescriptor(
            Label(area, spawn),
            Color(area.Value),
            (int)area.Value,
            Hulls(area.Hulls)))];
    }

    /// <summary>What the view draws for convex shapes.</summary>
    /// <param name="shapes">The shapes.</param>
    /// <returns>The descriptors, in the same order.</returns>
    public static HullDescriptor[] Hulls(IReadOnlyList<ConvexShape> shapes)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        return [.. shapes.Select(hull => new HullDescriptor([.. hull.Vertices.SelectMany(vertex => new[] { vertex.X, vertex.Y, vertex.Z })], [.. hull.Triangles]))];
    }

    [GeneratedRegex(@"_\d+$")]
    private static partial Regex InstanceNumber();

    [GeneratedRegex("(?<=[a-z])(?=[A-Z0-9])|(?<=[A-Z])(?=[A-Z][a-z])|(?<=[0-9])(?=[A-Za-z])")]
    private static partial Regex WordBreak();
}

/// <summary>One loot value area or loot placement limiter for the view (wwwroot/js/loot-areas.js); serialised to JSON by the JS interop (camelCase).</summary>
/// <param name="Label">The label shown over it.</param>
/// <param name="Color">Its colour, <c>#rrggbb</c>.</param>
/// <param name="Value">Its <see cref="LootArea"/> as a number (0 for a limiter).</param>
/// <param name="Hulls">The convex parts of its volume.</param>
public sealed record AreaDescriptor(string Label, string Color, int Value, HullDescriptor[] Hulls);

/// <summary>One convex part of an <see cref="AreaDescriptor"/> (see <see cref="ConvexShape"/>).</summary>
/// <param name="Vertices">The corners, flat <c>[x, y, z, …]</c> in Unreal units (cm, Z up).</param>
/// <param name="Triangles">Index triples into the corners, wound outward in Unreal's coordinates.</param>
public sealed record HullDescriptor(double[] Vertices, int[] Triangles);
