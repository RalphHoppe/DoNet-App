using System.Globalization;

namespace DoNet.Controls;

/// <summary>
/// The single letter a record card shows in its avatar.
/// </summary>
internal static class Initial
{
    private const string None = "-";

    /// <summary>
    /// First letter of <paramref name="value"/>, upper case, or a dash when there is
    /// nothing to take one from.
    /// </summary>
    /// <remarks>
    /// Taking value[0] would be wrong for a name that starts outside the basic plane
    /// or with a combining sequence: it would slice a surrogate pair in half and draw
    /// a replacement box. GetNextTextElement walks one grapheme, which is what a
    /// person means by "the first letter".
    /// </remarks>
    internal static string From(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return None;
        }

        string trimmed = value.TrimStart();
        string letter = StringInfo.GetNextTextElement(trimmed, 0);

        return letter.Length == 0 ? None : letter.ToUpperInvariant();
    }

    /// <summary>First letter of whichever of the two has one.</summary>
    internal static string From(string? preferred, string? fallback)
    {
        string first = From(preferred);
        return first == None ? From(fallback) : first;
    }
}
