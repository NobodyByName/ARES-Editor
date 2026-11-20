using System.Globalization;
using System.Text.RegularExpressions;
using Ares.Editor.Core.Content;
using Ares.Editor.Core.GameData;
using Ares.Editor.Core.Matching;

namespace Ares.Editor.App.Editing;

/// <summary>
/// How the editor words group fills: the line over a rule's chances, the rule's numbers on the map, a socket's place in
/// its group. Nouns come from the members' tag: its last segment names the sockets and the one before it the prop
/// (<c>Socket.LootContainer.Industrial.Lockers.Door</c>: doors of a Lockers prop).
/// </summary>
public static partial class FillDisplay
{
    /// <summary>A chance in percent as the editor shows and edits it, rounded to <see cref="GroupFillShares.Step"/>.</summary>
    /// <param name="percent">The chance from 0 to 100.</param>
    /// <returns>The rounded chance.</returns>
    public static double Rounded(double percent) => Math.Round(percent, 1, MidpointRounding.AwayFromZero);

    /// <summary>A chance in percent as text.</summary>
    /// <param name="percent">The chance from 0 to 100.</param>
    /// <returns>The text, for example <c>12.5 %</c>.</returns>
    public static string Percent(double percent) => Display.Number(Rounded(percent)) + " %";

    /// <summary>What the chances of a rule's group fill are about, in plain words.</summary>
    /// <param name="groups">The rule's groups on the map.</param>
    /// <param name="summary">Their summary.</param>
    /// <returns>The text, for example <c>Per Lockers prop: how many of its 6 doors get loot</c>.</returns>
    public static string Header(IReadOnlyList<LootFillGroup> groups, LootFillSummary summary)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(summary);
        if (summary.Sizes.Count == 0)
        {
            return "Per group of sockets: how many of them get loot";
        }

        (string prop, string socket) = Nouns(summary.Tag);
        int smallest = summary.Sizes[0].Key;
        int largest = summary.Sizes[^1].Key;
        if (groups is [{ IsListed: true }])
        {
            return $"The {smallest} listed {Plural(socket, smallest)} form one group: how many of them get loot";
        }

        string per = summary.Tag is null || groups.Any(group => group.IsListed) ? "Per group" : $"Per {prop} prop";
        return smallest == largest
            ? $"{per}: how many of its {smallest} {Plural(socket, smallest)} get loot"
            : $"{per} ({smallest}–{largest} {Plural(socket, largest)}): how many of them get loot";
    }

    /// <summary>A rule's groups and fill on the map.</summary>
    /// <param name="summary">The rule's summary.</param>
    /// <returns>The text, for example <c>60 groups on this map · ≈ 16.5 of 360 containers filled per raid</c>.</returns>
    public static string Stats(LootFillSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return summary.Groups == 0
            ? "No group on this map: the rule decides no socket here."
            : string.Create(CultureInfo.InvariantCulture,
                $"{Display.Count(summary.Groups, "group")} on this map · ≈ {summary.ExpectedFilled:0.#} of {summary.Containers} containers filled per raid");
    }

    /// <summary>A note for a number of filled sockets that no group has, or null when a group has that many.</summary>
    /// <param name="count">The number of filled sockets.</param>
    /// <param name="summary">The rule's summary.</param>
    /// <returns>The note, or null.</returns>
    public static string? Beyond(int count, LootFillSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        if (summary.Sizes.Count == 0 || count <= summary.Sizes[^1].Key)
        {
            return null;
        }

        int largest = summary.Sizes[^1].Key;
        return $"No group has more than {largest} sockets: a group that draws {count} fills all of them.";
    }

    /// <summary>A socket's place in its group.</summary>
    /// <param name="group">The group.</param>
    /// <param name="index">The socket's position in <see cref="MapLoot.Containers"/>.</param>
    /// <returns>The text, for example <c>Door 3 of 6</c>.</returns>
    public static string Position(LootFillGroup group, int index)
    {
        ArgumentNullException.ThrowIfNull(group);
        string socket = Nouns(group.Tag).Socket;
        return string.Create(CultureInfo.InvariantCulture, $"{char.ToUpperInvariant(socket[0])}{socket[1..]} {group.Members.ToList().IndexOf(index) + 1} of {group.Size}");
    }

    /// <summary>How the group is formed.</summary>
    /// <param name="group">The group.</param>
    /// <returns>The text.</returns>
    public static string Kind(LootFillGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return group.IsListed ? "The containers the rule lists by id" : "One placed prop's sockets of this tag";
    }

    /// <summary>A socket's name within its prop: the last part of its id.</summary>
    /// <param name="container">The socket.</param>
    /// <returns>The name, for example <c>ContainerSocket.Door.03</c>.</returns>
    public static string SocketName(LootContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        return container.Id[(container.Id.LastIndexOf('/') + 1)..];
    }

    /// <summary>The chances of a group fill in one line.</summary>
    /// <param name="shares">The chances.</param>
    /// <returns>The text, for example <c>0: 80 %, 1: 12.5 %, 2: 7.5 %</c>.</returns>
    public static string Chances(IEnumerable<FillShare> shares)
    {
        ArgumentNullException.ThrowIfNull(shares);
        return string.Join(", ", shares.Select(share => $"{share.Count}: {Percent(share.Percent)}"));
    }

    /// <summary>
    /// What the areas with a factor other than 1 turn a rule's chances into, one column per loot value with groups on the
    /// map, for its largest group there: the sliders set the chances outside every area with a loot value.
    /// </summary>
    /// <param name="groups">The rule's groups on the map.</param>
    /// <param name="shares">The chances as the sliders show them.</param>
    /// <returns>The columns in loot value order; empty when no group is in such an area.</returns>
    public static IReadOnlyList<FillAreaColumn> AreaColumns(IReadOnlyList<LootFillGroup> groups, GroupFillShares shares)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(shares);
        return [.. groups.Where(group => group.AreaMultiplier is not 1d)
            .GroupBy(group => (group.Area, group.AreaMultiplier))
            .OrderBy(area => area.Key.Area)
            .Select(area => new FillAreaColumn(area.Key.Area, area.Key.AreaMultiplier, area.Count(),
                shares.InArea(area.Max(group => group.Size), area.Key.AreaMultiplier)))];
    }

    /// <summary>A group's area and its factor on the chances.</summary>
    /// <param name="group">The group.</param>
    /// <returns>The text, for example <c>High ×5 (each socket's odds ×5)</c> or <c>Unknown ×1 (the rule's chances)</c>.</returns>
    public static string Area(LootFillGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        string factor = Display.Number(group.AreaMultiplier);
        return group.AreaMultiplier is 1d
            ? $"{group.Area} ×{factor} (the rule's chances)"
            : $"{group.Area} ×{factor} (each socket's odds ×{factor})";
    }

    // The prop and socket nouns of a tag: words of its last two segments, the socket noun in lower case.
    private static (string Prop, string Socket) Nouns(string? tag)
    {
        string[] segments = tag?.Split('.', StringSplitOptions.RemoveEmptyEntries) ?? [];
        string socket = segments.Length > 0 ? Words(segments[^1]).ToLowerInvariant() : "socket";
        string prop = segments.Length > 1 ? Words(segments[^2]) : "group";
        return (prop, socket.Length > 0 ? socket : "socket");
    }

    private static string Plural(string noun, int count) =>
        count == 1 ? noun
        : noun.EndsWith('s') || noun.EndsWith('x') || noun.EndsWith("ch", StringComparison.Ordinal) || noun.EndsWith("sh", StringComparison.Ordinal) ? noun + "es"
        : noun + "s";

    private static string Words(string segment) => WordBreak().Replace(segment.Replace('_', ' '), " ").Trim();

    [GeneratedRegex("(?<=[a-z])(?=[A-Z0-9])|(?<=[A-Z])(?=[A-Z][a-z])|(?<=[0-9])(?=[A-Za-z])")]
    private static partial Regex WordBreak();
}

/// <summary>A rule's chances in the areas of one loot value, as a column beside its sliders.</summary>
/// <param name="Area">The loot value.</param>
/// <param name="Multiplier">Its factor from <c>areaMultipliers</c>.</param>
/// <param name="Groups">How many of the rule's groups are in such an area.</param>
/// <param name="Shares">The chances there, in the order of the sliders' (see <see cref="GroupFillShares.InArea"/>).</param>
public sealed record FillAreaColumn(LootArea Area, double Multiplier, int Groups, IReadOnlyList<FillShare> Shares)
{
    /// <summary>The column's heading, for example <c>High ×5</c>.</summary>
    public string Heading => $"{Area} ×{Display.Number(Multiplier)}";

    /// <summary>The heading's tooltip.</summary>
    public string Title => $"{Display.Count(Groups, "group")} in {Area} areas: each socket's odds ×{Display.Number(Multiplier)}";
}
