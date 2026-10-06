using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DoNet.Models;

namespace DoNet.Contracts;

/// <summary>
/// The store behind the Persons directory.
/// </summary>
/// <remarks>
/// This is the seam the encrypted database lands in. Every method is asynchronous and
/// allowed to throw, which is what gives the screen real loading and error states: an
/// encrypted open is slow enough to see, and a wrong key or a damaged file is a genuine
/// failure the user has to be told about rather than shown an empty page.
///
/// Reads are paged rather than "fetch everything", because the screen loads only what
/// is on display and asks for more as the user scrolls. An implementation over SQL
/// turns <see cref="GetPageAsync"/> into OFFSET/FETCH and keeps that property.
/// </remarks>
public interface IPersonDirectory
{
    /// <summary>
    /// Opens the store ahead of time. Called while the welcome screen is showing, so
    /// the cost is paid during an animation the user is already watching instead of
    /// appearing as a delay on the directory.
    /// </summary>
    Task WarmUpAsync(CancellationToken cancellationToken = default);

    /// <summary>One page of records, newest first, optionally filtered.</summary>
    Task<IReadOnlyList<Person>> GetPageAsync(
        int skip, int take, string? search = null, CancellationToken cancellationToken = default);

    /// <summary>How many records match <paramref name="search"/>, for paging.</summary>
    Task<int> CountAsync(string? search = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// One page and the matching total together. The directory needs both on every
    /// refresh, and asking separately means two round trips - and, on an encrypted
    /// store, two chances to pay an open - for one screen update.
    /// </summary>
    Task<(IReadOnlyList<Person> Page, int Total)> GetPageWithTotalAsync(
        int skip, int take, string? search = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a person, assigning its id and creation time, and returns the stored
    /// record so the caller does not have to guess what the store decided.
    /// </summary>
    Task<Person> AddAsync(Person person, CancellationToken cancellationToken = default);

    Task UpdateAsync(Person person, CancellationToken cancellationToken = default);

    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
