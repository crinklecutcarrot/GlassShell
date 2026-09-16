param([switch]$Restore)

$ErrorActionPreference = 'Stop'
$mod = 'HKLM:\SOFTWARE\Windhawk\Engine\Mods\windows-11-taskbar-styler'
$settings = Join-Path $mod 'Settings'
if (-not (Test-Path $settings)) { throw 'Windows 11 Taskbar Styler is not installed.' }

$backupFolder = Join-Path $env:LOCALAPPDATA 'GlassShell'
New-Item -ItemType Directory -Path $backupFolder -Force | Out-Null
$backup = Join-Path $backupFolder 'windhawk-taskbar-styler-backup.reg'
if ($Restore) {
    if (-not (Test-Path $backup)) { throw "No taskbar settings backup exists at $backup" }
    & reg.exe import $backup | Out-Null
} elseif (-not (Test-Path $backup)) {
    & reg.exe export 'HKLM\SOFTWARE\Windhawk\Engine\Mods\windows-11-taskbar-styler' $backup /y | Out-Null
}

if (-not $Restore) {
    Set-ItemProperty -Path $mod -Name Disabled -Value 0
    Set-ItemProperty -Path $settings -Name theme -Value 'DockLike'
    Set-ItemProperty -Path $settings -Name xamlDiagnosticsHandling -Value 'allow'
    Set-ItemProperty -Path $settings -Name 'controlStyles[0].target' -Value 'SystemTray.ChevronIconView, SystemTray.NotifyIconView#NotifyItemIcon, SystemTray.OmniButton, SystemTray.CopilotIcon, SystemTray.DateTimeIconContent, SystemTray.Stack#ShowDesktopStack'
    Set-ItemProperty -Path $settings -Name 'controlStyles[0].styles[0]' -Value 'Opacity=0'
    Set-ItemProperty -Path $settings -Name 'controlStyles[0].styles[1]' -Value 'IsHitTestVisible=False'
    Set-ItemProperty -Path $settings -Name 'controlStyles[1].target' -Value 'StackPanel#SystemTrayFrameGrid, Grid#SystemTrayFrameGrid'
    Set-ItemProperty -Path $settings -Name 'controlStyles[1].styles[0]' -Value 'Background=Transparent'
    Set-ItemProperty -Path $settings -Name 'controlStyles[1].styles[1]' -Value 'BorderThickness=0'
    Set-ItemProperty -Path $mod -Name SettingsChangeTime -Value ([int][DateTimeOffset]::UtcNow.ToUnixTimeSeconds())
}

$windhawk = 'C:\Program Files\Windhawk\windhawk.exe'
if (Test-Path $windhawk) {
    Get-Process windhawk -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Process -FilePath $windhawk -ArgumentList '-tray-only'
}

Start-Sleep -Seconds 2
Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
Start-Process explorer.exe
