namespace SchleimigeScreenshots;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\SchleimigeScreenshots.SingleInstance", out var firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("Schleimige Screenshots läuft bereits (Symbol im Infobereich der Taskleiste).",
                "Schleimige Screenshots", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error("Unbehandelter UI-Fehler", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unbehandelter Fehler", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Error("Task-Fehler", e.Exception); e.SetObserved(); };

        Application.Run(new TrayApplicationContext());
    }
}
