# Releasing

A release is one pushed tag. The Release workflow (`.github/workflows/release.yml`) does the rest: it tests, builds,
packs, and publishes the GitHub release. Installed copies of Smart Paste find the new version on their next update
check.

## Cut a release

1. **Check `main` is green.** CI must pass on the commit you will release.
2. **Run the end-to-end tests** on a real desktop, as [CONTRIBUTING.md](../CONTRIBUTING.md#end-to-end-tests)
   describes. CI can't run them, and they cover the keyboard hook, the clipboard, and pasting into real forms.
3. **Write the release notes.** In `CHANGELOG.md`, rename `## [Unreleased]` to the new version and today's date,
   for example `## [1.2.0] - 2026-11-03`. Add a new, empty `## [Unreleased]` above it, and update the links at the
   bottom. These notes become the text of the GitHub release, and Smart Paste shows them on its About page when the
   update is ready to install. So write them for users.
4. **Set the version.** Put the same version in `<Version>` in `Directory.Build.props`, so local builds match.
5. **Merge** that change to `main` and wait for CI.
6. **Tag and push** that commit:

   ```powershell
   git tag v1.2.0
   git push origin v1.2.0
   ```

7. **Watch the run** under Actions > Release. It:
   - reads the version from the tag (`v1.2.0` becomes `1.2.0`) and stops if `CHANGELOG.md` has no section for it;
   - downloads the previous release, so it can build delta updates (small downloads with only the changed bytes);
   - runs `build/publish.ps1`: the unit tests, then publish and pack for `win-x64` and `win-arm64`;
   - uploads the x64 files as a draft release named "Smart Paste 1.2.0", then adds the arm64 files and publishes it.
8. **Check the release page.** Each architecture has a `Setup.exe`, a `Portable.zip`, a full package, a delta package
   (from the second release on), and a `releases.win-*.json` feed that installed copies read.

### Version numbers

- Versions follow Semantic Versioning: `1.2.0`, `1.2.1`, `1.3.0`.
- A version can only go up. Installed copies ignore releases that are not newer than what they run.
- Never reuse a version that was published, even briefly. Some PCs may already have it. Release a new patch version
  instead.
- A tag with a suffix, like `v1.3.0-beta.1`, makes a GitHub pre-release. Installed copies never update to
  pre-releases; testers install them from the release page.

### If the release fails

- **Before the upload step** nothing is published. Fix the problem, then move the tag to the fixed commit:
  `git tag -d v1.2.0`, `git push --delete origin v1.2.0`, and tag again.
- **During the upload step** a draft release may be left behind. Delete the draft on the Releases page first, then
  re-run the workflow, or fix and re-tag as above.

## Build release files locally

```powershell
./build/publish.ps1                    # both runtimes, version from Directory.Build.props
./build/publish.ps1 -Runtime win-x64   # one runtime
./build/publish.ps1 -Version 1.2.0     # a specific version
```

The files land in `artifacts/releases`. Use this to check packaging changes. Don't upload these files by hand; the
Release workflow builds what users get.

The script needs PowerShell 7. It fails if any published file, or the debug info inside it, contains your
repository folder or user folder, so release files never carry a developer's user name.

## Rules that keep updates working

- **The installer id stays `SmartPaste.App`** (`$PackId` in `build/publish.ps1`). Installed copies only update to
  packages with the same id. It must also differ from the data folder name (`%LOCALAPPDATA%\SmartPaste`), because
  the installer replaces its own folder when it installs over an existing copy.
- **The channels stay `win-x64` and `win-arm64`.** Each installed copy follows the channel it was installed from.
- **The Velopack library and the vpk tool stay on the same version** (`Directory.Packages.props` and
  `.config/dotnet-tools.json`). Dependabot updates them together.
- **The repository stays public.** Installed copies check GitHub Releases without a token, which only works for a
  public repository.

## One-time repository setup

- Make the repository public before the first release, for the reason above.
- Under Settings > Code security, turn on **Private vulnerability reporting**. `SECURITY.md` and
  `CODE_OF_CONDUCT.md` send private reports there.
- Under Settings > Actions > General, allow GitHub Actions to run. The Release job asks for write access to contents
  itself; nothing else needs write access.
- Optional: protect `main` so the CI check must pass before merging.

## Code signing (for later)

Releases are not code-signed yet, so Windows SmartScreen warns people the first time they run the installer. A code
signature is a certificate that proves who built a file; signed files earn SmartScreen's trust over time.

Velopack signs every file it packs, and Setup.exe, when `vpk pack` gets one of these options:

- `--azureTrustedSignFile <metadata.json>` for Azure Trusted Signing, Microsoft's cloud signing service. It is the
  simplest choice for a project like this: no certificate file to guard.
- `--signParams "<signtool options>"` to sign with `signtool.exe` and a certificate.
- `--signTemplate "<command>"` for any other signing tool. Velopack replaces `{{file}}` in the command with the file
  to sign.

To turn it on:

1. Get the signing identity: an Azure Trusted Signing account, or a code-signing certificate from a certificate
   authority.
2. Store the credentials as GitHub Actions secrets. Never commit a certificate (`*.pfx` files are already ignored by
   git).
3. Add a parameter to `build/publish.ps1` that passes the signing option to `vpk pack`, and pass it from
   `release.yml` using those secrets.
4. Cut a release and check the signature: right-click Setup.exe > Properties > Digital Signatures.

Signing does not change how updates work. The updater checks each package against the SHA-256 checksum in the
release feed, and GitHub serves both over HTTPS.
