namespace Ares.Editor.Core.GameData;

/// <summary>Finds container items by partial names.</summary>
public static class ContainerItemSearch
{
    /// <summary>
    /// The items whose technical or display name contains every word of <paramref name="query"/> (ignoring case),
    /// those with a name starting with the first word first, each group in display-name order.
    /// </summary>
    /// <param name="items">The items to search.</param>
    /// <param name="query">Words separated by spaces; empty matches every item.</param>
    /// <returns>The matching items.</returns>
    public static IReadOnlyList<ContainerItem> Find(IEnumerable<ContainerItem> items, string? query)
    {
        ArgumentNullException.ThrowIfNull(items);
        string[] words = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return
        [
            .. items.Where(item => words.All(word => Contains(item.TechnicalName, word) || Contains(item.DisplayName, word)))
                .OrderBy(item => words.Length > 0 && (StartsWith(item.DisplayName, words[0]) || StartsWith(Short(item.TechnicalName), words[0])) ? 0 : 1)
                .ThenBy(item => item.DisplayName ?? Short(item.TechnicalName), StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.TechnicalName, StringComparer.Ordinal),
        ];
    }

    private static bool Contains(string? text, string word) => text?.Contains(word, StringComparison.OrdinalIgnoreCase) == true;

    private static bool StartsWith(string? text, string word) => text?.StartsWith(word, StringComparison.OrdinalIgnoreCase) == true;

    // The technical name without its DA_Item_ prefix.
    private static string Short(string technicalName) =>
        technicalName.StartsWith("DA_Item_", StringComparison.Ordinal) ? technicalName["DA_Item_".Length..] : technicalName;
}
