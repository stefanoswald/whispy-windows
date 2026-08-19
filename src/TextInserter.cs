using System.Runtime.InteropServices;

namespace Whispy;

/// <summary>
/// Inserts final text into the focused app via clipboard paste (Ctrl+V) —
/// the only method that works everywhere, including Electron apps and
/// browsers. Saves and restores the previous clipboard text.
/// Must be called on the UI (STA) thread — clipboard access requires it.
/// </summary>
public sealed class TextInserter
{
    /// <returns>true if a paste was attempted.</returns>
    public bool Insert(string text, AppSettings settings)
    {
        string? savedText = null;
        if (settings.RestoreClipboard)
        {
            try { if (Clipboard.ContainsText()) savedText = Clipboard.GetText(); }
            catch { /* clipboard busy — skip restore */ }
        }

        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            return false; // clipboard locked by another app
        }

        Thread.Sleep(60);
        SendCtrlV();

        if (settings.PressEnterInChatApps && IsChatApp())
        {
            Thread.Sleep(150);
            SendKey(0x0D); // Enter
        }

        if (savedText != null)
        {
            var toRestore = savedText;
            Task.Delay(800).ContinueWith(_ =>
            {
                try
                {
                    // Clipboard needs an STA thread.
                    var t = new Thread(() =>
                    {
                        try { Clipboard.SetText(toRestore); } catch { }
                    });
                    t.SetApartmentState(ApartmentState.STA);
                    t.Start();
                    t.Join(2000);
                }
                catch { }
            });
        }
        return true;
    }

    public void CopyOnly(string text)
    {
        try { Clipboard.SetText(text); } catch { }
    }

    /// <summary>Deletes the previous insertion with backspaces ("scratch that").</summary>
    public void DeleteCharacters(int count)
    {
        int capped = Math.Min(count, 1000);
        for (int i = 0; i < capped; i++)
        {
            SendKey(0x08); // Backspace
            Thread.Sleep(3);
        }
    }

    /// <summary>Best-effort chat-app detection from the foreground window's process.</summary>
    private static bool IsChatApp()
    {
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            var name = process.ProcessName.ToLowerInvariant();
            string[] chat = { "slack", "discord", "telegram", "whatsapp", "teams", "signal", "messenger" };
            return chat.Any(name.Contains);
        }
        catch
        {
            return false;
        }
    }

    // ---- SendInput -----------------------------------------------------------

    private static void SendCtrlV()
    {
        var inputs = new[]
        {
            KeyInput(0x11, down: true),   // Ctrl down
            KeyInput(0x56, down: true),   // V down
            KeyInput(0x56, down: false),  // V up
            KeyInput(0x11, down: false)   // Ctrl up
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static void SendKey(ushort vk)
    {
        var inputs = new[] { KeyInput(vk, true), KeyInput(vk, false) };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT KeyInput(ushort vk, bool down) => new()
    {
        type = 1, // INPUT_KEYBOARD
        u = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                dwFlags = down ? 0u : 2u // KEYEVENTF_KEYUP
            }
        }
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
