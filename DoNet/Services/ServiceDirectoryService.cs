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
/// The service-type store, over the shared encrypted connection.
/// </summary>
/// <remarks>
/// The same shape as the other directories, with one addition: names are checked
/// for clashes, because a catalog that offers VPS twice is a catalog nobody trusts.
/// The comparison is case-insensitive through <see cref="EF.Functions.Like"/> for
/// the same reason as everywhere else - it happens in SQLite, without pulling rows
/// into memory to lower-case them.
/// </remarks>
public sealed class ServiceDirectoryService : IServiceDirectory
{
    private readonly IEncryptedStore _store;

    public ServiceDirectoryService(IEncryptedStore store)
    {
        _store = store;
    }

    public Task<(IReadOnlyList<Service> Page, int Total)> GetPageWithTotalAsync(
        int skip, int take, string? search = null, CancellationToken cancellationToken = default)
        => _store.RunAsync<(IReadOnlyList<Service> Page, int Total)>(
            async (db, token) =>
            {
                IQueryable<Service> filtered = Filter(db.Services.AsNoTracking(), search);

                int total = await filtered.CountAsync(token).ConfigureAwait(false);

                List<Service> page = await filtered
                    .OrderByDescending(s => s.Id)
                    .Skip(skip)
                    .Take(take)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                return ((IReadOnlyList<Service>)page, total);
            },
            cancellationToken);

    public Task<Service> AddAsync(Service service, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);

        return _store.RunAsync(
            async (db, token) =>
            {
                Service stored = service.Clone();
                stored.Id = 0;                          // the database assigns it

                db.Services.Add(stored);
                await db.SaveChangesAsync(token).ConfigureAwait(false);

                return stored;
            },
            cancellationToken);
    }

    public async Task UpdateAsync(Service service, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);

        await _store.RunAsync(
            async (db, token) =>
            {
                Service? existing = await db.Services
                    .FirstOrDefaultAsync(s => s.Id == service.Id, token)
                    .ConfigureAwait(false);

                if (existing is null)
                {
                    return false;
                }

                // The id belongs to the store, not to the form that edited it.
                existing.Name = service.Name;
                existing.Description = service.Description;
                existing.Note = service.Note;

                await db.SaveChangesAsync(token).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
        => await _store.RunAsync(
            async (db, token) =>
            {
                Service? existing = await db.Services
                    .FirstOrDefaultAsync(s => s.Id == id, token)
                    .ConfigureAwait(false);

                if (existing is not null)
                {
                    db.Services.Remove(existing);
                    await db.SaveChangesAsync(token).ConfigureAwait(false);
                }

                return true;
            },
            cancellationToken).ConfigureAwait(false);

    public Task<bool> NameInUseAsync(
        string name, int exceptId = 0, CancellationToken cancellationToken = default)
    {
        string trimmed = name?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            return Task.FromResult(false);
        }

        return _store.RunAsync(
            async (db, token) => await db.Services
                .AsNoTracking()
                .AnyAsync(s => s.Id != exceptId && EF.Functions.Like(s.Name, trimmed), token)
                .ConfigureAwait(false),
            cancellationToken);
    }

    /// <summary>
    /// Applies the search box to a query. Same LIKE reasoning as every other
    /// directory: instr() is case sensitive, and a search that misses "Cloud"
    /// because the record says "cloud" is broken.
    /// </summary>
    private static IQueryable<Service> Filter(IQueryable<Service> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return query;
        }

        string term = search.Trim();
        string pattern = SearchPattern.For(term);
        bool numeric = int.TryParse(term, out int id);

        return query.Where(s =>
            (numeric && s.Id == id)
            || EF.Functions.Like(s.Name, pattern, SearchPattern.Escape)
            || EF.Functions.Like(s.Description, pattern, SearchPattern.Escape)
            || EF.Functions.Like(s.Note, pattern, SearchPattern.Escape));
    }
}
