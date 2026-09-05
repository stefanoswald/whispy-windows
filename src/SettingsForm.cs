namespace Whispy;

/// <summary>Settings window — plain WinForms, tabbed like the Mac version.</summary>
public sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly HotkeyManager _hotkeys;
    private readonly HistoryStore _history;
    private readonly PipelineController _pipeline;
    private readonly Action _applyLaunchAtLogin;

    private Label _hotkeyLabel = null!;
    private Button _hotkeyButton = null!;

    public SettingsForm(AppSettings settings, HotkeyManager hotkeys,
        HistoryStore history, PipelineController pipeline, Action applyLaunchAtLogin)
    {
        _settings = settings;
        _hotkeys = hotkeys;
        _history = history;
        _pipeline = pipeline;
        _applyLaunchAtLogin = applyLaunchAtLogin;

        Text = "Whispy Settings";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 460);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildGeneralTab());
        tabs.TabPages.Add(BuildTranscriptionTab());
        tabs.TabPages.Add(BuildCleanupTab());
        tabs.TabPages.Add(BuildVocabularyTab());
        tabs.TabPages.Add(BuildHistoryTab());
        Controls.Add(tabs);

        FormClosing += (_, e) =>
        {
            _settings.Save();
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        };
    }

    // ---- General ---------------------------------------------------------------

    private TabPage BuildGeneralTab()
    {
        var page = new TabPage("General");
        int y = 18;

        page.Controls.Add(MakeLabel("Hotkey:", 16, y + 4));
        _hotkeyLabel = MakeLabel(_settings.Hotkey.DisplayName, 130, y + 4, bold: true);
        page.Controls.Add(_hotkeyLabel);
        _hotkeyButton = new Button { Text = "Change…", Location = new Point(400, y), Size = new Size(120, 28) };
        _hotkeyButton.Click += (_, _) => RecordHotkey();
        page.Controls.Add(_hotkeyButton);
        y += 40;

        page.Controls.Add(MakeLabel("Behavior:", 16, y + 4));
        var behavior = new ComboBox
        {
            Location = new Point(130, y), Width = 260,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        behavior.Items.Add("Push-to-talk (hold)");
        behavior.Items.Add("Toggle (press to start/stop)");
        behavior.SelectedIndex = _settings.HotkeyBehavior == HotkeyBehavior.PushToTalk ? 0 : 1;
        behavior.SelectedIndexChanged += (_, _) =>
        {
            _settings.HotkeyBehavior = behavior.SelectedIndex == 0
                ? HotkeyBehavior.PushToTalk : HotkeyBehavior.Toggle;
            _settings.Save();
        };
        page.Controls.Add(behavior);
        y += 40;

        page.Controls.Add(MakeLabel("Microphone:", 16, y + 4));
        var mic = new ComboBox
        {
            Location = new Point(130, y), Width = 300,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        var devices = AudioRecorder.InputDevices();
        foreach (var (_, name) in devices) mic.Items.Add(name);
        var selectedIndex = devices.FindIndex(d => d.Number == _settings.MicDeviceNumber);
        mic.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
        mic.SelectedIndexChanged += (_, _) =>
        {
            _settings.MicDeviceNumber = devices[mic.SelectedIndex].Number;
            _settings.Save();
        };
        page.Controls.Add(mic);
        y += 40;

        page.Controls.Add(MakeLabel("Default mode:", 16, y + 4));
        var mode = new ComboBox
        {
            Location = new Point(130, y), Width = 260,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        var modes = Enum.GetValues<WritingMode>().ToList();
        foreach (var m in modes) mode.Items.Add(m.DisplayName());
        mode.SelectedIndex = modes.IndexOf(_settings.DefaultMode);
        mode.SelectedIndexChanged += (_, _) =>
        {
            _settings.DefaultMode = modes[mode.SelectedIndex];
            _settings.Save();
        };
        page.Controls.Add(mode);
        y += 44;

        page.Controls.Add(MakeCheck("Press Enter automatically in chat apps", 16, y,
            _settings.PressEnterInChatApps, v => _settings.PressEnterInChatApps = v));
        y += 30;
        page.Controls.Add(MakeCheck("Restore previous clipboard after inserting", 16, y,
            _settings.RestoreClipboard, v => _settings.RestoreClipboard = v));
        y += 30;
        page.Controls.Add(MakeCheck("Launch at login", 16, y,
            _settings.LaunchAtLogin, v => { _settings.LaunchAtLogin = v; _applyLaunchAtLogin(); }));
        y += 30;
        page.Controls.Add(MakeCheck("Play sound on start/stop", 16, y,
            _settings.SoundsEnabled, v => _settings.SoundsEnabled = v));
        y += 30;
        page.Controls.Add(MakeCheck("Show floating status overlay", 16, y,
            _settings.OverlayEnabled, v => _settings.OverlayEnabled = v));

        return page;
    }

    private void RecordHotkey()
    {
        _hotkeyButton.Text = "Press keys / mouse buttons… (Esc cancels)";
        _hotkeyButton.Enabled = false;

        _hotkeys.Capture = new HotkeyCapture(
            onComplete: hotkey =>
            {
                _hotkeys.Capture = null;
                _settings.Hotkey = hotkey;
                _hotkeys.Hotkey = hotkey;
                _settings.Save();
                BeginInvoke(() =>
                {
                    _hotkeyLabel.Text = hotkey.DisplayName;
                    _hotkeyButton.Text = "Change…";
                    _hotkeyButton.Enabled = true;
                });
            },
            onCancel: () =>
            {
                _hotkeys.Capture = null;
                BeginInvoke(() =>
                {
                    _hotkeyButton.Text = "Change…";
                    _hotkeyButton.Enabled = true;
                });
            });
    }

    // ---- Transcription --------------------------------------------------------------

    private TabPage BuildTranscriptionTab()
    {
        var page = new TabPage("Transcription");
        int y = 18;

        page.Controls.Add(MakeLabel("Whisper model:", 16, y + 4));
        var model = new ComboBox
        {
            Location = new Point(140, y), Width = 200,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        string[] models = { "tiny", "base", "small", "medium" };
        foreach (var m in models) model.Items.Add(m);
        model.SelectedIndex = Math.Max(0, Array.IndexOf(models, _settings.WhisperModel));
        page.Controls.Add(model);

        var download = new Button { Text = "Download", Location = new Point(360, y), Size = new Size(110, 28) };
        var status = MakeLabel("", 16, y + 40);
        status.Size = new Size(520, 40);
        page.Controls.Add(status);

        void RefreshStatus()
        {
            var name = models[model.SelectedIndex];
            status.Text = WhisperEngine.IsModelDownloaded(name)
                ? $"\"{name}\" is downloaded and ready ({WhisperEngine.ModelSizeLabel(name)})."
                : $"\"{name}\" is not downloaded yet ({WhisperEngine.ModelSizeLabel(name)}). Click Download.";
        }
        RefreshStatus();

        model.SelectedIndexChanged += (_, _) =>
        {
            _settings.WhisperModel = models[model.SelectedIndex];
            _settings.Save();
            RefreshStatus();
        };
        download.Click += (_, _) =>
        {
            using var form = new ModelDownloadForm(models[model.SelectedIndex]);
            form.ShowDialog(this);
            RefreshStatus();
        };
        page.Controls.Add(download);
        y += 96;

        page.Controls.Add(MakeLabel("Language code:", 16, y + 4));
        var lang = new TextBox { Location = new Point(140, y), Width = 80, Text = _settings.Language };
        lang.TextChanged += (_, _) => { _settings.Language = lang.Text.Trim(); _settings.Save(); };
        page.Controls.Add(lang);
        page.Controls.Add(MakeLabel("(\"en\", \"de\", \"auto\" …)", 232, y + 4));
        y += 44;

        var note = MakeLabel(
            "Bigger models are more accurate but slower. \"small\" is a good balance.\n" +
            "Everything runs locally on this PC — audio never leaves your machine.", 16, y);
        note.Size = new Size(520, 44);
        page.Controls.Add(note);

        return page;
    }

    // ---- Cleanup ----------------------------------------------------------------------

    private TabPage BuildCleanupTab()
    {
        var page = new TabPage("Cleanup");
        int y = 18;
        page.Controls.Add(MakeCheck("Remove filler words (um, uh, you know…)", 16, y,
            _settings.RemoveFillers, v => _settings.RemoveFillers = v));
        y += 30;
        page.Controls.Add(MakeCheck("Spoken punctuation (\"comma\", \"period\"…)", 16, y,
            _settings.SpokenPunctuation, v => _settings.SpokenPunctuation = v));
        y += 30;
        page.Controls.Add(MakeCheck("Spoken commands (\"new paragraph\", \"scratch that\"…)", 16, y,
            _settings.SpokenCommands, v => _settings.SpokenCommands = v));
        y += 30;
        page.Controls.Add(MakeCheck("\"Scratch that\" deletes the last insertion", 16, y,
            _settings.BackspaceScratchThat, v => _settings.BackspaceScratchThat = v));
        y += 40;

        var note = MakeLabel(
            "Windows Whispy v1 uses the fast rule-based cleanup engine.\n" +
            "Voice commands: new paragraph, new line, bullet point, numbered list,\n" +
            "comma/period/question mark, scratch that, delete last sentence,\n" +
            "turn this into an email / a prompt, copy only, do not insert.", 16, y);
        note.Size = new Size(520, 80);
        page.Controls.Add(note);
        return page;
    }

    // ---- Vocabulary --------------------------------------------------------------------

    private TabPage BuildVocabularyTab()
    {
        var page = new TabPage("Vocabulary");

        var note = MakeLabel(
            "Words Whispy should spell exactly — names, brands, technical terms. Number words match digits (\"ElevenLabs\" catches \"11 labs\").", 16, 10);
        note.Size = new Size(520, 32);
        page.Controls.Add(note);

        var input = new TextBox { Location = new Point(16, 46), Width = 230 };
        var add = new Button { Text = "Add", Location = new Point(254, 44), Size = new Size(60, 26) };
        var remove = new Button { Text = "Remove", Location = new Point(320, 44), Size = new Size(74, 26) };
        var calibrate = new Button { Text = "Calibrate my voice…", Location = new Point(400, 44), Size = new Size(120, 26) };
        var list = new ListBox { Location = new Point(16, 78), Size = new Size(504, 130) };

        void Refresh()
        {
            list.Items.Clear();
            foreach (var term in _settings.Vocabulary) list.Items.Add(term);
        }
        Refresh();

        void AddTerm()
        {
            var term = input.Text.Trim();
            if (term.Length == 0 || _settings.Vocabulary.Contains(term)) return;
            _settings.Vocabulary.Insert(0, term);
            _settings.Save();
            input.Text = "";
            Refresh();
        }

        add.Click += (_, _) => AddTerm();
        input.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { AddTerm(); e.SuppressKeyPress = true; } };
        remove.Click += (_, _) =>
        {
            if (list.SelectedItem is string term)
            {
                _settings.Vocabulary.Remove(term);
                _settings.Save();
                Refresh();
            }
        };
        calibrate.Click += (_, _) =>
        {
            using var form = new CalibrationForm(_settings, _pipeline);
            form.ShowDialog(this);
            RefreshCorrections();
        };

        // Corrections: heard -> meant.
        var corrNote = MakeLabel(
            "Corrections — what Whispy hears → what you meant. Calibration fills this in; you can add your own.", 16, 218);
        corrNote.Size = new Size(520, 20);
        var heard = new TextBox { Location = new Point(16, 242), Width = 200, PlaceholderText = "Heard as… (11 labs)" };
        var arrow = MakeLabel("→", 222, 246);
        var meant = new TextBox { Location = new Point(242, 242), Width = 200, PlaceholderText = "Meant… (ElevenLabs)" };
        var addCorr = new Button { Text = "Add", Location = new Point(450, 240), Size = new Size(70, 26) };
        var corrList = new ListBox { Location = new Point(16, 274), Size = new Size(420, 120) };
        var removeCorr = new Button { Text = "Remove", Location = new Point(446, 274), Size = new Size(74, 26) };

        void AddCorrection()
        {
            var h = heard.Text.Trim();
            var m = meant.Text.Trim();
            if (h.Length == 0 || m.Length == 0) return;
            _settings.Corrections.Add(new Correction { Heard = h, Replacement = m });
            _settings.Save();
            heard.Text = "";
            meant.Text = "";
            RefreshCorrections();
        }
        addCorr.Click += (_, _) => AddCorrection();
        meant.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { AddCorrection(); e.SuppressKeyPress = true; } };
        removeCorr.Click += (_, _) =>
        {
            if (corrList.SelectedIndex >= 0 && corrList.SelectedIndex < _settings.Corrections.Count)
            {
                _settings.Corrections.RemoveAt(corrList.SelectedIndex);
                _settings.Save();
                RefreshCorrections();
            }
        };
        _correctionsList = corrList;
        RefreshCorrections();

        page.Controls.Add(input);
        page.Controls.Add(add);
        page.Controls.Add(remove);
        page.Controls.Add(calibrate);
        page.Controls.Add(list);
        page.Controls.Add(corrNote);
        page.Controls.Add(heard);
        page.Controls.Add(arrow);
        page.Controls.Add(meant);
        page.Controls.Add(addCorr);
        page.Controls.Add(corrList);
        page.Controls.Add(removeCorr);
        return page;
    }

    private ListBox? _correctionsList;

    private void RefreshCorrections()
    {
        if (_correctionsList == null) return;
        _correctionsList.Items.Clear();
        foreach (var c in _settings.Corrections)
        {
            _correctionsList.Items.Add($"{c.Heard}  →  {c.Replacement}");
        }
    }

    // ---- History ----------------------------------------------------------------------

    private TabPage BuildHistoryTab()
    {
        var page = new TabPage("History");
        int y = 18;
        page.Controls.Add(MakeCheck("Save dictation history (encrypted on this PC)", 16, y,
            _settings.HistoryEnabled, v => _settings.HistoryEnabled = v));
        y += 40;

        page.Controls.Add(MakeLabel("Keep at most:", 16, y + 4));
        var limit = new NumericUpDown
        {
            Location = new Point(130, y), Width = 90,
            Minimum = 50, Maximum = 5000, Increment = 50,
            Value = Math.Clamp(_settings.HistoryLimit, 50, 5000)
        };
        limit.ValueChanged += (_, _) => { _settings.HistoryLimit = (int)limit.Value; _settings.Save(); };
        page.Controls.Add(limit);
        page.Controls.Add(MakeLabel("entries", 230, y + 4));
        y += 48;

        var clear = new Button { Text = "Clear all history", Location = new Point(16, y), Size = new Size(160, 30) };
        clear.Click += (_, _) =>
        {
            _history.ClearAll();
            MessageBox.Show(this, "History cleared.", "Whispy");
        };
        page.Controls.Add(clear);
        y += 50;

        var note = MakeLabel(
            "History is encrypted with Windows DPAPI (your user account's key).\n" +
            "Audio is never saved — it exists only in memory during transcription.", 16, y);
        note.Size = new Size(520, 44);
        page.Controls.Add(note);
        return page;
    }

    // ---- helpers -------------------------------------------------------------------------

    private static Label MakeLabel(string text, int x, int y, bool bold = false)
    {
        var label = new Label { Text = text, Location = new Point(x, y), AutoSize = true };
        if (bold) label.Font = new Font(label.Font, FontStyle.Bold);
        return label;
    }

    private CheckBox MakeCheck(string text, int x, int y, bool value, Action<bool> setter)
    {
        var box = new CheckBox
        {
            Text = text, Location = new Point(x, y), AutoSize = true, Checked = value
        };
        box.CheckedChanged += (_, _) => { setter(box.Checked); _settings.Save(); };
        return box;
    }
}
