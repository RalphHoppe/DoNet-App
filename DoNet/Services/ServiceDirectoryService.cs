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

    public Task<ServiceDefinition?> GetDefinitionAsync(
        int serviceTypeId, CancellationToken cancellationToken = default)
        => _store.RunAsync<ServiceDefinition?>(
            async (db, token) => await db.ServiceDefinitions
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == serviceTypeId, token)
                .ConfigureAwait(false),
            cancellationToken);

    public Task SaveDefinitionAsync(
        ServiceDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return _store.RunAsync(
            async (db, token) =>
            {
                ServiceDefinition? existing = await db.ServiceDefinitions
                    .FirstOrDefaultAsync(d => d.Id == definition.Id, token)
                    .ConfigureAwait(false);

                if (existing is null)
                {
                    db.ServiceDefinitions.Add(new ServiceDefinition
                    {
                        Id = definition.Id,
                        FieldsJson = definition.FieldsJson,
                        TablesJson = definition.TablesJson,
                        // First save stamps it; later saves keep the original moment,
                        // which is what "set up" means as distinct from "edited".
                        ConfiguredAt = definition.ConfiguredAt ?? DateTimeOffset.Now,
                    });
                }
                else
                {
                    existing.FieldsJson = definition.FieldsJson;
                    existing.TablesJson = definition.TablesJson;
                    existing.ConfiguredAt = definition.ConfiguredAt;
                }

                await db.SaveChangesAsync(token).ConfigureAwait(false);
                return true;
            },
            cancellationToken);
    }

    public Task<int> CountRecordsAsync(
        int serviceTypeId, CancellationToken cancellationToken = default)
        => _store.RunAsync(
            async (db, token) => await db.ServiceRecords
                .AsNoTracking()
                .CountAsync(r => r.ServiceTypeId == serviceTypeId
                                 && r.ParentRecordId == null, token)
                .ConfigureAwait(false),
            cancellationToken);

    public Task<(IReadOnlyList<ServiceRecord> Page, int Total)> GetRecordPageWithTotalAsync(
        int serviceTypeId,
        int skip,
        int take,
        string? search = null,
        CancellationToken cancellationToken = default)
        => _store.RunAsync<(IReadOnlyList<ServiceRecord> Page, int Total)>(
            async (db, token) =>
            {
                IQueryable<ServiceRecord> records = db.ServiceRecords
                    .AsNoTracking()
                    .Where(r => r.ServiceTypeId == serviceTypeId && r.ParentRecordId == null);

                if (!string.IsNullOrWhiteSpace(search))
                {
                    string term = search.Trim();
                    string pattern = SearchPattern.For(term);
                    bool numeric = int.TryParse(term, out int id);

                    records = records.Where(r =>
                        (numeric && r.Id == id)
                        || EF.Functions.Like(r.Title, pattern, SearchPattern.Escape)
                        || EF.Functions.Like(r.SearchText, pattern, SearchPattern.Escape));
                }

                int total = await records.CountAsync(token).ConfigureAwait(false);

                List<ServiceRecord> page = await records
                    .OrderByDescending(r => r.Id)
                    .Skip(skip)
                    .Take(take)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                return ((IReadOnlyList<ServiceRecord>)page, total);
            },
            cancellationToken);

    public Task<ServiceRecord> AddRecordAsync(
        ServiceRecord record,
        IReadOnlyList<ServiceRecord> tableRows,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        return _store.RunAsync(
            async (db, token) =>
            {
                ServiceRecord stored = record.Clone();
                stored.Id = 0;                          // the database assigns it
                stored.CreatedAt = DateTimeOffset.Now;

                db.ServiceRecords.Add(stored);
                await db.SaveChangesAsync(token).ConfigureAwait(false);

                foreach (ServiceRecord row in tableRows)
                {
                    db.ServiceRecords.Add(new ServiceRecord
                    {
                        ServiceTypeId = stored.ServiceTypeId,
                        ParentRecordId = stored.Id,
                        TableKey = row.TableKey,
                        Title = row.Title,
                        DataJson = row.DataJson,
                        SearchText = row.SearchText,
                        CreatedAt = stored.CreatedAt,
                    });
                }

                if (tableRows.Count > 0)
                {
                    await db.SaveChangesAsync(token).ConfigureAwait(false);
                }

                return stored;
            },
            cancellationToken);
    }

    public Task UpdateRecordAsync(
        ServiceRecord record,
        IReadOnlyList<ServiceRecord> tableRows,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        return _store.RunAsync(
            async (db, token) =>
            {
                ServiceRecord? existing = await db.ServiceRecords
                    .FirstOrDefaultAsync(r => r.Id == record.Id, token)
                    .ConfigureAwait(false);

                if (existing is null)
                {
                    return false;
                }

                existing.Title = record.Title;
                existing.DataJson = record.DataJson;
                existing.SearchText = record.SearchText;

                // Rows are replaced wholesale: the form shows the full set, so the
                // difference between what is on screen and what is in the store is
                // exactly the rows the user removed.
                List<ServiceRecord> oldRows = await db.ServiceRecords
                    .Where(r => r.ParentRecordId == existing.Id)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                db.ServiceRecords.RemoveRange(oldRows);

                foreach (ServiceRecord row in tableRows)
                {
                    db.ServiceRecords.Add(new ServiceRecord
                    {
                        ServiceTypeId = existing.ServiceTypeId,
                        ParentRecordId = existing.Id,
                        TableKey = row.TableKey,
                        Title = row.Title,
                        DataJson = row.DataJson,
                        SearchText = row.SearchText,
                        CreatedAt = existing.CreatedAt,
                    });
                }

                await db.SaveChangesAsync(token).ConfigureAwait(false);
                return true;
            },
            cancellationToken);
    }

    public async Task DeleteRecordAsync(int id, CancellationToken cancellationToken = default)
        => await _store.RunAsync(
            async (db, token) =>
            {
                // Children first, by hand: ParentRecordId is a plain column with no
                // cascade behind it, so the store owns the order.
                List<ServiceRecord> rows = await db.ServiceRecords
                    .Where(r => r.ParentRecordId == id)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                db.ServiceRecords.RemoveRange(rows);

                ServiceRecord? record = await db.ServiceRecords
                    .FirstOrDefaultAsync(r => r.Id == id, token)
                    .ConfigureAwait(false);

                if (record is not null)
                {
                    db.ServiceRecords.Remove(record);
                }

                await db.SaveChangesAsync(token).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);

    public Task<IReadOnlyList<ServiceRecord>> GetTableRowsAsync(
        int recordId, CancellationToken cancellationToken = default)
        => _store.RunAsync<IReadOnlyList<ServiceRecord>>(
            async (db, token) => await db.ServiceRecords
                .AsNoTracking()
                .Where(r => r.ParentRecordId == recordId)
                .OrderBy(r => r.Id)
                .ToListAsync(token)
                .ConfigureAwait(false),
            cancellationToken);

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
