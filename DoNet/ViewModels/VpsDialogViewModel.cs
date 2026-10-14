using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using DoNet.Controls;
using DoNet.Models;

namespace DoNet.ViewModels;

/// <summary>Which of the three jobs the one VPS dialog is doing.</summary>
public enum VpsDialogMode
{
    Preview,
    Add,
    Edit,
}

/// <summary>
/// The choice lists the VPS form offers, gathered once per open.
/// </summary>
/// <param name="Persons">The saved people, for the person picker.</param>
/// <param name="Sites">The saved websites, for the site picker.</param>
/// <param name="Accounts">The saved accounts, for the account picker.</param>
/// <param name="VpsRecords">Every VPS, for the node picker.</param>
/// <param name="Wallets">Every wallet, for the "paid with" picker.</param>
public sealed record VpsChoiceOptions(
    IReadOnlyList<Person> Persons,
    IReadOnlyList<Website> Sites,
    IReadOnlyList<Account> Accounts,
    IReadOnlyList<VpsRecord> VpsRecords,
    IReadOnlyList<WalletRecord> Wallets);

/// <summary>
/// Backs the VPS dialog in all three of its modes.
/// </summary>
/// <remarks>
/// <para>
/// The pickers work in labels because that is what the list shows, so this holds
/// the label the user sees and the id it stands for side by side, and resolves
/// one to the other on save - the account dialog's pattern, three more times.
/// </para>
/// <para>
/// The balance trio is the form's one piece of arithmetic: the exchange rate is
/// what one Bitcoin is worth in the balance's currency, so Bitcoin equals
/// balance divided by rate. Whichever of the two the user types fills the other,
/// and editing the balance afterwards recomputes whichever was filled last -
/// the sheet's own sample row holds the same relation (13.7 / 103131 =
/// 0.00013281).
/// </para>
///</remarks>
public sealed partial class VpsDialogViewModel : ObservableObject
{
    /// <summary>The node picker's "no parent" choice; the sheet writes the record's own id.</summary>
    public const string MainNodeLabel = "This is the main VPS";

    /// <summary>Label to id, rebuilt whenever the form is filled.</summary>
    private readonly Dictionary<string, int> _personIds = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, int> _siteIds = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, int> _accountIds = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, int> _nodeIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Label to id, for the wallet the VPS was paid with.</summary>
    private readonly Dictionary<string, int> _walletIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Which of the pair the form last filled on its own.</summary>
    private enum DerivedSide
    {
        None,
        Bitcoin,
        Rate,
    }

    private DerivedSide _derivedSide = DerivedSide.None;

    /// <summary>Guards the interlock against reacting to its own writes.</summary>
    private bool _applying;

    private VpsRecord? _original;

    private VpsSystemInfo? _originalSystem;

    /// <summary>
    /// The cycle units the user asked for: days, weeks, months, years. Each entry
    /// is the drawer's three-part option - the row's label, the flag (a unit has
    /// none), and what the pick commits, which is the unit's own letter.
    /// </summary>
    public static readonly string[] CycleUnits =
    [
        "d · Days\u001F\u001Fd",
        "w · Weeks\u001F\u001Fw",
        "m · Months\u001F\u001Fm",
        "y · Years\u001F\u001Fy",
    ];

    /// <summary>
    /// The currencies the balance can be held in - the crypto list, by the
    /// user's instruction, shared with the wallets directory through the
    /// catalog. Each entry is the drawer's three-part option; the pick commits
    /// the bare code, which is what the store keeps.
    /// </summary>
    public static readonly string[] Currencies = Catalogs.CryptoCurrencies;


    /// <summary>The currency picker's list, as the binding wants it: an instance.</summary>
    public IReadOnlyList<string> CurrencyOptions => Currencies;

    /// <summary>The cycle unit picker's list, likewise.</summary>
    public IReadOnlyList<string> CycleUnitOptions => CycleUnits;

    public VpsDialogViewModel()
    {
    }

    // ------------------------------------------------------------------
    // Mode and chrome
    // ------------------------------------------------------------------

    [ObservableProperty]
    private VpsDialogMode _mode;

    /// <summary>
    /// Everything the chrome reads off the mode, announced in one place so the
    /// header and footer follow a mode change without each binding being told.
    /// </summary>
    partial void OnModeChanged(VpsDialogMode value)
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
        VpsDialogMode.Add => "ADD VPS",
        VpsDialogMode.Edit => "EDIT VPS",
        _ => "VPS",
    };

    /// <summary>The words under the title.</summary>
    public string Subtitle => Mode switch
    {
        VpsDialogMode.Add => "A new record, with its system information",
        VpsDialogMode.Edit => "Editing an existing record",
        _ => "The record as it is stored",
    };

    /// <summary>True in preview, where every field is read-only.</summary>
    public bool IsPreview => Mode == VpsDialogMode.Preview;

    /// <summary>True in the two modes that save.</summary>
    public bool IsEditing => Mode is VpsDialogMode.Add or VpsDialogMode.Edit;

    /// <summary>
    /// The preview hero's one-liner: the record's number, where it sits, and
    /// where it lives, joined with dots and empty parts dropped.
    /// </summary>
    public string HeroCaption =>
        string.Join(" \u00B7 ", new[]
        {
            IdDisplay,
            NodeLabel,
            Country,
        }.Where(part => !string.IsNullOrWhiteSpace(part)));

    /// <summary>
    /// True while the form is the thing to show - the add and edit modes. The
    /// preview is its own layout, not the form's grids read-only, so the two
    /// hosts trade visibility on this.
    /// </summary>
    public bool IsForm => !IsPreview;

    /// <summary>The one wide button's words, from the mode.</summary>
    public string PrimaryActionText => Mode switch
    {
        VpsDialogMode.Add => "SAVE VPS",
        VpsDialogMode.Edit => "SAVE CHANGES",
        _ => "EDIT VPS",
    };

    /// <summary>
    /// The record the form is holding, for the preview's edit action - the host
    /// reopens the form over it with fresh picker options.
    /// </summary>
    public VpsRecord? OriginalRecord => _original;

    /// <summary>Read-only grey, or editable, per the mode.</summary>
    public FieldCellKind FieldKind => IsPreview ? FieldCellKind.Display : FieldCellKind.Editable;

    /// <summary>The id box is always the store's to assign.</summary>
    public FieldCellKind AutoFieldKind => FieldCellKind.Automatic;

    /// <summary>The save button lights only when the one required field is filled.</summary>
    public bool CanSave => !IsPreview && Name.Trim().Length > 0;

    // ------------------------------------------------------------------
    // The VPS fields
    // ------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeroCaption))]
    private string _idDisplay = "Assigned on save";

    [ObservableProperty] private string _personLabel = string.Empty;

    [ObservableProperty] private string _siteLabel = string.Empty;

    [ObservableProperty] private string _accountLabel = string.Empty;

    [ObservableProperty] private string _walletLabel = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeroCaption))]
    private string _nodeLabel = MainNodeLabel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeroCaption))]
    private string _country = string.Empty;

    [ObservableProperty] private string _createdAt = string.Empty;

    [ObservableProperty] private string _outDate = string.Empty;

    [ObservableProperty] private string _cycleValueText = string.Empty;

    [ObservableProperty] private string _cycleUnitCode = string.Empty;

    [ObservableProperty] private string _note = string.Empty;

    [ObservableProperty] private string _balanceText = string.Empty;

    [ObservableProperty] private string _currencyCode = "USDT";

    [ObservableProperty] private string _btcText = string.Empty;

    [ObservableProperty] private string _rateText = string.Empty;

    // ------------------------------------------------------------------
    // The system fields
    // ------------------------------------------------------------------

    [ObservableProperty] private string _os = string.Empty;

    [ObservableProperty] private string _ip = string.Empty;

    [ObservableProperty] private string _port = string.Empty;

    [ObservableProperty] private string _portNumber = string.Empty;

    [ObservableProperty] private string _username = string.Empty;

    [ObservableProperty] private string _userPassword = string.Empty;

    [ObservableProperty] private string _rootUser = string.Empty;

    [ObservableProperty] private string _rootPassword = string.Empty;

    [ObservableProperty] private string _cpu = string.Empty;

    [ObservableProperty] private string _memory = string.Empty;

    [ObservableProperty] private string _disk = string.Empty;

    [ObservableProperty] private string _bandwidth = string.Empty;

    [ObservableProperty] private string _bandwidthSpeed = string.Empty;

    [ObservableProperty] private string _uploadSpeed = string.Empty;

    [ObservableProperty] private string _downloadSpeed = string.Empty;

    [ObservableProperty] private string _pingTime = string.Empty;

    [ObservableProperty] private string _systemNote = string.Empty;

    [ObservableProperty] private string _systemIdDisplay = "Assigned with the VPS";

    // ------------------------------------------------------------------
    // The choice lists
    // ------------------------------------------------------------------

    [ObservableProperty]
    private IReadOnlyList<string> _personOptions = Array.Empty<string>();

    [ObservableProperty]
    private IReadOnlyList<string> _siteOptions = Array.Empty<string>();

    [ObservableProperty]
    private IReadOnlyList<string> _accountOptions = Array.Empty<string>();

    [ObservableProperty]
    private IReadOnlyList<string> _nodeOptions = new[] { MainNodeLabel };

    [ObservableProperty]
    private IReadOnlyList<string> _walletOptions = Array.Empty<string>();

    /// <summary>
    /// Fills every picker from one gathered set of choices. Called before each
    /// open, so a record added a moment ago is already in the next form's lists.
    /// </summary>
    /// <param name="choices">The directories' current contents.</param>
    /// <param name="excluding">A VPS to leave out of the node picker - its own.</param>
    public void ApplyChoices(VpsChoiceOptions choices, int? excluding)
    {
        _personIds.Clear();
        PersonOptions = LabelsFor(choices.Persons, p => p.DisplayName, p => p.Id, _personIds);

        _siteIds.Clear();
        SiteOptions = LabelsFor(choices.Sites, w => w.PickerLabel, w => w.Id, _siteIds);

        _accountIds.Clear();
        AccountOptions = LabelsFor(choices.Accounts, AccountPickerLabel, a => a.Id, _accountIds);

        _nodeIds.Clear();
        NodeOptions = new[] { MainNodeLabel }
            .Concat(choices.VpsRecords
                .Where(v => v.Id != excluding)
                .Select(v => $"{v.Name} · #{v.Id}"))
            .ToList();

        foreach (VpsRecord vps in choices.VpsRecords.Where(v => v.Id != excluding))
        {
            _nodeIds[$"{vps.Name} · #{vps.Id}"] = vps.Id;
        }

        // The wallet the VPS was paid with: the brand names it, and the number
        // tells two of the same brand apart.
        _walletIds.Clear();
        WalletOptions = LabelsFor(
            choices.Wallets, WalletPickerLabel, w => w.Id, _walletIds);
    }

    /// <summary>
    /// The "paid with" picker's label: the brand, with the number's tail to
    /// tell two of the same brand apart.
    /// </summary>
    private static string WalletPickerLabel(WalletRecord wallet)
    {
        string brand = string.IsNullOrWhiteSpace(wallet.Wallet)
            ? $"Wallet · #{wallet.Id}"
            : wallet.Wallet;

        return wallet.WalletNumber.Length > 8
            ? $"{brand} · …{wallet.WalletNumber[^6..]}"
            : brand;
    }

    /// <summary>The account picker's label, the record dialog's own recipe.</summary>
    private static string AccountPickerLabel(Account account) =>
        string.IsNullOrWhiteSpace(account.Username)
            ? $"Account · #{account.Id}"
            : $"{account.Username} · {account.Website?.PickerLabel ?? "No site"}";

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
    // The balance interlock
    // ------------------------------------------------------------------

    partial void OnBalanceTextChanged(string value)
    {
        if (_applying)
        {
            return;
        }

        // Editing the balance recomputes whichever side the form last filled,
        // so the trio never drifts apart while the user works on it.
        if (_derivedSide == DerivedSide.Bitcoin)
        {
            RecomputeBitcoin();
        }
        else if (_derivedSide == DerivedSide.Rate)
        {
            RecomputeRate();
        }
    }

    partial void OnBtcTextChanged(string value)
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

        RecomputeBitcoin();
        _derivedSide = DerivedSide.Bitcoin;
    }

    /// <summary>Bitcoin = balance ÷ rate; left empty when either side is missing.</summary>
    private void RecomputeBitcoin()
    {
        if (TryParseAmount(BalanceText, out decimal balance)
            && TryParseAmount(RateText, out decimal rate)
            && rate > 0)
        {
            _applying = true;
            try
            {
                BtcText = FormatAmount(balance / rate);
            }
            finally
            {
                _applying = false;
            }
        }
    }

    /// <summary>Rate = balance ÷ Bitcoin; left empty when either side is missing.</summary>
    private void RecomputeRate()
    {
        if (TryParseAmount(BalanceText, out decimal balance)
            && TryParseAmount(BtcText, out decimal bitcoin)
            && bitcoin > 0)
        {
            _applying = true;
            try
            {
                RateText = FormatAmount(balance / bitcoin);
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
    public void BeginAdd(VpsChoiceOptions choices)
    {
        _original = null;
        _originalSystem = null;
        _derivedSide = DerivedSide.None;
        Mode = VpsDialogMode.Add;

        ApplyChoices(choices, excluding: null);

        IdDisplay = "Assigned on save";
        SystemIdDisplay = "Assigned with the VPS";
        Name = string.Empty;
        PersonLabel = string.Empty;
        SiteLabel = string.Empty;
        AccountLabel = string.Empty;
        WalletLabel = string.Empty;
        NodeLabel = MainNodeLabel;
        Country = string.Empty;
        CreatedAt = string.Empty;
        OutDate = string.Empty;
        CycleValueText = string.Empty;
        CycleUnitCode = string.Empty;
        Note = string.Empty;
        BalanceText = string.Empty;
        CurrencyCode = "USDT";
        BtcText = string.Empty;
        RateText = string.Empty;
        ClearSystemFields();

        OnPropertyChanged(nameof(CanSave));
    }

    /// <summary>Fills the form from a stored record and its system row.</summary>
    /// <param name="record">The record to edit.</param>
    /// <param name="system">Its system row.</param>
    /// <param name="choices">The directories' current contents.</param>
    public void BeginEdit(VpsRecord record, VpsSystemInfo system, VpsChoiceOptions choices)
    {
        _original = record;
        _originalSystem = system;
        _derivedSide = DerivedSide.None;
        Mode = VpsDialogMode.Edit;

        ApplyChoices(choices, excluding: record.Id);

        Fill(record, system);

        OnPropertyChanged(nameof(CanSave));
    }

    /// <summary>
    /// Fills the form read-only, for the card's open action.
    /// </summary>
    /// <param name="record">The record to show.</param>
    /// <param name="system">Its system row.</param>
    /// <param name="choices">
    /// The directories' current contents, so the linked labels resolve. Null
    /// when the gather itself failed, in which case the preview opens with
    /// what it has - a broken directory should not also break the record view.
    /// </param>
    public void BeginPreview(VpsRecord record, VpsSystemInfo system, VpsChoiceOptions? choices)
    {
        _original = record;
        _originalSystem = system;
        _derivedSide = DerivedSide.None;
        Mode = VpsDialogMode.Preview;

        if (choices is not null)
        {
            // Excluding nothing: the exclusion exists so an edit form cannot
            // pick a VPS as its own node, and a preview picks nothing.
            ApplyChoices(choices, excluding: null);
        }

        Fill(record, system);
    }

    /// <summary>Copies a stored record into the form's fields.</summary>
    private void Fill(VpsRecord record, VpsSystemInfo system)
    {
        IdDisplay = record.Id > 0 ? $"#{record.Id}" : "Assigned on save";
        SystemIdDisplay = system.Id > 0 ? $"#{system.Id}" : "Assigned with the VPS";
        Name = record.Name;
        PersonLabel = LabelForId(_personIds, record.PersonId);
        SiteLabel = LabelForId(_siteIds, record.WebsiteId);
        AccountLabel = LabelForId(_accountIds, record.AccountId);
        WalletLabel = LabelForId(_walletIds, record.WalletId);
        NodeLabel = record.IsMain || record.NodeVpsId == record.Id
            ? MainNodeLabel
            : LabelForId(_nodeIds, record.NodeVpsId);
        Country = record.Country;
        CreatedAt = record.CreatedAt;
        OutDate = record.OutDate;
        CycleValueText = record.CycleValue?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        CycleUnitCode = record.CycleUnit ?? string.Empty;
        Note = record.Note;
        BalanceText = record.Balance is { } balance ? FormatAmount(balance) : string.Empty;
        CurrencyCode = string.IsNullOrWhiteSpace(record.Currency) ? "USDT" : record.Currency;
        BtcText = record.BalanceBtc is { } bitcoin ? FormatAmount(bitcoin) : string.Empty;
        RateText = record.ExchangeRate is { } rate ? FormatAmount(rate) : string.Empty;

        Os = system.Os;
        Ip = system.Ip;
        Port = system.Port;
        PortNumber = system.PortNumber;
        Username = system.Username;
        UserPassword = system.UserPassword;
        RootUser = system.RootUser;
        RootPassword = system.RootPassword;
        Cpu = system.Cpu;
        Memory = system.Memory;
        Disk = system.Disk;
        Bandwidth = system.Bandwidth;
        BandwidthSpeed = system.BandwidthSpeed;
        UploadSpeed = system.UploadSpeed;
        DownloadSpeed = system.DownloadSpeed;
        PingTime = system.PingTime;
        SystemNote = system.Note;
    }

    private void ClearSystemFields()
    {
        Os = string.Empty;
        Ip = string.Empty;
        Port = string.Empty;
        PortNumber = string.Empty;
        Username = string.Empty;
        UserPassword = string.Empty;
        RootUser = string.Empty;
        RootPassword = string.Empty;
        Cpu = string.Empty;
        Memory = string.Empty;
        Disk = string.Empty;
        Bandwidth = string.Empty;
        BandwidthSpeed = string.Empty;
        UploadSpeed = string.Empty;
        DownloadSpeed = string.Empty;
        PingTime = string.Empty;
        SystemNote = string.Empty;
    }

    /// <summary>The label a stored id resolves back to, or empty.</summary>
    private static string LabelForId(Dictionary<string, int> ids, int? id)
        => id is { } value
            ? ids.FirstOrDefault(pair => pair.Value == value).Key ?? string.Empty
            : string.Empty;

    /// <summary>
    /// The form as a record to store. The id is zero on add and the original's on
    /// edit; the store owns it either way.
    /// </summary>
    public VpsRecord BuildRecord()
    {
        _original ??= new VpsRecord();

        return new VpsRecord
        {
            Id = _original.Id,
            PersonId = ResolveId(_personIds, PersonLabel),
            WebsiteId = ResolveId(_siteIds, SiteLabel),
            AccountId = ResolveId(_accountIds, AccountLabel),
            WalletId = ResolveId(_walletIds, WalletLabel),
            Name = Name.Trim(),
            Balance = TryParseAmount(BalanceText, out decimal balance) ? balance : null,
            Currency = string.IsNullOrWhiteSpace(CurrencyCode) ? "USDT" : CurrencyCode,
            BalanceBtc = TryParseAmount(BtcText, out decimal bitcoin) ? bitcoin : null,
            ExchangeRate = TryParseAmount(RateText, out decimal rate) ? rate : null,
            CreatedAt = CreatedAt.Trim(),
            OutDate = OutDate.Trim(),
            CycleValue = int.TryParse(
                CycleValueText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int cycle)
                ? cycle
                : null,
            CycleUnit = CycleUnitCode,
            NodeVpsId = string.Equals(NodeLabel, MainNodeLabel, StringComparison.OrdinalIgnoreCase)
                ? null
                : ResolveId(_nodeIds, NodeLabel),
            Country = Country.Trim(),
            Note = Note.Trim(),
        };
    }

    /// <summary>The form's system half, for the same save.</summary>
    public VpsSystemInfo BuildSystem()
    {
        _originalSystem ??= new VpsSystemInfo();

        return new VpsSystemInfo
        {
            Id = _originalSystem.Id,
            VpsId = _original.Id,
            Os = Os.Trim(),
            Ip = Ip.Trim(),
            Port = Port.Trim(),
            PortNumber = PortNumber.Trim(),
            Username = Username.Trim(),
            UserPassword = UserPassword,
            RootUser = RootUser.Trim(),
            RootPassword = RootPassword,
            Cpu = Cpu.Trim(),
            Memory = Memory.Trim(),
            Disk = Disk.Trim(),
            Bandwidth = Bandwidth.Trim(),
            BandwidthSpeed = BandwidthSpeed.Trim(),
            UploadSpeed = UploadSpeed.Trim(),
            DownloadSpeed = DownloadSpeed.Trim(),
            PingTime = PingTime.Trim(),
            Note = SystemNote.Trim(),
        };
    }

    /// <summary>An id for the chosen label, or null when the choice is empty.</summary>
    private static int? ResolveId(Dictionary<string, int> ids, string label) =>
        ids.TryGetValue((label ?? string.Empty).Trim(), out int id) ? id : null;
}
