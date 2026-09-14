$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskTools = Join-Path $taskRoot '.tools'
$taskZig = Join-Path $taskTools 'zig\zig.exe'
$taskVersion = '0.16.0'
$taskArchive = Join-Path $taskTools 'zig.zip'

if (-not (Test-Path -LiteralPath $taskZig)) {
    New-Item -ItemType Directory -Path $taskTools -Force | Out-Null
    $taskIndex = Invoke-RestMethod 'https://ziglang.org/download/index.json'
    $taskPackage = $taskIndex.$taskVersion.'x86_64-windows'
    Invoke-WebRequest -Uri $taskPackage.tarball -OutFile $taskArchive
    if ((Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskPackage.shasum) {
        throw 'Zig checksum mismatch'
    }
    $taskExtract = Join-Path $taskTools 'zig-extract'
    Expand-Archive -LiteralPath $taskArchive -DestinationPath $taskExtract -Force
    $taskFolder = Get-ChildItem -LiteralPath $taskExtract -Directory | Select-Object -First 1
    Move-Item -LiteralPath $taskFolder.FullName -Destination (Join-Path $taskTools 'zig')
}

$taskOutput = Join-Path $taskRoot 'src\GlassShell\Native'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
& $taskZig cc -x c -target x86_64-windows-gnu -shared -O2 `
    (Join-Path $taskRoot 'native\TrayHook\tray_hook.cpp') `
    '-luser32' '-lshell32' `
    '-o' (Join-Path $taskOutput 'GlassShell.TrayHook.dll')
if ($LASTEXITCODE -ne 0) { throw 'Native tray hook build failed' }
