using Microsoft.JSInterop;

namespace Ares.Editor.App.Editing;

/// <summary>Copies text to the clipboard through <c>wwwroot/js/editor.js</c>.</summary>
public static class Clipboard
{
    /// <summary>Copies text to the clipboard.</summary>
    /// <param name="js">The page's JavaScript runtime.</param>
    /// <param name="text">The text.</param>
    /// <returns>Whether it was copied.</returns>
    public static async Task<bool> CopyAsync(IJSRuntime js, string text)
    {
        ArgumentNullException.ThrowIfNull(js);
        await using IJSObjectReference module = await js.InvokeAsync<IJSObjectReference>("import", "./js/editor.js");
        return await module.InvokeAsync<bool>("copyText", text);
    }
}
