using Microsoft.Win32;

namespace Whispy;

/// <summary>
/// The tray app: icon, context menu, and wiring between the hotkey manager,
/// pipeline, overlay, and windows.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly AppSettings _settings;
    private readonly HistoryStore _history;
    private readonly OverlayForm _overlay;
    private readonly PipelineController _pipeline;
    private readonly HotkeyManager _hotkeys;

    private SettingsForm? _settingsForm;
    private HistoryForm? _historyForm;

    private readonly Icon _iconIdle;
    private readonly Icon _iconRecording;
    private readonly Icon _iconBusy;

    public TrayApplicationContext()
    {
        _settings = AppSettings.Load();
        _history = new HistoryStore();
        _overlay = new OverlayForm();
        _pipeline = new PipelineController(_settings, _history, _overlay);

        _iconIdle = MakeIcon(Color.FromArgb(190, 190, 195));
        _iconRecording = MakeIcon(Color.FromArgb(255, 80, 70));
        _iconBusy = MakeIcon(Color.FromArgb(110, 180, 255));

        _tray = new NotifyIcon
        {
            Icon = _iconIdle,
            Text = "Whispy — hold your hotkey and speak",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _tray.DoubleClick += (_, _) => OpenSettings();

        _pipeline.OnStateChanged = UpdateTray;

        _hotkeys = new HotkeyManager(_settings.Hotkey)
        {
            OnHotkeyDown = _pipeline.HotkeyDown,
            OnHotkeyUp = _pipeline.HotkeyUp
        };
        _hotkeys.Start();

        if (!WhisperEngine.IsModelDownloaded(_settings.WhisperModel))
        {
            // First run: get the model before anything can work.
            using var download = new ModelDownloadForm(_settings.WhisperModel);
            download.ShowDialog();
        }
        _pipeline.WarmUp();
        ApplyLaunchAtLogin();
    }

    // ---- menu ------------------------------------------------------------------

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) => RebuildMenu(menu);
        RebuildMenu(menu);
        return menu;
    }

    private void RebuildMenu(ContextMenuStrip menu)
    {
        menu.Items.Clear();

        var stateText = _pipeline.State switch
        {
            PipelineState.Recording => "Recording… (release to insert)",
            PipelineState.Processing => "Processing…",
            _ => "Whispy — ready"
        };
        menu.Items.Add(new ToolStripMenuItem(stateText) { Enabled = false });
        menu.Items.Add(new ToolStripMenuItem("Hotkey: " + _settings.Hotkey.DisplayName) { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());

        var toggle = new ToolStripMenuItem(
            _pipeline.State == PipelineState.Recording ? "Stop && Insert" : "Start Dictation");
        toggle.Click += (_, _) => _pipeline.ToggleFromMenu();
        menu.Items.Add(toggle);

        if (_pipeline.State == PipelineState.Recording)
        {
            var cancel = new ToolStripMenuItem("Cancel Recording");
            cancel.Click += (_, _) => _pipeline.CancelRecording();
            menu.Items.Add(cancel);
        }
        menu.Items.Add(new ToolStripSeparator());

        var modes = new ToolStripMenuItem("Writing Mode");
        foreach (WritingMode mode in Enum.GetValues<WritingMode>())
        {
            var item = new ToolStripMenuItem(mode.DisplayName())
            {
                Checked = mode == _pipeline.CurrentMode
            };
            var captured = mode;
            item.Click += (_, _) => _pipeline.CurrentMode = captured;
            modes.DropDownItems.Add(item);
        }
        menu.Items.Add(modes);
        menu.Items.Add(new ToolStripSeparator());

        var historyItem = new ToolStripMenuItem("History…");
        historyItem.Click += (_, _) => OpenHistory();
        menu.Items.Add(historyItem);

        var settingsItem = new ToolStripMenuItem("Settings…");
        settingsItem.Click += (_, _) => OpenSettings();
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());

        var quit = new ToolStripMenuItem("Quit Whispy");
        quit.Click += (_, _) => ExitApp();
        menu.Items.Add(quit);
    }

    private void UpdateTray()
    {
        _tray.Icon = _pipeline.State switch
        {
            PipelineState.Recording => _iconRecording,
            PipelineState.Processing => _iconBusy,
            _ => _iconIdle
        };
    }

    // ---- windows ------------------------------------------------------------------

    private void OpenSettings()
    {
        if (_settingsForm == null || _settingsForm.IsDisposed)
        {
            _settingsForm = new SettingsForm(_settings, _hotkeys, _history, _pipeline, ApplyLaunchAtLogin);
        }
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    private void OpenHistory()
    {
        if (_historyForm == null || _historyForm.IsDisposed)
        {
            _historyForm = new HistoryForm(_history, _pipeline, _settings);
        }
        _historyForm.Show();
        _historyForm.Activate();
    }

    private void ExitApp()
    {
        _hotkeys.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        Application.Exit();
    }

    // ---- helpers -------------------------------------------------------------------

    private void ApplyLaunchAtLogin()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key == null) return;
            if (_settings.LaunchAtLogin)
            {
                key.SetValue("Whispy", '"' + Application.ExecutablePath + '"');
            }
            else
            {
                key.DeleteValue("Whispy", throwOnMissingValue: false);
            }
        }
        catch { }
    }

    /// <summary>Draws a simple filled-circle tray icon — no icon assets needed.</summary>
    private static Icon MakeIcon(Color color)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 6, 4, 20, 20);
            using var stem = new Pen(color, 3);
            g.DrawLine(stem, 16, 24, 16, 29);
        }
        var handle = bitmap.GetHicon();
        return Icon.FromHandle(handle);
    }
}
