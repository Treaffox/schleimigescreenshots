using System.Text;

namespace SchleimigeScreenshots;

/// <summary>Minimaler Datei-Logger (%LocalAppData%\SchleimigeScreenshots\log.txt), der nie selbst Fehler wirft.</summary>
internal static class Log
{
    private static readonly object Gate = new();
    private const long MaxBytes = 512 * 1024;

    public static string Directory { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SchleimigeScreenshots");

    public static string FilePath { get; } = System.IO.Path.Combine(Directory, "log.txt");

    public static void Info(string message) => Write("INFO ", message);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > MaxBytes)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch { /* Logging darf die App nie stören */ }
    }
}
