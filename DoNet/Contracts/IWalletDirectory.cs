using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DoNet.Models;

namespace DoNet.Contracts;

/// <summary>
/// The wallet record store, over the shared encrypted connection.
/// </summary>
public interface IWalletDirectory
{
    /// <summary>
    /// One page of wallet records with the search's total, newest first.
    /// </summary>
    /// <param name="skip">Records to skip; page size times page number.</param>
    /// <param name="take">Records to return.</param>
    /// <param name="search">Filters the brand, the number and the note.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    Task<(IReadOnlyList<WalletRecord> Page, int Total)> GetPageWithTotalAsync(
        int skip, int take, string? search = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every wallet record - the VPS form's "paid with" picker's option list.
    /// </summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    Task<IReadOnlyList<WalletRecord>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a new wallet.
    /// </summary>
    /// <param name="record">The record to store; its id is assigned here.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    Task<WalletRecord> AddAsync(
        WalletRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits an edited wallet. False when the record vanished.
    /// </summary>
    /// <param name="record">The edited record; the id is taken from it.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    Task<bool> UpdateAsync(
        WalletRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a wallet. VPS records that were paid with it keep their history:
    /// their pointer clears, the record does not go.
    /// </summary>
    /// <param name="id">The record to delete.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
