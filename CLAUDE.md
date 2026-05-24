# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Windows-only WPF desktop app (.NET 10) that turns an Xbox controller into a macro pad + push-to-talk mic for Claude Code (used as a VS Code extension). Digital buttons type predefined text. Sticks open a 4-way radial overlay; releasing in a direction types that slot's text. `A` (or any button assigned to "voice") records mic audio and feeds it to a speech-to-text provider (local Whisper.net or OpenAI API) — transcribed text is injected at the last-clicked location.

## Build / publish / run

PowerShell (Windows-only project; no Linux/macOS path). Source lives under `src/`.

```powershell
# Debug build
dotnet build src/CCXboxController.sln -c Debug

# Release build
dotnet build src/CCXboxController.sln -c Release

# Run from source
dotnet run --project src/CCXboxController -c Release

# Self-contained single-file exe (.NET runtime embedded, ~168 MB)
dotnet publish src/CCXboxController/CCXboxController.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true
# Output: src/CCXboxController/bin/Release/net10.0-windows/win-x64/publish/CCXboxController.exe
```

`IncludeAllContentForSelfExtract=true` is set in the csproj — without it, Whisper.net's native loader cannot find `whisper.dll`/`ggml.*` because the default single-file extract only places "native-only" libs in subfolders the loader doesn't search. With this flag, all content extracts side-by-side and loading works.

Regenerate the application icon (multi-size ICO from PowerShell + System.Drawing):

```powershell
& tools/make-icon.ps1   # writes src/CCXboxController/app.ico
```

No test project exists. Verification is manual — see [README.md](README.md) "Verification (end-to-end)".

## Architecture — the parts that need multiple files to grasp

### Threading model (most important to get right)

Three logical threads, and crossing them wrong is the failure mode that historically killed the controller polling loop:

1. **Controller polling thread** — `ControllerService.Loop()` runs on a `Task.Run` background task at ~60 Hz, calling `XInputGetState` and diff'ing against previous state to emit `ButtonEvent` / `StickEvent` / `ConnectionChanged`.
2. **UI thread** — WPF dispatcher. Owns `MainWindow`, `RadialMenuWindow`, all `_config` reads via UI handlers.
3. **Transcription thread** — `Task.Run` inside `SpeechService.OnStopped` for the HTTP/Whisper call so audio capture isn't blocked.

Rules baked into the code:
- **Every event invocation in `ControllerService` goes through `SafeInvoke`** ([Services/ControllerService.cs](src/CCXboxController/Services/ControllerService.cs)). If a subscriber throws, the loop must not die. The whole loop body is also wrapped in a top-level try/catch — exceptions go to `Logger.Error` and the next poll continues.
- **Use `Dispatcher.BeginInvoke`, not `Invoke`, when crossing into the UI thread from the polling thread.** Synchronous `Invoke` blocks polling if the UI is busy. This applies to `ActionDispatcher.HandleStick` (radial menu show/hide) and the `MainWindow` event handlers (`OnButton`, `OnStick`, `OnConnectionChanged`).
- `KeyboardInjector.TypeText` is called directly from the polling thread — `SendInput` is thread-safe and touches no WPF state, so no marshaling needed.

### Stick → radial overlay → text injection flow

This flow involves four files, and the contract between them is subtle:

1. `ControllerService` ([Services/ControllerService.cs](src/CCXboxController/Services/ControllerService.cs)) maintains `_prevLeft` / `_prevRight` and emits a `StickEvent` only when the cardinal direction changes. `StickToDirection` uses **hysteresis** (enter at magnitude 7849 — matches `XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE` so the menu pops as soon as the user starts tilting, not at half-press; exit at 5000) so drifty sticks don't oscillate between None and a direction.
2. `ActionDispatcher.HandleStick` ([Services/ActionDispatcher.cs](src/CCXboxController/Services/ActionDispatcher.cs)) tracks `_leftDir` / `_rightDir`. On `active=true` it `BeginInvoke`s the radial menu show. On `active=false` it **always** hides the menu (no state-conditioned hide — that was a bug); the binding for the last direction is typed via `KeyboardInjector`.
3. `RadialMenuWindow` ([Views/RadialMenuWindow.xaml.cs](src/CCXboxController/Views/RadialMenuWindow.xaml.cs)) is the overlay. Two non-negotiable behaviors:
   - **Extended window styles `WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`** are applied in `OnSourceInitialized`. Without `WS_EX_NOACTIVATE` the overlay would steal focus from VS Code and the "type at last-clicked location" feature breaks. `WS_EX_TRANSPARENT` makes it click-through.
   - **Watchdog timer** (1.5s `DispatcherTimer`) auto-hides the menu if no `ShowForStick`/`UpdateDirection` arrives — defense against the overlay getting stuck if events stop flowing (controller disconnect mid-push, stuck stick, etc.). `ControllerService` also synthesizes a `(None, active=false)` event for any non-None stick when it detects disconnection.
4. `KeyboardInjector` ([Services/KeyboardInjector.cs](src/CCXboxController/Services/KeyboardInjector.cs)) uses `SendInput` with `KEYEVENTF_UNICODE` per character (so Turkish characters work) and translates `\n` → VK_RETURN.

### Transcription provider abstraction

`ITranscriber` ([Services/ITranscriber.cs](src/CCXboxController/Services/ITranscriber.cs)) is the swap point between local Whisper and OpenAI API. `SpeechService.SetTranscriber` swaps it live without restarting recording state. Provider selection is in `AppConfig.Whisper.Provider` (`"local"` | `"openai"`), and `MainWindow.BuildTranscriber` is the single place that turns config into an `ITranscriber` instance — call `RebuildTranscriber()` after any setting change that affects transcription (provider, language, API key, model, local model availability).

The OpenAI API key never lives in `config.json` as plaintext. `SecretProtector` ([Services/SecretProtector.cs](src/CCXboxController/Services/SecretProtector.cs)) wraps DPAPI (CurrentUser scope) and the stored field is `ApiKeyProtected` (base64 ciphertext). Decryption only works on the same Windows user account that wrote it.

### Config & data paths

- Config: `%APPDATA%\CCXboxController\config.json` (`ConfigStore.ConfigPath`)
- Whisper models: `%APPDATA%\CCXboxController\models\ggml-small.bin` (`WhisperModelManager`, downloaded from Hugging Face on demand)
- Diagnostic log: `%APPDATA%\CCXboxController\app.log` (`Logger.Write`) — **first place to look** when something silently misbehaves. Errors in the polling loop and event handlers all funnel here.
- Autostart: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\CCXboxController` (`AutostartService`)

### Keys, sticks, and binding lookup

`AppConfig.Buttons` is a `Dictionary<string, ButtonBinding>` keyed by names like `"A"`, `"DPadUp"`, `"LeftStickPress"`. Stick directions are stored separately in `AppConfig.Sticks["LeftStick"|"RightStick"]` as a `StickBinding` with `Up/Down/Left/Right` slots — they are NOT in `Buttons` because they aren't digital. The list of binding entries shown in the UI lives in `MainWindow.ListEntries`; entries with a `.` (e.g. `"LeftStick.Up"`) are resolved through `ResolveBinding` which splits and dereferences into `Sticks`.

When adding a new button or stick direction, three places must agree:
1. `ButtonMap` (or stick logic) in [ControllerService.cs](src/CCXboxController/Services/ControllerService.cs)
2. `AppConfig.CreateDefault` in [Models/AppConfig.cs](src/CCXboxController/Models/AppConfig.cs)
3. `ListEntries` in [MainWindow.xaml.cs](src/CCXboxController/MainWindow.xaml.cs) (+ `EnsureAllKeys` if it's not a stick direction)

## Conventions specific to this codebase

- **Don't add Invoke calls from the polling thread.** Always BeginInvoke; the polling thread must never block on the UI.
- **Don't subscribe to controller events without a try/catch in the handler** (or rely on the fact that `ControllerService` already wraps subscribers in `SafeInvoke`). Either way, a thrown subscriber must not break the loop.
- **WPF window styles for overlays must be re-applied in `OnSourceInitialized`** (after the HWND exists), not in the constructor.
- When publishing changes, the previous instance often holds the `.exe` on Desktop — stop the process before `Copy-Item` (see how the session has done it: `Get-Process | Where-Object { $_.ProcessName -like '*CC Xbox*' } | Stop-Process -Force`).
