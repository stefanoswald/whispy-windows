namespace Whispy;

/// <summary>
/// Floating status pill near the bottom of the screen — non-activating and
/// click-through, so it never steals focus from the app being dictated into.
/// </summary>
public sealed class OverlayForm : Form
{
    private readonly Label _label;
    private readonly System.Windows.Forms.Timer _hideTimer;

    public OverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(28, 28, 30);
        Size = new Size(240, 42);

        _label = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Regular),
            Text = ""
        };
        Controls.Add(_label);

        _hideTimer = new System.Windows.Forms.Timer();
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); Hide(); };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            const int WS_EX_NOACTIVATE = 0x08000000;
            const int WS_EX_TOOLWINDOW = 0x00000080;
            const int WS_EX_TRANSPARENT = 0x00000020;
            cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    public void ShowState(string text, Color accent)
    {
        _hideTimer.Stop();
        _label.Text = text;
        _label.ForeColor = accent;

        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(
            screen.Left + (screen.Width - Width) / 2,
            screen.Bottom - Height - 80);

        if (!Visible)
        {
            // Show without stealing focus.
            NativeShow();
        }
    }

    public void HideAfter(int milliseconds)
    {
        _hideTimer.Interval = Math.Max(1, milliseconds);
        _hideTimer.Start();
    }

    private void NativeShow()
    {
        const int SW_SHOWNOACTIVATE = 4;
        ShowWindow(Handle, SW_SHOWNOACTIVATE);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
