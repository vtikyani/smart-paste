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
| `tests/SmartPaste.E2E` | Windows | End-to-end tests that drive real forms on a real desktop. A console app, run by hand ([CONTRIBUTING.md](../CONTRIBUTING.md#end-to-end-tests)). |
| `tools/SmartPaste.LiveRun` | `net10.0` | Developer tool: runs parsing and Jev against the real APIs. |
| `tools/SmartPaste.IconGen` | Windows | Developer tool: renders the app icon and the logo images from the vector mark in `UI/Brand/BrandArt.cs`. |

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

## Start and exit

- One copy runs per Windows session (`SingleInstance`). Starting it again opens Settings in the running copy.
- At sign-in, Windows starts it with `--background`, so it stays quietly in the tray. A manual start shows the
  welcome tour on the first run and Settings after that.
- `SmartPaste.exe --exit` asks the running copy to exit and waits for it. The tray's Exit, an update, and the end
  of the Windows session exit the same normal way: the keyboard hook stops, held keys are let go, and the
  clipboard is put back.
- WPF's `Application.Run` is not used: its hidden window exits as soon as Windows *asks* whether the session may
  end, even when the user then cancels. `App.RunUntilExit` runs the dispatcher with a `SessionEndWatcher` and
  exits only when the session really ends.

## Copy flow

1. `ClipboardWatcher` gets `WM_CLIPBOARDUPDATE`. Our own writes are ignored by sequence number.
2. Privacy check first. Items Windows marks private (`ExcludeClipboardContentFromMonitorProcessing`, or
   history/cloud disabled) are skipped: no parse, no network, no log. So are copies made in password managers
   (`ClipboardPrivacy`), remote sessions and Hyper-V, and apps the user excluded.
3. Any new copy disarms the previous block.
4. Text that looks like a block of someone's contact details (`TextHeuristics.LooksMultiField`: at least two kinds,
   from an email, a phone, a street, a postal code by its city, and labeled lines; links and dates never count) is
   parsed locally right away (`LocalExtractor`), so Ctrl+V works within milliseconds even offline.
5. Then, with an OpenRouter key, the parsing model (`anthropic/claude-haiku-5.5` by default) reads the block.
   Before it leaves the PC, `Redaction` removes lines labeled as secrets and masks card numbers, SSNs, and IBANs.
   Values the model returns that do not appear in the text it was sent are dropped (`ProfileGrounding`), and
   `ProfileParser.Merge` fills the model's gaps only with sure local values (an email, or a phone written like
   one). The result replaces the local one.
6. Screenshots (only when "Read text from screenshots" is on) go through the built-in Windows OCR first. Only the
   resulting text continues, through the same path as copied text. The image never leaves the PC.

## Paste flow: Jev decides every paste

1. `KeyboardHook` sees Ctrl+V (not Ctrl+Shift+V, which is never touched). It swallows the key only if a block
   is armed and `InterceptGate` allows the foreground app: not elevated, not a terminal, not Remote Desktop or
   Hyper-V, not a password manager, not excluded by the user.
2. From that moment until the paste is done, the hook **buffers every key the user types** and replays them in
   order afterwards (`InputQueue`). So Ctrl+V, Tab, Ctrl+V never lands a value in the wrong field. The buffer has
   a hard limit of 4 seconds, so the keyboard can never get stuck.
3. `FocusInspector` reads the focused element through UI Automation: label, accessible name, help text,
   placeholder, automation id, nearby text, the sections it sits in, max length, password flag, and the app it
   belongs to. Only editable text boxes and combo boxes qualify. Anything else gets a normal paste.
4. Fields with no readable label get a label from OCR of the area just left of and above the field.
5. Sensitive fields (`KeywordRules.IsSensitive`: passwords and PINs, card and bank details, ID numbers, one-time
   codes, security questions) stop here with a normal paste. They never reach Jev, the parsing model, or the log.
6. `FieldDecider` asks **Jev** (`JevQuestion`). The state is the field's context. The options are every distinct
   value in the armed block ("Street (shipping address): 2 Oak Ave", "First name: Jordan", ..., and "none"), each
   cut to 60 characters. Jev answers with a choice, a confidence, and a probability for each option:
   - "none" is the most probable option: normal paste;
   - confidence above 0.8 **and** the chosen value at least 0.7 probable: paste that value;
   - otherwise: the picker shows Jev's three most probable values.
7. No key, offline, or a Jev error: the local label list decides instead (labels learned on this PC, the
   built-in list, then keyword rules). A field about someone else (emergency contact, reference) gets a normal
   paste.
8. **Ask ahead.** While a block is armed, `FocusPrefetcher` asks Jev about each field as soon as it gets focus,
   so the answer is usually ready before Ctrl+V. An answer is reused only when Jev gave it. An optional hint next
   to the field shows what Ctrl+V will paste.
9. `ValueFormatter` shapes the value for the field (NJ vs New Jersey, phone formats, date formats, ZIP+4).
10. `ClipboardPaster` pastes it as a real paste, so Ctrl+Z works:
    - check that the same field still has the focus (by its UI Automation runtime id) and that its window is
      still in front;
    - snapshot the clipboard;
    - put the value on it with **delayed rendering**, so Windows tells us the exact moment the target app reads
      it, and mark it private so clipboard history and cloud sync skip it;
    - send Ctrl+V;
    - once the target has read the value (or after a timeout), restore the original clipboard, unless the user
      copied something new in the meantime.

The picker never takes focus from the target app. It is a non-activating window; keys reach it through
`IKeyCapture` on the keyboard hook. In a picker that Ctrl+V opened, Escape pastes the clipboard as it is; in the
Win+Alt+V picker, Escape only closes it.

### Learning

When Jev pastes a value on its own, or the user picks one, `FieldDecider` remembers the field's label and the kind
of value in `LabelCache`, for the offline fallback. Only text the app labels the field with is learned (never help
text, ids, or OCR), and never a label that is built in, vague ("Number"), sensitive, about someone else, or that
contains one of the copied values. The Learned labels page lists them, and can remove them.

### When a service fails

`ServiceGuard` holds calls back after a failure, so pastes use the local rules at once instead of waiting: 30
minutes after a rejected key, 10 after "no credits", 1 after an unusable reply, 30 seconds after rate limiting, and
20 seconds after a network or server error. A single timeout holds nothing back; three in a row count as
unreachable. A new key resets it. With both keys on the Automatic route, a route that failed while the other one
answered is skipped for a while (`DecisionService`). The tray and Settings show the current status.

## Privacy rules

These are hard rules. Code review rejects changes that break them.

- The log holds event names, allow-listed field names, numbers, and short tokens. Never clipboard text,
  values, labels, or exception messages (`Log` enforces this).
- Copied text goes to the parsing model only when it looks like contact details, and only after `Redaction` takes
  its secrets out. Field context plus the block's values (each cut to 60 characters) go to Jev. Nothing else
  leaves the PC.
- When Smart Paste is off or paused, it watches nothing and makes no calls with user data.
- API keys live in Windows Credential Manager, never in files.
- The armed block lives in memory only and expires (15 minutes by default).

## Data on disk

All under `%LOCALAPPDATA%\SmartPaste` (or `SMARTPASTE_DATA_DIR` for isolated test runs):

| File | Holds |
| --- | --- |
| `settings.json` | Settings (`AppSettings`). Atomic writes. Migrates the v1 format. |
| `label-cache.json` | Labels learned on this PC (label to field type). No values. |
| `logs/` | Privacy-safe event logs and crash reports. Files older than 14 days are deleted at start. |

Uninstalling removes all of it, plus the saved keys and the start-at-login entry.

## Conventions

- C#: file-scoped namespaces, nullable on, warnings are errors. Comments explain *why*, briefly.
- No placeholder code, mock data, or demo content in shipped code.
- Every external call has a timeout and a fallback that still pastes something sensible.
- Change `AppState` on the UI thread only.
- Win32 interop lives next to the code that uses it, with `SetLastError` where the API sets it.
- Tests: Core logic gets unit tests; Windows behavior that can be tested headlessly goes in `App.Tests`;
  desktop behavior is covered by `tests/SmartPaste.E2E`.
