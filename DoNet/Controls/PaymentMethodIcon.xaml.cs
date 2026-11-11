using System;
using DoNet.Models;
using DoNet.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DoNet.Controls;

/// <summary>
/// The brand mark for a payment method.
/// </summary>
/// <remarks>
/// Set <see cref="Method"/> to the method's name; the control resolves it to one of
/// the six built-in marks, or to a neutral card outline for anything the user added
/// themselves. Resolution is case-insensitive, so a hand-typed "paypal" still gets the
/// PayPal mark.
///
/// Only the resolved mark is built. Each drawing is deferred and realised through
/// FindName, because these appear in quantity - six on a preview panel, twelve in the
/// add dialog's option list and chips - and building all seven in each would be most
/// of a thousand elements to show a handful of small logos.
/// </remarks>
public sealed partial class PaymentMethodIcon : UserControl
{
    public static readonly DependencyProperty MethodProperty = DependencyProperty.Register(
        nameof(Method),
        typeof(string),
        typeof(PaymentMethodIcon),
        new PropertyMetadata(string.Empty, OnMethodChanged));

    private FrameworkElement? _shown;
    private bool _ready;

    public PaymentMethodIcon()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            // Deferred children cannot be realised until the control is in the tree.
            _ready = true;
            Refresh();
        };
    }

    /// <summary>The payment method's name, as stored on the record.</summary>
    public string Method
    {
        get => (string)GetValue(MethodProperty);
        set => SetValue(MethodProperty, value);
    }

    private static void OnMethodChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((PaymentMethodIcon)sender).Refresh();

    private void Refresh()
    {
        if (!_ready)
        {
            return;
        }

        string name = PaymentMethodCatalog.IconKey(Method) is { } key
            ? key + "Mark"
            : "GenericMark";

        try
        {
            if (FindName(name) is not FrameworkElement mark)
            {
                return;
            }

            if (ReferenceEquals(mark, _shown))
            {
                return;
            }

            // A method can be renamed in place while editing, so the previous mark has
            // to go rather than stack underneath the new one.
            if (_shown is not null)
            {
                _shown.Visibility = Visibility.Collapsed;
            }

            mark.Visibility = Visibility.Visible;
            _shown = mark;
        }
        catch (Exception error)
        {
            AppLog.Error($"Could not show the '{Method}' payment mark", error);
        }
    }
}
