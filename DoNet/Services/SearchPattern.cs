using System;

namespace DoNet.Services;

/// <summary>
/// Turns what the user typed into a safe SQL LIKE pattern.
/// </summary>
/// <remarks>
/// Shared by every directory so the escaping rule exists once. Without it a user
/// typing <c>%</c> matches every record and <c>_</c> matches any single character -
/// the search box quietly becomes a wildcard console.
/// </remarks>
public static class SearchPattern
{
    /// <summary>The LIKE escape character, passed to EF.Functions.Like.</summary>
    public const string Escape = "\\";

    /// <summary>Wraps a term in wildcards, escaping any the user typed themselves.</summary>
    public static string For(string term)
    {
        ArgumentNullException.ThrowIfNull(term);

        string escaped = term
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

        return $"%{escaped}%";
    }
}
