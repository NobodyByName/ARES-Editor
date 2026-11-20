using Photino.Blazor;

namespace Ares.Editor.App.Platform;

/// <summary>
/// Lets the user choose a folder with the desktop's own dialog. On Linux that is the XDG desktop portal's chooser
/// (Photino's dialog there is GTK's own, whatever the desktop); Windows and macOS use Photino's native dialog. Linux
/// falls back to Photino's dialog when the portal cannot be used, and then keeps using it.
/// </summary>
public sealed class FolderPicker
{
    private readonly PhotinoBlazorApp _app;
    private bool _portalFailed;

    /// <summary>Creates a picker for the app's main window.</summary>
    /// <param name="app">The app whose main window shows Photino's dialog.</param>
    public FolderPicker(PhotinoBlazorApp app)
    {
        _app = app;
    }

    /// <summary>
    /// Shows a folder chooser and waits until the user closes it. The method keeps the caller's synchronization
    /// context, so a component awaiting it continues on the renderer's dispatcher.
    /// </summary>
    /// <param name="title">The dialog's title.</param>
    /// <param name="startDirectory">The folder the dialog starts in, or null.</param>
    /// <returns>The chosen folder, or null when the user cancelled.</returns>
    public async Task<string?> PickFolderAsync(string title, string? startDirectory)
    {
        if (OperatingSystem.IsLinux() && !_portalFailed)
        {
            try
            {
                return await XdgPortalFolderChooser.PickFolderAsync(title, startDirectory);
            }
            catch (Exception exception)
            {
                _portalFailed = true;
                await Console.Error.WriteLineAsync(
                    $"ARES-Editor: the desktop portal's folder chooser cannot be used ({exception.GetType().Name}: {exception.Message}); using Photino's dialog instead.");
            }
        }

        string[] picked = await _app.MainWindow.ShowOpenFolderAsync(title, startDirectory);
        return picked is [{ Length: > 0 } folder, ..] ? folder : null;
    }
}
