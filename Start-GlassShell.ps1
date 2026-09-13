$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskDotnet = Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $taskDotnet)) { throw 'Local SDK missing. See README.md for setup.' }
$env:DOTNET_ROOT = Split-Path $taskDotnet
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $taskDotnet build (Join-Path $taskRoot 'src\GlassShell\GlassShell.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$taskDll = Join-Path $taskRoot 'src\GlassShell\bin\Release\net10.0-windows10.0.19041.0\GlassShell.dll'
Start-Process -FilePath $taskDotnet -ArgumentList @('"' + $taskDll + '"') -WorkingDirectory $taskRoot -WindowStyle Hidden
