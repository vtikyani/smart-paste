# Smart Paste

Copy a block of text (a resume, an address, a contact card) or snip a screenshot. Then click into any
form field and press **Ctrl+V**: Smart Paste pastes only the value that field wants (first name, zip,
email...). **Ctrl+Shift+V** is never touched, so the app pastes your original clipboard.

Windows 10/11, x64. Tray app, no window.

**Status:** the core (parsing, label matching, value formatting, privacy rules) is covered by 60 unit tests, a
label fixture, and a blind holdout of 46 unseen form labels (42/46 on first run). The Windows shell (keyboard hook, paste and
clipboard restore, UI Automation labels in Chrome and Edge, OCR crop) has not been validated on real
hardware yet.

## Install

1. Install the .NET 10 Desktop Runtime (one time):
   ```
   winget install Microsoft.DotNet.DesktopRuntime.10
   ```
2. Publish the app (see Build below) and copy `dist/SmartPaste` anywhere (for example
   `%LOCALAPPDATA%\Programs\SmartPaste`).
3. Run `SmartPaste.App.exe`. A tray icon appears (gray = off, blue = on, green = a copied block is armed).

## Enter your API keys

Right-click the tray icon, then **Settings...**.

- **OpenRouter key**: parses copied text and screenshots (model `anthropic/claude-haiku-4.5`).
  Without it, only email, phone, and zip are recognized.
- **TypeSafe key** (optional): Jev picks which value fits a field label it has not seen before.
  When it is missing, Jev runs through your OpenRouter key instead.

Keys are stored in Windows Credential Manager (generic credentials `SmartPaste/OpenRouter` and
`SmartPaste/TypeSafe`), never in a file. To remove them, open Credential Manager and delete those two
entries.

## How it decides

1. At copy time the block is parsed once into a profile held in memory. The profile is cleared by the
   next copy, or after 15 minutes.
2. Ctrl+V is intercepted only while a profile is armed. The focused element must be a text box or combo
   box; anything else (a document, an editor, a terminal) gets a normal paste.
3. The field's label is read with UI Automation. If the field has no readable label, a small area left
   of and above the caret is read with local OCR (PP-OCRv5; the models download on first use).
4. The label is matched against a local cache (about 230 common labels plus what you have used). A miss
   asks Jev. When two values fit (billing vs shipping) or Jev is unsure, a small picker appears: press
   1-9, or Esc for a normal paste.
5. The value is formatted for the field (NJ vs New Jersey, phone digits vs dashes, date formats) and
   pasted as a real paste, so Ctrl+Z works. Your clipboard is restored right after, unless you copied
   something new in the meantime.

## What it never touches

- Anything copied from 1Password, or any clipboard item that Windows marks private
  (`ExcludeClipboardContentFromMonitorProcessing`, or history/cloud upload disabled): no parse, no
  network, no log.
- Password fields, fields labeled like passwords, card numbers, security codes, or SSNs, elevated
  (admin) windows, Remote Desktop and Hyper-V windows, and 1Password itself.
- When Smart Paste is off, it makes no network calls and does not watch the clipboard or keyboard.

Logs are in `%LOCALAPPDATA%\SmartPaste\logs` and hold event names, key names, reasons, and timings
only, never clipboard contents or values. Learned labels are in
`%LOCALAPPDATA%\SmartPaste\label-cache.json`.

## Build (from macOS or Windows)

```
dotnet test tests/SmartPaste.Core.Tests
dotnet publish src/SmartPaste.App -c Release -r win-x64 --self-contained false -o dist/SmartPaste
```

`tools/SmartPaste.LiveRun` runs the parser and label mapping against the real APIs (dev only; reads
`OPENROUTER_API_KEY` and `TYPESAFE_API_KEY` from the environment, or from the `env` object of a JSON file passed with `--keys <file>`).

## License

MIT
