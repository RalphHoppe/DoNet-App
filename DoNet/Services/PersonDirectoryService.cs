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
/// The person store, backed by an encrypted SQLite database.
/// </summary>
/// <remarks>
/// Every record lives in a SQLCipher-encrypted file keyed by the vault's data key, so
/// the records are readable only while the app is unlocked. Locking drops the key and
/// the next read fails, which is the behaviour the lock button is supposed to buy.
/// </remarks>
public sealed class PersonDirectoryService : IPersonDirectory
{
    private readonly IVaultService _vault;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private bool _schemaReady;

    public PersonDirectoryService(IVaultService vault)
    {
        _vault = vault;
    }

    private DoNetDbContext Open() => new(AppPaths.DatabasePath, _vault.DatabaseKey);

    public async Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        // Called from the welcome screen, which only runs after a successful unlock.
        // If the key is not there yet, there is nothing useful to do and failing here
        // would turn a timing detail into a visible error.
        if (!_vault.IsUnlocked)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_schemaReady)
            {
                return;
            }

            await using DoNetDbContext db = Open();

            // EnsureCreated rather than migrations: there is one schema version so far,
            // and migrations would add a toolchain for a problem that does not exist
            // yet. This is the line that changes when the schema first evolves.
            await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

            _schemaReady = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Drops the cached schema flag so a re-unlock re-opens the file.</summary>
    public void Reset() => _schemaReady = false;

    public async Task<IReadOnlyList<Person>> GetPageAsync(
        int skip, int take, string? search = null, CancellationToken cancellationToken = default)
    {
        await WarmUpAsync(cancellationToken).ConfigureAwait(false);

        await using DoNetDbContext db = Open();

        return await Filter(db.People.AsNoTracking(), search)
            .OrderByDescending(p => p.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> CountAsync(
        string? search = null, CancellationToken cancellationToken = default)
    {
        await WarmUpAsync(cancellationToken).ConfigureAwait(false);

        await using DoNetDbContext db = Open();

        return await Filter(db.People.AsNoTracking(), search)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Person> AddAsync(
        Person person, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(person);

        await WarmUpAsync(cancellationToken).ConfigureAwait(false);

        await using DoNetDbContext db = Open();

        Person stored = person.Clone();
        stored.Id = 0;                          // the database assigns it
        stored.CreatedAt = DateTimeOffset.Now;

        db.People.Add(stored);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return stored;
    }

    public async Task UpdateAsync(Person person, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(person);

        await using DoNetDbContext db = Open();

        Person? existing = await db.People
            .FirstOrDefaultAsync(p => p.Id == person.Id, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            return;
        }

        // Id and CreatedAt belong to the store, not to the form that edited it.
        existing.FirstName = person.FirstName;
        existing.LastName = person.LastName;
        existing.Gender = person.Gender;
        existing.DateOfBirth = person.DateOfBirth;
        existing.Country = person.Country;
        existing.State = person.State;
        existing.City = person.City;
        existing.Street = person.Street;
        existing.PostalCode = person.PostalCode;
        existing.PhoneNumber = person.PhoneNumber;
        existing.Email = person.Email;
        existing.EmailPassword = person.EmailPassword;
        existing.RecoveryEmail = person.RecoveryEmail;
        existing.RecoveryPassword = person.RecoveryPassword;
        existing.RecoveryWords = person.RecoveryWords;
        existing.Note = person.Note;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using DoNetDbContext db = Open();

        Person? existing = await db.People
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            db.People.Remove(existing);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Applies the search box to a query.
    /// </summary>
    /// <remarks>
    /// LIKE rather than string.Contains: EF translates Contains to instr(), which is
    /// case sensitive, and a directory search that misses "ahmed" because the record
    /// says "Ahmed" is broken. SQLite's LIKE is case insensitive for ASCII.
    ///
    /// Secrets are not searched. A search box that matches password fields is a way to
    /// confirm a password by guessing at it one character at a time.
    /// </remarks>
    private static IQueryable<Person> Filter(IQueryable<Person> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return query;
        }

        string term = search.Trim();

        // % and _ are wildcards; a user typing them means the literal character.
        string escaped = term
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

        string pattern = $"%{escaped}%";

        bool numeric = int.TryParse(term, out int id);

        return query.Where(p =>
            (numeric && p.Id == id)
            || EF.Functions.Like(p.FirstName, pattern, "\\")
            || EF.Functions.Like(p.LastName, pattern, "\\")
            || EF.Functions.Like(p.Email, pattern, "\\")
            || EF.Functions.Like(p.Country, pattern, "\\")
            || EF.Functions.Like(p.City, pattern, "\\")
            || EF.Functions.Like(p.State, pattern, "\\")
            || EF.Functions.Like(p.PhoneNumber, pattern, "\\")
            || EF.Functions.Like(p.Note, pattern, "\\"));
    }
}
