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
/// The website store, over the shared encrypted connection.
/// </summary>
public sealed class WebsiteDirectoryService : IWebsiteDirectory
{
    private readonly IEncryptedStore _store;

    public WebsiteDirectoryService(IEncryptedStore store)
    {
        _store = store;
    }

    public Task<(IReadOnlyList<Website> Page, int Total)> GetPageWithTotalAsync(
        int skip, int take, string? search = null, CancellationToken cancellationToken = default)
        => _store.RunAsync<(IReadOnlyList<Website> Page, int Total)>(
            async (db, token) =>
            {
                IQueryable<Website> filtered = Filter(db.Websites.AsNoTracking(), search);

                int total = await filtered.CountAsync(token).ConfigureAwait(false);

                List<Website> page = await filtered
                    .OrderByDescending(w => w.Id)
                    .Skip(skip)
                    .Take(take)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                return ((IReadOnlyList<Website>)page, total);
            },
            cancellationToken);

    public Task<Website> AddAsync(Website website, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(website);

        return _store.RunAsync(
            async (db, token) =>
            {
                Website stored = website.Clone();
                stored.Id = 0;                          // the database assigns it
                stored.CreatedAt = DateTimeOffset.Now;

                db.Websites.Add(stored);
                await db.SaveChangesAsync(token).ConfigureAwait(false);

                return stored;
            },
            cancellationToken);
    }

    public async Task UpdateAsync(Website website, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(website);

        await _store.RunAsync(
            async (db, token) =>
            {
                Website? existing = await db.Websites
                    .FirstOrDefaultAsync(w => w.Id == website.Id, token)
                    .ConfigureAwait(false);

                if (existing is null)
                {
                    return false;
                }

                // Id and CreatedAt belong to the store, not to the form that edited it.
                existing.Name = website.Name;
                existing.Domain = website.Domain;
                existing.Description = website.Description;
                existing.PaymentMethodsRaw = website.PaymentMethodsRaw;
                existing.Note = website.Note;

                await db.SaveChangesAsync(token).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
        => await _store.RunAsync(
            async (db, token) =>
            {
                Website? existing = await db.Websites
                    .FirstOrDefaultAsync(w => w.Id == id, token)
                    .ConfigureAwait(false);

                if (existing is not null)
                {
                    db.Websites.Remove(existing);
                    await db.SaveChangesAsync(token).ConfigureAwait(false);
                }

                return true;
            },
            cancellationToken).ConfigureAwait(false);

    public Task<IReadOnlyList<string>> GetPaymentMethodsAsync(
        CancellationToken cancellationToken = default)
        => _store.RunAsync<IReadOnlyList<string>>(
            async (db, token) =>
            {
                List<string> saved = await db.PaymentMethodOptions
                    .AsNoTracking()
                    .OrderBy(o => o.Id)
                    .Select(o => o.Name)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                return PaymentMethodCatalog.Merge(saved);
            },
            cancellationToken);

    public async Task RememberPaymentMethodAsync(
        string method, CancellationToken cancellationToken = default)
    {
        string trimmed = method?.Trim() ?? string.Empty;

        // Nothing to remember, and the six built-ins are already always offered.
        if (trimmed.Length == 0 || PaymentMethodCatalog.IsBuiltIn(trimmed))
        {
            return;
        }

        await _store.RunAsync(
            async (db, token) =>
            {
                // Case-insensitive duplicate check. EF.Functions.Like is used rather
                // than ToLower() so the comparison happens in SQLite rather than by
                // pulling every option into memory - and LIKE without wildcards is an
                // equality test that ignores ASCII case.
                bool exists = await db.PaymentMethodOptions
                    .AnyAsync(o => EF.Functions.Like(o.Name, trimmed), token)
                    .ConfigureAwait(false);

                if (exists)
                {
                    return false;
                }

                db.PaymentMethodOptions.Add(new PaymentMethodOption { Name = trimmed });
                await db.SaveChangesAsync(token).ConfigureAwait(false);

                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies the search box to a query. Same LIKE reasoning as the person store:
    /// instr() is case sensitive and a directory search that misses "Amazon" because
    /// the record says "amazon" is broken.
    /// </summary>
    private static IQueryable<Website> Filter(IQueryable<Website> query, string? search)
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
            || EF.Functions.Like(w.Name, pattern, SearchPattern.Escape)
            || EF.Functions.Like(w.Domain, pattern, SearchPattern.Escape)
            || EF.Functions.Like(w.Description, pattern, SearchPattern.Escape)
            || EF.Functions.Like(w.PaymentMethodsRaw, pattern, SearchPattern.Escape)
            || EF.Functions.Like(w.Note, pattern, SearchPattern.Escape));
    }
}
