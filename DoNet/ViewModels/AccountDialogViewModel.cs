using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using DoNet.Controls;
using DoNet.Models;

namespace DoNet.ViewModels;

/// <summary>Which of the three jobs the one account dialog is doing.</summary>
public enum AccountDialogMode
{
    Preview,
    Add,
    Edit,
}

/// <summary>
/// Backs the account dialog in all three of its modes.
/// </summary>
/// <remarks>
/// One dialog rather than three, for the same reason the other two are one: the modes
/// differ in which controls are editable and what the button says, not in what they
/// contain.
///
/// The website is the first field in the app that is a reference rather than a value.
/// The picker works in labels because that is what a combo box shows, so this holds
/// the label the user sees and the id it stands for side by side, and resolves one to
/// the other on save.
/// </remarks>
public sealed partial class AccountDialogViewModel : ObservableObject
{
    private Account? _original;

    /// <summary>Label to id, rebuilt whenever the form is filled.</summary>
    private readonly Dictionary<string, int> _websiteIds =
        new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    [NotifyPropertyChangedFor(nameof(PrimaryActionText))]
    [NotifyPropertyChangedFor(nameof(IsPreview))]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    [NotifyPropertyChangedFor(nameof(FieldKind))]
    private AccountDialogMode _mode = AccountDialogMode.Preview;

    [ObservableProperty] private string _websiteLabel = string.Empty;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _note = string.Empty;

    /// <summary>The labels the picker offers, in the order the store returned them.</summary>
    [ObservableProperty] private IReadOnlyList<string> _websiteOptions = Array.Empty<string>();

    public string Title => Mode switch
    {
        AccountDialogMode.Add => "ADD ACCOUNT",
        AccountDialogMode.Edit => "EDIT ACCOUNT",
        _ => "ACCOUNT INFO",
    };

    public string Subtitle => Mode switch
    {
        AccountDialogMode.Add => "Create an account for a saved website.",
        AccountDialogMode.Edit => "Edit this account record.",
        _ => "Full account record",
    };

    public string PrimaryActionText => Mode switch
    {
        AccountDialogMode.Add => "ADD ACCOUNT",
        AccountDialogMode.Edit => "SAVE CHANGES",
        _ => "EDIT ACCOUNT",
    };

    public bool IsPreview => Mode == AccountDialogMode.Preview;

    public bool IsEditing => Mode != AccountDialogMode.Preview;

    /// <summary>Display or editable, for every FieldCell on the dialog at once.</summary>
    public FieldCellKind FieldKind =>
        IsPreview ? FieldCellKind.Display : FieldCellKind.Editable;

    public void ShowPreview(Account account, IReadOnlyList<Website> websites)
    {
        _original = account;
        Fill(account, websites);
        Mode = AccountDialogMode.Preview;
    }

    public void ShowEdit(Account account, IReadOnlyList<Website> websites)
    {
        _original = account;
        Fill(account, websites);
        Mode = AccountDialogMode.Edit;
    }

    public void ShowAdd(IReadOnlyList<Website> websites)
    {
        _original = null;
        Fill(new Account(), websites);
        Mode = AccountDialogMode.Add;
    }

    /// <summary>Preview turns into edit without reopening.</summary>
    public void SwitchToEdit() => Mode = AccountDialogMode.Edit;

    /// <summary>
    /// Re-offers the websites without disturbing anything else on the form.
    /// </summary>
    /// <param name="websites">The sites to offer, freshly read.</param>
    /// <param name="selectLabel">
    /// A label to select as well, or null to leave the current value alone. Used
    /// when a website was just created from this form's own picker, so the record
    /// the user went away to make is the one they come back to find chosen.
    /// </param>
    /// <remarks>
    /// Not a refill: the user may have typed a username and a password by the time
    /// this runs, and rebuilding the form from the record would silently drop them.
    /// Only the picker's own state is replaced.
    /// </remarks>
    public void OfferWebsites(IReadOnlyList<Website> websites, string? selectLabel)
    {
        _websiteIds.Clear();

        List<string> labels = new(websites.Count);

        foreach (Website website in websites)
        {
            if (_websiteIds.TryAdd(website.PickerLabel, website.Id))
            {
                labels.Add(website.PickerLabel);
            }
        }

        WebsiteOptions = labels;

        if (selectLabel is not null && _websiteIds.ContainsKey(selectLabel))
        {
            WebsiteLabel = selectLabel;
        }
    }

    /// <summary>
    /// The form as a record, ready for the store.
    /// </summary>
    /// <remarks>
    /// Built from a clone of the original so anything the dialog does not show -
    /// the id, the creation stamp - survives an edit untouched.
    /// </remarks>
    public Account ToAccount()
    {
        Account account = _original?.Clone() ?? new Account();

        account.WebsiteId = ResolveWebsiteId();
        account.Username = Username.Trim();
        account.Password = Password;
        account.Note = Note.Trim();

        // The picker chose an id; the Website object the clone is carrying may now be
        // the wrong one, and the store would try to insert it as a new site.
        account.Website = null;

        return account;
    }

    /// <summary>
    /// True when the form has enough to save.
    /// </summary>
    /// <remarks>
    /// Only the website is required. An account can legitimately have no username -
    /// some sites identify you by email alone - and a blank password is a record of
    /// an account whose password is not known yet, which is worth keeping.
    /// </remarks>
    public bool CanSave => ResolveWebsiteId() > 0;

    private int ResolveWebsiteId() =>
        _websiteIds.TryGetValue(WebsiteLabel.Trim(), out int id) ? id : 0;

    private void Fill(Account account, IReadOnlyList<Website> websites)
    {
        _websiteIds.Clear();

        List<string> labels = new(websites.Count);

        foreach (Website website in websites)
        {
            string label = website.PickerLabel;

            // First one wins. Two sites can still collide if their name and domain
            // both match, in which case they are indistinguishable to a human too.
            if (_websiteIds.TryAdd(label, website.Id))
            {
                labels.Add(label);
            }
        }

        // The account's own site may have been deleted, or simply not be in the list
        // the form was given. Its label still has to show, or opening a record would
        // silently blank the one field that cannot be blank.
        if (account.Website is not null && !_websiteIds.ContainsKey(account.Website.PickerLabel))
        {
            _websiteIds[account.Website.PickerLabel] = account.WebsiteId;
            labels.Insert(0, account.Website.PickerLabel);
        }

        WebsiteOptions = labels;

        WebsiteLabel = account.Website?.PickerLabel
            ?? labels.FirstOrDefault(l => _websiteIds[l] == account.WebsiteId)
            ?? string.Empty;

        Username = account.Username;
        Password = account.Password;
        Note = account.Note;
    }
}
