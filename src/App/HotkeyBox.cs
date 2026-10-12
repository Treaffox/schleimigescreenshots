namespace SchleimigeScreenshots;

/// <summary>Textfeld, das eine gedrückte Tastenkombination aufzeichnet.</summary>
internal sealed class HotkeyBox : TextBox
{
    private HotkeyGesture _gesture = HotkeyGesture.Default;

    public HotkeyBox()
    {
        ReadOnly = true;
        ShortcutsEnabled = false;
        BackColor = SystemColors.Window;
        Text = _gesture.ToString();
    }

    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public HotkeyGesture Gesture
    {
        get => _gesture;
        set { _gesture = value; Text = value.ToString(); }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var g = HotkeyGesture.FromKeys(keyData);
        if (HotkeyGesture.IsModifierKey(g.Key)) return true; // nur Modifier gedrückt: abwarten

        // Tab/Esc/Enter ohne Modifier behalten ihre normale Dialog-Funktion.
        if (g.Modifiers == 0 && g.Key is Keys.Tab or Keys.Escape or Keys.Enter) return base.ProcessCmdKey(ref msg, keyData);

        if (g.IsValid) Gesture = g;
        else System.Media.SystemSounds.Beep.Play();
        return true;
    }
}
