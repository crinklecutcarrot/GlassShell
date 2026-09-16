param([switch]$Restore)

$ErrorActionPreference = 'Stop'
$mod = 'HKLM:\SOFTWARE\Windhawk\Engine\Mods\windows-11-taskbar-styler'
$settings = Join-Path $mod 'Settings'
if (-not (Test-Path $settings)) { throw 'Windows 11 Taskbar Styler is not installed.' }
$heightMod = 'HKLM:\SOFTWARE\Windhawk\Engine\Mods\taskbar-icon-size'
$heightSettings = Join-Path $heightMod 'Settings'
if (-not $Restore -and -not (Test-Path $heightSettings)) {
    throw 'Windhawk mod "Taskbar height and icon size" is required. Install mod ID taskbar-icon-size first.'
}

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
    Set-ItemProperty -Path $heightMod -Name Disabled -Value 0
    Set-ItemProperty -Path $heightSettings -Name TaskbarHeight -Value 56
    Set-ItemProperty -Path $heightSettings -Name IconSize -Value 24
    Set-ItemProperty -Path $heightSettings -Name TaskbarButtonWidth -Value 40
    Set-ItemProperty -Path $heightSettings -Name IconSizeSmall -Value 16
    Set-ItemProperty -Path $heightSettings -Name TaskbarButtonWidthSmall -Value 32
    Set-ItemProperty -Path $heightMod -Name SettingsChangeTime -Value ([int][DateTimeOffset]::UtcNow.ToUnixTimeSeconds())

    Set-ItemProperty -Path $mod -Name Disabled -Value 0
    Set-ItemProperty -Path $settings -Name theme -Value 'DockLike'
    Set-ItemProperty -Path $settings -Name xamlDiagnosticsHandling -Value 'allow'
    Set-ItemProperty -Path $settings -Name 'controlStyles[0].target' -Value 'SystemTray.ChevronIconView, SystemTray.NotifyIconView#NotifyItemIcon, SystemTray.OmniButton, SystemTray.CopilotIcon, SystemTray.DateTimeIconContent, SystemTray.Stack#ShowDesktopStack'
    Set-ItemProperty -Path $settings -Name 'controlStyles[0].styles[0]' -Value 'Opacity=0'
    Set-ItemProperty -Path $settings -Name 'controlStyles[0].styles[1]' -Value 'IsHitTestVisible=False'
    Set-ItemProperty -Path $settings -Name 'controlStyles[1].target' -Value 'StackPanel#SystemTrayFrameGrid, Grid#SystemTrayFrameGrid'
    Set-ItemProperty -Path $settings -Name 'controlStyles[1].styles[0]' -Value 'Background=Transparent'
    Set-ItemProperty -Path $settings -Name 'controlStyles[1].styles[1]' -Value 'BorderThickness=0'
    Set-ItemProperty -Path $settings -Name 'controlStyles[2].target' -Value 'Taskbar.TaskbarFrame > Grid#RootGrid'
    Set-ItemProperty -Path $settings -Name 'controlStyles[2].styles[0]' -Value 'Margin=0,4,0,4'
    Set-ItemProperty -Path $settings -Name 'controlStyles[2].styles[1]' -Value 'CornerRadius=12'
    Set-ItemProperty -Path $settings -Name 'controlStyles[2].styles[2]' -Value 'Padding=4'
    Set-ItemProperty -Path $settings -Name 'controlStyles[3].target' -Value 'Grid#IconPanel@RunningIndicatorStates > Rectangle#RunningIndicator, Taskbar.TaskListLabeledButtonPanel@RunningIndicatorStates > Rectangle#RunningIndicator'
    Set-ItemProperty -Path $settings -Name 'controlStyles[3].styles[0]' -Value 'Height=2'
    Set-ItemProperty -Path $settings -Name 'controlStyles[3].styles[1]' -Value 'VerticalAlignment=Bottom'
    Set-ItemProperty -Path $settings -Name 'controlStyles[3].styles[2]' -Value 'RenderTransform:=<TranslateTransform Y="2" />'
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
