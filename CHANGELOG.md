# Changelog

All notable changes to Smart Paste are listed here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

The work for version 1.0.0, the first public release. Smart Paste was rebuilt from the ground up.

### Added

- Jev, TypeSafe's decision model, decides what gets pasted on every paste, and says how sure it is. When it is
  unsure, a picker opens next to the field: press 1 to 9, or use the arrow keys and Enter.
- Smart Paste asks Jev as soon as you click into a field, so Ctrl+V feels instant.
- One OpenRouter key powers both reading the copied block (Claude Haiku 5.5 by default) and Jev. A TypeSafe key is an
  optional direct route to Jev.
- Without any key, Smart Paste still works, with a built-in label list and the labels it learns on your PC.
- Win+Alt+V opens a picker with every copied value, and typing filters it. The shortcut can be changed.
- Field hints show what Ctrl+V will paste into the focused field.
- A Windows 11 style interface: tray flyout, settings, and a welcome tour with a practice form. Light and dark.
- Pause, per-app exclusions, and a Learned labels page to review and remove what Smart Paste learned.
- Optional reading of copied screenshots with Windows' built-in offline text recognition (OCR). Off by default.
- An installer that installs the .NET 10 Desktop Runtime when it is missing, a portable zip, and automatic updates
  from GitHub Releases. Builds for x64 and ARM64 PCs. When an update is ready, the About page shows what's new.
- `SmartPaste.exe --exit` closes the running copy the normal way, for scripts and installers.
- Uninstalling removes saved keys, settings, learned labels, logs, and the start-at-sign-in entry.

### Changed

- Rebuilt as a WPF app (`SmartPaste.exe`) on .NET 10.
- Pasting keeps your clipboard: the original comes back as soon as the app reads the value, and smart-paste values
  stay out of Windows clipboard history.
- Settings from the first prototype carry over.

### Security

- Before a copied block goes to the parsing model, lines labeled as secrets are removed, and card numbers, social
  security numbers, and IBANs are masked.
- Sensitive fields (passwords and PINs, card and bank details, ID numbers, one-time codes, security questions) never
  get a smart paste and are never described to Jev.
- Copies made in password managers, Remote Desktop, Hyper-V, and apps you exclude are never read.
- Logs never contain clipboard text, values, field labels, or error messages.
- API keys are stored in Windows Credential Manager, never in files.
- Release files carry no local folder names; the release script checks every file before packing.

[Unreleased]: https://github.com/vtikyani/smart-paste/commits/main
