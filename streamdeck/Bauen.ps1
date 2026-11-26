# Builds the Stream Deck plugin into de.hendrik4k.kopfhoererpegel.sdPlugin (exe + pictures).
#   -Installieren  copies it into the Stream Deck plugin folder and restarts Stream Deck
#   -Paket         also writes Kopfhoererpegel.streamDeckPlugin (double-click installs it on any PC)
param([switch]$Installieren, [switch]$Paket)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$uuid = 'de.hendrik4k.kopfhoererpegel'
$plugin = Join-Path $root "$uuid.sdPlugin"
$exe = Join-Path $plugin 'kopfhoererpegel-streamdeck.exe'

$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs = 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Web.Extensions.dll' |
        ForEach-Object { '/reference:' + (Join-Path $fw $_) }
# Same measurement as the tray app: Meter.cs + CoreAudio.cs from ..\src
$sources = @((Get-ChildItem -LiteralPath "$root\src" -Filter '*.cs').FullName) +
           (Join-Path $root '..\src\Meter.cs'), (Join-Path $root '..\src\CoreAudio.cs')

if ($Installieren) {
    # The running plugin locks its exe.
    Get-Process kopfhoererpegel-streamdeck -ErrorAction SilentlyContinue | Stop-Process -Force
}
& (Join-Path $fw 'csc.exe') /nologo /codepage:65001 /target:winexe /platform:anycpu /optimize+ `
    "/out:$exe" @refs $sources
if ($LASTEXITCODE) { throw 'Kompilierung fehlgeschlagen.' }
& $exe --icons (Join-Path $plugin 'bilder') | Out-Null
Write-Host "Gebaut: $plugin"

if ($Paket) {
    $ziel = Join-Path $root 'Kopfhoererpegel.streamDeckPlugin'
    Remove-Item -LiteralPath $ziel -ErrorAction SilentlyContinue
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::Open($ziel, 'Create')
    try {
        Get-ChildItem -LiteralPath $plugin -Recurse -File | ForEach-Object {
            $name = "$uuid.sdPlugin/" + $_.FullName.Substring($plugin.Length + 1).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $name) | Out-Null
        }
    } finally { $zip.Dispose() }
    Write-Host "Paket: $ziel"
}

if ($Installieren) {
    $sd = Get-Process StreamDeck -ErrorAction SilentlyContinue | Select-Object -First 1
    $sdExe = if ($sd) { $sd.Path } else { "$env:ProgramFiles\Elgato\StreamDeck\StreamDeck.exe" }
    if ($sd) { $sd | Stop-Process -Force; Start-Sleep -Seconds 2 }
    $ziel = Join-Path $env:APPDATA "Elgato\StreamDeck\Plugins\$uuid.sdPlugin"
    New-Item -ItemType Directory -Force $ziel | Out-Null
    Copy-Item -Path "$plugin\*" -Destination $ziel -Recurse -Force
    Start-Process $sdExe
    Write-Host "Installiert: $ziel (Stream Deck neu gestartet)"
}
