using System.Globalization;
using Ares.Editor.Core.Content;
using Ares.Editor.Core.GameData;

namespace Ares.Editor.App.Editing;

/// <summary>How the editor writes numbers, names and positions, and reads numbers typed by the operator.</summary>
public static class Display
{
    /// <summary>A number with at most six decimals, invariant culture (as the content files write it).</summary>
    /// <param name="value">The number.</param>
    /// <returns>The text.</returns>
    public static string Number(double value) => Math.Round(value, 6).ToString(CultureInfo.InvariantCulture);

    /// <summary>A number, or an empty text for none.</summary>
    /// <param name="value">The number, or null.</param>
    /// <returns>The text.</returns>
    public static string Number(double? value) => value is { } set ? Number(set) : string.Empty;

    /// <summary>A chance as a percentage.</summary>
    /// <param name="chance">The chance from 0 to 1.</param>
    /// <returns>The text, for example <c>17.48 %</c>.</returns>
    public static string Percent(double chance) => (Math.Round(chance * 100, 2)).ToString(CultureInfo.InvariantCulture) + " %";

    /// <summary>A count with its noun, plural when it is not 1.</summary>
    /// <param name="count">The count.</param>
    /// <param name="noun">The noun in the singular; the plural adds <c>s</c>.</param>
    /// <returns>The text, for example <c>1 tag</c> or <c>3 tags</c>.</returns>
    public static string Count(int count, string noun) => string.Create(CultureInfo.InvariantCulture, $"{count} {noun}{(count == 1 ? string.Empty : "s")}");

    /// <summary>The name of a rule as the server's logs show it: its name, else its position (<c>#1</c>, ...).</summary>
    /// <param name="rule">The rule.</param>
    /// <param name="rules">The document's rules.</param>
    /// <returns>The name.</returns>
    public static string RuleName(LootRule rule, ObjectList<LootRule> rules) => rule.Name ?? $"#{rules.IndexOf(rule) + 1}";

    /// <summary>A world location in Unreal units.</summary>
    /// <param name="location">The location.</param>
    /// <returns>The text, for example <c>X 120, Y -40, Z 10 cm</c>.</returns>
    public static string Location(WorldVector location) =>
        string.Create(CultureInfo.InvariantCulture, $"X {location.X:0}, Y {location.Y:0}, Z {location.Z:0} cm");

    /// <summary>Reads a number typed in an input (invariant culture, as number inputs give it).</summary>
    /// <param name="text">The text.</param>
    /// <param name="value">The finite number.</param>
    /// <returns>False when the text is not a finite number.</returns>
    public static bool TryParse(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
}
