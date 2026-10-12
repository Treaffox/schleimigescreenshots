using System.Text.Json.Serialization;

namespace SchleimigeScreenshots;

/// <summary>Tastenkombination: Win32-Modifier (Alt=1, Strg=2, Umschalt=4, Win=8) plus Taste.</summary>
public sealed record HotkeyGesture(uint Modifiers, Keys Key)
{
    public const uint Alt = 0x1, Control = 0x2, Shift = 0x4, Win = 0x8;

    public static HotkeyGesture Default { get; } = new(Control | Shift, Keys.Q);

    [JsonIgnore]
    public bool IsValid =>
        Key != Keys.None && !IsModifierKey(Key) && (Modifiers != 0 || (Key >= Keys.F1 && Key <= Keys.F24));

    public static bool IsModifierKey(Keys k) =>
        k is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin
            or Keys.LControlKey or Keys.RControlKey or Keys.LShiftKey or Keys.RShiftKey or Keys.LMenu or Keys.RMenu;

    public static HotkeyGesture FromKeys(Keys keyData)
    {
        uint mods = 0;
        if ((keyData & Keys.Control) != 0) mods |= Control;
        if ((keyData & Keys.Shift) != 0) mods |= Shift;
        if ((keyData & Keys.Alt) != 0) mods |= Alt;
        return new HotkeyGesture(mods, keyData & Keys.KeyCode);
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if ((Modifiers & Control) != 0) parts.Add("Strg");
        if ((Modifiers & Alt) != 0) parts.Add("Alt");
        if ((Modifiers & Shift) != 0) parts.Add("Umschalt");
        if ((Modifiers & Win) != 0) parts.Add("Win");
        parts.Add(Key.ToString());
        return string.Join(" + ", parts);
    }
}
