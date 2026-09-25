# Builds Epic Chime Muter with the C# compiler that ships with Windows (.NET Framework 4.x).
# No SDK or Visual Studio needed.  Usage:  powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
# /codepage:65001 = sources are UTF-8 (csc otherwise assumes the system ANSI code page and garbles symbols like the bullet and ellipsis).
$refs = '/codepage:65001', '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll'
$lib = "$root\src\Art.cs", "$root\src\ChimeManager.cs", "$root\src\MainForm.cs"

New-Item -ItemType Directory -Force "$root\build", "$root\dist" | Out-Null

Write-Host '> Running logic tests' -ForegroundColor Cyan
& $csc /nologo /target:exe /out:"$root\build\LogicTest.exe" $refs "$root\tools\LogicTest.cs" $lib
if ($LASTEXITCODE) { throw 'LogicTest compile failed' }
& "$root\build\LogicTest.exe"
if ($LASTEXITCODE) { throw 'Logic tests failed' }

Write-Host '> Rendering icon and README graphics' -ForegroundColor Cyan
& $csc /nologo /target:exe /out:"$root\build\MakeAssets.exe" $refs "$root\tools\MakeAssets.cs" $lib
if ($LASTEXITCODE) { throw 'MakeAssets compile failed' }
& "$root\build\MakeAssets.exe" $root
if ($LASTEXITCODE) { throw 'MakeAssets failed' }

Write-Host '> Building EpicChimeMuter.exe' -ForegroundColor Cyan
& $csc /nologo /target:winexe /optimize+ /platform:anycpu /out:"$root\dist\EpicChimeMuter.exe" `
    /win32icon:"$root\assets\icon.ico" /win32manifest:"$root\src\app.manifest" $refs `
    "$root\src\Program.cs" "$root\src\AssemblyInfo.cs" $lib
if ($LASTEXITCODE) { throw 'App compile failed' }

Get-Item "$root\dist\EpicChimeMuter.exe" | Select-Object Name, Length
