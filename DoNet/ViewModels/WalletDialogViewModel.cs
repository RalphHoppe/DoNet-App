using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using DoNet.Controls;
using DoNet.Models;

namespace DoNet.ViewModels;

/// <summary>The dialog's three modes, one shape per job.</summary>
public enum WalletDialogMode
{
    /// <summary>Read-only, from the card's open action.</summary>
    Preview,

    /// <summary>A new record.</summary>
    Add,

    /// <summary>An existing one.</summary>
    Edit,
}

/// <summary>
/// The choice lists the wallet form offers, gathered once per open.
/// </summary>
/// <param name="Persons">The saved people, for the person picker.</param>
public sealed record WalletChoiceOptions(
    IReadOnlyList<Person> Persons);

/// <summary>
/// Backs the wallet dialog in all three of its modes.
///</summary>
/// <remarks>
/// <para>
/// The person picker works in labels, the account dialog's pattern: this holds
/// the label the user sees and the id it stands for side by side, and resolves
/// one to the other on save.
///</para>
/// <para>
/// The money is a trio with one relation in it, the VPS form's interlock in a
/// new key: the exchange rate is what one unit of the wallet's currency is
/// worth in dollars, so dollars equal balance times rate. Whichever of the two
/// the user types fills the other, and editing the balance afterwards
/// recomputes whichever was filled last.
///</para>
///</remarks>
public sealed partial class WalletDialogViewModel : ObservableObject
{
    /// <summary>Label to id, rebuilt whenever the form is filled.</summary>
    private readonly Dictionary<string, int> _personIds = new(StringComparer.OrdinalIgnoreCase);

    private WalletRecord? _original;

    /// <summary>Which of the pair the form last filled on its own.</summary>
    private enum DerivedSide
    {
        None,

        /// <summary>Dollars were derived; the user typed the rate.</summary>
        Dollars,

        /// <summary>The rate was derived; the user typed the dollars.</summary>
        Rate,
    }

    private DerivedSide _derivedSide;

    /// <summary>Guards the interlock's writes back into the fields it reads.</summary>
    private bool _applying;

    // ------------------------------------------------------------------
    // Mode and chrome
    // ------------------------------------------------------------------

    /// <summary>The dialog's current mode.</summary>
    [ObservableProperty]
    private WalletDialogMode _mode;

    partial void OnModeChanged(WalletDialogMode value)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(IsPreview));
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(IsForm));
        OnPropertyChanged(nameof(PrimaryActionText));
        OnPropertyChanged(nameof(FieldKind));
        OnPropertyChanged(nameof(CanSave));
    }

    /// <summary>The dialog's title, from its mode.</summary>
    public string Title => Mode switch
    {
        WalletDialogMode.Add => "ADD WALLET",
        WalletDialogMode.Edit => "EDIT WALLET",
        _ => "WALLET",
    };

    /// <summary>The words under the title.</summary>
    public string Subtitle => Mode switch
    {
        WalletDialogMode.Add => "A new wallet record",
        WalletDialogMode.Edit => "Editing an existing record",
        _ => "The record as it is stored",
    };

    /// <summary>True in preview, where every field is read-only.</summary>
    public bool IsPreview => Mode == WalletDialogMode.Preview;

    /// <summary>True in the two modes that save.</summary>
    public bool IsEditing => Mode is WalletDialogMode.Add or WalletDialogMode.Edit;

    /// <summary>
    /// True while the form is the thing to show; the preview is its own sheet.
    /// </summary>
    public bool IsForm => !IsPreview;

    /// <summary>The one wide button's words, from the mode.</summary>
    public string PrimaryActionText => Mode switch
    {
        WalletDialogMode.Add => "SAVE WALLET",
        WalletDialogMode.Edit => "SAVE CHANGES",
        _ => "EDIT WALLET",
    };

    /// <summary>
    /// The record the form is holding, for the preview's edit action.
    /// </summary>
    public WalletRecord? OriginalRecord => _original;

    /// <summary>Read-only grey, or editable, per the mode.</summary>
    public FieldCellKind FieldKind => IsPreview ? FieldCellKind.Display : FieldCellKind.Editable;

    /// <summary>The id box is always the store's to assign.</summary>
    public FieldCellKind AutoFieldKind => FieldCellKind.Automatic;

    /// <summary>The save button lights only when the one required field is filled.</summary>
    public bool CanSave => !IsPreview && Wallet.Trim().Length > 0;

    // ------------------------------------------------------------------
    // The wallet fields
    // ------------------------------------------------------------------

    /// <summary>The wallet's brand, e.g. "MetaMask". The one required field.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(HeroCaption))]
    private string _wallet = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeroCaption))]
    private string _idDisplay = "Assigned on save";

    [ObservableProperty] private string _personLabel = string.Empty;

    [ObservableProperty] private string _walletNumber = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeroCaption))]
    [NotifyPropertyChangedFor(nameof(RatePrefix))]
    private string _currencyCode = "BTC";

    [ObservableProperty] private string _note = string.Empty;

    [ObservableProperty] private string _balanceText = string.Empty;

    [ObservableProperty] private string _dollarsText = string.Empty;

    [ObservableProperty] private string _rateText = string.Empty;

    [ObservableProperty] private string _password = string.Empty;

    [ObservableProperty] private string _recoveryWords = string.Empty;

    /// <summary>
    /// The preview hero's one-liner: the record's number, whose it is, and what
    /// it holds, joined with dots and empty parts dropped.
    /// </summary>
    public string HeroCaption =>
        string.Join(" \u00B7 ", new[]
        {
            IdDisplay,
            PersonLabel,
            CurrencyCode,
        }.Where(part => !string.IsNullOrWhiteSpace(part)));

    /// <summary>
    /// What sits inside the rate's box before the number, stating what it
    /// prices: "1 BTC = $". Follows the chosen currency.
    /// </summary>
    public string RatePrefix => $"1 {CurrencyCode} = $";

    // ------------------------------------------------------------------
    // The choice lists
    // ------------------------------------------------------------------

    [ObservableProperty]
    private IReadOnlyList<string> _personOptions = Array.Empty<string>();

    /// <summary>The brand drawer's list, straight from the catalog.</summary>
    public IReadOnlyList<string> WalletBrandOptions => Catalogs.WalletBrands;

    /// <summary>The currency picker's list, the shared crypto catalog.</summary>
    public IReadOnlyList<string> CurrencyOptions => Catalogs.CryptoCurrencies;

    /// <summary>
    /// Fills the person picker from one gathered set of choices. Called before
    /// each open, so a record added a moment ago is already in the next form's
    /// list.
    /// </summary>
    public void ApplyChoices(WalletChoiceOptions choices)
    {
        _personIds.Clear();
        PersonOptions = LabelsFor(choices.Persons, p => p.DisplayName, p => p.Id, _personIds);
    }

    /// <summary>Builds one picker's labels and their id map in a single pass.</summary>
    private static IReadOnlyList<string> LabelsFor<T>(
        IReadOnlyList<T> items,
        Func<T, string> labelOf,
        Func<T, int> idOf,
        Dictionary<string, int> ids)
    {
        List<string> labels = new(items.Count);

        foreach ((T item, int index) in items.Select((item, index) => (item, index)))
        {
            string label = labelOf(item);
            string unique = labels.Contains(label, StringComparer.OrdinalIgnoreCase)
                ? $"{label} · #{index + 1}"
                : label;
            labels.Add(unique);
            ids[unique] = idOf(item);
        }

        return labels;
    }

    // ------------------------------------------------------------------
    // The dollars/rate interlock
    // ------------------------------------------------------------------

    partial void OnBalanceTextChanged(string value)
    {
        if (_applying)
        {
            return;
        }

        // Editing the balance recomputes whichever side the form last filled,
        // so the trio never drifts apart while the user works on it.
        if (_derivedSide == DerivedSide.Dollars)
        {
            RecomputeDollars();
        }
        else if (_derivedSide == DerivedSide.Rate)
        {
            RecomputeRate();
        }
    }

    partial void OnDollarsTextChanged(string value)
    {
        if (_applying)
        {
            return;
        }

        RecomputeRate();
        _derivedSide = DerivedSide.Rate;
    }

    partial void OnRateTextChanged(string value)
    {
        if (_applying)
        {
            return;
        }

        RecomputeDollars();
        _derivedSide = DerivedSide.Dollars;
    }

    /// <summary>Dollars = balance × rate; left empty when either side is missing.</summary>
    private void RecomputeDollars()
    {
        if (TryParseAmount(BalanceText, out decimal balance)
            && TryParseAmount(RateText, out decimal rate))
        {
            _applying = true;
            try
            {
                DollarsText = FormatAmount(balance * rate);
            }
            finally
            {
                _applying = false;
            }
        }
    }

    /// <summary>Rate = dollars ÷ balance; left empty when the balance is missing.</summary>
    private void RecomputeRate()
    {
        if (TryParseAmount(BalanceText, out decimal balance)
            && balance > 0
            && TryParseAmount(DollarsText, out decimal dollars))
        {
            _applying = true;
            try
            {
                RateText = FormatAmount(dollars / balance);
            }
            finally
            {
                _applying = false;
            }
        }
    }

    /// <summary>Parses what the user typed as a plain amount; dot or comma decimal.</summary>
    private static bool TryParseAmount(string text, out decimal value)
        => decimal.TryParse(
            (text ?? string.Empty).Trim(),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out value);

    /// <summary>Up to eight decimals, trailing zeros off, never scientific.</summary>
    private static string FormatAmount(decimal value)
        => value.ToString("0.########", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------
    // Fill and collect
    // ------------------------------------------------------------------

    /// <summary>Clears the form for a new record.</summary>
    /// <param name="choices">The directories' current contents.</param>
    public void BeginAdd(WalletChoiceOptions choices)
    {
        _original = null;
        _derivedSide = DerivedSide.None;
        Mode = WalletDialogMode.Add;

        ApplyChoices(choices);

        IdDisplay = "Assigned on save";
        Wallet = string.Empty;
        PersonLabel = string.Empty;
        WalletNumber = string.Empty;
        CurrencyCode = "BTC";
        Note = string.Empty;
        BalanceText = string.Empty;
        DollarsText = string.Empty;
        RateText = string.Empty;
        Password = string.Empty;
        RecoveryWords = string.Empty;

        OnPropertyChanged(nameof(CanSave));
    }

    /// <summary>Fills the form from a stored record.</summary>
    /// <param name="record">The record to edit.</param>
    /// <param name="choices">The directories' current contents.</param>
    public void BeginEdit(WalletRecord record, WalletChoiceOptions choices)
    {
        _original = record;
        _derivedSide = DerivedSide.None;
        Mode = WalletDialogMode.Edit;

        ApplyChoices(choices);
        Fill(record);

        OnPropertyChanged(nameof(CanSave));
    }

    /// <summary>Fills the form read-only, for the card's open action.</summary>
    /// <param name="record">The record to show.</param>
    public void BeginPreview(WalletRecord record)
    {
        _original = record;
        _derivedSide = DerivedSide.None;
        Mode = WalletDialogMode.Preview;

        Fill(record);
    }

    /// <summary>Copies a stored record into the form's fields.</summary>
    private void Fill(WalletRecord record)
    {
        IdDisplay = record.Id > 0 ? $"#{record.Id}" : "Assigned on save";
        Wallet = record.Wallet;
        PersonLabel = PersonLabelFor(record);
        WalletNumber = record.WalletNumber;
        CurrencyCode = string.IsNullOrWhiteSpace(record.Currency) ? "BTC" : record.Currency;
        Note = record.Note;
        BalanceText = record.Balance is { } balance ? FormatAmount(balance) : string.Empty;
        DollarsText = record.BalanceDollars is { } dollars ? FormatAmount(dollars) : string.Empty;
        RateText = record.ExchangeRate is { } rate ? FormatAmount(rate) : string.Empty;
        Password = record.Password;
        RecoveryWords = record.RecoveryWords;
    }

    /// <summary>
    /// The person's label, from the form's map when it has one and from the
    /// record when it does not.
    /// </summary>
    /// <remarks>
    /// The preview opens without gathering anything - the record is in hand and
    /// a read-only view should not have to wait on the directories - but the
    /// map is what an add or edit last filled. The directory stamps each record
    /// with its person's name on the way out, so the preview reads that.
    /// </remarks>
    private string PersonLabelFor(WalletRecord record) =>
        LabelForId(_personIds, record.PersonId) is { Length: > 0 } mapped
            ? mapped
            : record.PersonName ?? string.Empty;

    /// <summary>The label a stored id resolves back to, or empty.</summary>
    private static string LabelForId(Dictionary<string, int> ids, int? id)
        => id is { } value
            ? ids.FirstOrDefault(pair => pair.Value == value).Key ?? string.Empty
            : string.Empty;

    /// <summary>
    /// The form as a record to store. The id is zero on add and the original's
    /// on edit; the store owns it either way.
    /// </summary>
    public WalletRecord BuildRecord()
    {
        _original ??= new WalletRecord();

        return new WalletRecord
        {
            Id = _original.Id,
            PersonId = ResolveId(_personIds, PersonLabel),
            Wallet = Wallet.Trim(),
            WalletNumber = WalletNumber.Trim(),
            Currency = string.IsNullOrWhiteSpace(CurrencyCode) ? "BTC" : CurrencyCode,
            Balance = TryParseAmount(BalanceText, out decimal balance) ? balance : null,
            BalanceDollars = TryParseAmount(DollarsText, out decimal dollars) ? dollars : null,
            ExchangeRate = TryParseAmount(RateText, out decimal rate) ? rate : null,
            Password = Password,
            RecoveryWords = RecoveryWords,
            Note = Note.Trim(),
        };
    }

    /// <summary>An id for the chosen label, or null when the choice is empty.</summary>
    private static int? ResolveId(Dictionary<string, int> ids, string label) =>
        ids.TryGetValue((label ?? string.Empty).Trim(), out int id) ? id : null;
}
