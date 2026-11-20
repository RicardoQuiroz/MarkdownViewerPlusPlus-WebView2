<#
  Installs the built MarkdownViewer++ plugin into Notepad++.
  - Notepad++ must be closed.
  - Needs administrator rights when Notepad++ is in "C:\Program Files" (the script asks for them).
  - The previous plugin folder is kept as a backup: plugins\MarkdownViewerPlusPlus.backup-<date>

  Usage:  powershell -ExecutionPolicy Bypass -File install.ps1 [-NppDir "C:\Program Files\Notepad++"] [-Platform x64]
#>
param(
    [string]$NppDir = "C:\Program Files\Notepad++",
    [ValidateSet('x64', 'x86')][string]$Platform = 'x64'
)
$ErrorActionPreference = 'Stop'

$source = Join-Path $PSScriptRoot "dist\$Platform\MarkdownViewerPlusPlus"
$pluginDir = Join-Path $NppDir "plugins\MarkdownViewerPlusPlus"

if (-not (Test-Path (Join-Path $source 'MarkdownViewerPlusPlus.dll'))) {
    throw "Build first: dotnet build MarkdownViewerPlusPlus\MarkdownViewerPlusPlus.csproj -c Release -p:Platform=$Platform"
}
if (-not (Test-Path (Join-Path $NppDir 'notepad++.exe'))) { throw "Notepad++ not found in $NppDir" }

$running = Get-Process notepad++ -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $NppDir 'notepad++.exe') }
if ($running) { throw 'Close Notepad++ first (all windows), then run this script again.' }

# Re-launch elevated if we cannot write to the plugins folder
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$testFile = Join-Path $NppDir "plugins\.mdv-write-test"
try { [IO.File]::WriteAllText($testFile, ''); Remove-Item $testFile } catch {
    if ($isAdmin) { throw }
    Write-Host 'Administrator rights are needed to write to' $NppDir '- asking for them...'
    Start-Process powershell -Verb RunAs -Wait -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-NppDir', "`"$NppDir`"", '-Platform', $Platform)
    exit
}

if (Test-Path $pluginDir) {
    $backup = "$pluginDir.backup-$(Get-Date -Format yyyyMMdd-HHmmss)"
    Move-Item $pluginDir $backup
    # Notepad++ would try to load a second copy from the backup folder; keep it out of plugins\
    $backupTarget = Join-Path $NppDir ("plugins\disabled\" + (Split-Path $backup -Leaf))
    New-Item -ItemType Directory -Force (Split-Path $backupTarget) | Out-Null
    Move-Item $backup $backupTarget
    Write-Host "Previous version saved in $backupTarget"
}

New-Item -ItemType Directory -Force $pluginDir | Out-Null
Copy-Item (Join-Path $source '*') $pluginDir -Recurse -Force
Write-Host "MarkdownViewer++ installed in $pluginDir. Start Notepad++ and press Ctrl+Shift+M."
if ($isAdmin) { Read-Host 'Press Enter to close' | Out-Null }
