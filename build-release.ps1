# Builds the downloads for players:
#   dist\AION2DpsMeter-Setup-v<version>.exe    - installer: shortcuts, uninstall, Npcap check (needs Inno Setup 7)
#   dist\AION2DpsMeter-v<version>-win-x64.zip  - the same program without installation (no .NET install needed)
#   dist\AION2DpsMeter-v<version>-source.zip   - the source code (GPL-3.0: hand it out alongside the program)
#   dist\AION2DpsMeter-Setup.exe, dist\AION2DpsMeter-win-x64.zip - copies under names that never change, for links
#   that always give the newest version (releases/latest/download/<name>)
# Packet recordings (captures\) and build output never go into any of them.
param([string]$Version = "")

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

# Zip a folder with "/" in entry names (Compress-Archive in Windows PowerShell writes "\", which some unzippers mangle).
function New-Zip([string]$folder, [string]$zipPath) {
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    $zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        Get-ChildItem $folder -Recurse -File | ForEach-Object {
            $entry = $_.FullName.Substring($folder.TrimEnd('\').Length + 1).Replace('\', '/')
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $entry, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally {
        $zip.Dispose()
    }
}

# Inno Setup 7's compiler: on PATH or in its per-user / per-machine install folder.
function Find-Iscc {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    "$env:LOCALAPPDATA\Programs\Inno Setup 7", "$env:ProgramFiles\Inno Setup 7", "${env:ProgramFiles(x86)}\Inno Setup 7" |
        ForEach-Object { Join-Path $_ "ISCC.exe" } | Where-Object { Test-Path $_ } | Select-Object -First 1
}

$project = Join-Path $root "src\AionMeter.App\AionMeter.App.csproj"
if (-not $Version) {
    $Version = (Select-Xml -Path $project -XPath "//Version").Node.InnerText | Select-Object -First 1
}
$dist = Join-Path $root "dist"
$name = "AION2DpsMeter-v$Version-win-x64"
$stage = Join-Path $dist $name
New-Item -ItemType Directory -Force $dist | Out-Null
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }

# Self-contained, no symbols; .NET's own messages only in English and Russian (%3B is MSBuild's escaped ";").
$publish = @("-c", "Release", "-r", "win-x64", "--self-contained", "true",
    "-p:DebugType=none", "-p:DebugSymbols=false", "-p:SatelliteResourceLanguages=en%3Bru")

# Portable: one exe + data folder.
dotnet publish $project @publish -o $stage `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Copy-Item (Join-Path $root "LICENSE") $stage
Copy-Item (Join-Path $root "INSTALL.txt") $stage
if (-not (Test-Path (Join-Path $stage "data\npcs\en.json"))) { throw "data folder missing from the publish output" }

$programZip = Join-Path $dist "$name.zip"
New-Zip $stage $programZip
$stableZip = Join-Path $dist "AION2DpsMeter-win-x64.zip" # the never-changing name, as for the installer below
Copy-Item $programZip $stableZip -Force
$outputs = @($programZip, $stableZip)

# Installer: a regular (not single-file) publish starts faster once installed, and Inno Setup's LZMA2 packs it
# smaller than the single-file exe.
$iscc = Find-Iscc
if ($iscc) {
    $setupStage = Join-Path $dist ".setup-stage"
    if (Test-Path $setupStage) { Remove-Item $setupStage -Recurse -Force }
    dotnet publish $project @publish -o $setupStage -p:PublishSingleFile=false
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish (installer) failed" }
    Copy-Item (Join-Path $root "LICENSE"), (Join-Path $root "INSTALL.txt") $setupStage
    if (-not (Test-Path (Join-Path $setupStage "data\npcs\en.json"))) { throw "data folder missing from the installer publish" }

    & $iscc /Qp "/DAppVer=$Version" "/DPayloadDir=$setupStage" "/O$dist" (Join-Path $root "installer\AION2DpsMeter.iss")
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }
    Remove-Item $setupStage -Recurse -Force
    $setupExe = Join-Path $dist "AION2DpsMeter-Setup-v$Version.exe"
    # Same file under a name that never changes: the download page links to
    # releases/latest/download/AION2DpsMeter-Setup.exe and always gets the newest version.
    $stableSetup = Join-Path $dist "AION2DpsMeter-Setup.exe"
    Copy-Item $setupExe $stableSetup -Force
    $outputs = @($setupExe, $stableSetup) + $outputs
}
else {
    Write-Warning "Inno Setup 7 not found (https://jrsoftware.org/isdl.php): no installer this time, the zip is ready."
}

# Source: everything tracked by the project, without recordings, build output or local tooling.
$sourceStage = Join-Path $dist "AION2DpsMeter-v$Version-source"
if (Test-Path $sourceStage) { Remove-Item $sourceStage -Recurse -Force }
$skip = '\\(bin|obj|captures|dist|\.vs|\.claude|TestResults)(\\|$)'
Get-ChildItem $root -Recurse -File |
    Where-Object { $_.FullName.Substring($root.Length) -notmatch $skip -and $_.Extension -notin '.pcap', '.pcapng' } |
    ForEach-Object {
        $target = Join-Path $sourceStage $_.FullName.Substring($root.Length + 1)
        New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
        Copy-Item $_.FullName $target
    }
$sourceZip = "$sourceStage.zip"
New-Zip $sourceStage $sourceZip
Remove-Item $sourceStage -Recurse -Force
$outputs += $sourceZip

Get-Item $outputs | Select-Object Name, @{ n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } }
