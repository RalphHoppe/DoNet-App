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

        AvatarText.Text = person.Initial;
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

    private void OnMenuOpened(object? sender, object args)
    {
        if (Resources["MenuIntro"] is Storyboard intro)
        {
            intro.Begin();
        }
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        if (Person is { } person)
        {
            OpenRequested?.Invoke(this, person);
        }
    }

    private void OnEditClick(object sender, RoutedEventArgs args)
    {
        MenuFlyout.Hide();

        if (Person is { } person)
        {
            EditRequested?.Invoke(this, person);
        }
    }

    private void OnDeleteClick(object sender, RoutedEventArgs args)
    {
        MenuFlyout.Hide();

        if (Person is { } person)
        {
            DeleteRequested?.Invoke(this, person);
        }
    }
}
