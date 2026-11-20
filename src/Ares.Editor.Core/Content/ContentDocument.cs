using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ares.Editor.Core.Content;

/// <summary>
/// A loot content file of the server's content folder, edited in place: <see cref="Save"/> writes the file the way
/// ARES-Server's files are written (2-space indent, LF line ends, a final newline, characters unescaped), keeping
/// members the editor does not know, the member order and the text of unchanged numbers, so saving an unchanged file
/// rewrites it byte for byte.
/// </summary>
public abstract class ContentDocument : JsonObjectView
{
    private static readonly JsonWriterOptions s_writer = new()
    {
        Indented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private protected ContentDocument(JsonObject node, string[] order, string filePath)
        : base(node, order)
    {
        FilePath = filePath;
    }

    /// <summary>Full path of the file the document was read from and saves to.</summary>
    public string FilePath { get; }

    /// <summary>The file's text as <see cref="Save"/> writes it.</summary>
    /// <returns>The JSON text, ending with a newline.</returns>
    public string ToJson() => Encoding.UTF8.GetString(Serialize());

    /// <summary>Writes the document to <see cref="FilePath"/>, replacing the file only once the new one is complete.</summary>
    /// <exception cref="EditorDataException">The file cannot be written.</exception>
    public void Save()
    {
        string temporary = FilePath + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, Serialize());
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new EditorDataException($"Cannot write {FilePath}: {error.Message}", error);
        }
    }

    /// <summary>Reads a content file and builds its view, which checks the members it knows.</summary>
    /// <exception cref="EditorDataException">The file is missing, cannot be read, or is not a valid file of its kind.</exception>
    private protected static T Read<T>(string path, Func<JsonObject, string, T> create)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        string full = Path.GetFullPath(path);
        if (!File.Exists(full))
        {
            throw new EditorDataException($"The content file {full} does not exist. ARES ships it in the server's content folder.");
        }

        try
        {
            return JsonNode.Parse(File.ReadAllBytes(full)) is JsonObject root ? create(root, full) : throw new JsonException("the file is not a JSON object");
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or FormatException)
        {
            throw new EditorDataException($"{full} is not a valid content file: {error.Message}", error);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new EditorDataException($"Cannot read {full}: {error.Message}", error);
        }
    }

    private byte[] Serialize()
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, s_writer))
        {
            Node.WriteTo(writer);
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }
}
