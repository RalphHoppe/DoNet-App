using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DoNet.Models;

namespace DoNet.Contracts;

/// <summary>
/// The source of person records for the directory screen.
/// </summary>
/// <remarks>
/// This is the seam the encrypted data layer will slot into. The view model knows only
/// this interface, so replacing the placeholder implementation with EF Core over
/// SQLCipher is a container registration change and nothing else - no view rework.
///
/// The method is asynchronous and allowed to throw. That is what gives the screen its
/// loading and error states something real to represent: a database open is slow enough
/// to see, and a wrong key or a corrupt file is a genuine failure the user must be told
/// about rather than shown an empty page.
/// </remarks>
public interface IPersonDirectory
{
    /// <summary>
    /// Returns the cards for the directory, or an empty list when there are none.
    /// </summary>
    /// <exception cref="System.Exception">
    /// Any failure reaching the store. The view model turns this into the error state
    /// rather than letting it reach the dispatcher.
    /// </exception>
    Task<IReadOnlyList<PersonPreview>> GetPreviewsAsync(CancellationToken cancellationToken = default);
}
