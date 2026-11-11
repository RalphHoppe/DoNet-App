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

                List<string> hidden = await db.HiddenPaymentMethods
                    .AsNoTracking()
                    .Select(h => h.Name)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                IReadOnlyList<string> offered = PaymentMethodCatalog.Merge(saved);

                // The built-ins are in code and always offered, so a deleted one comes
                // back unless its hiding is remembered here.
                return hidden.Count == 0
                    ? offered
                    : offered.Where(m => !hidden.Any(h => PaymentMethodCatalog.Matches(h, m))).ToList();
            },
            cancellationToken);

    public Task<int> CountWebsitesUsingAsync(
        IReadOnlyList<string> methods, CancellationToken cancellationToken = default)
        => _store.RunAsync(
            async (db, token) =>
            {
                string[] wanted = NormalisedNames(methods);
                if (wanted.Length == 0)
                {
                    return 0;
                }

                // The methods are one delimited column, so SQL can only pre-filter;
                // the exact test - split the column, compare the tokens - happens
                // here. What is filtered out by the LIKE never reaches the loop.
                List<string> raws = await db.Websites
                    .AsNoTracking()
                    .Where(w => w.PaymentMethodsRaw != string.Empty)
                    .Select(w => w.PaymentMethodsRaw)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                int count = 0;

                foreach (string raw in raws)
                {
                    if (raw.Split(Website.MethodSeparator, StringSplitOptions.RemoveEmptyEntries)
                        .Any(part => wanted.Contains(part, StringComparer.OrdinalIgnoreCase)))
                    {
                        count++;
                    }
                }

                return count;
            },
            cancellationToken);

    public Task RenamePaymentMethodAsync(
        string oldName, string newName, CancellationToken cancellationToken = default)
    {
        string oldMethod = oldName?.Trim() ?? string.Empty;
        string newMethod = newName?.Trim() ?? string.Empty;

        if (oldMethod.Length == 0 || newMethod.Length == 0
            || PaymentMethodCatalog.Matches(oldMethod, newMethod))
        {
            return Task.CompletedTask;
        }

        return _store.RunAsync(
            async (db, token) =>
            {
                await RewriteRecordsAsync(
                    db,
                    raw => RenameTokens(raw, oldMethod, newMethod),
                    token).ConfigureAwait(false);

                List<PaymentMethodOption> options = await db.PaymentMethodOptions
                    .ToListAsync(token).ConfigureAwait(false);
                List<HiddenPaymentMethod> hidden = await db.HiddenPaymentMethods
                    .ToListAsync(token).ConfigureAwait(false);

                bool oldIsBuiltIn = PaymentMethodCatalog.IsBuiltIn(oldMethod);
                bool newIsBuiltIn = PaymentMethodCatalog.IsBuiltIn(newMethod);

                // The new name is offered from now on, unless it is a built-in - the
                // catalog always offers those, and the line below revives this one if
                // it had been deleted.
                if (!newIsBuiltIn)
                {
                    PaymentMethodOption? row = options.FirstOrDefault(
                        o => PaymentMethodCatalog.Matches(o.Name, newMethod));

                    if (row is null)
                    {
                        db.PaymentMethodOptions.Add(new PaymentMethodOption { Name = newMethod });
                    }
                }

                // Naming a method after a deleted built-in brings that built-in back.
                HiddenPaymentMethod? revived = hidden.FirstOrDefault(
                    h => PaymentMethodCatalog.Matches(h.Name, newMethod));
                if (revived is not null)
                {
                    db.HiddenPaymentMethods.Remove(revived);
                }

                // Renaming a built-in off its shipped name hides the shipped name,
                // exactly as deleting it would.
                if (oldIsBuiltIn && hidden.All(h => !PaymentMethodCatalog.Matches(h.Name, oldMethod)))
                {
                    db.HiddenPaymentMethods.Add(new HiddenPaymentMethod { Name = oldMethod });
                }
                else if (!oldIsBuiltIn)
                {
                    PaymentMethodOption? oldRow = options.FirstOrDefault(
                        o => PaymentMethodCatalog.Matches(o.Name, oldMethod));

                    if (oldRow is not null)
                    {
                        // Another row may already offer the new name, in which case
                        // keeping both would list it twice.
                        bool covered = newIsBuiltIn || options.Any(
                            o => o != oldRow && PaymentMethodCatalog.Matches(o.Name, newMethod));

                        if (covered)
                        {
                            db.PaymentMethodOptions.Remove(oldRow);
                        }
                        else
                        {
                            oldRow.Name = newMethod;
                        }
                    }
                }

                await db.SaveChangesAsync(token).ConfigureAwait(false);
                return true;
            },
            cancellationToken);
    }

    public Task DeletePaymentMethodsAsync(
        IReadOnlyList<string> names, CancellationToken cancellationToken = default)
    {
        string[] wanted = NormalisedNames(names);

        if (wanted.Length == 0)
        {
            return Task.CompletedTask;
        }

        return _store.RunAsync(
            async (db, token) =>
            {
                await RewriteRecordsAsync(
                    db,
                    raw => string.Join(
                        Website.MethodSeparator,
                        raw.Split(Website.MethodSeparator, StringSplitOptions.RemoveEmptyEntries)
                            .Where(part => !wanted.Contains(part, StringComparer.OrdinalIgnoreCase))),
                    token).ConfigureAwait(false);

                List<PaymentMethodOption> options = await db.PaymentMethodOptions
                    .ToListAsync(token).ConfigureAwait(false);
                List<HiddenPaymentMethod> hidden = await db.HiddenPaymentMethods
                    .ToListAsync(token).ConfigureAwait(false);

                foreach (PaymentMethodOption row in options.Where(
                             o => wanted.Contains(o.Name, StringComparer.OrdinalIgnoreCase)).ToList())
                {
                    db.PaymentMethodOptions.Remove(row);
                }

                // A built-in cannot be removed from the code it lives in; deleting it
                // means hiding it from every catalog read from now on.
                foreach (string name in wanted.Where(PaymentMethodCatalog.IsBuiltIn))
                {
                    if (hidden.All(h => !PaymentMethodCatalog.Matches(h.Name, name)))
                    {
                        db.HiddenPaymentMethods.Add(new HiddenPaymentMethod { Name = name });
                    }
                }

                await db.SaveChangesAsync(token).ConfigureAwait(false);
                return true;
            },
            cancellationToken);
    }

    /// <summary>
    /// Applies a rewrite to every website whose method column changes under it, in
    /// one save.
    /// </summary>
    /// <remarks>
    /// The rewriter is given the raw column and returns the new one; a result that
    /// equals the input leaves that record untouched, which keeps the change tracker
    /// from writing rows that did not change.
    /// </remarks>
    private static async Task RewriteRecordsAsync(
        DoNetDbContext db,
        Func<string, string> rewrite,
        CancellationToken token)
    {
        List<Website> rows = await db.Websites
            .Where(w => w.PaymentMethodsRaw != string.Empty)
            .ToListAsync(token).ConfigureAwait(false);

        foreach (Website row in rows)
        {
            string rewritten = rewrite(row.PaymentMethodsRaw);

            if (!string.Equals(rewritten, row.PaymentMethodsRaw, StringComparison.Ordinal))
            {
                row.PaymentMethodsRaw = rewritten;
            }
        }
    }

    /// <summary>
    /// Replaces one method name inside a raw column, without ever producing a
    /// duplicate of the name it was replaced with.
    /// </summary>
    private static string RenameTokens(string raw, string oldMethod, string newMethod)
    {
        string[] parts = raw.Split(Website.MethodSeparator, StringSplitOptions.RemoveEmptyEntries);

        List<string> rebuilt = new(parts.Length);

        foreach (string part in parts)
        {
            if (PaymentMethodCatalog.Matches(part, oldMethod))
            {
                // The same record may already list the new name - "PayPal" being
                // renamed to a second "PayPal" row, essentially - and keeping both
                // would offer the method twice on that record.
                if (!rebuilt.Any(p => PaymentMethodCatalog.Matches(p, newMethod)))
                {
                    rebuilt.Add(newMethod);
                }
            }
            else
            {
                rebuilt.Add(part);
            }
        }

        return string.Join(Website.MethodSeparator, rebuilt);
    }

    /// <summary>Trimmed, non-blank, case-insensitively distinct.</summary>
    private static string[] NormalisedNames(IReadOnlyList<string>? names)
        => (names ?? Array.Empty<string>())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();

    public async Task RememberPaymentMethodAsync(
        string method, CancellationToken cancellationToken = default)
    {
        string trimmed = method?.Trim() ?? string.Empty;

        // Nothing to remember, and the six built-ins are already always offered.
        if (trimmed.Length == 0)
        {
            return;
        }

        // A built-in that was deleted is being asked for by name again. Its hidden
        // row is the only thing standing between it and being offered, so remembering
        // it means removing that row - otherwise the method would be ticked, saved on
        // records, and then quietly gone from the list at the next catalog read.
        if (PaymentMethodCatalog.IsBuiltIn(trimmed))
        {
            await _store.RunAsync(
                async (db, token) =>
                {
                    List<HiddenPaymentMethod> hidden = await db.HiddenPaymentMethods
                        .ToListAsync(token).ConfigureAwait(false);

                    HiddenPaymentMethod? match = hidden.FirstOrDefault(
                        h => PaymentMethodCatalog.Matches(h.Name, trimmed));

                    if (match is not null)
                    {
                        db.HiddenPaymentMethods.Remove(match);
                        await db.SaveChangesAsync(token).ConfigureAwait(false);
                    }

                    return true;
                },
                cancellationToken).ConfigureAwait(false);

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
