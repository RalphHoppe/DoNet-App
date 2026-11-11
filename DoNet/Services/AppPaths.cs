using System;
using System.Collections.Generic;
using System.IO;

namespace DoNet.Services;

/// <summary>
/// Where the app keeps its files.
/// </summary>
/// <remarks>
/// The packaged location is used when it is available, falling back to
/// %LOCALAPPDATA%\DoNet when the app is running unpackaged and
/// <c>ApplicationData.Current</c> throws. The vault and the database must agree on
/// this, or an unlock would succeed against one folder while the records lived in
/// another.
/// </remarks>
public static class AppPaths
{
    public static string DataFolder
    {
        get
        {
            try
            {
                return Windows.Storage.ApplicationData.Current.LocalFolder.Path;
            }
            catch (Exception)
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DoNet");
            }
        }
    }

    /// <summary>The encrypted person database.</summary>
    public static string DatabasePath => Path.Combine(DataFolder, "donet.db");

    /// <summary>
    /// Brings a former MSIX install's data home, once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The app runs unpackaged now, so its data lives in
    /// <c>%LOCALAPPDATA%\DoNet</c>. The MSIX builds kept it under the
    /// package's own folder - <c>Packages\DoNet_&lt;hash&gt;\LocalCache\Local</c> -
    /// which Windows removes when that package is uninstalled. Anyone whose
    /// data starts life there would see a fresh, empty app after switching to
    /// the unpackaged build, and their real vault would be a folder they have
    /// no reason to know about, waiting to be deleted by an uninstall.
    /// </para>
    /// <para>
    /// So the very first thing the app does is look: if the unpackaged
    /// location has no vault yet and a packaged one exists, the vault and the
    /// database (with its sidecars) are copied across. It runs once and never
    /// overwrites - the unpackaged location's own data, once it exists, always
    /// wins. Never throws: a migration is a courtesy, not a gate, and the app
    /// starts the same way whether it brought anything over or not.
    /// </para>
    /// </remarks>
    public static void MigratePackagedData()
    {
        try
        {
            string localAppData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            string target = Path.Combine(localAppData, "DoNet");

            if (File.Exists(Path.Combine(target, "vault.json")))
            {
                return;
            }

            string packages = Path.Combine(localAppData, "Packages");

            if (!Directory.Exists(packages))
            {
                return;
            }

            foreach (string package in Directory.EnumerateDirectories(packages, "DoNet_*"))
            {
                string source = Path.Combine(package, "LocalCache", "Local");

                if (!File.Exists(Path.Combine(source, "vault.json")))
                {
                    continue;
                }

                Directory.CreateDirectory(target);

                foreach (string name in new[]
                         {
                             "vault.json",
                             "donet.db",
                             "donet.db-wal",
                             "donet.db-shm",
                         })
                {
                    string from = Path.Combine(source, name);

                    if (File.Exists(from))
                    {
                        File.Copy(from, Path.Combine(target, name), overwrite: false);
                    }
                }

                AppLog.Info($"Brought the packaged install's data over from {source}.");
                return;
            }
        }
        catch (Exception error)
        {
            AppLog.Error("Bringing the packaged install's data over failed", error);
        }
    }

    /// <summary>
    /// Every file the database consists of, including the ones SQLite creates beside
    /// it.
    /// </summary>
    /// <remarks>
    /// The sidecars matter. The connection runs in WAL mode, so committed pages live
    /// in <c>donet.db-wal</c> until a checkpoint folds them back into the main file.
    /// Deleting only <c>donet.db</c> would leave a write-ahead log describing pages of
    /// a database that no longer exists, and SQLite would try to recover it into the
    /// next one - so a reset has to take all of them or none.
    ///
    /// <c>-journal</c> is listed too: it is what a rollback-mode database leaves
    /// behind, which is what exists if the connection ever failed before WAL was
    /// applied.
    /// </remarks>
    public static IReadOnlyList<string> DatabaseFiles
    {
        get
        {
            string database = DatabasePath;

            return new[]
            {
                database,
                database + "-wal",
                database + "-shm",
                database + "-journal",
            };
        }
    }
}
