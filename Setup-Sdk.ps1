$ErrorActionPreference = 'Stop'
$taskVersion = '10.0.401'
$taskTools = Join-Path $PSScriptRoot '.tools'
New-Item -ItemType Directory -Path $taskTools -Force | Out-Null
$taskReleases = Invoke-RestMethod 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
$taskSdk = $taskReleases.releases.sdk | Where-Object { $_.version -eq $taskVersion } | Select-Object -First 1
if (-not $taskSdk) { throw "SDK $taskVersion not found in official release metadata" }
$taskArchive = $taskSdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' } | Select-Object -First 1
$taskZip = Join-Path $taskTools 'dotnet-sdk.zip'
Invoke-WebRequest -Uri $taskArchive.url -OutFile $taskZip
if ((Get-FileHash -LiteralPath $taskZip -Algorithm SHA512).Hash -ne $taskArchive.hash) { throw 'SDK checksum mismatch' }
Expand-Archive -LiteralPath $taskZip -DestinationPath (Join-Path $taskTools 'dotnet') -Force
& (Join-Path $taskTools 'dotnet\dotnet.exe') --version
