using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DoNet.Models;

namespace DoNet.Contracts;

/// <summary>
/// The store behind the Websites directory.
/// </summary>
/// <remarks>
/// The same shape as <see cref="IPersonDirectory"/>, over the same shared connection,
/// plus the payment-method catalog. Opening and closing are not here: the connection
/// belongs to <see cref="IEncryptedStore"/>.
/// </remarks>
public interface IWebsiteDirectory
{
    /// <summary>One page and the matching total together, newest first.</summary>
    Task<(IReadOnlyList<Website> Page, int Total)> GetPageWithTotalAsync(
        int skip, int take, string? search = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a website, assigning its id and creation time, and returns the stored
    /// record so the caller does not have to guess what the store decided.
    /// </summary>
    Task<Website> AddAsync(Website website, CancellationToken cancellationToken = default);

    Task UpdateAsync(Website website, CancellationToken cancellationToken = default);

    Task DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every payment method the forms should offer: the six built in, then any the
    /// user has added before.
    /// </summary>
    Task<IReadOnlyList<string>> GetPaymentMethodsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a user-typed payment method so it is offered next time. Ignores anything
    /// blank, and anything that already exists under any casing.
    /// </summary>
    Task RememberPaymentMethodAsync(string method, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many saved websites list any of these methods among their own.
    /// </summary>
    /// <param name="methods">Method names, matched case-insensitively and trimmed.</param>
    Task<int> CountWebsitesUsingAsync(
        IReadOnlyList<string> methods, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a payment method everywhere it exists: the offered catalog, and every
    /// website that lists it.
    /// </summary>
    /// <remarks>
    /// A method is one string on each record, so a rename that touched only the
    /// catalog would strand every record on the old name. Both halves are rewritten
    /// in one transaction; a website left half-renamed is worse than either outcome
    /// alone.
    /// </remarks>
    Task RenamePaymentMethodAsync(
        string oldName, string newName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes payment methods everywhere they exist: the offered catalog, and every
    /// website that lists them.
    /// </summary>
    /// <remarks>
    /// Deleting a built-in hides it rather than editing the code it lives in; see
    /// <see cref="HiddenPaymentMethod"/> for why that distinction matters.
    /// </remarks>
    Task DeletePaymentMethodsAsync(
        IReadOnlyList<string> names, CancellationToken cancellationToken = default);
}
