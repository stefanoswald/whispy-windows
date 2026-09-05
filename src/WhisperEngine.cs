using Whisper.net;

namespace Whispy;

/// <summary>
/// Local transcription via Whisper.net (whisper.cpp bindings). The ggml model
/// file downloads once from Hugging Face, then everything runs offline.
/// </summary>
public sealed class WhisperEngine : IDisposable
{
    private WhisperFactory? _factory;
    private string _loadedModel = "";

    public static string ModelDir
    {
        get
        {
            var dir = Path.Combine(AppSettings.AppDataDir, "models");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string ModelPath(string modelName) =>
        Path.Combine(ModelDir, $"ggml-{modelName}.bin");

    public static bool IsModelDownloaded(string modelName) =>
        File.Exists(ModelPath(modelName)) && new FileInfo(ModelPath(modelName)).Length > 10_000_000;

    public static string ModelUrl(string modelName) =>
        $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-{modelName}.bin";

    /// <summary>Approximate download sizes, for the UI.</summary>
    public static string ModelSizeLabel(string modelName) => modelName switch
    {
        "tiny" => "~75 MB",
        "base" => "~140 MB",
        "small" => "~470 MB",
        "medium" => "~1.5 GB",
        _ => ""
    };

    public static async Task DownloadModelAsync(string modelName,
        IProgress<double> progress, CancellationToken cancel)
    {
        var url = ModelUrl(modelName);
        var target = ModelPath(modelName);
        var temp = target + ".part";

        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromMinutes(30);
        using var response = await client.GetAsync(url,
            HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();

        long total = response.Content.Headers.ContentLength ?? -1;
        long readSoFar = 0;

        await using (var source = await response.Content.ReadAsStreamAsync(cancel))
        await using (var dest = File.Create(temp))
        {
            var buffer = new byte[1 << 16];
            int read;
            while ((read = await source.ReadAsync(buffer, cancel)) > 0)
            {
                await dest.WriteAsync(buffer.AsMemory(0, read), cancel);
                readSoFar += read;
                if (total > 0) progress.Report((double)readSoFar / total);
            }
        }
        File.Move(temp, target, overwrite: true);
    }

    public void EnsureLoaded(string modelName)
    {
        if (_factory != null && _loadedModel == modelName) return;
        _factory?.Dispose();
        _factory = WhisperFactory.FromPath(ModelPath(modelName));
        _loadedModel = modelName;
    }

    /// <summary>Vocabulary priming: Whisper conditions on a text prompt, so
    /// listing the user's terms biases it toward their spellings
    /// ("ElevenLabs" instead of "11 Labs").</summary>
    public string VocabularyPrompt { get; set; } = "";

    public async Task<string> TranscribeAsync(float[] samples, string modelName, string language)
    {
        EnsureLoaded(modelName);
        if (_factory == null) throw new InvalidOperationException("Whisper model not loaded.");

        var builder = _factory.CreateBuilder()
            .WithLanguage(string.IsNullOrWhiteSpace(language) ? "en" : language);
        if (!string.IsNullOrWhiteSpace(VocabularyPrompt))
        {
            builder = builder.WithPrompt(VocabularyPrompt);
        }
        await using var processor = builder.Build();

        var pieces = new List<string>();
        await foreach (var segment in processor.ProcessAsync(samples))
        {
            pieces.Add(segment.Text);
        }
        return string.Join(" ", pieces)
            .Replace("  ", " ")
            .Trim();
    }

    public void Dispose()
    {
        _factory?.Dispose();
        _factory = null;
    }
}
