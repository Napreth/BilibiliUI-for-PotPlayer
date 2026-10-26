param([string]$Compiler = 'gcc')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$layout = Join-Path $repo 'src/Native/PlayerLayout'
& $Compiler -shared -O2 -s -static-libgcc '-Wl,--no-insert-timestamp' -o "$layout/PlayerLayout.dll" "$layout/PlayerLayout.c" -luser32
if ($LASTEXITCODE -ne 0) { throw 'PlayerLayout compilation failed (x64 MinGW-w64 is required).' }
$translation = Join-Path $repo 'src/Native/SubtitleTranslation'
& $Compiler -shared -O2 -s -static-libgcc '-Wl,--no-insert-timestamp' -o "$translation/BilibiliTranslation.dll" "$translation/SubtitleTranslation.c" -luser32
if ($LASTEXITCODE -ne 0) { throw 'Native subtitle translation bridge compilation failed.' }
$lock = (Get-Content (Join-Path $PSScriptRoot 'dependencies.lock.json') -Raw | ConvertFrom-Json).ffmpeg
$source = Join-Path $repo ".local/dependencies/ffmpeg-$($lock.version)"
$libraries = Join-Path $repo 'third_party/FFmpeg/win-x64'
$text = Join-Path $repo 'src/Native/TimedText'
& $Compiler -shared -O2 -s -static-libgcc '-Wl,--no-insert-timestamp' "-I$source" -o "$text/BilibiliTimedText.dll" "$text/TimedText.c" "$libraries/avformat.lib" "$libraries/avcodec.lib" "$libraries/avutil.lib"
if ($LASTEXITCODE -ne 0) { throw 'Timed text bridge compilation failed. Run build/ffmpeg.ps1 first.' }
$ass=Join-Path $repo 'src/Native/AssRenderer'
$assSource=Join-Path $repo '.local/dependencies/libass/libass-0.17.5'
& $Compiler -shared -O2 -s -static-libgcc '-Wl,--no-insert-timestamp' "-I$assSource" -o "$ass/BilibiliAss.dll" "$ass/AssRenderer.c" "$repo/third_party/libass/win-x64/libass.dll.a"
if ($LASTEXITCODE -ne 0) { throw 'ASS renderer bridge compilation failed. Run build/libass.ps1 first.' }
& (Join-Path $PSScriptRoot 'danmaku-factory.ps1') -BridgeOnly -Compiler $Compiler
& (Join-Path $PSScriptRoot 'raw-compression.ps1') -BridgeOnly -Compiler $Compiler
