using System.ComponentModel;
using System.Threading.Tasks;

namespace DoNet.Contracts;

/// <summary>
/// A directory that can ask for a delete to be confirmed.
/// </summary>
/// <remarks>
/// One confirmation dialog serves every directory. The alternative was a second copy
/// of the same control with "person" swapped for "website", which is how two dialogs
/// that are supposed to look identical start to drift.
/// </remarks>
public interface IDeleteConfirmHost : INotifyPropertyChanged
{
    bool IsConfirmingDelete { get; }

    string ConfirmDeleteTitle { get; }

    string ConfirmDeleteBody { get; }

    void CancelDelete();

    Task ConfirmDeleteAsync();
}
