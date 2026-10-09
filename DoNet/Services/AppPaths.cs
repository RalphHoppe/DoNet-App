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
