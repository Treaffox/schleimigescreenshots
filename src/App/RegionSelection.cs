namespace SchleimigeScreenshots;

/// <summary>
/// Bereichsauswahl über alle Monitore: zeigt pro Monitor ein <see cref="SelectionOverlay"/> auf Basis der eingefrorenen
/// Aufnahme. <see cref="Result"/> liefert den gewählten Bereich in Aufnahme-Koordinaten oder null bei Abbruch.
/// Läuft auf dem UI-Thread; räumt sich selbst auf (Overlays, Bitmap, Timer).
/// </summary>
internal sealed class RegionSelection
{
    /// <summary>Ohne Eingabe wird die Auswahl nach dieser Zeit abgebrochen, damit der Bildschirm nie dauerhaft verdeckt bleibt.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    private readonly TaskCompletionSource<Rectangle?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<SelectionOverlay> _overlays = [];
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private readonly System.Windows.Forms.Timer _timeout = new() { Interval = (int)Timeout.TotalMilliseconds };
    private Bitmap? _bitmap;
    private bool _finished;

    public Task<Rectangle?> Result => _result.Task;

    public RegionSelection(CaptureResult shot)
    {
        _bitmap = ScreenCapture.CreateBitmap(shot.Pixels, shot.Width, shot.Height);
        var whole = new Rectangle(0, 0, shot.Width, shot.Height);

        foreach (var screen in Screen.AllScreens)
        {
            var inShot = new Rectangle(screen.Bounds.X - shot.Bounds.X, screen.Bounds.Y - shot.Bounds.Y,
                screen.Bounds.Width, screen.Bounds.Height);
            inShot.Intersect(whole);
            if (inShot.Width <= 0 || inShot.Height <= 0) continue;

            var overlay = new SelectionOverlay(_bitmap, screen.Bounds, inShot);
            overlay.Selected += region => Finish(region);
            overlay.Cancelled += () => Finish(null);
            _overlays.Add(overlay);
        }

        if (_overlays.Count == 0) // sollte nie vorkommen: dann wenigstens ein Overlay über den ganzen Desktop
        {
            var overlay = new SelectionOverlay(_bitmap, shot.Bounds, whole);
            overlay.Selected += region => Finish(region);
            overlay.Cancelled += () => Finish(null);
            _overlays.Add(overlay);
        }
    }

    public void Show()
    {
        // Das Overlay unter dem Mauszeiger zuletzt zeigen: es bekommt Tastaturfokus (Esc/Enter).
        var cursor = Cursor.Position;
        foreach (var overlay in _overlays.OrderBy(o => o.Bounds.Contains(cursor) ? 1 : 0))
            overlay.Show();

        _timeout.Tick += (_, _) => Finish(null);
        _timeout.Start();
    }

    public void Cancel() => Finish(null);

    private void Finish(Rectangle? region)
    {
        if (_finished) return;
        _finished = true;
        _timeout.Stop();

        // Overlays nicht innerhalb ihres eigenen Ereignis-Handlers schließen, sondern danach.
        void Cleanup(object? _)
        {
            foreach (var overlay in _overlays)
            {
                overlay.Close();
                overlay.Dispose();
            }
            _overlays.Clear();
            _bitmap?.Dispose();
            _bitmap = null;
            _timeout.Dispose();
        }

        if (_ui is not null) _ui.Post(Cleanup, null);
        else Cleanup(null);

        _result.TrySetResult(region);
    }
}
