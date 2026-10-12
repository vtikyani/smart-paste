# Privacy

Smart Paste reads what you copy so it can paste the right part into each field. This page says exactly what leaves
your PC, when, and where it goes. It also lists what stays on your PC and how to delete it.

## The short version

- **Without a key, nothing you copy leaves your PC.**
- **With a key, two things can leave it:** copied text that looks like contact details, to be read by an AI model
  through OpenRouter; and a short description of the field you are in, with your copied values cut short, to Jev.
- **No telemetry.** No usage tracking, no analytics, no ads, no account.
- **Your keys stay in Windows Credential Manager**, and only ever go to the service they belong to.
- **Uninstalling removes everything Smart Paste stored.**

## What can leave your PC

### 1. Reading a copied block (OpenRouter)

Smart Paste turns a copied block into fields like first name, street, and ZIP code.

- **When:** you copy text, Smart Paste is on and not paused, an OpenRouter key is saved, and the text looks like
  contact details. That means it has at least two kinds of them (for example an email address and a phone number)
  and is at most 20,000 characters long.
- **What is sent:** the copied text.
- **Where:** [OpenRouter](https://openrouter.ai), which passes it to the reading model. That is Anthropic's Claude
  Haiku 5.5 (`anthropic/claude-haiku-5.5`) unless you pick another model in Settings.
- **What comes back:** the fields. Smart Paste keeps only values that really appear in what you copied.

Smart Paste also reads the block on your PC the moment you copy it, so Ctrl+V works even before the reply arrives,
and even with no key at all.

**Screenshots** (only when you turn on "Read screenshots"; it is off by default): Windows' built-in text recognition
(OCR) reads the screenshot on your PC. The image never leaves your PC. If the text it finds looks like contact
details, that text is handled like copied text, as described above.

### 2. Deciding what a field gets (Jev)

Jev is TypeSafe's decision model. It decides which of your copied values belongs in a field.

- **When:** a copied block is ready, a key is saved, and you click into a field. Smart Paste asks as soon as the
  field gets focus, so the answer is ready when you press Ctrl+V. If you turn off asking ahead in Settings, it asks
  when you press Ctrl+V.
- **What is sent:** a description of the field (its label, and text around it such as help text, nearby text, and
  the section it sits in) and the values from your copied block, each cut short and named by kind, like "First name"
  or "Street, shipping address".
- **Where:** directly to [TypeSafe](https://typesafe.ai) when a TypeSafe key is saved, otherwise through
  [OpenRouter](https://openrouter.ai). You can pick the route in Settings.

Fields that look sensitive are never described to Jev and never get a smart paste. That covers password fields, and
fields for card numbers, security codes and expiry dates, bank account and routing numbers, social security numbers
and tax IDs, and one-time or verification codes. They get a normal paste.

When a field has no label Smart Paste can read, it reads the small area just left of and above the field with
Windows' built-in OCR, on your PC. That image never leaves your PC. Only the label text it finds can become part of
the field description.

### 3. Checking a key

When you save or test a key, Smart Paste sends a small request with that key to its service (OpenRouter or
TypeSafe), to check that the key works. Nothing you copied is part of that request.

### 4. Updates

When "Check for updates" is on (it is by default), Smart Paste asks GitHub from time to time for the list of Smart
Paste releases, and downloads a new version when there is one. These are ordinary web requests. GitHub sees your IP
address and that the request comes from the updater. Nothing about you, your settings, or what you copy is sent.
You can turn update checks off in Settings.

During setup, if the .NET 10 Desktop Runtime is missing, the installer downloads it from Microsoft.

**Nothing else leaves your PC.** Crash reports are not sent anywhere. They stay in the logs folder on your PC.

## What Smart Paste never touches

- **Private copies.** Windows lets apps mark a copy as private, and password managers do this. Smart Paste ignores
  those copies completely: it does not read them, send them, or log them. It also ignores copies made in 1Password.
- **Some apps.** In admin windows (apps you ran as administrator), Remote Desktop, terminals, password managers, and
  apps you exclude in Settings, Ctrl+V works the normal way and Smart Paste does not look at the field.
- **Sensitive fields,** as listed above.
- **Anything, while it is off or paused.** Then it does not watch the clipboard or the keyboard, and it makes no
  requests with your data.

When Smart Paste pastes a value, it puts the value on the clipboard for a moment, marked so that Windows clipboard
history and cloud clipboard sync skip it. Then it puts your original clipboard back, unless you copied something new
in the meantime.

## What stays on your PC

| What | Where | How long |
| --- | --- | --- |
| Your copied block | Memory only, never written to disk | Until you copy something else, clear it, or quit; at most 15 minutes (5, 30, or 60 if you change it) |
| API keys | Windows Credential Manager, as `SmartPaste/OpenRouter` and `SmartPaste/TypeSafe`. Windows encrypts them for your account, and they stay on this PC. | Until you remove them or uninstall |
| Settings | `%LOCALAPPDATA%\SmartPaste\settings.json` | Until you uninstall |
| Learned labels | `%LOCALAPPDATA%\SmartPaste\label-cache.json`. Field labels and the kind of value each one wants (for example "postal code" means ZIP code). Never values. | Until you remove them or uninstall |
| Logs and crash reports | `%LOCALAPPDATA%\SmartPaste\logs` | 14 days |
| Recent pastes in the tray flyout | Memory only. The field label and the kind of value, never the value. | Until you quit |
| The app itself | `%LOCALAPPDATA%\SmartPaste.App` | Until you uninstall |

**What the logs hold:** event names (like "paste"), the kind of field (like `first_name`), the program name of the
app you pasted into (like `chrome.exe`), timings, counts, and the type of an error. Never copied text, values, field
labels, or error messages, because error messages can quote the text that caused them. Crash reports hold the error
type and where in Smart Paste's code it happened, never the message.

**The installer's own log:** Velopack, the installer and updater, keeps a log of install and update steps in
`%LOCALAPPDATA%\velopack`. It holds versions and folder paths, nothing you copied.

## Deleting your data

- **The copied block:** copy something else, clear it from the tray flyout, or quit Smart Paste.
- **Learned labels:** remove any or all of them on the Learned labels page in Settings.
- **Keys:** remove them in Settings.
- **Logs:** delete the `%LOCALAPPDATA%\SmartPaste\logs` folder at any time.
- **Everything:** uninstall Smart Paste from Windows Settings > Apps > Installed apps. That removes your keys,
  settings, learned labels, logs, the start-at-sign-in entry, and the app. Only the installer's own log in
  `%LOCALAPPDATA%\velopack` stays; you can delete it by hand.

## Other services and their policies

- **OpenRouter** handles requests made with your OpenRouter key, under
  [OpenRouter's privacy policy](https://openrouter.ai/privacy) and your OpenRouter account settings.
- **TypeSafe** handles requests made with your TypeSafe key, under
  [TypeSafe's privacy policy](https://typesafe.ai/legal/privacy-policy).
- **GitHub** hosts the releases that update checks read, under the
  [GitHub General Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement).

## Changes and questions

Changes to how Smart Paste handles data are listed in [CHANGELOG.md](CHANGELOG.md). Questions are welcome as a
[GitHub issue](https://github.com/vtikyani/smart-paste/issues). To report a privacy or security problem privately,
follow [SECURITY.md](SECURITY.md).
