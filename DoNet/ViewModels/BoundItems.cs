using System.Collections.ObjectModel;

namespace DoNet.ViewModels;

/// <summary>
/// Helpers for collections an <c>ItemsRepeater</c> is watching.
/// </summary>
internal static class BoundItems
{
    /// <summary>
    /// Empties <paramref name="items"/>, raising nothing when it is already empty.
    /// </summary>
    /// <remarks>
    /// <see cref="ObservableCollection{T}.Clear"/> raises a Reset whether or not it
    /// actually removed anything, and Reset is the one notification an ItemsRepeater
    /// cannot always service: if the repeater sits inside a collapsed ScrollViewer it
    /// has no measured viewport to work against, and the call comes back as
    /// <c>COMException: Unspecified error</c>. Clearing a list that is already empty
    /// is wasted work in every case and a crash in that one, so it is skipped.
    /// </remarks>
    internal static void ClearSafely<T>(this ObservableCollection<T> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        items.Clear();
    }
}
