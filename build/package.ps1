param([switch]$SkipBuild, [switch]$NoInstaller)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'ffmpeg.ps1')
    & (Join-Path $PSScriptRoot 'libass.ps1')
    & (Join-Path $PSScriptRoot 'danmaku-factory.ps1') -DependenciesOnly
    & (Join-Path $PSScriptRoot 'raw-compression.ps1') -DependenciesOnly
    & (Join-Path $PSScriptRoot 'native.ps1')
    & (Join-Path $PSScriptRoot 'managed.ps1') -Test
    & (Join-Path $PSScriptRoot 'skin.ps1')
}
$stage = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'out/package'))
$safeRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'out')).TrimEnd('\') + '\'
if (-not $stage.StartsWith($safeRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe package staging path.' }
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
$runtime = Join-Path $stage 'runtime'
New-Item -ItemType Directory -Path $runtime -Force | Out-Null
$compiled = Join-Path $PSScriptRoot 'out/runtime'
Copy-Item -Path (Join-Path $compiled '*.dll') -Destination $runtime
Copy-Item -LiteralPath (Join-Path $compiled 'BilibiliBehaviors.exe') -Destination $runtime
Copy-Item -LiteralPath (Join-Path $repo 'config/input-profile.json') -Destination $runtime
Copy-Item -LiteralPath (Join-Path $repo 'config/input-profile.json') -Destination $stage
Copy-Item -LiteralPath (Join-Path $repo 'src/Host/MediaPlayParse - BilibiliBehaviors.as') -Destination $stage
Copy-Item -LiteralPath (Join-Path $repo 'skin/BilibiliLite.dsf') -Destination $stage
$notices = Join-Path $runtime 'third-party'
New-Item -ItemType Directory -Path $notices -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $runtime
Copy-Item -LiteralPath (Join-Path $repo 'LICENSES') -Destination $runtime -Recurse
Copy-Item -Path (Join-Path $repo 'third_party/FFmpeg/config.*') -Destination $notices
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ffmpeg.sh'),(Join-Path $PSScriptRoot 'dependencies.lock.json') -Destination $notices
$lock = (Get-Content (Join-Path $PSScriptRoot 'dependencies.lock.json') -Raw | ConvertFrom-Json).ffmpeg
Copy-Item -LiteralPath (Join-Path $repo ".local/dependencies/ffmpeg-$($lock.version).tar.xz") -Destination $notices
$assNotices=Join-Path $notices 'libass'
New-Item -ItemType Directory -Path $assNotices -Force | Out-Null
$assLock=(Get-Content (Join-Path $PSScriptRoot 'dependencies.lock.json') -Raw | ConvertFrom-Json).libass
foreach($source in $assLock.sources) { Copy-Item -LiteralPath (Join-Path $repo ('.local/dependencies/'+$source.archive)) -Destination $assNotices }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'libass.ps1'),(Join-Path $PSScriptRoot 'libass-options.json') -Destination $assNotices
$factoryNotices=Join-Path $notices 'danmaku-factory'
New-Item -ItemType Directory -Path $factoryNotices -Force | Out-Null
$factoryLock=(Get-Content (Join-Path $PSScriptRoot 'dependencies.lock.json') -Raw | ConvertFrom-Json).danmakuFactory
Copy-Item -LiteralPath (Join-Path $repo '.local/dependencies/DanmakuFactory-source.zip'),(Join-Path $repo '.local/dependencies/DanmakuFactory-revision.json'),(Join-Path $repo ('.local/dependencies/'+$factoryLock.pcre2.archive)),(Join-Path $PSScriptRoot 'danmaku-factory.ps1') -Destination $factoryNotices
$rawNotices = Join-Path $notices 'raw-compression'
New-Item -ItemType Directory -Path $rawNotices -Force | Out-Null
$rawLock = (Get-Content (Join-Path $PSScriptRoot 'dependencies.lock.json') -Raw | ConvertFrom-Json).rawCompression
foreach ($item in $rawLock.sources) { Copy-Item -LiteralPath (Join-Path $repo ('.local/dependencies/'+$item.archive)) -Destination $rawNotices }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'raw-compression.ps1') -Destination $rawNotices
$relativeFiles = @(Get-ChildItem -LiteralPath $runtime -File -Recurse | ForEach-Object { $_.FullName.Substring($runtime.Length+1).Replace('\','/') })
$relativeFiles | ConvertTo-Json | Set-Content (Join-Path $runtime 'installed-files.json') -Encoding UTF8
$hashes = @(Get-ChildItem -LiteralPath $stage -File -Recurse | ForEach-Object { [ordered]@{Path=$_.FullName.Substring($stage.Length+1).Replace('\','/');SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
$hashes | ConvertTo-Json | Set-Content (Join-Path $stage 'checksums.json') -Encoding UTF8
if (-not $NoInstaller) {
    $compiler = & (Join-Path $PSScriptRoot 'inno.ps1')
    $version = (Get-Content (Join-Path $PSScriptRoot 'version.txt') -Raw).Trim()
    & $compiler "/DRepoRoot=$repo" "/DAppVersion=$version" (Join-Path $PSScriptRoot 'installer.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $installer = Join-Path $PSScriptRoot "out/dist/BilibiliBehaviors-$version-win-x64-setup.exe"
    ((Get-FileHash -LiteralPath $installer).Hash + '  ' + (Split-Path $installer -Leaf)) | Set-Content -LiteralPath ($installer + '.sha256') -Encoding ASCII
}
Write-Output "Package prepared: $stage"
