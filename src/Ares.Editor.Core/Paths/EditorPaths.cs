namespace Ares.Editor.Core.Paths;

/// <summary>
/// Where the editor keeps its own settings: the per-user configuration folder (<c>%APPDATA%\ARES-Editor</c> on Windows,
/// <c>$XDG_CONFIG_HOME/ARES-Editor</c> or <c>~/.config/ARES-Editor</c> elsewhere). Never inside a server install.
/// </summary>
public sealed class EditorPaths
{
    /// <summary>Uses the per-user configuration folder, or <paramref name="overrideDirectory"/> when given.</summary>
    /// <param name="overrideDirectory">A folder to use instead, for example in tests.</param>
    public EditorPaths(string? overrideDirectory = null)
    {
        if (overrideDirectory is not null)
        {
            DataDirectory = overrideDirectory;
            return;
        }

        string root = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg && Path.IsPathRooted(xdg)
                ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        DataDirectory = Path.Combine(root, "ARES-Editor");
    }

    /// <summary>The editor's settings folder.</summary>
    public string DataDirectory { get; }

    /// <summary>The settings file, <c>settings.json</c> in <see cref="DataDirectory"/>.</summary>
    public string SettingsFile => Path.Combine(DataDirectory, "settings.json");
}
