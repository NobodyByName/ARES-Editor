using System.Globalization;
using Ares.Editor.Core.GameData;
using Ares.Editor.Core.Matching;

namespace Ares.Editor.App.Editing;

/// <summary>How the 3D view colours containers.</summary>
public enum ColorMode
{
    /// <summary>By the effective spawn chance, with own colours for decisions that have no single chance.</summary>
    SpawnChance,

    /// <summary>By the game data table the container comes from.</summary>
    Source,

    /// <summary>By the tag category (see <see cref="LootTagTree.Categories"/>).</summary>
    Category,
}

/// <summary>An sRGB colour with channels from 0 to 1.</summary>
/// <param name="R">Red.</param>
/// <param name="G">Green.</param>
/// <param name="B">Blue.</param>
public readonly record struct Rgb(double R, double G, double B)
{
    /// <summary>The colour as CSS (<c>#rrggbb</c>).</summary>
    public string Css => string.Create(CultureInfo.InvariantCulture, $"#{Channel(R):x2}{Channel(G):x2}{Channel(B):x2}");

    /// <summary>A colour from <c>#rrggbb</c>.</summary>
    /// <param name="hex">The colour.</param>
    /// <returns>The colour.</returns>
    public static Rgb FromHex(string hex)
    {
        int value = int.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new Rgb(((value >> 16) & 0xff) / 255.0, ((value >> 8) & 0xff) / 255.0, (value & 0xff) / 255.0);
    }

    /// <summary>The colour a fraction of the way to another one.</summary>
    /// <param name="other">The other colour.</param>
    /// <param name="amount">0 for this colour, 1 for <paramref name="other"/>.</param>
    /// <returns>The mixed colour.</returns>
    public Rgb Lerp(Rgb other, double amount) => new(R + ((other.R - R) * amount), G + ((other.G - G) * amount), B + ((other.B - B) * amount));

    private static int Channel(double value) => (int)Math.Round(Math.Clamp(value, 0, 1) * 255);
}

/// <summary>One entry of the view's legend.</summary>
/// <param name="Label">What the colour means.</param>
/// <param name="Swatch">CSS background of its swatch: a colour, or the gradient of a scale.</param>
/// <param name="Count">How many containers of the map have it.</param>
public sealed record LegendEntry(string Label, string Swatch, int Count);

/// <summary>The colour of each container in each <see cref="ColorMode"/>, and the matching legend.</summary>
public static class ContainerColoring
{
    // Spawn chance 0 to 1, evenly spaced stops; kept clear of the decision colours below.
    private static readonly Rgb[] s_chanceStops = [.. new[] { "#4c5a78", "#3f8fd2", "#2fc4b2", "#9be15d", "#fde725" }.Select(Rgb.FromHex)];

    // Containers without a category.
    private static readonly Rgb s_neutral = Rgb.FromHex("#8a919e");

    private static readonly Rgb s_groupFill = Rgb.FromHex("#e879f9");
    private static readonly Rgb s_never = Rgb.FromHex("#e5484d");

    private static readonly Dictionary<LootContainerSource, Rgb> s_sources = new()
    {
        [LootContainerSource.Socket] = Rgb.FromHex("#3f8fd2"),
        [LootContainerSource.Placed] = Rgb.FromHex("#f2c94c"),
        [LootContainerSource.Spawner] = Rgb.FromHex("#e879f9"),
    };

    private static readonly Rgb[] s_categories =
    [
        .. new[]
        {
            "#4e79a7", "#f28e2b", "#e15759", "#76b7b2", "#59a14f", "#edc948", "#b07aa1", "#ff9da7",
            "#9c755f", "#bab0ac", "#86bcb6", "#d37295", "#a0cbe8", "#ffbe7d", "#8cd17d", "#f1ce63",
        }.Select(Rgb.FromHex),
    ];

    /// <summary>The colour of one container.</summary>
    /// <param name="mode">The colour mode.</param>
    /// <param name="container">The container.</param>
    /// <param name="match">What the documents decide for it.</param>
    /// <param name="tree">The tag hierarchy, for categories.</param>
    /// <returns>The colour.</returns>
    public static Rgb ColorOf(ColorMode mode, LootContainer container, ContainerMatch match, LootTagTree tree) => mode switch
    {
        ColorMode.Source => s_sources[container.Source],
        ColorMode.Category => CategoryColor(tree, container),
        _ => match.Decision switch
        {
            SpawnDecision.GroupFill => s_groupFill,
            SpawnDecision.Never => s_never,
            _ => ChanceColor(match.Chance ?? 0),
        },
    };

    /// <summary>The colours of every container of a map, flat (<c>r, g, b</c> per container) as the view takes them.</summary>
    /// <param name="mode">The colour mode.</param>
    /// <param name="map">The map.</param>
    /// <param name="matches">What the documents decide for each container, by position.</param>
    /// <param name="tree">The tag hierarchy, for categories.</param>
    /// <returns>Three channels per container.</returns>
    public static double[] Colors(ColorMode mode, MapLoot map, IReadOnlyList<ContainerMatch> matches, LootTagTree tree)
    {
        double[] colors = new double[map.Containers.Count * 3];
        for (int index = 0; index < map.Containers.Count; index++)
        {
            Rgb color = ColorOf(mode, map.Containers[index], matches[index], tree);
            colors[index * 3] = color.R;
            colors[(index * 3) + 1] = color.G;
            colors[(index * 3) + 2] = color.B;
        }

        return colors;
    }

    /// <summary>
    /// The legend of a mode with how many containers of the map have each colour. In <see cref="ColorMode.SpawnChance"/>
    /// the first entry is the chance scale from 0 (left) to 1.
    /// </summary>
    /// <param name="mode">The colour mode.</param>
    /// <param name="map">The map.</param>
    /// <param name="matches">What the documents decide for each container, by position.</param>
    /// <param name="tree">The tag hierarchy, for categories.</param>
    /// <returns>The entries.</returns>
    public static IReadOnlyList<LegendEntry> Legend(ColorMode mode, MapLoot map, IReadOnlyList<ContainerMatch> matches, LootTagTree tree)
    {
        switch (mode)
        {
            case ColorMode.Source:
                return [.. s_sources.Select(entry => new LegendEntry(entry.Key.ToString(), entry.Value.Css, map.Containers.Count(container => container.Source == entry.Key)))];
            case ColorMode.Category:
                Dictionary<string, int> byCategory = map.Containers
                    .GroupBy(container => Category(tree, container)?.Tag ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
                return
                [
                    .. tree.Categories.Select((category, index) =>
                        new LegendEntry(category.Segment, s_categories[index % s_categories.Length].Css, byCategory.GetValueOrDefault(category.Tag))),
                    new LegendEntry("No category", s_neutral.Css, byCategory.GetValueOrDefault(string.Empty)),
                ];
            default:
                return
                [
                    new LegendEntry("Chance 0 → 1", $"linear-gradient(to right, {string.Join(", ", s_chanceStops.Select(stop => stop.Css))})", matches.Count(match => match.Decision is SpawnDecision.Default or SpawnDecision.RuleChance or SpawnDecision.SpawnerGroups)),
                    new LegendEntry("Group fill", s_groupFill.Css, matches.Count(match => match.Decision == SpawnDecision.GroupFill)),
                    new LegendEntry("Never", s_never.Css, matches.Count(match => match.Decision == SpawnDecision.Never)),
                ];
        }
    }

    // The colour of a spawn chance from 0 to 1 (clamped) on the scale.
    private static Rgb ChanceColor(double chance)
    {
        double position = Math.Clamp(chance, 0, 1) * (s_chanceStops.Length - 1);
        int stop = Math.Min((int)position, s_chanceStops.Length - 2);
        return s_chanceStops[stop].Lerp(s_chanceStops[stop + 1], position - stop);
    }

    private static LootTagNode? Category(LootTagTree tree, LootContainer container) =>
        container.Tags.Count > 0 ? tree.CategoryOf(container.Tags[0]) : null;

    private static Rgb CategoryColor(LootTagTree tree, LootContainer container)
    {
        if (Category(tree, container) is not { } category)
        {
            return s_neutral;
        }

        int index = 0;
        foreach (LootTagNode candidate in tree.Categories)
        {
            if (ReferenceEquals(candidate, category))
            {
                return s_categories[index % s_categories.Length];
            }

            index++;
        }

        return s_neutral;
    }
}
