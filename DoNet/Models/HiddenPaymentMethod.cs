namespace DoNet.Models;

/// <summary>
/// A payment method that ships with the app but that the user has deleted from the
/// offered list.
/// </summary>
/// <remarks>
/// The six built-in methods live in code so that they are always offered - which is
/// the right default, and the wrong behaviour once the user deletes one: a delete
/// that quietly undoes itself the next time the catalog is read is not a delete.
/// This table is the memory of that decision. Only built-ins appear in it; a
/// user-added method is deleted by removing its row from
/// <see cref="PaymentMethodOption"/>.
///
/// Renaming plays into it from both sides. Naming something after a built-in that is
/// hidden revives that built-in, so its hidden row goes; renaming a built-in away
/// from its shipped name hides the shipped name.
/// </remarks>
public sealed class HiddenPaymentMethod
{
    public int Id { get; set; }

    /// <summary>As the catalog spells the built-in, trimmed. Unique, case-insensitively.</summary>
    public string Name { get; set; } = string.Empty;
}
