using System.Text;
using Tmds.DBus.Protocol;

namespace Ares.Editor.App.Platform;

/// <summary>
/// Shows the desktop's own folder chooser through the XDG desktop portal (<c>org.freedesktop.portal.FileChooser</c> on
/// the session bus), so KDE, GNOME and other desktops show their native dialog.
/// </summary>
internal static class XdgPortalFolderChooser
{
    private const string PortalService = "org.freedesktop.portal.Desktop";
    private const string PortalPath = "/org/freedesktop/portal/desktop";
    private const string FileChooserInterface = "org.freedesktop.portal.FileChooser";
    private const string PropertiesInterface = "org.freedesktop.DBus.Properties";
    private const string RequestInterface = "org.freedesktop.portal.Request";
    private const string RequestPathPrefix = "/org/freedesktop/portal/desktop/request/";

    // The "directory" option of OpenFile needs version 3 of the FileChooser interface.
    private const uint MinimumVersion = 3;

    private const uint ResponseSuccess = 0;
    private const uint ResponseCancelled = 1;

    /// <summary>Shows the folder chooser and waits until the user closes it.</summary>
    /// <param name="title">The dialog's title.</param>
    /// <param name="startDirectory">The folder the dialog starts in; ignored when it does not exist.</param>
    /// <returns>The chosen folder, or null when the user cancelled.</returns>
    /// <exception cref="PortalUnavailableException">The portal cannot show the chooser or did not return a local folder.</exception>
    /// <exception cref="DBusException">The portal or the bus answered with an error.</exception>
    /// <exception cref="ConnectException">There is no session bus to connect to.</exception>
    public static async Task<string?> PickFolderAsync(string title, string? startDirectory)
    {
        string address = Address.Session ?? throw new PortalUnavailableException("there is no session bus");
        using Connection connection = new(address);
        await connection.ConnectAsync();

        uint version = await GetFileChooserVersionAsync(connection);
        if (version < MinimumVersion)
        {
            throw new PortalUnavailableException($"FileChooser version {version} cannot choose folders");
        }

        string uniqueName = connection.UniqueName ?? throw new PortalUnavailableException("the bus gave no unique name");
        string token = "ares_" + Guid.NewGuid().ToString("N");
        string expectedHandle = RequestPathPrefix + uniqueName.TrimStart(':').Replace('.', '_') + "/" + token;

        // The portal may answer before OpenFile returns, so the Response signal is subscribed to first, on the request
        // path the portal builds from the unique name and the token.
        TaskCompletionSource<PortalResponse> response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IDisposable subscription = await SubscribeToResponseAsync(connection, expectedHandle, response);
        try
        {
            string handle = await connection.CallMethodAsync(CreateOpenFileCall(connection, title, startDirectory, token), ReadObjectPath);
            if (handle != expectedHandle)
            {
                // Portals older than the handle_token convention return a path of their own.
                IDisposable followed = await SubscribeToResponseAsync(connection, handle, response);
                subscription.Dispose();
                subscription = followed;
            }

            PortalResponse result = await response.Task;
            return result.Code switch
            {
                ResponseSuccess => ToLocalPath(result.Uris),
                ResponseCancelled => null,
                _ => throw new PortalUnavailableException($"the chooser ended with response {result.Code}"),
            };
        }
        finally
        {
            subscription.Dispose();
        }
    }

    private static async Task<uint> GetFileChooserVersionAsync(Connection connection)
    {
        MessageBuffer call = CreateVersionCall(connection);
        return await connection.CallMethodAsync(call, static (message, _) => message.GetBodyReader().ReadVariantValue().GetUInt32());
    }

    private static MessageBuffer CreateVersionCall(Connection connection)
    {
        using MessageWriter writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: PortalService, path: PortalPath, @interface: PropertiesInterface, member: "Get", signature: "ss");
        writer.WriteString(FileChooserInterface);
        writer.WriteString("version");
        return writer.CreateMessage();
    }

    // OpenFile(s parent_window, s title, a{sv} options) -> o handle.
    private static MessageBuffer CreateOpenFileCall(Connection connection, string title, string? startDirectory, string token)
    {
        using MessageWriter writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: PortalService, path: PortalPath, @interface: FileChooserInterface, member: "OpenFile", signature: "ssa{sv}");

        // Photino exposes no Wayland or X11 handle of its window, so the dialog has no parent window.
        writer.WriteString(string.Empty);
        writer.WriteString(title);

        ArrayStart options = writer.WriteDictionaryStart();
        writer.WriteDictionaryEntryStart();
        writer.WriteString("handle_token");
        writer.WriteVariantString(token);
        writer.WriteDictionaryEntryStart();
        writer.WriteString("directory");
        writer.WriteVariantBool(true);
        writer.WriteDictionaryEntryStart();
        writer.WriteString("modal");
        writer.WriteVariantBool(true);
        if (startDirectory is not null && Directory.Exists(startDirectory))
        {
            // current_folder is a null-terminated byte string (ay), not a D-Bus string.
            byte[] folder = new byte[Encoding.UTF8.GetByteCount(startDirectory) + 1];
            Encoding.UTF8.GetBytes(startDirectory, folder);
            writer.WriteDictionaryEntryStart();
            writer.WriteString("current_folder");
            writer.WriteSignature("ay");
            writer.WriteArray(folder);
        }

        writer.WriteDictionaryEnd(options);
        return writer.CreateMessage();
    }

    private static string ReadObjectPath(Message message, object? state) => message.GetBodyReader().ReadObjectPathAsString();

    private static ValueTask<IDisposable> SubscribeToResponseAsync(Connection connection, string handle, TaskCompletionSource<PortalResponse> response)
    {
        MatchRule rule = new()
        {
            Type = MessageType.Signal,
            Sender = PortalService,
            Path = handle,
            Interface = RequestInterface,
            Member = "Response",
        };
        return connection.AddMatchAsync(
            rule,
            ReadResponse,
            (exception, value, _, _) =>
            {
                if (exception is not null)
                {
                    response.TrySetException(exception);
                }
                else
                {
                    response.TrySetResult(value);
                }
            },
            ObserverFlags.None,
            emitOnCapturedContext: false);
    }

    // Response(u response, a{sv} results); the chosen folder is the first entry of results["uris"] (as).
    private static PortalResponse ReadResponse(Message message, object? state)
    {
        Reader reader = message.GetBodyReader();
        uint code = reader.ReadUInt32();
        Dictionary<string, VariantValue> results = reader.ReadDictionaryOfStringToVariantValue();
        string[] uris = results.TryGetValue("uris", out VariantValue value) ? value.GetArray<string>() : [];
        return new PortalResponse(code, uris);
    }

    private static string ToLocalPath(string[] uris)
    {
        if (uris is not [{ } first, ..])
        {
            throw new PortalUnavailableException("the chooser returned no folder");
        }

        if (!Uri.TryCreate(first, UriKind.Absolute, out Uri? uri) || !uri.IsFile)
        {
            throw new PortalUnavailableException($"the chooser returned '{first}', which is not a local folder");
        }

        // LocalPath undoes the percent-encoding.
        return uri.LocalPath;
    }

    private readonly record struct PortalResponse(uint Code, string[] Uris);
}
