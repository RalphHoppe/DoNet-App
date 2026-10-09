using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DoNet.Models;

namespace DoNet.Contracts;

/// <summary>
/// The store behind the Services catalog.
/// </summary>
/// <remarks>
/// The same shape as <see cref="IWebsiteDirectory"/> minus everything payment
/// methods need: a service type is a plain row, and the table is the catalog -
/// there is nothing merged in from code at read time, and so nothing extra to
/// offer or remember. Opening and closing are not here: the connection belongs to
/// <see cref="IEncryptedStore"/>.
/// </remarks>
public interface IServiceDirectory
{
    /// <summary>One page and the matching total together, newest first.</summary>
    Task<(IReadOnlyList<Service> Page, int Total)> GetPageWithTotalAsync(
        int skip, int take, string? search = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a service type, assigning its id, and returns the stored record so
    /// the caller does not have to guess what the store decided.
    /// </summary>
    Task<Service> AddAsync(Service service, CancellationToken cancellationToken = default);

    Task UpdateAsync(Service service, CancellationToken cancellationToken = default);

    Task DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a name is already taken by another service type, matched
    /// case-insensitively and trimmed.
    /// </summary>
    /// <param name="name">The name the form wants to save.</param>
    /// <param name="exceptId">
    /// A record to leave out of the comparison - the edit form passes the record
    /// it is editing, so saving a type under its own current name is not a clash
    /// with itself.
    /// </param>
    Task<bool> NameInUseAsync(
        string name, int exceptId = 0, CancellationToken cancellationToken = default);
}
