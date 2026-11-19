using System.Runtime.InteropServices;
using System.Windows;

namespace UEManager.UI.Services;

/// <summary>
/// Puts text on the clipboard, and reads it. An app that watches the clipboard, such as a translator or a remote
/// desktop, may hold it for a moment: Windows then reports a failure, often although the text is already there.
/// </summary>
internal static class ClipboardText
{
    private const int Tries = 3;

    /// <summary>The text on the clipboard; null when it holds none, or cannot be read right now.</summary>
    public static string? Get()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <returns>Null when the text is on the clipboard, else what Windows said after the last try.</returns>
    public static string? Set(string text)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return null;
            }
            catch (COMException ex)
            {
                if (IsThere(text))
                    return null;
                if (attempt == Tries)
                    return ex.Message;
                Thread.Sleep(150);
            }
        }
    }

    private static bool IsThere(string text)
    {
        try
        {
            return Clipboard.ContainsText() && Clipboard.GetText() == text;
        }
        catch (COMException)
        {
            return false;
        }
    }
}
