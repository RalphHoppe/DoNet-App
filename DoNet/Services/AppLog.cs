using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace DoNet.Services;

/// <summary>
/// A small rolling log file, for diagnosing failures on a machine you do not have.
/// </summary>
/// <remarks>
/// <para>
/// The one rule this class has to obey is that it never throws. It is called from
/// crash handlers, so an exception raised while reporting an exception would replace a
/// diagnosable failure with an undiagnosable one. Every path is wrapped, and after a
/// write failure it switches itself off rather than retrying on every call - a full
/// disk or a locked file does not get better by being asked again a thousand times.
/// </para>
/// <para>
/// Nothing sensitive is written here. Exception messages and stack traces only; never
/// a password, a key, or a field value. The log sits next to an encrypted database and
/// must not become the plaintext copy of it.
/// </para>
/// </remarks>
public static class AppLog
{
    /// <summary>Roll at a quarter of a megabyte - large enough to hold a session's worth
    /// of detail, small enough to paste into a bug report.</summary>
    private const long MaxBytes = 256 * 1024;

    private static readonly object Gate = new();

    private static string? _path;
    private static bool _disabled;

    /// <summary>The log file, for showing the user where to find it.</summary>
    public static string FilePath
    {
        get
        {
            try
            {
                return Path.Combine(AppPaths.DataFolder, "logs", "donet.log");
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message) => Write("WARN", message, null);

    /// <param name="context">Where it happened, in a few words. Shows up as the line's tag.</param>
    public static void Error(string context, Exception? error) => Write("ERROR", context, error);

    private static void Write(string level, string message, Exception? error)
    {
        if (_disabled)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                // Re-check inside the lock: another thread may have given up while this
                // one was waiting.
                if (_disabled)
                {
                    return;
                }

                string path = EnsurePath();
                if (path.Length == 0)
                {
                    _disabled = true;
                    return;
                }

                Roll(path);

                StringBuilder line = new();
                line.Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
                    .Append("  ")
                    .Append(level.PadRight(5))
                    .Append("  ")
                    .Append(message);

                if (error is not null)
                {
                    // ToString rather than Message: the type, the stack and any inner
                    // exceptions are the parts that actually identify a fault.
                    line.Append(Environment.NewLine).Append(error);
                }

                line.Append(Environment.NewLine);

                File.AppendAllText(path, line.ToString(), Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // Logging is best effort by definition. If the folder is gone, the disk is
            // full or the file is locked, stop trying for the rest of the session.
            _disabled = true;
        }
    }

    private static string EnsurePath()
    {
        if (_path is not null)
        {
            return _path;
        }

        string folder = Path.Combine(AppPaths.DataFolder, "logs");
        Directory.CreateDirectory(folder);

        _path = Path.Combine(folder, "donet.log");
        return _path;
    }

    /// <summary>
    /// Keeps one previous file, so a crash and the run before it both survive.
    /// </summary>
    private static void Roll(string path)
    {
        FileInfo file = new(path);

        if (!file.Exists || file.Length < MaxBytes)
        {
            return;
        }

        string previous = path + ".1";

        if (File.Exists(previous))
        {
            File.Delete(previous);
        }

        File.Move(path, previous);
    }
}
