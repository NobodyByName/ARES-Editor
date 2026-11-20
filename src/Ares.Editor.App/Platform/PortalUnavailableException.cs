namespace Ares.Editor.App.Platform;

/// <summary>The desktop portal cannot show a chooser, or its answer cannot be used.</summary>
internal sealed class PortalUnavailableException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">Why the portal cannot be used.</param>
    public PortalUnavailableException(string message)
        : base(message)
    {
    }
}
