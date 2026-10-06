using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DoNet.Contracts;
using DoNet.Models;

namespace DoNet.Services;

/// <summary>
/// How the placeholder directory should behave. Temporary - delete with this class once
/// the encrypted store is real.
/// </summary>
public enum DirectoryPreviewMode
{
    /// <summary>Four blank cards, matching the supplied design.</summary>
    Loaded,

    /// <summary>No records, so the empty state is shown.</summary>
    Empty,

    /// <summary>Throws, so the error state is shown.</summary>
    Error,
}

/// <summary>
/// Stands in for the encrypted store until it is built.
/// </summary>
/// <remarks>
/// Returns the four blank cards the design specifies - the screen is explicitly labelled
/// "Static placeholder values only", so this is the designed content rather than fake
/// data standing in for something richer.
///
/// <see cref="Mode"/> exists because the empty and error states would otherwise be
/// unreachable: a service that always succeeds gives you no way to look at them. Change
/// the one line in <c>App.xaml.cs</c> to view those states. The real implementation will
/// reach them on its own and this property disappears with the class.
/// </remarks>
public sealed class PersonDirectoryService : IPersonDirectory
{
    /// <summary>Number of placeholder cards, per the design's 2x2 grid.</summary>
    private const int PlaceholderCount = 4;

    /// <summary>
    /// Long enough that the loading state is visible rather than a flicker, short enough
    /// not to feel broken. A real encrypted open lands in roughly this range.
    /// </summary>
    private static readonly TimeSpan SimulatedLatency = TimeSpan.FromMilliseconds(450);

    public DirectoryPreviewMode Mode { get; set; } = DirectoryPreviewMode.Loaded;

    public async Task<IReadOnlyList<PersonPreview>> GetPreviewsAsync(
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(SimulatedLatency, cancellationToken).ConfigureAwait(false);

        return Mode switch
        {
            DirectoryPreviewMode.Empty => Array.Empty<PersonPreview>(),

            DirectoryPreviewMode.Error => throw new InvalidOperationException(
                "The placeholder directory is set to simulate a failure."),

            _ => Enumerable.Range(0, PlaceholderCount)
                           .Select(_ => PersonPreview.Placeholder())
                           .ToArray(),
        };
    }
}
