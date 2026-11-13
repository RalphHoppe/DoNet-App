using System;
using System.IO;

namespace EasyNet.Services;

/// <summary>
/// Where the app keeps its files: one folder of its own under local
/// application data, created before anything tries to write into it.
/// </summary>
/// <remarks>
/// The folder is the app's name, not the company's and not a product code -
/// a user who goes looking for their own data finds it in a place called
/// EasyNet, which is the least surprising place it can be.
///</remarks>
public sealed class AppPaths
{
    /// <summary>The app's folder under local application data.</summary>
    public string Root { get; }

    /// <summary>The vault's front-door file: the password's verifier, nothing else.</summary>
    public string VaultFile { get; }

    public AppPaths()
    {
        Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EasyNet");

        VaultFile = Path.Combine(Root, "vault.json");

        Directory.CreateDirectory(Root);
    }
}
