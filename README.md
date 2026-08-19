# Whispy for Windows

Local push-to-talk dictation for Windows — the PC sibling of [Whispy for Mac](https://github.com/stefanoswald/whispy). Hold a hotkey (or your mouse's side buttons), speak, release: cleaned-up text is pasted into whatever app has focus. Everything runs on your PC. No account, no subscription, no cloud, no telemetry.

## For friends: just run it

If someone sent you `Whispy.exe`:

1. Double-click it. Windows SmartScreen may warn about an unrecognized app — click **More info → Run anyway** (the app is unsigned, not unsafe; the full source is in this repo).
2. First launch downloads the Whisper speech model (~470 MB, one time). After that it works fully offline.
3. Hold **Left Ctrl + Left Alt**, speak, release. Text appears where your cursor is.
4. Right-click the tray icon (gray microphone dot) for modes, history, and settings — including changing the hotkey to your mouse's side buttons.

## Building from source

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Double-click `build.bat` (or run it in a terminal).
3. Your app is `dist\Whispy.exe` — a single self-contained file you can share.

## Features

- **Push-to-talk or toggle** with any key combo — including extra mouse buttons (Mouse 4/5), which Whispy swallows so they stop triggering browser back/forward.
- **Fully local transcription** via whisper.cpp (Whisper.net). Model choice from tiny to medium in Settings.
- **Cleanup engine**: removes filler words, fixes punctuation/capitalization, splits run-on sentences, preserves your custom vocabulary (names, brands, technical terms).
- **Voice commands**: "new paragraph", "new line", "bullet point", "numbered list", "comma"/"period"/"question mark", "scratch that", "delete last sentence", "turn this into an email/a prompt", "copy only", "do not insert". A dictation of just "scratch that" deletes your previous insertion.
- **Writing modes**: Raw, Clean, Friendly, Email, Social, Ideas, Prompt, Code — pick in the tray menu or say "turn this into…".
- **History**: searchable, DPAPI-encrypted, optional. Audio is never stored.
- **Insertion**: clipboard paste with previous-clipboard restore; optional auto-Enter in chat apps.

## Architecture

| File | Role |
|---|---|
| `src/Program.cs` / `TrayApplicationContext.cs` | Single-instance tray app, menu, wiring |
| `src/HotkeyManager.cs` | Low-level keyboard+mouse hooks; chords of keys and/or mouse buttons; swallows hotkey mouse buttons |
| `src/AudioRecorder.cs` | NAudio 16 kHz mono capture, device selection |
| `src/WhisperEngine.cs` | Whisper.net transcription + one-time model download |
| `src/CommandParser.cs` / `TextCleaner.cs` | Ported from Mac Whispy — spoken commands, rules, vocabulary |
| `src/TextInserter.cs` | Clipboard paste via SendInput, clipboard restore, backspace-undo |
| `src/PipelineController.cs` | record → transcribe → parse → clean → insert → history |
| `src/HistoryStore.cs` | DPAPI-encrypted local history |
| `src/OverlayForm.cs` / `SettingsForm.cs` / `HistoryForm.cs` / `ModelDownloadForm.cs` | UI |

## Troubleshooting

- **Nothing happens on the hotkey** → another app may already own that combo; record a different one in Settings > General.
- **Text lands in the wrong place** → the paste goes to whatever window has keyboard focus; click into your target field first.
- **Slow transcription** → switch to the "base" model (Settings > Transcription) — much faster, still good for clear speech.
- **Mouse side buttons still navigate** → make sure the buttons are recorded as the hotkey (they're only swallowed while assigned), and check your mouse driver software (Logitech G HUB / Options+) isn't remapping them to something else first.
- **Antivirus flags the exe** → a false positive common to unsigned single-file .NET apps that use keyboard hooks; build from source yourself or whitelist it.

## Roadmap

Local LLM cleanup pass (llama.cpp) for the styled writing modes, per-app insertion profiles, and — the big one — a personal next-word prediction engine that learns how you write.

## Honest caveat

Written without a Windows machine to compile on; the first build may surface an error or two. Paste the exact error text back to whoever gave you the repo and it's usually a one-line fix.

MIT licensed, same as the Mac version.
