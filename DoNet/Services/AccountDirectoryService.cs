using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DoNet.Contracts;
using DoNet.Models;
using Microsoft.EntityFrameworkCore;

namespace DoNet.Services;

/// <summary>
/// The account store, over the shared encrypted connection.
/// </summary>
public sealed class AccountDirectoryService : IAccountDirectory
{
    private readonly IEncryptedStore _store;

    public AccountDirectoryService(IEncryptedStore store)
    {
        _store = store;
    }

    public Task<(IReadOnlyList<Account> Page, int Total)> GetPageWithTotalAsync(
        int skip, int take, string? search = null, CancellationToken cancellationToken = default)
        => _store.RunAsync<(IReadOnlyList<Account> Page, int Total)>(
            async (db, token) =>
            {
                // Include, not a manual join: the card names the site, and without the
                // navigation loaded every card would have to go back to the database
                // for one string - the classic page of twenty-four extra queries.
                IQueryable<Account> filtered = Filter(
                    db.Accounts.AsNoTracking().Include(a => a.Website), search);

                int total = await filtered.CountAsync(token).ConfigureAwait(false);

                List<Account> page = await filtered
                    .OrderByDescending(a => a.Id)
                    .Skip(skip)
                    .Take(take)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                return ((IReadOnlyList<Account>)page, total);
            },
            cancellationToken);

    public Task<Account> AddAsync(Account account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        return _store.RunAsync(
            async (db, token) =>
            {
                Account stored = account.Clone();
                stored.Id = 0;                          // the database assigns it
                stored.CreatedAt = DateTimeOffset.Now;

                // The clone carries whatever Website instance the form was holding.
                // Leaving it attached would make EF treat that object as a second,
                // unsaved website and insert a duplicate alongside the account.
                stored.Website = null;

                db.Accounts.Add(stored);
                await db.SaveChangesAsync(token).ConfigureAwait(false);

                // Read the site back so the caller can name it without another trip.
                stored.Website = await db.Websites
                    .AsNoTracking()
                    .FirstOrDefaultAsync(w => w.Id == stored.WebsiteId, token)
                    .ConfigureAwait(false);

                return stored;
            },
            cancellationToken);
    }

    public async Task UpdateAsync(Account account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        await _store.RunAsync(
            async (db, token) =>
            {
                Account? existing = await db.Accounts
                    .FirstOrDefaultAsync(a => a.Id == account.Id, token)
                    .ConfigureAwait(false);

                if (existing is null)
                {
                    return false;
                }

                // Id and CreatedAt belong to the store, not to the form that edited it.
                existing.WebsiteId = account.WebsiteId;
                existing.Username = account.Username;
                existing.Password = account.Password;
                existing.Note = account.Note;

                await db.SaveChangesAsync(token).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
        => await _store.RunAsync(
            async (db, token) =>
            {
                Account? existing = await db.Accounts
                    .FirstOrDefaultAsync(a => a.Id == id, token)
                    .ConfigureAwait(false);

                if (existing is not null)
                {
                    db.Accounts.Remove(existing);
                    await db.SaveChangesAsync(token).ConfigureAwait(false);
                }

                return true;
            },
            cancellationToken).ConfigureAwait(false);

    public Task<IReadOnlyList<Account>> GetOptionsAsync(
        CancellationToken cancellationToken = default)
        => _store.RunAsync<IReadOnlyList<Account>>(
            async (db, token) =>
            {
                List<Account> options = await db.Accounts
                    .AsNoTracking()
                    .Include(a => a.Website)
                    .OrderBy(a => a.Id)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                return options;
            },
            cancellationToken);

    public Task<IReadOnlyList<Website>> GetWebsiteOptionsAsync(
        CancellationToken cancellationToken = default)
        => _store.RunAsync<IReadOnlyList<Website>>(
            async (db, token) =>
            {
                List<Website> options = await db.Websites
                    .AsNoTracking()
                    .OrderBy(w => w.Name)
                    .ThenBy(w => w.Domain)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                return options;
            },
            cancellationToken);

    /// <summary>
    /// Applies the search box to a query. Same LIKE reasoning as the other two stores:
    /// instr() is case sensitive and a directory search that misses "Gmail" because the
    /// record says "gmail" is broken.
    /// </summary>
    /// <remarks>
    /// The site's name and domain are searched through the relationship, so typing a
    /// site finds every account on it. The password is deliberately not searched: it
    /// would mean a typed fragment of one secret could confirm another, and nobody
    /// looks for an account by its password.
    /// </remarks>
    private static IQueryable<Account> Filter(IQueryable<Account> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return query;
        }

        string term = search.Trim();
        string pattern = SearchPattern.For(term);
        bool numeric = int.TryParse(term, out int id);

        return query.Where(a =>
            (numeric && a.Id == id)
            || EF.Functions.Like(a.Username, pattern, SearchPattern.Escape)
            || EF.Functions.Like(a.Note, pattern, SearchPattern.Escape)
            || (a.Website != null
                && (EF.Functions.Like(a.Website.Name, pattern, SearchPattern.Escape)
                    || EF.Functions.Like(a.Website.Domain, pattern, SearchPattern.Escape))));
    }
}
