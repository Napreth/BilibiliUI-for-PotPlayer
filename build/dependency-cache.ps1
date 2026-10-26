param([Parameter(Mandatory=$true)][ValidateSet('Export','Restore')][string]$Mode)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$cache = Join-Path $repo '.local/ci-dependencies'
$lock = Get-Content (Join-Path $PSScriptRoot 'dependencies.lock.json') -Raw | ConvertFrom-Json
function ChildPath([string]$Root,[string]$Relative) {
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $path = [IO.Path]::GetFullPath((Join-Path $rootPath $Relative))
    if (-not $path.StartsWith($rootPath,[StringComparison]::OrdinalIgnoreCase)) { throw "Dependency path escaped its root: $Relative" }
    return $path
}
if ($Mode -eq 'Export') {
    $files = @(
        '.local/dependencies/DanmakuFactory-source.zip',
        '.local/dependencies/DanmakuFactory-revision.json',
        '.local/dependencies/danmaku-factory/win-x64/libDanmakuFactory.a',
        '.local/dependencies/danmaku-factory/win-x64/libpcre2-8.a',
        ".local/dependencies/ffmpeg-$($lock.ffmpeg.version).tar.xz",
        ('.local/dependencies/'+$lock.danmakuFactory.pcre2.archive),
        'third_party/FFmpeg/config.h','third_party/FFmpeg/config.mak',
        'third_party/libass/win-x64/libass-9.dll','third_party/libass/win-x64/libass.dll.a'
    )
    $files += $lock.libass.sources | ForEach-Object { '.local/dependencies/'+$_.archive }
    $files += $lock.rawCompression.sources | ForEach-Object { '.local/dependencies/'+$_.archive }
    $files += '.local/dependencies/raw-compression/win-x64/libRawCompression.a'
    $files += '.local/dependencies/raw-compression/win-x64/source-fingerprint.txt'
    $rawHeaders = Join-Path $repo '.local/dependencies/raw-compression/win-x64/include'
    $files += Get-ChildItem -LiteralPath $rawHeaders -Filter '*.h' -File -Recurse | ForEach-Object { $_.FullName.Substring($repo.Length+1).Replace('\','/') }
    foreach ($library in @('avcodec','avformat','avutil')) {
        $dll = @(Get-ChildItem -LiteralPath (Join-Path $repo 'third_party/FFmpeg/win-x64') -Filter "$library-*.dll" -File)
        if ($dll.Count -ne 1) { throw "Expected one FFmpeg DLL: $library" }
        $files += 'third_party/FFmpeg/win-x64/'+$dll[0].Name
        $files += "third_party/FFmpeg/win-x64/$library.lib"
    }
    # Keep public/generated headers for our bridges, never machine-specific build trees.
    $headers = @(".local/dependencies/ffmpeg-$($lock.ffmpeg.version)")
    $headers += @('libavcodec','libavformat','libavutil') | ForEach-Object { ".local/dependencies/ffmpeg-$($lock.ffmpeg.version)/$_" }
    $headers += ".local/dependencies/libass/libass-$($lock.libass.version)/libass"
    foreach ($directory in $headers) {
        $files += Get-ChildItem -LiteralPath (Join-Path $repo $directory) -Filter '*.h' -File | ForEach-Object { $directory+'/'+$_.Name }
    }
    $notices = @('LICENSE.LGPL-2.1.txt','libass-ISC.txt','FreeType-FTL.txt','HarfBuzz-MIT.txt','HarfBuzz-Microsoft-MIT.txt','FriBidi-LGPL-2.1.txt','DanmakuFactory-MIT.txt','PCRE2-BSD.txt','Zstandard-BSD.txt','Brotli-MIT.txt')
    $files += $notices | ForEach-Object { 'LICENSES/'+$_ }
    $files = @($files | Sort-Object -Unique)
    foreach ($relative in $files) {
        if (-not (Test-Path -LiteralPath (ChildPath $repo $relative) -PathType Leaf)) { throw "Missing dependency cache input: $relative" }
    }
    $safeCache = ChildPath (Join-Path $repo '.local') 'ci-dependencies'
    if (Test-Path -LiteralPath $safeCache) { Remove-Item -LiteralPath $safeCache -Recurse -Force }
    New-Item -ItemType Directory -Path $cache -Force | Out-Null
    $entries = foreach ($relative in $files) {
        $from = ChildPath $repo $relative
        $to = ChildPath $cache $relative
        New-Item -ItemType Directory -Path (Split-Path $to -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $from -Destination $to -Force
        [ordered]@{Path=$relative;SHA256=(Get-FileHash -LiteralPath $to).Hash}
    }
    [ordered]@{Format=1;Files=@($entries)} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $cache 'manifest.json') -Encoding UTF8
    Write-Output "Exported $($entries.Count) dependency files for CI."
} else {
    $manifest = Get-Content (Join-Path $cache 'manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.Format -ne 1 -or -not $manifest.Files.Count) { throw 'Unsupported or empty dependency cache.' }
    # Validate the complete bundle before replacing any dependency file.
    foreach ($entry in $manifest.Files) {
        $from = ChildPath $cache $entry.Path
        $null = ChildPath $repo $entry.Path
        if ((Get-FileHash -LiteralPath $from).Hash -ne $entry.SHA256) { throw "Dependency cache checksum mismatch: $($entry.Path)" }
    }
    foreach ($entry in $manifest.Files) {
        $to = ChildPath $repo $entry.Path
        New-Item -ItemType Directory -Path (Split-Path $to -Parent) -Force | Out-Null
        Copy-Item -LiteralPath (ChildPath $cache $entry.Path) -Destination $to -Force
    }
    Write-Output "Restored $($manifest.Files.Count) verified dependency files for CI."
}
