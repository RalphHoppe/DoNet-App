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

    /// <summary>
    /// The structure of a service type, or null when the type has never been
    /// designed.
    /// </summary>
    Task<ServiceDefinition?> GetDefinitionAsync(
        int serviceTypeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a type's structure, creating the definition row on first save.
    /// </summary>
    Task SaveDefinitionAsync(
        ServiceDefinition definition, CancellationToken cancellationToken = default);

    /// <summary>How many records a type has, not counting its table rows.</summary>
    Task<int> CountRecordsAsync(
        int serviceTypeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of a type's records and the matching total, newest first. Table
    /// rows are not included - they travel with the record they belong to.
    /// </summary>
    Task<(IReadOnlyList<ServiceRecord> Page, int Total)> GetRecordPageWithTotalAsync(
        int serviceTypeId,
        int skip,
        int take,
        string? search = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a record with its table rows, assigning ids and creation times, and
    /// returns the stored record.
    /// </summary>
    Task<ServiceRecord> AddRecordAsync(
        ServiceRecord record,
        IReadOnlyList<ServiceRecord> tableRows,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a record and replaces its table rows wholesale - rows removed from
    /// the form are deleted, rows added are inserted, in one save.
    /// </summary>
    Task UpdateRecordAsync(
        ServiceRecord record,
        IReadOnlyList<ServiceRecord> tableRows,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a record and the table rows attached to it.</summary>
    Task DeleteRecordAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The table rows attached to one record, in definition order is not the
    /// store's to know - they arrive in id order and the form sorts itself.
    /// </summary>
    Task<IReadOnlyList<ServiceRecord>> GetTableRowsAsync(
        int recordId, CancellationToken cancellationToken = default);
}
