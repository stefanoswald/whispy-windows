using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Whispy;

/// <summary>
/// Global trigger detection via low-level keyboard and mouse hooks
/// (WH_KEYBOARD_LL / WH_MOUSE_LL). Tracks which of the hotkey's keys and
/// mouse buttons are held; fires Down when all are held, Up when any is
/// released. Extra mouse buttons (XButton1/2) that belong to the hotkey are
/// SWALLOWED so they never reach other apps (no browser back/forward).
///
/// Must be created on the UI thread (hooks need its message loop). No admin
/// rights or special permissions required — this is standard Windows API.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    public Hotkey Hotkey { get; set; }
    public Action? OnHotkeyDown;
    public Action? OnHotkeyUp;

    /// <summary>When set, every key/button press is reported here instead of
    /// driving the hotkey (used by the settings window's recorder).</summary>
    public HotkeyCapture? Capture;

    private IntPtr _keyboardHook = IntPtr.Zero;
    private IntPtr _mouseHook = IntPtr.Zero;
    // Keep delegate references alive or the GC collects them and hooks crash.
    private readonly LowLevelProc _keyboardProc;
    private readonly LowLevelProc _mouseProc;

    private readonly HashSet<int> _pressedKeys = new();
    private int _pressedMouse;
    private bool _engaged;

    public HotkeyManager(Hotkey hotkey)
    {
        Hotkey = hotkey;
        _keyboardProc = KeyboardProc;
        _mouseProc = MouseProc;
    }

    public void Start()
    {
        Stop();
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        var moduleHandle = GetModuleHandle(module.ModuleName);
        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
    }

    public void Stop()
    {
        if (_keyboardHook != IntPtr.Zero) { UnhookWindowsHookEx(_keyboardHook); _keyboardHook = IntPtr.Zero; }
        if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        _pressedKeys.Clear();
        _pressedMouse = 0;
        _engaged = false;
    }

    public void Dispose() => Stop();

    // ---- keyboard hook -------------------------------------------------------

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int vk = (int)info.vkCode;
            bool down = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
            bool up = wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP;

            if (Capture != null)
            {
                if (down) Capture.KeyDown(vk);
                if (up && Capture.KeyUp(vk)) { /* capture finished */ }
            }
            else
            {
                if (down) _pressedKeys.Add(vk);
                if (up) _pressedKeys.Remove(vk);
                Evaluate();
            }
        }
        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    // ---- mouse hook ------------------------------------------------------------

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            if (msg is WM_XBUTTONDOWN or WM_XBUTTONUP)
            {
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                int which = (int)(info.mouseData >> 16); // 1 = XButton1, 2 = XButton2
                int bit = which == 2 ? 2 : 1;
                bool down = msg == WM_XBUTTONDOWN;

                if (Capture != null)
                {
                    if (down) Capture.MouseDown(bit);
                    else Capture.MouseUp(bit);
                    return (IntPtr)1; // swallow while recording a hotkey
                }

                if (down) _pressedMouse |= bit;
                else _pressedMouse &= ~bit;
                Evaluate();

                // Swallow buttons that belong to the hotkey — they trigger
                // Whispy and nothing else.
                if ((Hotkey.MouseButtonMask & bit) != 0)
                {
                    return (IntPtr)1;
                }
            }
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void Evaluate()
    {
        if (Hotkey.IsEmpty) return;
        bool keysHeld = Hotkey.KeyCodes.All(_pressedKeys.Contains);
        bool mouseHeld = (_pressedMouse & Hotkey.MouseButtonMask) == Hotkey.MouseButtonMask;
        bool engaged = keysHeld && mouseHeld;
        if (engaged == _engaged) return;
        _engaged = engaged;
        if (engaged) OnHotkeyDown?.Invoke();
        else OnHotkeyUp?.Invoke();
    }

    // ---- Win32 -------------------------------------------------------------------

    private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_XBUTTONUP = 0x020C;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
}

/// <summary>
/// Accumulates a combination while the settings window records a new hotkey.
/// Commit rule: once anything was pressed and everything is released, the
/// accumulated set becomes the hotkey.
/// </summary>
public sealed class HotkeyCapture
{
    private readonly HashSet<int> _accumulatedKeys = new();
    private readonly HashSet<int> _heldKeys = new();
    private int _accumulatedMouse;
    private int _heldMouse;
    private readonly Action<Hotkey> _onComplete;
    private readonly Action _onCancel;

    public HotkeyCapture(Action<Hotkey> onComplete, Action onCancel)
    {
        _onComplete = onComplete;
        _onCancel = onCancel;
    }

    public void KeyDown(int vk)
    {
        if (vk == 0x1B) // Escape cancels
        {
            _onCancel();
            return;
        }
        _accumulatedKeys.Add(vk);
        _heldKeys.Add(vk);
    }

    /// <returns>true when the capture completed.</returns>
    public bool KeyUp(int vk)
    {
        _heldKeys.Remove(vk);
        return MaybeComplete();
    }

    public void MouseDown(int bit)
    {
        _accumulatedMouse |= bit;
        _heldMouse |= bit;
    }

    public void MouseUp(int bit)
    {
        _heldMouse &= ~bit;
        MaybeComplete();
    }

    private bool MaybeComplete()
    {
        bool anythingAccumulated = _accumulatedKeys.Count > 0 || _accumulatedMouse != 0;
        bool everythingReleased = _heldKeys.Count == 0 && _heldMouse == 0;
        if (!anythingAccumulated || !everythingReleased) return false;

        var hotkey = new Hotkey
        {
            KeyCodes = _accumulatedKeys.OrderBy(k => k).ToList(),
            MouseButtonMask = _accumulatedMouse,
            DisplayName = BuildName(_accumulatedKeys, _accumulatedMouse)
        };
        _onComplete(hotkey);
        return true;
    }

    public static string BuildName(IEnumerable<int> keys, int mouseMask)
    {
        var parts = keys.OrderBy(k => k).Select(KeyName).ToList();
        if ((mouseMask & 1) != 0) parts.Add("Mouse 4");
        if ((mouseMask & 2) != 0) parts.Add("Mouse 5");
        return parts.Count == 0 ? "None" : string.Join(" + ", parts);
    }

    public static string KeyName(int vk) => vk switch
    {
        0xA0 => "Left Shift", 0xA1 => "Right Shift",
        0xA2 => "Left Ctrl", 0xA3 => "Right Ctrl",
        0xA4 => "Left Alt", 0xA5 => "Right Alt",
        0x5B => "Left Win", 0x5C => "Right Win",
        0x14 => "Caps Lock", 0x20 => "Space", 0x0D => "Enter", 0x09 => "Tab",
        >= 0x70 and <= 0x87 => "F" + (vk - 0x6F),
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        _ => "Key " + vk
    };
}
