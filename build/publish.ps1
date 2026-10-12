#Requires -Version 7.2
<#
.SYNOPSIS
    Builds the Smart Paste release files: Setup.exe, the portable zip, and the update packages.

.DESCRIPTION
    Restores the local tools (the Velopack CLI, vpk), runs every test, publishes the app for each Windows
    runtime, and packs it with Velopack into artifacts/releases. Each runtime is its own update channel
    (win-x64, win-arm64), so an installed copy only ever updates to its own architecture.

    The app is framework-dependent. Setup.exe installs the .NET 10 Desktop Runtime first when it is missing.

.PARAMETER Version
    The version to build, for example 1.2.0 or 1.3.0-beta.1. Defaults to <Version> in Directory.Build.props.

.PARAMETER Runtime
    The runtimes to build. Defaults to both.

.PARAMETER ReleaseNotes
    A Markdown file with the notes for this version. They show in the update prompt and on the GitHub release.
    Defaults to this version's section of CHANGELOG.md, when there is one.

.PARAMETER KeepPrevious
    Keep the packages already in artifacts/releases instead of starting clean. The release workflow downloads
    the previous release there first, so vpk can build a delta update (only the changed bytes) from it.

.EXAMPLE
    ./build/publish.ps1 -Runtime win-x64
#>
[CmdletBinding()]
param(
    [string] $Version,

    [ValidateSet('win-x64', 'win-arm64')]
    [string[]] $Runtime = @('win-x64', 'win-arm64'),

    [string] $ReleaseNotes,

    [switch] $KeepPrevious
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# The installer's id. Velopack installs to %LOCALAPPDATA%\<PackId> and replaces or deletes that whole folder when
# it installs over an existing copy or uninstalls. It must differ from the data folder (%LOCALAPPDATA%\SmartPaste,
# see AppPaths) so settings and learned labels survive a reinstall. Never change it after the first release:
# installed copies only update to packages with the same id.
$PackId = 'SmartPaste.App'
$PackTitle = 'Smart Paste'
$MainExe = 'SmartPaste.exe'

$Root = Split-Path -Parent $PSScriptRoot
$Artifacts = Join-Path $Root 'artifacts'
$PublishRoot = Join-Path $Artifacts 'publish'
$ReleaseDir = Join-Path $Artifacts 'releases'
$Icon = Join-Path $Root 'src/SmartPaste.App/Assets/SmartPaste.ico'

# Finds a dotnet that can build this repo. A PC can have several installs (machine-wide, per-user, DOTNET_ROOT),
# and the first one on PATH may not have the SDK that global.json asks for.
function Resolve-Dotnet {
    $candidates = @(
        if ($env:DOTNET_ROOT) { Join-Path $env:DOTNET_ROOT 'dotnet.exe' }
        Get-Command dotnet -CommandType Application -All -ErrorAction SilentlyContinue | ForEach-Object Source
        if ($env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe' }
        if ($env:ProgramFiles) { Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' }
    )
    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (-not (Test-Path $candidate)) { continue }
        # Run from the repo root, 'dotnet --version' fails unless this install has an SDK that global.json accepts.
        $sdk = & $candidate --version 2>$null
        if ($LASTEXITCODE -eq 0 -and $sdk) { return $candidate }
    }
    throw 'No .NET SDK that matches global.json was found. Install the .NET 10 SDK from https://dotnet.microsoft.com/download and try again.'
}

function Invoke-Dotnet {
    & $script:Dotnet @args
    if ($LASTEXITCODE -ne 0) {
        throw "'dotnet $(($args | Select-Object -First 2) -join ' ')' failed with exit code $LASTEXITCODE."
    }
}

function Write-Step([string] $Text) {
    Write-Host ''
    Write-Host "==> $Text" -ForegroundColor Cyan
}

function Get-BuildProperty([string] $Name) {
    [xml] $props = Get-Content -Raw (Join-Path $Root 'Directory.Build.props')
    $node = $props.SelectSingleNode("/Project/PropertyGroup/$Name")
    if (-not $node -or -not $node.InnerText.Trim()) { throw "Directory.Build.props has no <$Name>." }
    return $node.InnerText.Trim()
}

# The portable PDB that the compiler embeds in an assembly, decompressed, or $null when there is none.
function Get-EmbeddedPdb([byte[]] $Image) {
    try {
        $reader = [System.Reflection.PortableExecutable.PEReader]::new([IO.MemoryStream]::new($Image))
        $entry = $reader.ReadDebugDirectory() | Where-Object Type -eq 'EmbeddedPortablePdb' | Select-Object -First 1
        $reader.Dispose()
    }
    catch [BadImageFormatException], [InvalidOperationException] {
        return $null
    }
    if (-not $entry) { return $null }
    # The entry holds "MPDB", the uncompressed size, then the PDB compressed with Deflate.
    $compressed = [IO.MemoryStream]::new($Image, $entry.DataPointer + 8, $entry.DataSize - 8)
    $deflate = [IO.Compression.DeflateStream]::new($compressed, [IO.Compression.CompressionMode]::Decompress)
    $pdb = [IO.MemoryStream]::new()
    $deflate.CopyTo($pdb)
    $deflate.Dispose()
    return , $pdb.ToArray()
}

# Release files must never carry this PC's folder names, which include the Windows user name. Checks every
# published file, and the compressed debug info inside each assembly (with its Source Link map).
function Assert-NoLocalPaths([string] $Folder) {
    $needles = foreach ($path in @($Root, $env:USERPROFILE)) {
        if (-not $path) { continue }
        $path = $path.TrimEnd('\')
        if ($path.Length -lt 6) { continue } # a drive root would match by chance
        $path
        $path.Replace('\', '\\') # as JSON writes it
        $path.Replace('\', '/')
    }
    foreach ($file in Get-ChildItem $Folder -Recurse -File) {
        $image = [IO.File]::ReadAllBytes($file.FullName)
        $blobs = @(, $image)
        if ($file.Extension -in '.dll', '.exe') {
            $pdb = Get-EmbeddedPdb $image
            if ($pdb) { $blobs += , $pdb }
        }
        foreach ($blob in $blobs) {
            # UTF-16 text can start at an odd byte, so decode from both offsets.
            $texts = @(
                [Text.Encoding]::Latin1.GetString($blob)
                [Text.Encoding]::Unicode.GetString($blob)
                [Text.Encoding]::Unicode.GetString($blob, 1, $blob.Length - 1)
            )
            foreach ($needle in $needles) {
                foreach ($text in $texts) {
                    if ($text.IndexOf($needle, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                        throw "$($file.Name) contains the local path '$needle'. Release files must not carry local folder names."
                    }
                }
            }
        }
    }
}

# Returns this version's section of CHANGELOG.md (Keep a Changelog format) as a file vpk can read, or $null.
function Get-ChangelogNotes([string] $ForVersion) {
    $changelog = Join-Path $Root 'CHANGELOG.md'
    if (-not (Test-Path $changelog)) { return $null }
    $lines = @(Get-Content $changelog)
    $heading = '^## \[' + [regex]::Escape($ForVersion) + '\]'
    $section = [System.Collections.Generic.List[string]]::new()
    $inSection = $false
    foreach ($line in $lines) {
        if ($inSection) {
            # The section ends at the next version heading or at the link definitions at the bottom.
            if ($line -match '^## \[' -or $line -match '^\[[^\]]+\]:\s') { break }
            $section.Add($line)
        }
        elseif ($line -match $heading) {
            $inSection = $true
        }
    }
    $text = ($section -join "`n").Trim()
    if (-not $text) { return $null }
    $path = Join-Path $Artifacts "release-notes-$ForVersion.md"
    Set-Content -Path $path -Value $text -Encoding utf8NoBOM
    return $path
}

Push-Location $Root
try {
    $script:Dotnet = Resolve-Dotnet

    if (-not $Version) { $Version = Get-BuildProperty 'Version' }
    if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$') {
        throw "Version '$Version' is not a version like 1.2.0 or 1.3.0-beta.1."
    }
    $authors = Get-BuildProperty 'Authors'

    New-Item -ItemType Directory -Force -Path $Artifacts | Out-Null
    if (-not $ReleaseNotes) { $ReleaseNotes = Get-ChangelogNotes $Version }
    elseif (-not (Test-Path $ReleaseNotes)) { throw "Release notes file '$ReleaseNotes' does not exist." }
    else { $ReleaseNotes = (Resolve-Path $ReleaseNotes).Path }

    Write-Host "Smart Paste $Version for $($Runtime -join ', ')"
    Write-Host "dotnet: $script:Dotnet"
    if (-not $ReleaseNotes) {
        Write-Warning "CHANGELOG.md has no section for $Version, so this release has no notes."
    }
    if (-not (Test-Path $Icon)) {
        Write-Warning "No icon at src/SmartPaste.App/Assets/SmartPaste.ico, so Setup.exe uses Velopack's default icon."
    }

    Write-Step 'Restoring local tools'
    Invoke-Dotnet tool restore

    Write-Step 'Running tests'
    Invoke-Dotnet test SmartPaste.slnx --configuration Release

    if (Test-Path $PublishRoot) { Remove-Item -Recurse -Force $PublishRoot }
    if (-not $KeepPrevious -and (Test-Path $ReleaseDir)) { Remove-Item -Recurse -Force $ReleaseDir }
    New-Item -ItemType Directory -Force -Path $ReleaseDir | Out-Null

    foreach ($rid in $Runtime) {
        $publishDir = Join-Path $PublishRoot $rid
        $arch = $rid.Split('-')[1]

        Write-Step "Publishing $rid"
        # ContinuousIntegrationBuild maps the source root to /_/ for Source Link as well. Without it the Source Link
        # map in the embedded debug info keeps this PC's repository folder.
        Invoke-Dotnet publish src/SmartPaste.App/SmartPaste.App.csproj `
            --configuration Release `
            --runtime $rid `
            --self-contained false `
            --output $publishDir `
            "-p:Version=$Version" `
            '-p:ContinuousIntegrationBuild=true'
        Assert-NoLocalPaths $publishDir

        Write-Step "Packing $rid"
        $pack = @(
            'vpk', 'pack',
            '--skip-updates',
            '--packId', $PackId,
            '--packVersion', $Version,
            '--packDir', $publishDir,
            '--mainExe', $MainExe,
            '--packTitle', $PackTitle,
            '--packAuthors', $authors,
            '--channel', $rid,
            '--runtime', $rid,
            # Setup.exe and the updater install the .NET 10 Desktop Runtime for this CPU when it is missing.
            '--framework', "net10.0-$arch-desktop",
            # A tray app starts at login, so a Start menu entry is enough; no desktop shortcut.
            '--shortcuts', 'StartMenuRoot',
            '--outputDir', $ReleaseDir
        )
        if (Test-Path $Icon) { $pack += @('--icon', $Icon) }
        if ($ReleaseNotes) { $pack += @('--releaseNotes', $ReleaseNotes) }
        Invoke-Dotnet @pack
    }

    Write-Step 'Release files'
    Get-ChildItem $ReleaseDir -File | Sort-Object Name | ForEach-Object {
        '{0,-48} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB)
    }
}
finally {
    Pop-Location
}
