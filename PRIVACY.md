# Privacy

Smart Paste reads what you copy so it can paste the right part into each field. This page says exactly what leaves
your PC, when, and where it goes. It also lists what stays on your PC and how to delete it.

## The short version

- **Without a key, nothing you copy leaves your PC.**
- **With a key, two things can leave it:** copied text that looks like contact details, with its secrets taken out,
  to be read by an AI model through OpenRouter; and a description of the field you are in, with your copied values,
  to Jev.
- **No telemetry.** No usage tracking, no analytics, no ads, no account.
- **Your keys stay in Windows Credential Manager**, and only ever go to the service they belong to.
- **Uninstalling removes everything Smart Paste stored.**

## What can leave your PC

### 1. Reading a copied block (OpenRouter)

Smart Paste turns a copied block into fields like first name, street, and ZIP code.

- **When:** you copy text, Smart Paste is on and not paused, an OpenRouter key is saved, you did not copy it in an
  app Smart Paste skips (see [below](#what-smart-paste-never-touches)), and the text looks like contact details.
  That means it holds at least two kinds of personal contact details, each counted once:
  - an email address;
  - a phone number written like one (with dashes, dots, brackets, or a +), or on a line labeled as a phone;
  - a street address;
  - a ZIP or postal code next to its city or state, or on or under a street line;
  - a line labeled as a name, email, phone, address, city, state, or ZIP code.

  A "Company" line counts only next to one of these. Links and dates never count. Lines labeled as secrets, or as
  someone else's details (like an emergency contact), are not counted. The text can be at most 20,000 characters.
- **What is sent:** the copied text, with its secrets taken out first:
  - Lines labeled as secrets are removed. That covers passwords and PINs; card numbers, security codes, and expiry
    dates; bank account, routing, IBAN, and SWIFT numbers; social security, tax, ID, passport, and driver's license
    numbers; one-time codes; security answers; and API keys. A label on a line of its own ("Account number") takes
    the line below it along.
  - Card numbers, US social security numbers (like 123-45-6789), and IBANs are replaced with "[removed]" wherever
    they appear, labeled or not.
  - Everything else is sent as you copied it. Bank account and routing numbers have no fixed shape, so they are
    removed only when they are labeled.
- **Where:** [OpenRouter](https://openrouter.ai), which passes it to the reading model. That is Anthropic's Claude
  Haiku 5.5 (`anthropic/claude-haiku-5.5`) unless you pick another model in Settings.
- **What comes back:** the fields. Smart Paste keeps only values that really appear in the text it sent, and never
  keeps a card number, social security number, or IBAN.

Smart Paste also reads the block on your PC the moment you copy it, so Ctrl+V works even before the reply arrives,
and even with no key at all.

**Screenshots** (only when you turn on "Read text from screenshots"; it is off by default): Windows' built-in text
recognition (OCR) reads the screenshot on your PC. The image never leaves your PC. If the text it finds looks like
contact details, that text is handled like copied text, as described above.

### 2. Deciding what a field gets (Jev)

Jev is TypeSafe's decision model. It decides which of your copied values belongs in a field.

- **When:** a copied block is ready, a key is saved, and you click into a field. With "Ask Jev ahead for faster
  pastes" on (it is by default), Smart Paste asks as soon as the field gets focus, so the answer is ready when you
  press Ctrl+V. With it off, Smart Paste asks when you press Ctrl+V.
- **What is sent:**
  - The field: its label, the name screen readers use, its placeholder, help text, type, and length limit, and its
    id when that reads like words.
  - Up to six nearby texts and section names, and label text read from the screen when the field has no label.
  - The name of the app you're in, like "chrome".
  - Every value from your copied block, named by kind, like "First name: Jordan" or "Street (shipping address):
    2 Oak Ave". Each value is cut to 60 characters, so most are sent whole.
- **Where:** you choose under "Send Jev requests through" on the Jev & AI page in Settings:
  - **Automatic** (the default) uses the one service you have a key for. With both keys, it asks
    [TypeSafe](https://typesafe.ai) first. If TypeSafe fails (a rejected key, no credits, an outage), it asks again
    through [OpenRouter](https://openrouter.ai), and skips TypeSafe for a while.
  - **TypeSafe** or **OpenRouter** sends every request to that service only.

Fields that look sensitive are never described to Jev and never get a smart paste. That covers password fields, and
fields for passwords and PINs; card numbers, security codes, and expiry dates; bank account, routing, IBAN, and
SWIFT numbers; social security, national ID, tax, passport, and driver's license numbers; one-time and verification
codes; and security questions. They get a normal paste.

When a field has no label Smart Paste can read, it reads the small area just left of and above the field with
Windows' built-in OCR, on your PC. That image never leaves your PC. Only the label text it finds can become part of
the field description.

### 3. Checking a key

When you save or test a key, Smart Paste sends a small request with that key to its service (OpenRouter or
TypeSafe), to check that the key works. Nothing you copied is part of that request.

### 4. Updates

When "Check for updates automatically" is on (it is by default), Smart Paste asks GitHub from time to time for the
list of Smart Paste releases. When there is a new version, it downloads it in the background and installs it the
next time Smart Paste starts. These are ordinary web requests. GitHub sees your IP address and that the request comes
from the updater. Nothing about you, your settings, or what you copy is sent. You can turn this off on the About page
in Settings.

During setup, if the .NET 10 Desktop Runtime is missing, the installer downloads it from Microsoft.

**Nothing else leaves your PC.** Crash reports are not sent anywhere. They stay in the logs folder on your PC. The
welcome tour's practice form works only with its own sample details, never with what's on your clipboard.

## What Smart Paste never touches

- **Private copies.** Windows lets apps mark a copy as private, and password managers do this. Smart Paste ignores
  those copies completely: it does not read them, send them, or log them.
- **Copies made in some apps,** even when they are not marked private. Smart Paste never reads what you copy in:
  - password managers: 1Password, Bitwarden, Buttercup, Enpass, iCloud Passwords, Kaspersky Password Manager,
    KeePass, KeePassXC, Keeper, KeeWeb, NordPass, Passbolt, Password Depot, Password Safe, Proton Pass, RoboForm,
    SafeInCloud, and Sticky Password;
  - Remote Desktop, Windows App, and Hyper-V, which pass your clipboard on to another computer;
  - apps you exclude in Settings.
- **Pasting in some apps.** In those same apps, and in admin windows (apps you ran as administrator) and terminals,
  Ctrl+V works the normal way and Smart Paste does not look at the field. The Privacy page in Settings lists them.
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
| Learned labels | `%LOCALAPPDATA%\SmartPaste\label-cache.json`. Field labels and the kind of value each one wants (for example "mailing postcode" means ZIP code). Only labels an app shows for its field, never one that holds a value you copied. Never values. | Until you remove them or uninstall |
| Logs and crash reports | `%LOCALAPPDATA%\SmartPaste\logs` | 14 days. Older ones are deleted each time Smart Paste starts. |
| Recent pastes in the tray flyout | Memory only. The field label, the kind of value, and the app, never the value. | Until you quit |
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
  `%LOCALAPPDATA%\velopack` stays; you can delete it by hand. If the installer set up .NET for you, the .NET runtime
  stays installed (other apps can use it), and Microsoft's installer file for it (`inst-dotnet-*.exe`) stays in
  your Downloads folder.

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
