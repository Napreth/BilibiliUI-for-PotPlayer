param([switch]$Rebuild, [switch]$DependenciesOnly, [switch]$BridgeOnly, [string]$Compiler = 'gcc')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$lock = (Get-Content (Join-Path $PSScriptRoot 'dependencies.lock.json') -Raw | ConvertFrom-Json).rawCompression
$cache = Join-Path $repo '.local/dependencies'
$root = Join-Path $cache 'raw-compression'
$prepared = Join-Path $root 'win-x64'
$include = Join-Path $prepared 'include'
$library = Join-Path $prepared 'libRawCompression.a'
$stamp = Join-Path $prepared 'source-fingerprint.txt'
$bridge = Join-Path $repo 'src/Native/RawCompression'
if ($BridgeOnly -and ($Rebuild -or $DependenciesOnly)) { throw 'BridgeOnly cannot prepare external dependencies.' }
if (-not $BridgeOnly) {
    New-Item -ItemType Directory -Path $root,$prepared,$include -Force | Out-Null
    foreach ($item in $lock.sources) {
        $archive = Join-Path $cache $item.archive
        if (-not (Test-Path -LiteralPath $archive)) { Invoke-WebRequest -Uri $item.url -OutFile $archive }
        if ((Get-FileHash -LiteralPath $archive).Hash -ne $item.sha256) { throw ('Compression source checksum mismatch: '+$item.name) }
        $source = Join-Path $root $item.directory
        if ($item.name -eq 'zstd') {
            if (-not (Test-Path -LiteralPath (Join-Path $source 'lib/zstd.h'))) {
                & tar.exe -xf $archive -C $root ($item.directory+'/lib') ($item.directory+'/LICENSE')
                if ($LASTEXITCODE -ne 0) { throw 'Zstandard source extraction failed.' }
            }
            Copy-Item -LiteralPath (Join-Path $source 'lib/zstd.h'),(Join-Path $source 'lib/zstd_errors.h') -Destination $include -Force
            Copy-Item -LiteralPath (Join-Path $source 'LICENSE') -Destination (Join-Path $repo 'LICENSES/Zstandard-BSD.txt') -Force
        } else {
            if (-not (Test-Path -LiteralPath (Join-Path $source 'c/include/brotli/decode.h'))) {
                & tar.exe -xf $archive -C $root ($item.directory+'/c') ($item.directory+'/LICENSE')
                if ($LASTEXITCODE -ne 0) { throw 'Brotli source extraction failed.' }
            }
            Copy-Item -LiteralPath (Join-Path $source 'c/include/brotli') -Destination $include -Recurse -Force
            Copy-Item -LiteralPath (Join-Path $source 'LICENSE') -Destination (Join-Path $repo 'LICENSES/Brotli-MIT.txt') -Force
        }
    }
    $fingerprint = ($lock.sources.sha256 -join ':') + ':' + (Get-FileHash -LiteralPath $PSCommandPath).Hash
    if ($Rebuild -or -not (Test-Path -LiteralPath $library) -or -not (Test-Path -LiteralPath $stamp) -or (Get-Content -LiteralPath $stamp -Raw).Trim() -ne $fingerprint) {
        $zstd = Join-Path $root $lock.sources[0].directory
        $brotli = Join-Path $root $lock.sources[1].directory
        $objects = @()
        foreach ($kind in @('zstd','brotli')) {
            $directories = if ($kind -eq 'zstd') { @('lib/common','lib/decompress') | ForEach-Object { Join-Path $zstd $_ } } else { @('c/common','c/dec') | ForEach-Object { Join-Path $brotli $_ } }
            foreach ($directory in $directories) {
                foreach ($file in Get-ChildItem -LiteralPath $directory -Filter '*.c' -File) {
                    $object = Join-Path $prepared ($kind+'-'+$file.BaseName+'.o')
                    & $Compiler -std=c11 -c -O2 -ffunction-sections -fdata-sections -DZSTD_DISABLE_ASM -DDYNAMIC_BMI2=0 -DBROTLI_STATIC "-I$zstd/lib" "-I$brotli/c/include" -o $object $file.FullName
                    if ($LASTEXITCODE -ne 0) { throw ('Compression decoder compilation failed: '+$file.Name) }
                    $objects += $object
                }
            }
        }
        if (Test-Path -LiteralPath $library) { Remove-Item -LiteralPath $library }
        $archiver = Join-Path (Split-Path (Get-Command $Compiler -ErrorAction Stop).Source -Parent) 'ar.exe'
        & $archiver rcs $library @objects
        if ($LASTEXITCODE -ne 0) { throw 'Compression decoder archive failed.' }
        Set-Content -LiteralPath $stamp -Value $fingerprint -Encoding ASCII
    }
}
if ($DependenciesOnly) { Write-Output 'Prepared Zstandard and Brotli decoders.'; return }
if (-not (Test-Path -LiteralPath $library)) { throw 'Run build/raw-compression.ps1 or restore the external dependency cache first.' }
& $Compiler -std=c11 -shared -O2 -s -static-libgcc -DBROTLI_STATIC -ffunction-sections -fdata-sections '-Wl,--gc-sections' '-Wl,--exclude-all-symbols' '-Wl,--no-insert-timestamp' "-I$include" -o "$bridge/BilibiliRawCompression.dll" "$bridge/RawCompression.c" $library
if ($LASTEXITCODE -ne 0) { throw 'Raw compression bridge compilation failed.' }
Write-Output 'Built bounded raw metadata decompression bridge.'
