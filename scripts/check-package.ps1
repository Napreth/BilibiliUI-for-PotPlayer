$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$package = Join-Path $repo 'build/out/package'
$runtime = Join-Path $package 'runtime'
$checksums = Get-Content -LiteralPath (Join-Path $package 'checksums.json') -Raw | ConvertFrom-Json
foreach ($entry in $checksums) {
    $file = [IO.Path]::GetFullPath((Join-Path $package $entry.Path))
    if (-not $file.StartsWith($package + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Package path escaped its root.' }
    if ((Get-FileHash -LiteralPath $file).Hash -ne $entry.SHA256) { throw "Package checksum mismatch: $($entry.Path)" }
}
$modules = @('Contracts','Runtime','Platform','Input','Media','Windowing','Playback','Captions')
foreach ($module in $modules) {
    if (-not (Test-Path -LiteralPath (Join-Path $runtime "BilibiliBehaviors.$module.dll"))) { throw "Missing module: $module" }
}
if ((Get-ChildItem -LiteralPath $runtime -Filter 'BilibiliBehaviors.*.dll').Count -ne $modules.Count) { throw 'Unexpected module in runtime payload.' }
if ((Get-ChildItem -LiteralPath $runtime -Filter '*.exe').Count -ne 1) { throw 'Only the behavior host executable belongs in the runtime payload.' }
$ffmpegLock = (Get-Content (Join-Path $repo 'build/dependencies.lock.json') -Raw | ConvertFrom-Json).ffmpeg
$source = Join-Path $runtime "third-party/ffmpeg-$($ffmpegLock.version).tar.xz"
if ((Get-FileHash -LiteralPath $source).Hash -ne $ffmpegLock.sha256) { throw 'Bundled FFmpeg source does not match its pinned checksum.' }
foreach ($notice in @('LICENSE.LGPL-2.1.txt','Winpthreads-LICENSE.txt','GCC-Runtime-Exception.txt','GCC-GPL-3.txt','MediaInfo.License.html','README.md','libass-ISC.txt','FreeType-FTL.txt','HarfBuzz-MIT.txt','HarfBuzz-Microsoft-MIT.txt','FriBidi-LGPL-2.1.txt','DanmakuFactory-MIT.txt','PCRE2-BSD.txt','Zstandard-BSD.txt','Brotli-MIT.txt','DanAnyAss-MIT.txt','Base16384-MIT.txt')) {
    $installed = Join-Path $runtime "LICENSES/$notice"
    if (-not (Test-Path -LiteralPath $installed)) { throw "Missing third-party notice: $notice" }
    if ((Get-FileHash -LiteralPath $installed).Hash -ne (Get-FileHash -LiteralPath (Join-Path $repo "LICENSES/$notice")).Hash) { throw "Third-party notice differs from source: $notice" }
}
$assLock=(Get-Content (Join-Path $repo 'build/dependencies.lock.json') -Raw | ConvertFrom-Json).libass
foreach($item in $assLock.sources) {
    $archive=Join-Path $runtime ('third-party/libass/'+$item.archive)
    if((Get-FileHash -LiteralPath $archive).Hash -ne $item.sha256) { throw ('libass source checksum mismatch: '+$item.name) }
}
foreach($dll in @('BilibiliAss.dll','libass-9.dll','BilibiliDanmakuFactory.dll','BilibiliRawCompression.dll')) { if(-not (Test-Path -LiteralPath (Join-Path $runtime $dll))) { throw ('Missing text rendering/conversion dependency: '+$dll) } }
$factoryLock=(Get-Content (Join-Path $repo 'build/dependencies.lock.json') -Raw|ConvertFrom-Json).danmakuFactory
$factory=Join-Path $runtime 'third-party/danmaku-factory'
$revision=Get-Content (Join-Path $factory 'DanmakuFactory-revision.json') -Raw|ConvertFrom-Json
if($revision.Commit -ne $factoryLock.commit -or (Get-FileHash (Join-Path $factory 'DanmakuFactory-source.zip')).Hash -ne $revision.SourceSHA256) {throw 'DanmakuFactory source/revision mismatch.'}
if((Get-FileHash (Join-Path $factory $factoryLock.pcre2.archive)).Hash -ne $factoryLock.pcre2.sha256) {throw 'PCRE2 source checksum mismatch.'}
$rawLock = (Get-Content (Join-Path $repo 'build/dependencies.lock.json') -Raw | ConvertFrom-Json).rawCompression
foreach ($item in $rawLock.sources) {
    if ((Get-FileHash -LiteralPath (Join-Path $runtime ('third-party/raw-compression/'+$item.archive))).Hash -ne $item.sha256) { throw ('Compression source checksum mismatch: '+$item.name) }
}
if ((Get-FileHash -LiteralPath (Join-Path $runtime 'third-party/raw-compression/raw-compression.ps1')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $repo 'build/raw-compression.ps1')).Hash) { throw 'Compression build script mismatch.' }
foreach ($legacyNotice in @('MediaInfo.License.html','THIRD_PARTY_NOTICES.md','third-party/LICENSE.LGPL-2.1.txt','third-party/Winpthreads-LICENSE.txt','third-party/GCC-Runtime-Exception.txt','third-party/GCC-GPL-3.txt')) {
    if (Test-Path -LiteralPath (Join-Path $runtime $legacyNotice)) { throw "Third-party notice outside LICENSES: $legacyNotice" }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $package 'BilibiliLite.dsf'))
try { if ($zip.Entries.Count -ne 47) { throw 'Skin package lost required default-skin resources.' } } finally { $zip.Dispose() }
Write-Output "PASS: $($checksums.Count) payload checksums, eight modules, third-party sources/notices, and complete skin."
