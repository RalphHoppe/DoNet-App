using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DoNet.Models;

namespace DoNet.Contracts;

/// <summary>
/// The VPS record store, over the shared encrypted connection.
/// </summary>
public interface IVpsDirectory
{
    /// <summary>
    /// One page of VPS records with the search's total, newest first.
    /// </summary>
    /// <param name="skip">Records to skip; page size times page number.</param>
    /// <param name="take">Records to return.</param>
    /// <param name="search">Filters name, system fields, country and note.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    Task<(IReadOnlyList<VpsRecord> Page, int Total)> GetPageWithTotalAsync(
        int skip, int take, string? search = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every VPS record, id and name included - the node picker's option list.
    /// </summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    Task<IReadOnlyList<VpsRecord>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a new VPS with its system row.
    /// </summary>
    /// <param name="record">The record to store; its id is assigned here.</param>
    /// <param name="system">The system row that belongs to it.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    Task<VpsRecord> AddAsync(
        VpsRecord record, VpsSystemInfo system, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits an edited VPS and its system row. False when the record vanished.
    /// </summary>
    /// <param name="record">The edited record; id and system link are taken from it.</param>
    /// <param name="system">The edited system row.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    Task<bool> UpdateAsync(
        VpsRecord record, VpsSystemInfo system, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a VPS, its system row, and reparents the VPS records connected to
    /// it - they become their own main.
    /// </summary>
    /// <param name="id">The record to delete.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
