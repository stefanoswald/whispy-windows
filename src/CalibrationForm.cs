namespace Whispy;

/// <summary>Read the script, Whispy listens, then shows accuracy and the
/// vocabulary terms it misheard — save those as corrections.</summary>
public sealed class CalibrationForm : Form
{
    private readonly AppSettings _settings;
    private readonly PipelineController _pipeline;

    private readonly TextBox _script;
    private readonly Button _recordButton;
    private readonly Label _status;
    private readonly CheckedListBox _suggestions;
    private readonly Button _saveButton;

    private List<string> _terms = new();
    private List<Calibration.TermResult> _current = new();
    private bool _recording;

    public CalibrationForm(AppSettings settings, PipelineController pipeline)
    {
        _settings = settings;
        _pipeline = pipeline;

        Text = "Whispy Voice Calibration";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(640, 600);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var intro = new Label
        {
            Text = "Read the script below at your normal pace. Whispy compares what it heard to what you " +
                   "said, scores its accuracy, and learns how to fix the words it gets wrong for your voice. " +
                   "(This teaches Whispy to repair mishearings — it can't retrain the speech model itself.)",
            Location = new Point(16, 12),
            Size = new Size(608, 52)
        };

        _terms = settings.Vocabulary.Where(t => t.Trim().Length > 0).Take(Calibration.MaxTerms).ToList();
        _script = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(16, 70),
            Size = new Size(608, 200),
            Font = new Font("Segoe UI", 11f),
            Text = Calibration.Script(_terms)
        };

        _recordButton = new Button { Text = "Start recording", Location = new Point(16, 282), Size = new Size(150, 32) };
        _recordButton.Click += (_, _) => ToggleRecording();
        _status = new Label { Location = new Point(180, 288), Size = new Size(444, 40), Text = "" };

        var suggestionsLabel = new Label
        {
            Text = "Misheard terms — checked ones become corrections:",
            Location = new Point(16, 330),
            AutoSize = true
        };
        _suggestions = new CheckedListBox
        {
            Location = new Point(16, 352),
            Size = new Size(608, 190),
            CheckOnClick = true
        };
        _saveButton = new Button
        {
            Text = "Save corrections",
            Location = new Point(474, 552),
            Size = new Size(150, 32),
            Enabled = false
        };
        _saveButton.Click += (_, _) => SaveCorrections();

        Controls.Add(intro);
        Controls.Add(_script);
        Controls.Add(_recordButton);
        Controls.Add(_status);
        Controls.Add(suggestionsLabel);
        Controls.Add(_suggestions);
        Controls.Add(_saveButton);

        FormClosing += (_, _) => { if (_recording) _pipeline.EndCalibrationRecording(); };
    }

    private async void ToggleRecording()
    {
        if (!_recording)
        {
            try
            {
                _pipeline.BeginCalibrationRecording();
            }
            catch (Exception ex)
            {
                _status.Text = "Couldn't start: " + ex.Message;
                return;
            }
            _recording = true;
            _suggestions.Items.Clear();
            _saveButton.Enabled = false;
            _recordButton.Text = "Stop && analyze";
            _status.Text = "● Listening… read the whole script, then click Stop.";
            _status.ForeColor = Color.Firebrick;
            return;
        }

        _recording = false;
        var samples = _pipeline.EndCalibrationRecording();
        _recordButton.Enabled = false;
        _status.ForeColor = SystemColors.ControlText;
        _status.Text = "Transcribing…";

        try
        {
            var transcript = await _pipeline.TranscribeRawAsync(samples);
            var analysis = Calibration.Analyze(transcript, _terms);
            ShowResults(analysis);
        }
        catch (Exception ex)
        {
            _status.Text = "Transcription failed: " + ex.Message;
        }
        finally
        {
            _recordButton.Text = "Start recording";
            _recordButton.Enabled = true;
        }
    }

    private void ShowResults(Calibration.Analysis analysis)
    {
        int percent = (int)Math.Round(analysis.Accuracy * 100);
        var text = $"Recognition accuracy on the passage: {percent}%.";
        if (percent < 90)
        {
            text += " Below 90% usually means a noisy mic or a small model — try a closer mic or a larger model.";
        }
        _current = analysis.Suggestions;
        _suggestions.Items.Clear();
        foreach (var r in _current)
        {
            _suggestions.Items.Add($"\"{r.Heard}\"  →  {r.Term}", isChecked: true);
        }
        if (_current.Count == 0)
        {
            text += analysis.MissedCount == 0
                ? " Every vocabulary term was heard correctly."
                : $" {analysis.MissedCount} line(s) weren't found — read every \"Number N\" line and try again.";
        }
        _status.Text = text;
        _saveButton.Enabled = _current.Count > 0;
    }

    private void SaveCorrections()
    {
        int added = 0;
        for (int i = 0; i < _current.Count; i++)
        {
            if (!_suggestions.GetItemChecked(i)) continue;
            var r = _current[i];
            bool exists = _settings.Corrections.Any(c =>
                Calibration.Normalize(c.Heard) == Calibration.Normalize(r.Heard));
            if (exists) continue;
            _settings.Corrections.Add(new Correction { Heard = r.Heard, Replacement = r.Term });
            added++;
        }
        _settings.Save();
        _status.Text = $"Saved {added} correction(s). Whispy will fix these automatically from now on.";
        _saveButton.Enabled = false;
    }
}
