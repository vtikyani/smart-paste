# Architecture

Smart Paste is a Windows tray app. You copy a block of text (a resume, an address, a contact card) or,
optionally, a screenshot. Then you click into a form field and press **Ctrl+V**: Smart Paste pastes only the
value that field wants. **Jev**, TypeSafe's decision model, makes that choice on every paste.

## Projects

| Project | Target | What it holds |
| --- | --- | --- |
| `src/SmartPaste.Core` | `net10.0` | Platform-neutral logic: parsing copied text, Jev decisions, formatting, privacy rules. No Windows APIs, so it builds and tests anywhere. |
| `src/SmartPaste.App` | `net10.0-windows10.0.19041.0` | The WPF tray app (`SmartPaste.exe`): keyboard hook, clipboard, UI Automation, OCR, UI. |
| `tests/SmartPaste.Core.Tests` | `net10.0` | Unit tests for Core, plus real-world form-label fixtures. |
| `tests/SmartPaste.App.Tests` | Windows | Unit tests for the app layer. Run one at a time, against a temp data folder. |
| `tests/SmartPaste.E2E` | Windows | End-to-end tests that drive real forms on a real desktop. Run by hand. |
| `tools/SmartPaste.LiveRun` | `net10.0` | Developer tool: runs parsing and Jev against the real APIs. |

`SmartPaste.App` folders:

- `Infrastructure/`: paths, settings, credentials, logging, crash reports, single instance, start at login, updates.
- `Shell/`: Windows integration: keyboard hook, hotkeys, clipboard watcher and paster, UI Automation, OCR,
  caret location, the intercept gate.
- `Services/`: app logic that ties Core to the shell: app state, copy pipeline, paste service, focus
  prefetcher, contracts between layers (`Contracts.cs`).
- `UI/`: tray icon and flyout, settings, welcome tour, picker and chips, theme and brand art.

## Threads

| Thread | Runs |
| --- | --- |
| UI (WPF dispatcher) | Windows, the clipboard watcher and paster (they own message windows), hotkeys, all `AppState` changes. |
| Keyboard hook | A low-level keyboard hook with its own message loop. Decides in microseconds; never blocks. |
| UI Automation | One dedicated MTA thread for all UIA calls, with UIA's own timeouts set, so a frozen app can't stall the rest. |
| Thread pool | Network calls (OpenRouter, Jev) and OCR. |

## Copy flow

1. `ClipboardWatcher` gets `WM_CLIPBOARDUPDATE`. Our own writes are ignored by sequence number.
2. Privacy check first. Items Windows marks private (`ExcludeClipboardContentFromMonitorProcessing`, or
   history/cloud disabled) and copies owned by password managers are skipped: no parse, no network, no log.
3. Any new copy disarms the previous block.
4. Text that looks like several contact fields (`TextHeuristics.LooksMultiField`) is parsed locally right away
   (`LocalExtractor`), so Ctrl+V works within milliseconds even offline. Then, with an OpenRouter key, the
   parsing model (`anthropic/claude-haiku-5.5` by default) reads the block and the richer result replaces the
   local one. Values the model returns that do not appear in the copied text are dropped (grounding check).
5. Screenshots (only when "Read screenshots" is on) go through the built-in Windows OCR first. Only the
   resulting text continues, through the same path as copied text. The image never leaves the PC.

## Paste flow: Jev decides every paste

1. `KeyboardHook` sees Ctrl+V (not Ctrl+Shift+V, which is never touched). It swallows the key only if a block
   is armed and `InterceptGate` allows the foreground app (not elevated, not Remote Desktop, not a password
   manager, not excluded by the user).
2. From that moment until the paste is done, the hook **buffers every key the user types** and replays them in
   order afterwards. So Ctrl+V, Tab, Ctrl+V never lands a value in the wrong field. The buffer has a hard time
   limit, so the keyboard can never get stuck.
3. `FocusInspector` reads the focused element through UI Automation: label, help text, placeholder, nearby
   text, the sections it sits in, max length, password flag. Only editable text boxes and combo boxes qualify.
   Anything else gets a normal paste.
4. Fields with no readable label get a label from OCR of the area just left of and above the field.
5. `FieldDecider` asks **Jev**. The state is the field's context. The options are every distinct value in the
   armed block ("Street, shipping address: 2 Oak Ave", "First name: Jordan", ..., and "none"). Jev returns a
   choice with calibrated probabilities:
   - confident (at least 0.8) and not "none": paste that value;
   - confident "none": normal paste;
   - unsure: the picker shows Jev's top choices.
6. No key, offline, or a Jev error: the local label list decides instead (labels learned on this PC, the
   built-in list, then keyword rules).
7. **Ask ahead.** While a block is armed, the focus prefetcher asks Jev about each field as soon as it gets
   focus, so the answer is usually ready before Ctrl+V. An optional hint next to the field shows what Ctrl+V
   will paste.
8. `ValueFormatter` shapes the value for the field (NJ vs New Jersey, phone formats, date formats, ZIP+4).
9. `ClipboardPaster` pastes it as a real paste, so Ctrl+Z works:
   - snapshot the clipboard;
   - put the value on it with **delayed rendering**, so Windows tells us the exact moment the target app reads
     it, and mark it private so clipboard history and cloud sync skip it;
   - send Ctrl+V;
   - once the target has read the value (or after a timeout), restore the original clipboard, unless the user
     copied something new in the meantime.
10. Sensitive fields (password, card number, security code, SSN, one-time codes) never reach Jev, the parsing
    model, or the log. They get a normal paste.

The picker never takes focus from the target app. It is a non-activating window; keys reach it through
`IKeyCapture` on the keyboard hook.

## Privacy rules

These are hard rules. Code review rejects changes that break them.

- The log holds event names, allow-listed field names, numbers, and short tokens. Never clipboard text,
  values, labels, or exception messages (`Log` enforces this).
- Copied text goes to the parsing model only when it looks like contact details. Field context plus trimmed
  candidate values go to Jev. Nothing else leaves the PC.
- When Smart Paste is off or paused, it watches nothing and makes no calls with user data.
- API keys live in Windows Credential Manager, never in files.
- The armed block lives in memory only and expires (15 minutes by default).

## Data on disk

All under `%LOCALAPPDATA%\SmartPaste` (or `SMARTPASTE_DATA_DIR` for isolated test runs):

| File | Holds |
| --- | --- |
| `settings.json` | Settings (`AppSettings`). Atomic writes. Migrates the v1 format. |
| `label-cache.json` | Labels learned on this PC (label to field type). No values. |
| `logs/` | Privacy-safe event logs and crash reports, kept 14 days. |

Uninstalling removes all of it, plus the saved keys and the start-at-login entry.

## Conventions

- C#: file-scoped namespaces, nullable on, warnings are errors. Comments explain *why*, briefly.
- No placeholder code, mock data, or demo content in shipped code.
- Every external call has a timeout and a fallback that still pastes something sensible.
- Change `AppState` on the UI thread only.
- Win32 interop lives next to the code that uses it, with `SetLastError` where the API sets it.
- Tests: Core logic gets unit tests; Windows behavior that can be tested headlessly goes in `App.Tests`;
  desktop behavior is covered by `tests/SmartPaste.E2E`.
