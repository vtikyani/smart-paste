# Contributing to Smart Paste

Thanks for helping! Bug reports, ideas, docs fixes, and code are all welcome.

- **Found a bug?** Open a [bug report](https://github.com/vtikyani/smart-paste/issues/new/choose).
- **Have an idea?** Open a feature request first, so we can agree on the approach before you write code.
- **Found a security problem?** Please report it privately, as [SECURITY.md](SECURITY.md) explains.

Everyone taking part follows the [Code of Conduct](CODE_OF_CONDUCT.md).

## Set up

You need:

- Windows 10 (version 2004 or later) or Windows 11.
- The [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). `global.json` pins the version range.
- Git, and any C# editor: Visual Studio, VS Code with the C# Dev Kit, or Rider.
- PowerShell 7, only if you build release files.

```powershell
git clone https://github.com/vtikyani/smart-paste.git
cd smart-paste
dotnet build SmartPaste.slnx
```

Warnings are errors, so a build with a warning fails. Packages come only from nuget.org (see `nuget.config`).

## Test

```powershell
dotnet test SmartPaste.slnx
```

- `tests/SmartPaste.Core.Tests` covers the parsing, decision, formatting, and privacy logic. It builds and runs on
  any OS.
- `tests/SmartPaste.App.Tests` covers the Windows app layer. Its tests run one at a time, and each run uses a
  throwaway data folder, so they never touch your real settings or logs.

Continuous integration (CI) runs both on every pull request.

## Run Smart Paste from source

Always give a source run its own data folder:

```powershell
$env:SMARTPASTE_DATA_DIR = "$env:TEMP\SmartPaste-dev"
dotnet run --project src/SmartPaste.App
```

With `SMARTPASTE_DATA_DIR` set, this copy keeps its own settings, learned labels, and logs. It never registers itself
to start at sign-in, and it can run next to your installed copy. Without it, the run uses your real settings and
registers the build folder to start at sign-in.

Two things to know:

- **Keys are shared.** Both copies read the same Windows Credential Manager entries (`SmartPaste/OpenRouter` and
  `SmartPaste/TypeSafe`). Removing a key in one removes it for the other.
- **Only one copy should handle Ctrl+V.** Pause or quit your installed Smart Paste while you test.

## Try parsing and Jev against the real services

`tools/SmartPaste.LiveRun` sends a built-in sample contact (not your data) to OpenRouter and Jev, and prints what
comes back and how long it took. It reads keys from the `OPENROUTER_API_KEY` (required) and `TYPESAFE_API_KEY`
environment variables:

```powershell
$env:OPENROUTER_API_KEY = "<your OpenRouter key>"
dotnet run --project tools/SmartPaste.LiveRun
```

It can also read keys from the `env` object of a JSON file passed with `--keys <file>`. Add `--fixture` to also check
the form-label fixture (`tests/SmartPaste.Core.Tests/fixtures/form-labels.json`) with the local rules and Jev, and
print the pass rate. Run it from the repository root.

## End-to-end tests

`tests/SmartPaste.E2E` drives real forms on a real desktop: it opens forms, moves the focus, types, and uses the
clipboard. CI does not run it. Run it by hand after you change the keyboard hook, the clipboard code, UI Automation,
or the paste flow, and before each release:

1. Use a desktop you can leave alone for a few minutes. Close other apps.
2. Pause or quit your installed Smart Paste.
3. Run `dotnet test tests/SmartPaste.E2E`, and don't touch the keyboard or mouse until it finishes.

The tests run Smart Paste with its own data folder, so your settings and learned labels stay as they are.

## How we write code

[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) explains how the app fits together. Please read it before larger changes.

- **No placeholders.** No stubs, TODOs, fake data, or demo content in shipped code. Every change is complete and
  works for real.
- **C# style:** file-scoped namespaces, nullable reference types on, warnings as errors. `.editorconfig` has the
  details. Comments explain *why*, briefly.
- **Every network call** has a timeout and a fallback that still pastes something sensible.
- **Threads:** change `AppState` on the UI thread only.
- **Win32 interop** lives next to the code that uses it, with `SetLastError` where the API sets it.
- **Tests:** Core logic gets unit tests. Windows behavior that can run without a desktop goes in `App.Tests`.
  Desktop behavior goes in `tests/SmartPaste.E2E`.
- **Docs:** plain, short, friendly English. Short sentences. Explain any technical term the first time you use it.

## Privacy rules

These are hard rules. A pull request that breaks one is not merged.

- The log holds event names, allow-listed field names, numbers, and short tokens. Never clipboard text, values, field
  labels, or exception messages. `Log` enforces this; don't work around it.
- Copied text goes to the parsing model only when it looks like contact details. The field description and trimmed
  values go to Jev. Nothing else leaves the PC. A new network call that carries user data needs a change to
  [PRIVACY.md](PRIVACY.md) in the same pull request.
- When Smart Paste is off or paused, it watches nothing and makes no calls with user data.
- API keys live in Windows Credential Manager, never in files, logs, or settings.
- The copied block lives in memory only, and expires.
- Sensitive fields (passwords, card numbers, security codes, social security numbers, one-time codes) never get a
  smart paste and are never described to Jev.

## Dependencies

- Package versions live in `Directory.Packages.props` (central package management). Projects list packages without
  versions.
- A new package that ships in the app needs an entry in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), with its
  license checked from the package itself.
- The Velopack library (`Directory.Packages.props`) and the vpk tool (`.config/dotnet-tools.json`) must stay on the
  same version. Dependabot updates them together.

## Pull requests

1. Fork the repository and create a branch from `main`.
2. Keep each pull request to one change, with tests.
3. Add a line for user-visible changes under **Unreleased** in [CHANGELOG.md](CHANGELOG.md).
4. Run `dotnet build SmartPaste.slnx` and `dotnet test SmartPaste.slnx`.
5. Open the pull request and fill in its checklist. CI must pass before review.

Maintainers cut releases as [docs/RELEASING.md](docs/RELEASING.md) describes.
