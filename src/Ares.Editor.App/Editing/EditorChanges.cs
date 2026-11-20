namespace Ares.Editor.App.Editing;

/// <summary>What changed in an <see cref="EditorSession"/>, so views refresh only what depends on it.</summary>
[Flags]
public enum EditorChanges
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>Another server was opened or the content was reloaded from disk; everything is new.</summary>
    Workspace = 1,

    /// <summary>Another map was selected: new containers, counts and matches.</summary>
    Map = 2,

    /// <summary>A content document was edited: the matches were recomputed and the session is dirty.</summary>
    Content = 4,

    /// <summary>The selected container, rule or tag changed.</summary>
    Selection = 8,

    /// <summary>The colour mode changed.</summary>
    Colors = 16,

    /// <summary>The documents were saved, or an error was shown or cleared.</summary>
    Status = 32,

    /// <summary>The view should fly to the selected container.</summary>
    Focus = 64,
}
