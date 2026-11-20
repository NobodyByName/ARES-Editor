namespace Ares.Editor.Core.Paths;

/// <summary>
/// The settings of an <c>ares-host.conf</c>, read the way ARES-Host reads them (ARES-Server <c>HostConfigurationFile</c>):
/// <c>key = value</c> lines, <c>#</c> comments and blank lines skipped, a value in double quotes unquoted, paths relative
/// to the file's folder.
/// </summary>
internal sealed class HostConfigFile
{
    private readonly Dictionary<string, string> _values;
    private readonly string _directory;

    private HostConfigFile(string path, Dictionary<string, string> values)
    {
        _values = values;
        _directory = Path.GetDirectoryName(path)!;
    }

    /// <summary>Reads a configuration file.</summary>
    /// <param name="path">Full path of the file.</param>
    /// <returns>Its settings.</returns>
    /// <exception cref="EditorDataException">The file cannot be read, or a line is neither a comment nor <c>key = value</c>.</exception>
    public static HostConfigFile Load(string path)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        int lineNumber = 0;
        try
        {
            foreach (string raw in File.ReadLines(path))
            {
                lineNumber++;
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                int separator = line.IndexOf('=', StringComparison.Ordinal);
                if (separator <= 0)
                {
                    throw new EditorDataException($"{path}:{lineNumber}: expected key = value");
                }

                string value = line[(separator + 1)..].Trim();
                if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
                {
                    value = value[1..^1];
                }

                values[line[..separator].Trim()] = value;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new EditorDataException($"Cannot read {path}: {error.Message}", error);
        }

        return new HostConfigFile(path, values);
    }

    /// <summary>A path setting resolved against the file's folder, or <paramref name="fallback"/> when it is missing or empty.</summary>
    /// <param name="key">The setting's key, for example <c>content-dir</c>.</param>
    /// <param name="fallback">The value used when the file does not set it, or null.</param>
    /// <returns>The full path, or null when neither the file nor the fallback gives one.</returns>
    public string? GetPath(string key, string? fallback)
    {
        string? value = _values.TryGetValue(key, out string? set) && set.Length > 0 ? set : fallback;
        return value is null ? null : Path.GetFullPath(value, _directory);
    }
}
