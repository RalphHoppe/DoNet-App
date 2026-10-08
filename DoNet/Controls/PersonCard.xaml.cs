using System;
using DoNet.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;

namespace DoNet.Controls;

/// <summary>
/// One person in the directory grid.
/// </summary>
public sealed partial class PersonCard : UserControl
{
    private const string Dash = "\u2014";

    public PersonCard()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty PersonProperty = DependencyProperty.Register(
        nameof(Person), typeof(Person), typeof(PersonCard),
        new PropertyMetadata(null, OnPersonChanged));

    public Person? Person
    {
        get => (Person?)GetValue(PersonProperty);
        set => SetValue(PersonProperty, value);
    }

    /// <summary>Double-click: open the full record.</summary>
    public event EventHandler<Person>? OpenRequested;

    public event EventHandler<Person>? EditRequested;

    public event EventHandler<Person>? DeleteRequested;

    private static void OnPersonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((PersonCard)d).Apply();

    /// <summary>
    /// Pushes the record into the card.
    /// </summary>
    /// <remarks>
    /// Assigned in code rather than by binding because <see cref="Person"/> is a plain
    /// mutable class with no change notification - it is edited through a copy and
    /// committed, so the card is told to refresh rather than watching each property.
    /// </remarks>
    private void Apply()
    {
        Person? person = Person;

        if (person is null)
        {
            return;
        }

        // The record number, not an initial. The approved design puts the id in the
        // disc; it is the one field that is always present and always unique.
        AvatarText.Text = Initial.From(person.FirstName, person.LastName);
        NameText.Text = person.DisplayName;

        IdValue.Text = person.Id > 0 ? person.Id.ToString() : Dash;
        FirstNameValue.Text = Or(person.FirstName);
        LastNameValue.Text = Or(person.LastName);
        GenderValue.Text = Or(person.Gender);
        DobValue.Text = Or(person.DateOfBirth);
        CountryValue.Text = Or(person.Country);
        EmailValue.Text = Or(person.Email);
        CreatedValue.Text = Or(person.CreatedAtDisplay);
        NoteValue.Text = Or(person.Note);

        static string Or(string value) => string.IsNullOrWhiteSpace(value) ? Dash : value;
    }

    private bool _menuOpen;

    /// <summary>
    /// Opens or closes the drawer.
    /// </summary>
    /// <remarks>
    /// The items are hit-test invisible while closed. They are still in the tree with
    /// zero opacity, and without this an invisible Delete would sit over the card's
    /// first field, catching clicks aimed at the record.
    /// </remarks>
    private void SetMenu(bool open)
    {
        if (_menuOpen == open)
        {
            return;
        }

        _menuOpen = open;

        EditItem.IsHitTestVisible = open;
        DeleteItem.IsHitTestVisible = open;

        if (Resources[open ? "MenuOpen" : "MenuClose"] is Storyboard board)
        {
            board.Begin();
        }
    }

    private void OnMenuToggle(object sender, RoutedEventArgs args) => SetMenu(!_menuOpen);

    /// <summary>
    /// Light dismiss. A drawer attached to the card closes when the pointer leaves the
    /// card, which is the gesture people already make when they change their mind.
    /// </summary>
    private void OnCardPointerExited(object sender, PointerRoutedEventArgs args)
        => SetMenu(false);

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        SetMenu(false);

        if (Person is { } person)
        {
            OpenRequested?.Invoke(this, person);
        }
    }

    private void OnEditClick(object sender, RoutedEventArgs args)
    {
        SetMenu(false);

        if (Person is { } person)
        {
            EditRequested?.Invoke(this, person);
        }
    }

    private void OnDeleteClick(object sender, RoutedEventArgs args)
    {
        SetMenu(false);

        if (Person is { } person)
        {
            DeleteRequested?.Invoke(this, person);
        }
    }
}
