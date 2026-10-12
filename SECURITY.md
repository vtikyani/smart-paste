# Security

Smart Paste sees what you copy and types into other apps, so we take security problems seriously. Thank you for
helping keep its users safe.

## Reporting a problem

Please report security problems privately, not in a public issue.

1. Open the repository's [Security tab](https://github.com/vtikyani/smart-paste/security).
2. Click **Report a vulnerability**. This opens a private report that only the maintainers can see.
3. Describe the problem, the Smart Paste and Windows versions, and the steps to see it. A short proof of concept
   helps. Please leave out real personal data and real API keys.

We will reply in the report, work on a fix with you, and publish a release with the fix. Unless you would rather stay
anonymous, we will credit you in the release notes.

Please give us a fair chance to ship a fix before you share details publicly.

## Supported versions

Smart Paste updates itself, so only the latest release gets security fixes. If you turned off update checks, update
from the [Releases page](https://github.com/vtikyani/smart-paste/releases).

## What is in scope

Problems in this repository's code and in the release files built from it. For example:

- Copied text, values, or field labels written to disk, logs, or the network beyond what [PRIVACY.md](PRIVACY.md)
  describes.
- API keys leaving Windows Credential Manager, or going anywhere but their own service.
- Smart Paste acting where it promises not to: password and other sensitive fields, admin windows, Remote Desktop,
  Hyper-V, terminals, password managers, private copies, or copies made in an app it skips.
- A secret in copied text (a password, a card or social security number) reaching the parsing model, when
  [PRIVACY.md](PRIVACY.md) says it is taken out first.
- Crafted clipboard content, field labels, or web pages that make Smart Paste run code, crash in a way an attacker
  controls, or paste into the wrong place.
- The update or install path accepting files that are not the official release files.
- The build and release workflows in `.github/workflows`.

## What is out of scope

- Problems in OpenRouter, TypeSafe, GitHub, or Windows itself. Please report those to them.
- Attacks that already need admin rights or full control of the PC.
- The SmartScreen warning when you run the installer. Releases are not code-signed yet, as the README explains.
