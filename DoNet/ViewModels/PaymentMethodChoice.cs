using CommunityToolkit.Mvvm.ComponentModel;
using DoNet.Models;

namespace DoNet.ViewModels;

/// <summary>
/// One tick box in the add and edit forms' payment-method list.
/// </summary>
/// <remarks>
/// Observable because the chips below the list and the ticks in it are two views of
/// the same selection - unticking a box has to remove its chip, and dismissing a chip
/// has to clear its box.
/// </remarks>
public sealed partial class PaymentMethodChoice : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public PaymentMethodChoice(string name, bool isSelected)
    {
        Name = name;
        _isSelected = isSelected;
    }

    public string Name { get; }

    /// <summary>True for the six that ship with the app, which have brand marks.</summary>
    public bool IsBuiltIn => PaymentMethodCatalog.IsBuiltIn(Name);
}
