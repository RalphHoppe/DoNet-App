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
/// The person store. Queries only - the connection belongs to
/// <see cref="IEncryptedStore"/>, which every directory shares.
/// </summary>
public sealed class PersonDirectoryService : IPersonDirectory
{
    private readonly IEncryptedStore _store;

    public PersonDirectoryService(IEncryptedStore store)
    {
        _store = store;
    }

    public Task<IReadOnlyList<Person>> GetPageAsync(
        int skip, int take, string? search = null, CancellationToken cancellationToken = default)
        => _store.RunAsync<IReadOnlyList<Person>>(
            async (db, token) => await Filter(db.People.AsNoTracking(), search)
                .OrderByDescending(p => p.Id)
                .Skip(skip)
                .Take(take)
                .ToListAsync(token)
                .ConfigureAwait(false),
            cancellationToken);

    public Task<int> CountAsync(string? search = null, CancellationToken cancellationToken = default)
        => _store.RunAsync(
            async (db, token) => await Filter(db.People.AsNoTracking(), search)
                .CountAsync(token)
                .ConfigureAwait(false),
            cancellationToken);

    public Task<(IReadOnlyList<Person> Page, int Total)> GetPageWithTotalAsync(
        int skip, int take, string? search = null, CancellationToken cancellationToken = default)
        => _store.RunAsync<(IReadOnlyList<Person> Page, int Total)>(
            async (db, token) =>
            {
                IQueryable<Person> filtered = Filter(db.People.AsNoTracking(), search);

                int total = await filtered.CountAsync(token).ConfigureAwait(false);

                List<Person> page = await filtered
                    .OrderByDescending(p => p.Id)
                    .Skip(skip)
                    .Take(take)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                return ((IReadOnlyList<Person>)page, total);
            },
            cancellationToken);

    public Task<Person> AddAsync(Person person, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(person);

        return _store.RunAsync(
            async (db, token) =>
            {
                Person stored = person.Clone();
                stored.Id = 0;                          // the database assigns it
                stored.CreatedAt = DateTimeOffset.Now;

                db.People.Add(stored);
                await db.SaveChangesAsync(token).ConfigureAwait(false);

                return stored;
            },
            cancellationToken);
    }

    public async Task UpdateAsync(Person person, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(person);

        await _store.RunAsync(
            async (db, token) =>
            {
                Person? existing = await db.People
                    .FirstOrDefaultAsync(p => p.Id == person.Id, token)
                    .ConfigureAwait(false);

                if (existing is null)
                {
                    return false;
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

                await db.SaveChangesAsync(token).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
        => await _store.RunAsync(
            async (db, token) =>
            {
                Person? existing = await db.People
                    .FirstOrDefaultAsync(p => p.Id == id, token)
                    .ConfigureAwait(false);

                if (existing is not null)
                {
                    db.People.Remove(existing);
                    await db.SaveChangesAsync(token).ConfigureAwait(false);
                }

                return true;
            },
            cancellationToken).ConfigureAwait(false);

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
        string pattern = SearchPattern.For(term);
        bool numeric = int.TryParse(term, out int id);

        return query.Where(p =>
            (numeric && p.Id == id)
            || EF.Functions.Like(p.FirstName, pattern, SearchPattern.Escape)
            || EF.Functions.Like(p.LastName, pattern, SearchPattern.Escape)
            || EF.Functions.Like(p.Email, pattern, SearchPattern.Escape)
            || EF.Functions.Like(p.Country, pattern, SearchPattern.Escape)
            || EF.Functions.Like(p.City, pattern, SearchPattern.Escape)
            || EF.Functions.Like(p.State, pattern, SearchPattern.Escape)
            || EF.Functions.Like(p.PhoneNumber, pattern, SearchPattern.Escape)
            || EF.Functions.Like(p.Note, pattern, SearchPattern.Escape));
    }
}
