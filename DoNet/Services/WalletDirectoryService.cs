using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DoNet.Contracts;
using DoNet.Data;
using DoNet.Models;
using Microsoft.EntityFrameworkCore;

namespace DoNet.Services;

/// <inheritdoc cref="IWalletDirectory" />
public sealed class WalletDirectoryService : IWalletDirectory
{
    private readonly IEncryptedStore _store;

    public WalletDirectoryService(IEncryptedStore store)
    {
        _store = store;
    }

    /// <inheritdoc />
    public Task<(IReadOnlyList<WalletRecord> Page, int Total)> GetPageWithTotalAsync(
        int skip, int take, string? search = null, CancellationToken cancellationToken = default)
        => _store.RunAsync<(IReadOnlyList<WalletRecord> Page, int Total)>(
            async (db, token) =>
            {
                IQueryable<WalletRecord> filtered = Filter(db.WalletRecords.AsNoTracking(), search);

                int total = await filtered.CountAsync(token).ConfigureAwait(false);

                List<WalletRecord> page = await filtered
                    .OrderByDescending(w => w.Id)
                    .Skip(skip)
                    .Take(take)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                await FillPersonNamesAsync(db, page, token).ConfigureAwait(false);

                return ((IReadOnlyList<WalletRecord>)page, total);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<WalletRecord>> GetAllAsync(
        CancellationToken cancellationToken = default)
        => _store.RunAsync<IReadOnlyList<WalletRecord>>(
            async (db, token) =>
            {
                List<WalletRecord> all = await db.WalletRecords
                    .AsNoTracking()
                    .OrderByDescending(w => w.Id)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                await FillPersonNamesAsync(db, all, token).ConfigureAwait(false);

                return (IReadOnlyList<WalletRecord>)all;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<WalletRecord> AddAsync(
        WalletRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        return _store.RunAsync(
            async (db, token) =>
            {
                WalletRecord stored = Clone(record);
                stored.Id = 0;                          // the database assigns it

                db.WalletRecords.Add(stored);
                await db.SaveChangesAsync(token).ConfigureAwait(false);

                return stored;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(
        WalletRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        return await _store.RunAsync(
            async (db, token) =>
            {
                WalletRecord? existing = await db.WalletRecords
                    .FirstOrDefaultAsync(w => w.Id == record.Id, token)
                    .ConfigureAwait(false);

                if (existing is null)
                {
                    return false;
                }

                CopyFields(record, existing);
                await db.SaveChangesAsync(token).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task DeleteAsync(int id, CancellationToken cancellationToken = default)
        => _store.RunAsync(
            async (db, token) =>
            {
                WalletRecord? doomed = await db.WalletRecords
                    .FirstOrDefaultAsync(w => w.Id == id, token)
                    .ConfigureAwait(false);

                if (doomed is not null)
                {
                    // VPS records paid with this wallet keep their history: the
                    // pointer clears and the records stay. The model declares
                    // that as a SetNull foreign key, but SQLite ships with
                    // enforcement off and these rows are not loaded anywhere,
                    // so the service clears the pointers itself - in the same
                    // save, so a wallet is only ever gone together with its
                    // pointers.
                    List<VpsRecord> paidWith = await db.VpsRecords
                        .Where(v => v.WalletId == id)
                        .ToListAsync(token)
                        .ConfigureAwait(false);

                    foreach (VpsRecord vps in paidWith)
                    {
                        vps.WalletId = null;
                    }

                    db.WalletRecords.Remove(doomed);
                    await db.SaveChangesAsync(token).ConfigureAwait(false);
                }

                return true;
            },
            cancellationToken);

    /// <summary>Search over the fields a wallet is found by.</summary>
    private static IQueryable<WalletRecord> Filter(IQueryable<WalletRecord> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return query;
        }

        string term = search.Trim();
        string pattern = SearchPattern.For(term);
        bool numeric = int.TryParse(term, out int id);

        return query.Where(w =>
            (numeric && w.Id == id)
            || EF.Functions.Like(w.Wallet, pattern)
            || EF.Functions.Like(w.WalletNumber, pattern)
            || EF.Functions.Like(w.Note, pattern));
    }

    /// <summary>
    /// Fills <see cref="WalletRecord.PersonName"/> from one map query, so a page
    /// costs two round trips instead of one per record.
    /// </summary>
    private static async Task FillPersonNamesAsync(
        DoNetDbContext db, IReadOnlyList<WalletRecord> records, CancellationToken token)
    {
        int[] ids = records
            .Where(w => w.PersonId is not null)
            .Select(w => w.PersonId!.Value)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return;
        }

        List<Person> people = await db.People
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(token)
            .ConfigureAwait(false);

        Dictionary<int, string> names = people.ToDictionary(
            p => p.Id,
            p => string.IsNullOrWhiteSpace(p.DisplayName) ? $"Person · #{p.Id}" : p.DisplayName);

        foreach (WalletRecord record in records)
        {
            record.PersonName = record.PersonId is { } id && names.TryGetValue(id, out string? name)
                ? name
                : null;
        }
    }

    private static void CopyFields(WalletRecord from, WalletRecord to)
    {
        to.PersonId = from.PersonId;
        to.Wallet = from.Wallet;
        to.WalletNumber = from.WalletNumber;
        to.Currency = from.Currency;
        to.Balance = from.Balance;
        to.BalanceDollars = from.BalanceDollars;
        to.ExchangeRate = from.ExchangeRate;
        to.Password = from.Password;
        to.RecoveryWords = from.RecoveryWords;
        to.Note = from.Note;
    }

    private static WalletRecord Clone(WalletRecord record)
    {
        WalletRecord copy = new();
        CopyFields(record, copy);
        return copy;
    }
}
