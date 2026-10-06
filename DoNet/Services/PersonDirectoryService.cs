using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DoNet.Contracts;
using DoNet.Models;

namespace DoNet.Services;

/// <summary>
/// An in-memory person store.
/// </summary>
/// <remarks>
/// Real behaviour, not placeholder content: adding, editing and deleting all genuinely
/// work and the screen reflects exactly what is here. What it does not yet do is
/// survive a restart - that arrives with the encrypted database, which replaces this
/// one class and nothing else.
///
/// The semaphore is not ceremony. Warm-up runs from the welcome screen while the user
/// is still watching an animation, and the directory can start loading its first page
/// before that has finished, so two callers really can be inside this object at once.
/// </remarks>
public sealed class PersonDirectoryService : IPersonDirectory
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<Person> _items = new();

    private int _nextId = 1;

    /// <summary>
    /// Stands in for the cost of opening an encrypted file. Keeps the loading state
    /// honest: without it the screen would flash through a state that will exist in
    /// earnest later, and it would never get looked at.
    /// </summary>
    private static readonly TimeSpan OpenLatency = TimeSpan.FromMilliseconds(420);

    private bool _opened;

    public async Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_opened)
            {
                return;
            }

            await Task.Delay(OpenLatency, cancellationToken).ConfigureAwait(false);
            _opened = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<Person>> GetPageAsync(
        int skip, int take, string? search = null, CancellationToken cancellationToken = default)
    {
        await WarmUpAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return Query(search).Skip(skip).Take(take).Select(p => p.Clone()).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> CountAsync(
        string? search = null, CancellationToken cancellationToken = default)
    {
        await WarmUpAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return Query(search).Count();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Person> AddAsync(
        Person person, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(person);

        await WarmUpAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Person stored = person.Clone();
            stored.Id = _nextId++;
            stored.CreatedAt = DateTimeOffset.Now;
            _items.Add(stored);
            return stored.Clone();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateAsync(Person person, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(person);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            int index = _items.FindIndex(p => p.Id == person.Id);
            if (index >= 0)
            {
                // Id and CreatedAt belong to the store, not to the form that edited it.
                Person updated = person.Clone();
                updated.Id = _items[index].Id;
                updated.CreatedAt = _items[index].CreatedAt;
                _items[index] = updated;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _items.RemoveAll(p => p.Id == id);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Newest first, which is what a directory of recent additions wants.</summary>
    private IEnumerable<Person> Query(string? search)
    {
        IEnumerable<Person> source = _items.OrderByDescending(p => p.Id);

        return string.IsNullOrWhiteSpace(search)
            ? source
            : source.Where(p => p.Matches(search.Trim()));
    }
}
