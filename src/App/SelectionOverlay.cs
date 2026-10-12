using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace SchleimigeScreenshots;

/// <summary>
/// Vollbild-Overlay für genau einen Monitor: zeigt das eingefrorene, abgedunkelte Bild; der Nutzer zieht mit der Maus
/// einen Bereich auf (wie bei einem Screenshot-Programm). Pro Monitor ein eigenes Fenster, damit jedes Fenster genau
/// seine eigene DPI hat und das Bild 1:1 (Pixel für Pixel) gezeichnet werden kann.
/// </summary>
internal sealed class SelectionOverlay : Form
{
    private const int WM_DPICHANGED = 0x02E0;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int MinSelection = 8;

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    private static readonly Color Accent = Color.FromArgb(163, 230, 53);

    private readonly Bitmap _screen;            // gesamter virtueller Desktop; gehört dem Aufrufer
    private readonly Rectangle _desktopBounds;  // Monitor in Desktop-Koordinaten (physische Pixel)
    private readonly Rectangle _monitorInShot;  // derselbe Monitor in Koordinaten der Aufnahme
    private readonly Font _hintFont = new("Segoe UI", 11f);
    private readonly Font _sizeFont = new("Segoe UI", 10f, FontStyle.Bold);
    private Point _start;
    private Rectangle _selection;
    private bool _dragging;

    /// <summary>Gewählter Bereich in Koordinaten der Aufnahme.</summary>
    public event Action<Rectangle>? Selected;
    public event Action? Cancelled;

    public SelectionOverlay(Bitmap screen, Rectangle desktopBounds, Rectangle monitorInShot)
    {
        _screen = screen;
        _desktopBounds = desktopBounds;
        _monitorInShot = monitorInShot;

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Black;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Bounds = desktopBounds;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW; // nicht in Alt+Tab
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        // Das Fenster muss exakt den Monitor in physischen Pixeln decken; Windows' DPI-Anpassung der Fenstergröße unterdrücken.
        if (m.Msg == WM_DPICHANGED) return;
        base.WndProc(ref m);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (Bounds != _desktopBounds) Bounds = _desktopBounds;
        Activate();
        ForceForeground(Handle);
    }

    /// <summary>
    /// Bringt das Fenster sicher in den Vordergrund, damit Esc/Enter sofort wirken. Windows verweigert
    /// SetForegroundWindow oft für Prozesse im Hintergrund (Vordergrundsperre, z. B. nach einem Hotkey); das Anhängen an die
    /// Eingabewarteschlange des aktuellen Vordergrundfensters umgeht das ohne sichtbare Nebenwirkung.
    /// </summary>
    private static void ForceForeground(IntPtr handle)
    {
        try
        {
            var foreground = GetForegroundWindow();
            var foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);
            var thisThread = GetCurrentThreadId();
            var attached = foregroundThread != 0 && foregroundThread != thisThread && AttachThreadInput(thisThread, foregroundThread, true);
            try
            {
                BringWindowToTop(handle);
                SetForegroundWindow(handle);
            }
            finally
            {
                if (attached) AttachThreadInput(thisThread, foregroundThread, false);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Overlay in den Vordergrund holen", ex);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hintFont.Dispose();
            _sizeFont.Dispose();
        }
        base.Dispose(disposing);
    }

    // ---- Zeichnen ---------------------------------------------------------------------------------------------

    protected override void OnPaintBackground(PaintEventArgs e) { /* alles in OnPaint */ }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int w = Math.Min(ClientSize.Width, _monitorInShot.Width);
        int h = Math.Min(ClientSize.Height, _monitorInShot.Height);
        var whole = new Rectangle(0, 0, w, h);
        float scale = DeviceDpi / 96f;

        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(_screen, whole, _monitorInShot.X, _monitorInShot.Y, w, h, GraphicsUnit.Pixel);

        g.CompositingMode = CompositingMode.SourceOver;
        using (var dim = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
            g.FillRectangle(dim, whole);

        if (_selection.Width > 0 && _selection.Height > 0)
        {
            var state = g.Save();
            g.SetClip(_selection, CombineMode.Intersect);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(_screen, whole, _monitorInShot.X, _monitorInShot.Y, w, h, GraphicsUnit.Pixel); // Auswahl in Originalhelligkeit
            g.Restore(state);

            DrawBorderAndLabel(g, scale);
        }
        else if (!_dragging)
        {
            DrawHint(g, whole, scale);
        }
    }

    private void DrawBorderAndLabel(Graphics g, float scale)
    {
        float bw = Math.Max(2f, 2f * scale);
        using (var dark = new Pen(Color.FromArgb(200, 0, 0, 0), bw + 2f))
            g.DrawRectangle(dark, Rectangle.Inflate(_selection, (int)(bw / 2) + 1, (int)(bw / 2) + 1));
        using (var pen = new Pen(Accent, bw))
            g.DrawRectangle(pen, Rectangle.Inflate(_selection, (int)(bw / 2), (int)(bw / 2)));

        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var text = $"{_selection.Width} × {_selection.Height}";
        var size = g.MeasureString(text, _sizeFont);
        int padX = (int)(8 * scale), padY = (int)(4 * scale), gap = (int)(8 * scale);
        int lw = (int)size.Width + 2 * padX, lh = (int)size.Height + 2 * padY;
        int lx = Math.Max(0, Math.Min(_selection.Left, ClientSize.Width - lw));
        int ly = _selection.Top - lh - gap >= 0 ? _selection.Top - lh - gap : _selection.Top + gap; // über der Auswahl, sonst darin
        var label = new Rectangle(lx, ly, lw, lh);
        using (var back = new SolidBrush(Color.FromArgb(215, 15, 23, 42)))
            g.FillRectangle(back, label);
        g.DrawString(text, _sizeFont, Brushes.White, label.Left + padX, label.Top + padY);
    }

    private void DrawHint(Graphics g, Rectangle whole, float scale)
    {
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        const string text = "Bereich mit der Maus aufziehen   ·   Esc oder Rechtsklick: abbrechen   ·   Enter: ganzer Monitor";
        var size = g.MeasureString(text, _hintFont);
        int padX = (int)(18 * scale), padY = (int)(10 * scale);
        int bw = (int)size.Width + 2 * padX, bh = (int)size.Height + 2 * padY;
        var box = new Rectangle((whole.Width - bw) / 2, (int)(32 * scale), bw, bh);
        using (var back = new SolidBrush(Color.FromArgb(225, 15, 23, 42)))
            g.FillRectangle(back, box);
        using (var accent = new Pen(Accent, Math.Max(1f, scale)))
            g.DrawRectangle(accent, box);
        g.DrawString(text, _hintFont, Brushes.White, box.Left + padX, box.Top + padY);
    }

    // ---- Maus und Tastatur ------------------------------------------------------------------------------------

    private Point Clamp(Point p) => new(Math.Clamp(p.X, 0, ClientSize.Width), Math.Clamp(p.Y, 0, ClientSize.Height));

    private void SetSelection(Rectangle value)
    {
        var pad = (int)(64 * DeviceDpi / 96f); // Rand für Rahmen und Größenanzeige
        var area = _selection.IsEmpty ? value : Rectangle.Union(_selection, value);
        _selection = value;
        Invalidate(area.IsEmpty ? ClientRectangle : Rectangle.Inflate(area, pad, pad));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Right)
        {
            Cancelled?.Invoke();
            return;
        }
        if (e.Button != MouseButtons.Left) return;

        _dragging = true;
        _start = Clamp(e.Location);
        Capture = true;
        SetSelection(new Rectangle(_start, Size.Empty));
        Invalidate(); // Hinweis ausblenden
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        var p = Clamp(e.Location);
        SetSelection(Rectangle.FromLTRB(Math.Min(_start.X, p.X), Math.Min(_start.Y, p.Y), Math.Max(_start.X, p.X), Math.Max(_start.Y, p.Y)));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging || e.Button != MouseButtons.Left) return;
        _dragging = false;
        Capture = false;

        var r = _selection;
        if (r.Width < MinSelection || r.Height < MinSelection)
        {
            SetSelection(Rectangle.Empty); // nur ein Klick: weiter auf einen echten Bereich warten
            Invalidate();
            return;
        }
        Selected?.Invoke(new Rectangle(_monitorInShot.X + r.X, _monitorInShot.Y + r.Y, r.Width, r.Height));
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Escape:
                Cancelled?.Invoke();
                return true;
            case Keys.Enter:
                Selected?.Invoke(_monitorInShot); // ganzer Monitor
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
