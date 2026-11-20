using Ares.Editor.Core;
using Ares.Editor.Core.Content;
using Ares.Editor.Core.GameData;
using Ares.Editor.Core.Matching;

namespace Ares.Editor.App.Editing;

/// <summary>What the profile panel edits the contents profile of: a container or loose item tag, or a container class.</summary>
/// <param name="Tag">The tag, or null for a class.</param>
/// <param name="Class">The container class path, or null for a tag.</param>
public sealed record ProfileTarget(string? Tag, string? Class)
{
    /// <summary>Whether the target is a loose item tag, whose profile sets no container-only member.</summary>
    public bool IsLoose => Tag is not null && ContentProfiles.IsLooseTag(Tag);

    /// <summary>The target as shown: the tag or the class path.</summary>
    public string Text => Tag ?? Class ?? string.Empty;

    /// <summary>The profile the session's selection opens: the selected class's, else the selected tag's when profiles can have it.</summary>
    /// <param name="session">The session.</param>
    /// <returns>The target, or null.</returns>
    public static ProfileTarget? Of(EditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.SelectedClass is { } containerClass)
        {
            return new ProfileTarget(null, containerClass);
        }

        return session.SelectedTag is { } tag && (LootMatching.IsTagOrBelow(tag, ContentProfiles.ContainerRoot) || ContentProfiles.IsLooseTag(tag))
            ? new ProfileTarget(tag, null)
            : null;
    }

    /// <summary>The target's own profile, or null.</summary>
    /// <param name="contents">The contents file.</param>
    /// <returns>The profile.</returns>
    public LootProfile? Own(LootContentsDocument contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        return contents.Profiles.FirstOrDefault(profile => Tag is not null
            ? profile.Tag is { } own && string.Equals(own, Tag, StringComparison.OrdinalIgnoreCase)
            : string.Equals(profile.Class, Class, StringComparison.Ordinal));
    }

    /// <summary>
    /// What the target inherits: the merged profiles of the tags above a tag, or of a class's container tag and the tags
    /// above it.
    /// </summary>
    /// <param name="workspace">The workspace.</param>
    /// <returns>The merge.</returns>
    public MergedProfile Inherited(EditorWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        string? tag = Tag is not null ? (Tag.LastIndexOf('.') is var dot and > 0 ? Tag[..dot] : null) : workspace.GameData.ContainerTagOf(Class!);
        return ContentProfiles.Resolve(workspace.Contents, tag, null);
    }

    /// <summary>A name for a new profile: the tag's last two parts, or the class's asset name; made unique.</summary>
    /// <param name="contents">The contents file.</param>
    /// <returns>The name.</returns>
    public string NewName(LootContentsDocument contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        string name;
        if (Tag is not null)
        {
            string[] parts = Tag.Split('.');
            name = string.Join(' ', parts.Skip(Math.Max(0, parts.Length - 2)));
        }
        else
        {
            string path = Class ?? string.Empty;
            string asset = path[(path.LastIndexOf('/') + 1)..];
            name = asset.Split('.')[0];
        }

        string unique = name;
        for (int counter = 2; contents.Profiles.Any(profile => string.Equals(profile.Name, unique, StringComparison.Ordinal)); counter++)
        {
            unique = $"{name} {counter}";
        }

        return unique;
    }

    /// <summary>The drop tags the items carry and every tag above them, sorted, for drop tag inputs.</summary>
    /// <param name="data">What the roll reads from the game data.</param>
    /// <returns>The tags.</returns>
    public static IReadOnlyList<string> DropTags(RollData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        SortedSet<string> tags = new(StringComparer.OrdinalIgnoreCase);
        foreach (string tag in data.Items.SelectMany(item => item.DropTags))
        {
            for (string current = tag; current.Length > 0; current = current.LastIndexOf('.') is var dot and > 0 ? current[..dot] : string.Empty)
            {
                if (!tags.Add(current))
                {
                    break;
                }
            }
        }

        return [.. tags];
    }

    /// <summary>The container items the default roll draws from (profiles name only those), for item pickers.</summary>
    /// <param name="gameData">The game data.</param>
    /// <returns>The items.</returns>
    public static IReadOnlyList<ContainerItem> Items(EditorGameData gameData)
    {
        ArgumentNullException.ThrowIfNull(gameData);
        HashSet<string> plain = new(gameData.Roll.Items.Select(item => item.TechnicalName), StringComparer.Ordinal);
        return [.. gameData.ContainerItems.Where(item => plain.Contains(item.TechnicalName))];
    }

    /// <summary>How a match reads: the item's in-game name, else its technical name, or the drop tag.</summary>
    /// <param name="match">The match.</param>
    /// <param name="items">The items to name it from.</param>
    /// <returns>The text.</returns>
    public static string Label(LootMatch match, IReadOnlyList<ContainerItem> items)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(items);
        return match.Item is { } name ? items.FirstOrDefault(item => item.TechnicalName == name)?.DisplayName ?? name : match.Text;
    }
}

/// <summary>The lists of a contents profile that match items.</summary>
public enum ProfileList
{
    /// <summary><c>only</c>: keeps only matching items.</summary>
    Only,

    /// <summary><c>weights</c>: multipliers on matching items.</summary>
    Weights,

    /// <summary><c>add</c>: items the source does not offer.</summary>
    Add,
}
