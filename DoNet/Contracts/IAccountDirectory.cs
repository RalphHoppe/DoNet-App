using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DoNet.Models;

namespace DoNet.Contracts;

/// <summary>
/// The store behind the Accounts directory.
/// </summary>
/// <remarks>
/// The same shape as <see cref="IPersonDirectory"/> and <see cref="IWebsiteDirectory"/>,
/// over the same shared connection, plus the list of websites the add form offers.
/// Opening and closing are not here: the connection belongs to
/// <see cref="IEncryptedStore"/>.
/// </remarks>
public interface IAccountDirectory
{
    /// <summary>
    /// One page and the matching total together, newest first, each account carrying
    /// the website it belongs to.
    /// </summary>
    Task<(IReadOnlyList<Account> Page, int Total)> GetPageWithTotalAsync(
        int skip, int take, string? search = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts an account, assigning its id and creation time, and returns the stored
    /// record so the caller does not have to guess what the store decided.
    /// </summary>
    Task<Account> AddAsync(Account account, CancellationToken cancellationToken = default);

    Task UpdateAsync(Account account, CancellationToken cancellationToken = default);

    Task DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every saved website, for the picker on the add and edit forms.
    /// </summary>
    /// <remarks>
    /// An account has to point at a website that exists, so the form offers a list
    /// rather than a text box. Ordered by name so the list reads the way the user
    /// would look through it, not the way the rows happen to sit on disk.
    /// </remarks>
    /// <summary>
    /// Every account with its website, for the pickers other records use to point
    /// at one.
    /// </summary>
    Task<IReadOnlyList<Account>> GetOptionsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Website>> GetWebsiteOptionsAsync(
        CancellationToken cancellationToken = default);
}
