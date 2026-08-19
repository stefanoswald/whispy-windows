using NAudio.Wave;

namespace Whispy;

/// <summary>
/// Captures microphone audio as 16 kHz mono float samples — the format
/// whisper.cpp expects. Uses NAudio's WaveInEvent (WASAPI under the hood).
/// </summary>
public sealed class AudioRecorder : IDisposable
{
    private WaveInEvent? _waveIn;
    private readonly List<float> _samples = new();
    private readonly object _lock = new();

    public bool IsRecording { get; private set; }

    /// <param name="deviceNumber">NAudio device number; -1 = system default.</param>
    public void Start(int deviceNumber)
    {
        StopInternal();
        lock (_lock) { _samples.Clear(); }

        var waveIn = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(16000, 16, 1),
            BufferMilliseconds = 50
        };
        waveIn.DataAvailable += OnData;
        waveIn.StartRecording();
        _waveIn = waveIn;
        IsRecording = true;
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        // 16-bit little-endian PCM -> float [-1, 1]
        int count = e.BytesRecorded / 2;
        var chunk = new float[count];
        for (int i = 0; i < count; i++)
        {
            short sample = BitConverter.ToInt16(e.Buffer, i * 2);
            chunk[i] = sample / 32768f;
        }
        lock (_lock) { _samples.AddRange(chunk); }
    }

    /// <summary>Stops capture and returns everything recorded since Start().</summary>
    public float[] Stop()
    {
        StopInternal();
        lock (_lock) { return _samples.ToArray(); }
    }

    private void StopInternal()
    {
        if (_waveIn != null)
        {
            try { _waveIn.StopRecording(); } catch { /* already stopped */ }
            _waveIn.DataAvailable -= OnData;
            _waveIn.Dispose();
            _waveIn = null;
        }
        IsRecording = false;
    }

    public double DurationSeconds
    {
        get { lock (_lock) { return _samples.Count / 16000.0; } }
    }

    public void Dispose() => StopInternal();

    /// <summary>Input devices for the settings dropdown: (deviceNumber, name).</summary>
    public static List<(int Number, string Name)> InputDevices()
    {
        var list = new List<(int, string)> { (-1, "System default") };
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            try
            {
                var caps = WaveInEvent.GetCapabilities(i);
                list.Add((i, caps.ProductName));
            }
            catch
            {
                // Skip devices that error out.
            }
        }
        return list;
    }
}
