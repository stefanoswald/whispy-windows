namespace Whispy;

/// <summary>First-run dialog that downloads the Whisper model with progress.</summary>
public sealed class ModelDownloadForm : Form
{
    private readonly ProgressBar _bar;
    private readonly Label _label;
    private readonly Button _button;
    private readonly string _modelName;
    private CancellationTokenSource? _cts;

    public ModelDownloadForm(string modelName)
    {
        _modelName = modelName;
        Text = "Whispy — first-time setup";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(420, 150);

        _label = new Label
        {
            Text = $"Whispy needs the local Whisper \"{modelName}\" speech model " +
                   $"({WhisperEngine.ModelSizeLabel(modelName)}).\n" +
                   "One-time download — afterwards everything runs offline.",
            Location = new Point(16, 14),
            Size = new Size(388, 48)
        };
        _bar = new ProgressBar
        {
            Location = new Point(16, 70),
            Size = new Size(388, 22),
            Minimum = 0,
            Maximum = 1000
        };
        _button = new Button
        {
            Text = "Download",
            Location = new Point(300, 106),
            Size = new Size(104, 30)
        };
        _button.Click += async (_, _) => await OnButton();

        Controls.Add(_label);
        Controls.Add(_bar);
        Controls.Add(_button);

        FormClosing += (_, _) => _cts?.Cancel();
    }

    private async Task OnButton()
    {
        if (_button.Text == "Close")
        {
            Close();
            return;
        }
        _button.Enabled = false;
        _cts = new CancellationTokenSource();
        var progress = new Progress<double>(p =>
        {
            _bar.Value = Math.Min(1000, (int)(p * 1000));
            _label.Text = $"Downloading… {p * 100:F0}%";
        });
        try
        {
            await WhisperEngine.DownloadModelAsync(_modelName, progress, _cts.Token);
            _label.Text = "Done! Whispy is ready — hold your hotkey and speak.";
            _button.Text = "Close";
            _button.Enabled = true;
        }
        catch (Exception ex)
        {
            _label.Text = "Download failed: " + ex.Message + "\nCheck your connection and try again.";
            _button.Text = "Retry";
            _button.Enabled = true;
        }
    }
}
