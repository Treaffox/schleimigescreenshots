namespace SchleimigeScreenshots;

internal static class Program
{
    private static bool _fatalShown;

    [STAThread]
    private static void Main(string[] args)
    {
        // Nach einem Update startet die neue Version, während die alte noch beendet wird: kurz auf den Mutex warten.
        var afterUpdate = args.Contains("--restarted") || args.Contains("--updated");
        using var mutex = new Mutex(false, @"Local\SchleimigeScreenshots.SingleInstance");
        bool firstInstance;
        try { firstInstance = mutex.WaitOne(afterUpdate ? TimeSpan.FromSeconds(20) : TimeSpan.Zero); }
        catch (AbandonedMutexException) { firstInstance = true; } // frühere Instanz wurde hart beendet
        if (!firstInstance)
        {
            MessageBox.Show("Schleimige Screenshots läuft bereits (Symbol im Infobereich der Taskleiste).",
                "Schleimige Screenshots", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => { Log.Error("Unbehandelter UI-Fehler", e.Exception); ShowFatal("Unbehandelter Fehler", e.Exception); };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unbehandelter Fehler", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Error("Task-Fehler", e.Exception); e.SetObserved(); };

        try
        {
            Application.Run(new TrayApplicationContext(args));
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    /// <summary>
    /// Zeigt einen Fehler als Dialog. Der Text lässt sich im Dialog mit Strg+C kopieren.
    /// Nie mehr als ein Dialog gleichzeitig, damit eine Fehlerserie die App nicht lahmlegt.
    /// </summary>
    internal static void ShowFatal(string title, Exception ex)
    {
        if (_fatalShown) return;
        _fatalShown = true;
        try
        {
            var text = ex.ToString();
            if (text.Length > 1800) text = text[..1800] + "…";
            MessageBox.Show($"{text}\n\nLog: {Log.FilePath}\n(Strg+C kopiert diesen Text)",
                "Schleimige Screenshots – " + title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch { /* Fehleranzeige darf nie selbst scheitern */ }
        finally { _fatalShown = false; }
    }
}
