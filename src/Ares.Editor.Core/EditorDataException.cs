namespace Ares.Editor.Core;

/// <summary>A folder or file the editor needs is missing, unreadable or malformed; the message tells the operator which and why.</summary>
public sealed class EditorDataException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is wrong, naming the folder or file.</param>
    public EditorDataException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with the failure that caused it.</summary>
    /// <param name="message">What is wrong, naming the folder or file.</param>
    /// <param name="innerException">The underlying failure.</param>
    public EditorDataException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
