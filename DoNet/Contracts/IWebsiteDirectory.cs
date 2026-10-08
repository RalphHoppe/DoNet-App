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
}
