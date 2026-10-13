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

/// <summary>
/// The VPS record store, over the shared encrypted connection.
/// </summary>
/// <remarks>
/// The same shape as the other directories: paged reads with a search total, and
/// writes that copy from the form's objects rather than attaching them. The
/// differences are the VPS's own: every read carries the 1:1 system row, node
/// names are filled from a name map because the card shows them, and a delete
/// reparents children to null before it removes the row.
///</remarks>
public sealed class VpsDirectoryService : IVpsDirectory
{
    private readonly IEncryptedStore _store;

    public VpsDirectoryService(IEncryptedStore store)
    {
        _store = store;
    }

    /// <inheritdoc />
    public Task<(IReadOnlyList<VpsRecord> Page, int Total)> GetPageWithTotalAsync(
        int skip, int take, string? search = null, CancellationToken cancellationToken = default)
        => _store.RunAsync<(IReadOnlyList<VpsRecord> Page, int Total)>(
            async (db, token) =>
            {
                IQueryable<VpsRecord> filtered = Filter(
                    db.VpsRecords.AsNoTracking().Include(v => v.System), search);

                int total = await filtered.CountAsync(token).ConfigureAwait(false);

                List<VpsRecord> page = await filtered
                    .OrderByDescending(v => v.Id)
                    .Skip(skip)
                    .Take(take)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                await FillNodeNamesAsync(db, page, token).ConfigureAwait(false);

                return ((IReadOnlyList<VpsRecord>)page, total);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<VpsRecord>> GetAllAsync(
        CancellationToken cancellationToken = default)
        => _store.RunAsync<IReadOnlyList<VpsRecord>>(
            async (db, token) =>
            {
                List<VpsRecord> all = await db.VpsRecords
                    .AsNoTracking()
                    .OrderByDescending(v => v.Id)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                await FillNodeNamesAsync(db, all, token).ConfigureAwait(false);

                return (IReadOnlyList<VpsRecord>)all;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<VpsRecord> AddAsync(
        VpsRecord record, VpsSystemInfo system, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(system);

        return _store.RunAsync(
            async (db, token) =>
            {
                VpsRecord stored = Clone(record);
                stored.Id = 0;                          // the database assigns it

                // "This is the main VPS" is stored as no parent, per the model.
                stored.NodeVpsId = NormalizeNode(stored.NodeVpsId);

                VpsSystemInfo storedSystem = Clone(system);
                storedSystem.Id = 0;
                storedSystem.VpsId = 0;

                stored.System = storedSystem;
                db.VpsRecords.Add(stored);
                await db.SaveChangesAsync(token).ConfigureAwait(false);

                return stored;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(
        VpsRecord record, VpsSystemInfo system, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(system);

        await _store.RunAsync(
            async (db, token) =>
            {
                VpsRecord? existing = await db.VpsRecords
                    .Include(v => v.System)
                    .FirstOrDefaultAsync(v => v.Id == record.Id, token)
                    .ConfigureAwait(false);

                if (existing is null)
                {
                    return false;
                }

                CopyFields(record, existing);
                existing.NodeVpsId = NormalizeNode(record.NodeVpsId);

                // The system row is created on the first save that has one and
                // updated on every save after; a 1:1 that arrives late is still
                // exactly one row, which is what the unique index will hold.
                if (existing.System is { } target)
                {
                    CopyFields(system, target);
                }
                else
                {
                    VpsSystemInfo fresh = Clone(system);
                    fresh.Id = 0;
                    fresh.VpsId = existing.Id;
                    existing.System = fresh;
                }

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
                // Children of this VPS become their own main first: a dangling
                // node id would read as a broken record on every card after.
                List<VpsRecord> children = await db.VpsRecords
                    .Where(v => v.NodeVpsId == id)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                foreach (VpsRecord child in children)
                {
                    child.NodeVpsId = null;
                }

                VpsRecord? doomed = await db.VpsRecords
                    .FirstOrDefaultAsync(v => v.Id == id, token)
                    .ConfigureAwait(false);

                if (doomed is not null)
                {
                    db.VpsRecords.Remove(doomed);   // the system row cascades
                }

                await db.SaveChangesAsync(token).ConfigureAwait(false);
            },
            cancellationToken);

    /// <summary>Search over the fields a VPS is found by, including its system's.</summary>
    private static IQueryable<VpsRecord> Filter(IQueryable<VpsRecord> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return query;
        }

        string term = search.Trim();
        string pattern = SearchPattern.For(term);
        bool numeric = int.TryParse(term, out int id);

        return query.Where(v =>
            (numeric && v.Id == id)
            || EF.Functions.Like(v.Name, pattern, SearchPattern.Escape)
            || EF.Functions.Like(v.Country, pattern, SearchPattern.Escape)
            || EF.Functions.Like(v.Note, pattern, SearchPattern.Escape)
            || (v.System != null
                && (EF.Functions.Like(v.System.Os, pattern, SearchPattern.Escape)
                    || EF.Functions.Like(v.System.Ip, pattern, SearchPattern.Escape)
                    || EF.Functions.Like(v.System.Username, pattern, SearchPattern.Escape))));
    }

    /// <summary>
    /// Writes each record's parent name from a single id-to-name read, so a page
    /// of cards costs one query for the map instead of one per card.
    /// </summary>
    private static async Task FillNodeNamesAsync(
        DoNetDbContext db, IReadOnlyList<VpsRecord> records, CancellationToken token)
    {
        List<VpsRecord> named = records.Where(v => !v.IsMain).ToList();
        if (named.Count == 0)
        {
            return;
        }

        List<int> ids = named.Select(v => v.NodeVpsId!.Value).Distinct().ToList();
        Dictionary<int, string> names = await db.VpsRecords
            .AsNoTracking()
            .Where(v => ids.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.Name, token)
            .ConfigureAwait(false);

        foreach (VpsRecord record in named)
        {
            record.NodeName = names.TryGetValue(record.NodeVpsId!.Value, out string? name)
                ? name
                : string.Empty;
        }
    }

    /// <summary>
    /// The form's "main" choice arrives as a null parent already; this keeps the
    /// store honest if a stale self-reference ever reaches it.
    /// </summary>
    private static int? NormalizeNode(int? node) => node is { } value ? value : null;

    private static VpsRecord Clone(VpsRecord record) => new()
    {
        PersonId = record.PersonId,
        WebsiteId = record.WebsiteId,
        AccountId = record.AccountId,
        Name = record.Name,
        Balance = record.Balance,
        Currency = record.Currency,
        BalanceBtc = record.BalanceBtc,
        ExchangeRate = record.ExchangeRate,
        CreatedAt = record.CreatedAt,
        OutDate = record.OutDate,
        CycleValue = record.CycleValue,
        CycleUnit = record.CycleUnit,
        NodeVpsId = record.NodeVpsId,
        Country = record.Country,
        Note = record.Note,
    };

    /// <summary>Id, VpsId and the relation belong to the store, not to the form.</summary>
    private static void CopyFields(VpsRecord from, VpsRecord to)
    {
        to.PersonId = from.PersonId;
        to.WebsiteId = from.WebsiteId;
        to.AccountId = from.AccountId;
        to.Name = from.Name;
        to.Balance = from.Balance;
        to.Currency = from.Currency;
        to.BalanceBtc = from.BalanceBtc;
        to.ExchangeRate = from.ExchangeRate;
        to.CreatedAt = from.CreatedAt;
        to.OutDate = from.OutDate;
        to.CycleValue = from.CycleValue;
        to.CycleUnit = from.CycleUnit;
        to.Country = from.Country;
        to.Note = from.Note;
    }

    private static void CopyFields(VpsSystemInfo from, VpsSystemInfo to)
    {
        to.Os = from.Os;
        to.Ip = from.Ip;
        to.Port = from.Port;
        to.PortNumber = from.PortNumber;
        to.Username = from.Username;
        to.UserPassword = from.UserPassword;
        to.RootUser = from.RootUser;
        to.RootPassword = from.RootPassword;
        to.Cpu = from.Cpu;
        to.Memory = from.Memory;
        to.Disk = from.Disk;
        to.Bandwidth = from.Bandwidth;
        to.BandwidthSpeed = from.BandwidthSpeed;
        to.UploadSpeed = from.UploadSpeed;
        to.DownloadSpeed = from.DownloadSpeed;
        to.PingTime = from.PingTime;
        to.Note = from.Note;
    }

    private static VpsSystemInfo Clone(VpsSystemInfo system) => new()
    {
        VpsId = system.VpsId,
        Os = system.Os,
        Ip = system.Ip,
        Port = system.Port,
        PortNumber = system.PortNumber,
        Username = system.Username,
        UserPassword = system.UserPassword,
        RootUser = system.RootUser,
        RootPassword = system.RootPassword,
        Cpu = system.Cpu,
        Memory = system.Memory,
        Disk = system.Disk,
        Bandwidth = system.Bandwidth,
        BandwidthSpeed = system.BandwidthSpeed,
        UploadSpeed = system.UploadSpeed,
        DownloadSpeed = system.DownloadSpeed,
        PingTime = system.PingTime,
        Note = system.Note,
    };
}
