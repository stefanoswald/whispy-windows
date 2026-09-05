using System.Media;

namespace Whispy;

public enum PipelineState { Idle, Recording, Processing }

/// <summary>
/// Orchestrates the dictation flow:
/// hotkey ▶ record ▶ transcribe ▶ parse commands ▶ clean ▶ insert ▶ history.
/// Created on the UI thread; heavy work runs on background tasks and results
/// marshal back via the captured SynchronizationContext.
/// </summary>
public sealed class PipelineController
{
    public PipelineState State { get; private set; } = PipelineState.Idle;
    public WritingMode CurrentMode;
    public Action? OnStateChanged;

    private readonly AppSettings _settings;
    private readonly HistoryStore _history;
    private readonly OverlayForm _overlay;
    private readonly AudioRecorder _recorder = new();
    private readonly WhisperEngine _whisper = new();
    private readonly TextInserter _inserter = new();
    private readonly SynchronizationContext _ui;

    private string? _lastInsertedText;
    private DateTime _lastInsertedAt;

    private static readonly Color ColorRecording = Color.FromArgb(255, 95, 86);
    private static readonly Color ColorWorking = Color.FromArgb(120, 190, 255);
    private static readonly Color ColorNotice = Color.FromArgb(255, 214, 110);

    public PipelineController(AppSettings settings, HistoryStore history, OverlayForm overlay)
    {
        _settings = settings;
        _history = history;
        _overlay = overlay;
        CurrentMode = settings.DefaultMode;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
    }

    /// <summary>Preload the whisper model so the first dictation is fast.</summary>
    public void WarmUp()
    {
        Task.Run(() =>
        {
            try { _whisper.EnsureLoaded(_settings.WhisperModel); } catch { }
        });
    }

    /// <summary>Vocabulary list handed to Whisper as a spelling prompt.</summary>
    private string VocabularyPrompt
    {
        get
        {
            var terms = _settings.Vocabulary.Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
            return terms.Count == 0 ? "" : "Vocabulary: " + string.Join(", ", terms) + ".";
        }
    }

    private Task<string> TranscribeAsync(float[] samples)
    {
        _whisper.VocabularyPrompt = VocabularyPrompt;
        return _whisper.TranscribeAsync(samples, _settings.WhisperModel, _settings.Language);
    }

    // ---- calibration (driven by CalibrationForm) --------------------------------

    public void BeginCalibrationRecording()
    {
        if (State != PipelineState.Idle)
            throw new InvalidOperationException("Whispy is busy — try again in a moment.");
        if (!WhisperEngine.IsModelDownloaded(_settings.WhisperModel))
            throw new InvalidOperationException("Whisper model not downloaded — see Settings > Transcription.");
        _recorder.Start(_settings.MicDeviceNumber);
        SetState(PipelineState.Recording);
    }

    public float[] EndCalibrationRecording()
    {
        var samples = _recorder.Stop();
        SetState(PipelineState.Processing);
        return samples;
    }

    /// <summary>Raw engine transcript (no cleanup) — calibration needs the engine's actual mistakes.</summary>
    public async Task<string> TranscribeRawAsync(float[] samples)
    {
        try
        {
            return await TranscribeAsync(samples);
        }
        finally
        {
            SetState(PipelineState.Idle);
        }
    }

    public void HotkeyDown()
    {
        if (_settings.HotkeyBehavior == HotkeyBehavior.Toggle)
        {
            if (State == PipelineState.Recording) StopAndProcess();
            else StartRecording();
        }
        else
        {
            StartRecording();
        }
    }

    public void HotkeyUp()
    {
        if (_settings.HotkeyBehavior == HotkeyBehavior.PushToTalk &&
            State == PipelineState.Recording)
        {
            StopAndProcess();
        }
    }

    public void ToggleFromMenu()
    {
        if (State == PipelineState.Recording) StopAndProcess();
        else StartRecording();
    }

    public void CancelRecording()
    {
        if (State != PipelineState.Recording) return;
        _recorder.Stop();
        _overlay.Hide();
        SetState(PipelineState.Idle);
    }

    private void StartRecording()
    {
        if (State != PipelineState.Idle) return;
        if (!WhisperEngine.IsModelDownloaded(_settings.WhisperModel))
        {
            ShowNotice("Whisper model not downloaded — open Settings");
            return;
        }
        try
        {
            _recorder.Start(_settings.MicDeviceNumber);
        }
        catch (Exception ex)
        {
            ShowNotice("Mic error: " + ex.Message);
            return;
        }
        SetState(PipelineState.Recording);
        if (_settings.OverlayEnabled) _overlay.ShowState("● Listening…", ColorRecording);
        PlaySound(start: true);
    }

    private void StopAndProcess()
    {
        if (State != PipelineState.Recording) return;
        var samples = _recorder.Stop();
        PlaySound(start: false);
        SetState(PipelineState.Processing);

        if (samples.Length / 16000.0 < 0.35)
        {
            _overlay.Hide();
            SetState(PipelineState.Idle);
            return;
        }

        if (_settings.OverlayEnabled) _overlay.ShowState("Transcribing…", ColorWorking);

        Task.Run(async () =>
        {
            string raw;
            try
            {
                raw = await TranscribeAsync(samples);
            }
            catch (Exception ex)
            {
                Finish(() => ShowNotice("Transcription failed: " + ex.Message));
                return;
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                Finish(() => ShowNotice("Didn't catch that"));
                return;
            }

            var parsed = CommandParser.Parse(raw, _settings);

            if (parsed.DeletePreviousInsertion)
            {
                Finish(PerformScratchThat);
                return;
            }
            if (parsed.Text.Length == 0)
            {
                Finish(() => ShowNotice("Nothing to insert"));
                return;
            }

            var mode = parsed.ModeOverride ?? CurrentMode;
            var cleaned = TextCleaner.Clean(parsed.Text, mode, _settings);
            if (cleaned.Length == 0)
            {
                Finish(() => ShowNotice("Nothing to insert"));
                return;
            }

            if (_settings.HistoryEnabled)
            {
                _ui.Post(_ => _history.Add(new HistoryEntry
                {
                    RawText = raw,
                    CleanedText = cleaned,
                    Mode = mode
                }, _settings.HistoryLimit), null);
            }

            bool copyOnly = parsed.CopyOnly;
            Finish(() =>
            {
                if (copyOnly)
                {
                    _inserter.CopyOnly(cleaned);
                    ShowNotice("Copied to clipboard");
                }
                else
                {
                    if (_settings.OverlayEnabled) _overlay.ShowState("Inserting…", ColorWorking);
                    if (_inserter.Insert(cleaned, _settings))
                    {
                        _lastInsertedText = cleaned;
                        _lastInsertedAt = DateTime.Now;
                        _overlay.HideAfter(500);
                    }
                    else
                    {
                        _inserter.CopyOnly(cleaned);
                        ShowNotice("Couldn't paste — text is on your clipboard");
                    }
                }
            });
        });
    }

    /// <summary>Runs an action on the UI thread, then returns the pipeline to idle.</summary>
    private void Finish(Action action)
    {
        _ui.Post(_ =>
        {
            try { action(); }
            finally { SetState(PipelineState.Idle); }
        }, null);
    }

    private void PerformScratchThat()
    {
        if (!_settings.BackspaceScratchThat ||
            _lastInsertedText == null ||
            (DateTime.Now - _lastInsertedAt).TotalSeconds > 120)
        {
            ShowNotice("Nothing recent to delete");
            return;
        }
        ShowNotice("Deleting last dictation");
        _inserter.DeleteCharacters(_lastInsertedText.Length);
        _lastInsertedText = null;
    }

    public string RecleanFromHistory(HistoryEntry entry, WritingMode mode) =>
        TextCleaner.Clean(entry.RawText, mode, _settings);

    private void ShowNotice(string text)
    {
        if (!_settings.OverlayEnabled) return;
        _overlay.ShowState(text, ColorNotice);
        _overlay.HideAfter(3000);
    }

    private void SetState(PipelineState state)
    {
        void Apply()
        {
            State = state;
            OnStateChanged?.Invoke();
        }
        if (SynchronizationContext.Current == _ui) Apply();
        else _ui.Post(_ => Apply(), null);
    }

    private void PlaySound(bool start)
    {
        if (!_settings.SoundsEnabled) return;
        try
        {
            if (start) SystemSounds.Exclamation.Play();
            else SystemSounds.Asterisk.Play();
        }
        catch { }
    }
}
