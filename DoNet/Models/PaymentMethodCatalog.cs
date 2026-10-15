using System;
using System.Collections.Generic;
using System.Linq;

namespace DoNet.Models;

/// <summary>
/// The payment methods the add and edit forms offer.
/// </summary>
/// <remarks>
/// Two sources, deliberately separate. The six below are built in: they ship with the
/// app, have brand marks drawn for them, and are always offered. Anything else the
/// user types is stored in the database and merged in on top, so a method added once
/// is a tick box from then on.
///
/// Matching is case-insensitive and whitespace-trimmed throughout. "paypal" typed by
/// hand must not become a seventh option sitting next to PayPal.
/// </remarks>
public static class PaymentMethodCatalog
{
    /// <summary>
    /// The built-in methods, in the order the design lays them out: three across,
    /// two down, digital wallets first and then currencies.
    /// </summary>
    public static IReadOnlyList<string> BuiltIn { get; } = new[]
    {
        "PayPal", "Apple Pay", "Google Pay",
        "Bitcoin", "Ethereum", "USDT",
    };

    /// <summary>
    /// The brand-mark key for a method, or null when there is no mark to draw.
    /// </summary>
    /// <remarks>
    /// Keys rather than names so the icon control is not matching on display text.
    /// A method with no key gets the neutral fallback mark, which is what every
    /// user-added method uses.
    /// </remarks>
    public static string? IconKey(string? method) => Normalise(method) switch
    {
        "paypal" => "PayPal",
        "apple pay" => "ApplePay",
        "google pay" => "GooglePay",
        "bitcoin" => "Bitcoin",
        "ethereum" => "Ethereum",
        "usdt" => "Usdt",
        _ => null,
    };

    /// <summary>Whether this is one of the six that ship with the app.</summary>
    public static bool IsBuiltIn(string? method)
        => BuiltIn.Any(b => Matches(b, method));

    /// <summary>Case-insensitive, trimmed comparison. The only way methods are compared.</summary>
    public static bool Matches(string? left, string? right)
        => string.Equals(Normalise(left), Normalise(right), StringComparison.Ordinal);

    /// <summary>
    /// The built-in six followed by the user's own, with duplicates of the built-ins
    /// removed.
    /// </summary>
    /// <param name="custom">Saved user-added methods, in any order.</param>
    public static IReadOnlyList<string> Merge(IEnumerable<string>? custom)
    {
        List<string> all = new(BuiltIn);

        if (custom is null)
        {
            return all;
        }

        foreach (string candidate in custom)
        {
            string trimmed = candidate?.Trim() ?? string.Empty;

            if (trimmed.Length == 0 || all.Any(existing => Matches(existing, trimmed)))
            {
                continue;
            }

            all.Add(trimmed);
        }

        return all;
    }

    private static string Normalise(string? value)
        => value?.Trim().ToLowerInvariant() ?? string.Empty;
}
