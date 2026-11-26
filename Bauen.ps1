# -Ausgabe <datei>: anderer Zielpfad (zum Testen, während die App läuft).
param([string]$Ausgabe)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# Same toolchain as the other Windows Tune tools: the C# compiler that ships with Windows (.NET Framework 4.x).
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs = 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll',
        'System.Xaml.dll', 'WPF\WindowsBase.dll', 'WPF\PresentationCore.dll', 'WPF\PresentationFramework.dll' |
        ForEach-Object { '/reference:' + (Join-Path $fw $_) }
$sources = (Get-ChildItem -LiteralPath "$root\src" -Filter '*.cs').FullName
$out = if ($Ausgabe) { $Ausgabe } else { "$root\Kopfhoererpegel.exe" }
$icon = if (Test-Path "$root\Kopfhoererpegel.ico") { "/win32icon:$root\Kopfhoererpegel.ico" }

& (Join-Path $fw 'csc.exe') /nologo /codepage:65001 /target:winexe /platform:anycpu /optimize+ `
    "/win32manifest:$root\src\App.manifest" $icon "/out:$out" @refs $sources
if ($LASTEXITCODE) { throw 'Kompilierung fehlgeschlagen.' }
Write-Host "Gebaut: $out"
