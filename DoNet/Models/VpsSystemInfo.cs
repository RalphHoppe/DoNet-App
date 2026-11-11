namespace DoNet.Models;

/// <summary>
/// The system information of one VPS record: the row behind "VPS Systems
/// Informations".
/// </summary>
/// <remarks>
/// The relation to the VPS is one-to-one by the user's design: a VPS has exactly
/// one system and a system belongs to exactly one VPS. The unique index on
/// <see cref="VpsId"/> makes that a database guarantee rather than a convention,
/// and the system is created and saved inside the VPS form - there is no screen
/// that edits one on its own, which is what "picked" would imply.
///
/// Every column is text exactly as the user's sheet and samples hold it, units
/// included ("32 GB DDR5", "290 Gbps", "22 Sec"): these are display strings the
/// user reads back, not quantities the app computes over.
///</remarks>
public sealed class VpsSystemInfo
{
    public int Id { get; set; }

    /// <summary>The VPS this information belongs to; unique, so the pair stays 1:1.</summary>
    public int VpsId { get; set; }

    /// <summary>The operating system as the user writes it, e.g. "Debian 12".</summary>
    public string Os { get; set; } = string.Empty;

    /// <summary>The address, IPv4 or IPv6, as text.</summary>
    public string Ip { get; set; } = string.Empty;

    /// <summary>The port's label - the protocol, e.g. "SSH" - not the number.</summary>
    public string Port { get; set; } = string.Empty;

    /// <summary>The numeric port, e.g. "55475". Text, so nothing rejects a range.</summary>
    public string PortNumber { get; set; } = string.Empty;

    /// <summary>The login user, e.g. a generated handle.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Its password. Never shown on a card; the reveal lives in the form.</summary>
    public string UserPassword { get; set; } = string.Empty;

    /// <summary>The root account's name, when the host gives one.</summary>
    public string RootUser { get; set; } = string.Empty;

    /// <summary>The root password. Same policy as the user password.</summary>
    public string RootPassword { get; set; } = string.Empty;

    /// <summary>The CPU as the user writes it, e.g. "Intel Xeon E-2388G".</summary>
    public string Cpu { get; set; } = string.Empty;

    /// <summary>RAM with its unit, e.g. "32 GB DDR5".</summary>
    public string Memory { get; set; } = string.Empty;

    /// <summary>Disk with its unit, e.g. "2 TB".</summary>
    public string Disk { get; set; } = string.Empty;

    /// <summary>The connection's bandwidth, e.g. "290 Gbps".</summary>
    public string Bandwidth { get; set; } = string.Empty;

    /// <summary>The billed bandwidth speed, e.g. "1 Gbps".</summary>
    public string BandwidthSpeed { get; set; } = string.Empty;

    /// <summary>Measured upload speed, e.g. "316 Mb".</summary>
    public string UploadSpeed { get; set; } = string.Empty;

    /// <summary>Measured download speed, e.g. "952 Mb".</summary>
    public string DownloadSpeed { get; set; } = string.Empty;

    /// <summary>The ping as the user records it, e.g. "22 Sec".</summary>
    public string PingTime { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;
}
