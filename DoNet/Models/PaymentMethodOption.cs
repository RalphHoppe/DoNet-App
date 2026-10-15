namespace DoNet.Models;

/// <summary>
/// A payment method the user typed in themselves, remembered so it is offered again.
/// </summary>
/// <remarks>
/// The six built-in methods live in code; this table is only the additions. It exists
/// because a method added once has to come back next time - typing "Revolut" on one
/// record should make it a tick box on the next, not something to retype.
///
/// Deriving the list from the records instead was the alternative, and it loses
/// methods the moment the last record using one is edited or deleted. A method the
/// user deliberately added should not disappear because nothing currently uses it.
/// </remarks>
public sealed class PaymentMethodOption
{
    public int Id { get; set; }

    /// <summary>As the user typed it, trimmed. Unique, case-insensitively.</summary>
    public string Name { get; set; } = string.Empty;
}
