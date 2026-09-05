using System.Text.Json;
using System.Text.Json.Serialization;

namespace Whispy;

/// <summary>
/// A configurable global trigger. Unlike the Mac version's three kinds, the
/// Windows low-level hooks see keyboard and mouse uniformly, so a hotkey is
/// simply a set of keyboard virtual-key codes plus a set of extra mouse
/// buttons — engaged while ALL of them are held. Examples:
///   [VK_LCONTROL, VK_LMENU]           -> hold Left Ctrl + Left Alt
///   mouse XButton1 | XButton2          -> hold both Logitech side buttons
/// </summary>
public class Hotkey
{
    /// <summary>Virtual-key codes that must be held (left/right specific, e.g. 0xA2 = Left Ctrl).</summary>
    public List<int> KeyCodes { get; set; } = new() { 0xA2, 0xA4 }; // LCtrl + LAlt

    /// <summary>Bitmask of extra mouse buttons: 1 = XButton1 (Mouse 4), 2 = XButton2 (Mouse 5).</summary>
    public int MouseButtonMask { get; set; } = 0;

    public string DisplayName { get; set; } = "Left Ctrl + Left Alt";

    public bool IsEmpty => KeyCodes.Count == 0 && MouseButtonMask == 0;
}

/// <summary>
/// A learned mishearing: when the transcript contains Heard, replace it with
/// Replacement. Matching is case-insensitive and tolerant of spaces/hyphens
/// and number words vs digits ("11 labs" == "eleven labs").
/// </summary>
public class Correction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Heard { get; set; } = "";
    public string Replacement { get; set; } = "";
}

public enum HotkeyBehavior
{
    PushToTalk,
    Toggle
}

public enum WritingMode
{
    Raw,
    Clean,
    Friendly,
    Email,
    Social,
    Ideas,
    Prompt,
    Code
}

public static class WritingModeInfo
{
    public static string DisplayName(this WritingMode mode) => mode switch
    {
        WritingMode.Raw => "Raw Transcript",
        WritingMode.Clean => "Clean Text",
        WritingMode.Friendly => "Friendly Message",
        WritingMode.Email => "Professional Email",
        WritingMode.Social => "Social Caption",
        WritingMode.Ideas => "Idea Capture",
        WritingMode.Prompt => "Prompt Mode",
        WritingMode.Code => "Code / Technical",
        _ => mode.ToString()
    };
}

public class AppSettings
{
    public Hotkey Hotkey { get; set; } = new();
    public HotkeyBehavior HotkeyBehavior { get; set; } = HotkeyBehavior.PushToTalk;

    public string WhisperModel { get; set; } = "small";  // tiny | base | small | medium
    public string Language { get; set; } = "en";

    public WritingMode DefaultMode { get; set; } = WritingMode.Clean;

    public bool SpokenPunctuation { get; set; } = true;
    public bool SpokenCommands { get; set; } = true;
    public bool RemoveFillers { get; set; } = true;
    public bool BackspaceScratchThat { get; set; } = true;

    public bool PressEnterInChatApps { get; set; } = false;
    public bool RestoreClipboard { get; set; } = true;

    public bool HistoryEnabled { get; set; } = true;
    public int HistoryLimit { get; set; } = 500;

    public bool SoundsEnabled { get; set; } = true;
    public bool OverlayEnabled { get; set; } = true;
    public bool LaunchAtLogin { get; set; } = false;

    /// <summary>NAudio input device number; -1 = system default.</summary>
    public int MicDeviceNumber { get; set; } = -1;

    public List<string> Vocabulary { get; set; } = DefaultVocabulary();
    public List<Correction> Corrections { get; set; } = new();

    public static List<string> DefaultVocabulary() => new()
    {
        "Stefan Oswald", "The Magic Mansion", "MagicTrickGuy", "Oscorp",
        "Energy for Energy", "Fable 5", "ElevenLabs", "Hermes", "OpenClaw", "Codex",
        "Top Hat", "Lygia", "Whispy",
        "sleight of hand", "misdirection", "double lift", "palming",
        "close-up magic", "mentalism", "card force", "French drop"
    };

    // ---- persistence -------------------------------------------------------

    public static string AppDataDir
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Whispy");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string FilePath => Path.Combine(AppDataDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(FilePath), JsonOptions);
                if (loaded != null) return loaded;
            }
        }
        catch
        {
            // Corrupt settings fall back to defaults rather than crashing.
        }
        var fresh = new AppSettings();
        fresh.Save();
        return fresh;
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // Non-fatal: settings just won't persist this round.
        }
    }
}
