using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SchleimigeScreenshots;

/// <summary>Unsichtbares Fenster, das WM_HOTKEY für einen globalen Hotkey empfängt.</summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int HotkeyId = 0x5153;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private bool _registered;

    public event Action? Pressed;

    public HotkeyWindow() => CreateHandle(new CreateParams());

    public bool Register(HotkeyGesture gesture, out string? error)
    {
        error = null;
        if (!gesture.IsValid) { error = "Ungültige Tastenkombination."; return false; }

        Unregister();
        if (RegisterHotKey(Handle, HotkeyId, gesture.Modifiers | MOD_NOREPEAT, (uint)gesture.Key))
        {
            _registered = true;
            return true;
        }

        var code = Marshal.GetLastWin32Error();
        error = code == 1409
            ? $"{gesture} wird bereits von einem anderen Programm verwendet."
            : $"{gesture} konnte nicht registriert werden: {new Win32Exception(code).Message}";
        return false;
    }

    public void Unregister()
    {
        if (!_registered) return;
        UnregisterHotKey(Handle, HotkeyId);
        _registered = false;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
        {
            try { Pressed?.Invoke(); }
            catch (Exception ex) { Log.Error("Hotkey-Handler", ex); }
            return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Unregister();
        if (Handle != IntPtr.Zero) DestroyHandle();
    }
}
