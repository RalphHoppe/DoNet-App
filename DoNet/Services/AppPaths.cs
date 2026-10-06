using System;
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
}
