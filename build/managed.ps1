param([switch]$Test)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$output = Join-Path $repo 'build/out/runtime'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The x64 .NET Framework compiler is required.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$framework = @('/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll','/r:System.Xml.dll')
$modules = [ordered]@{
    Contracts = @()
    Platform = @('Contracts')
    Input = @('Contracts','Platform')
    Windowing = @('Contracts','Platform')
    Media = @('Contracts','Platform')
    Captions = @('Contracts','Platform')
    Playback = @('Contracts','Platform','Media')
    Runtime = @('Contracts','Platform','Input','Windowing','Media','Captions','Playback')
}
# Branch switches can leave assemblies from modules absent in the current graph.
# Never let package.ps1 silently redistribute those stale DLLs.
$moduleFiles = @($modules.Keys | ForEach-Object { "BilibiliBehaviors.$_.dll" })
Get-ChildItem -LiteralPath $output -Filter 'BilibiliBehaviors.*.dll' -File |
    Where-Object { $_.Name -notin $moduleFiles } | ForEach-Object { Remove-Item -LiteralPath $_.FullName }
$version = (Get-Content (Join-Path $PSScriptRoot 'version.txt') -Raw).Trim()
foreach ($module in $modules.Keys) {
    $friends = @('BilibiliBehaviors.Tests')
    foreach ($consumer in $modules.Keys) { if ($modules[$consumer] -contains $module) { $friends += "BilibiliBehaviors.$consumer" } }
    if ($module -eq 'Runtime') { $friends += 'BilibiliBehaviors' }
    $attributes = @('using System.Reflection;','using System.Runtime.CompilerServices;',"[assembly: AssemblyVersion(`"$version`")]")
    $attributes += $friends | ForEach-Object { "[assembly: InternalsVisibleTo(`"$_`") ]" }
    $info = Join-Path $output "$module.AssemblyInfo.cs"
    $attributes | Set-Content -LiteralPath $info -Encoding UTF8
    $references = @($modules[$module] | ForEach-Object { "/r:$output/BilibiliBehaviors.$_.dll" })
    $sources = @(Get-ChildItem -LiteralPath (Join-Path $repo "src/$module") -Filter '*.cs' -File | ForEach-Object FullName)
    $resources = @()
    if ($module -eq 'Platform') {
        $asset = Join-Path $repo 'src/Platform/assets'
        $resources += "/resource:$asset/dolby-double-d.svg,BilibiliPotPlayer.DolbyDoubleD.svg"
        $resources += @('scroll','fixed','color','advanced' | ForEach-Object { "/resource:$asset/bili-filter-$_.svg,BilibiliPotPlayer.Filter.$_.svg" })
    }
    & $compiler /nologo /optimize+ /platform:x64 /target:library @framework @references @resources "/out:$output/BilibiliBehaviors.$module.dll" $info @sources
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $module" }
}
$hostReferences = @($modules.Keys | ForEach-Object { "/r:$output/BilibiliBehaviors.$_.dll" })
$manifest = "/win32manifest:$repo/src/Host/app.manifest"
$hostInfo = Join-Path $output 'Host.AssemblyInfo.cs'
@('using System.Reflection;',"[assembly: AssemblyVersion(`"$version`")]", "[assembly: AssemblyFileVersion(`"$version`")]") | Set-Content -LiteralPath $hostInfo -Encoding UTF8
& $compiler /nologo /optimize+ /platform:x64 /target:winexe @framework @hostReferences $manifest "/out:$output/BilibiliBehaviors.exe" $hostInfo (Join-Path $repo 'src/Host/Program.cs')
if ($LASTEXITCODE -ne 0) { throw 'Host compilation failed.' }
$testSources = @(Get-ChildItem (Join-Path $repo 'tests') -Filter '*.cs' -File | ForEach-Object FullName)
& $compiler /nologo /optimize+ /platform:x64 /target:exe /main:BridgeTests @framework @hostReferences $manifest "/out:$output/BilibiliBehaviors.Tests.exe" @testSources
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
Copy-Item -LiteralPath (Join-Path $repo 'config/input-profile.json') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $repo 'third_party/MediaInfo/MediaInfo.dll') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $repo 'LICENSES') -Destination $output -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repo 'src/Native/PlayerLayout/PlayerLayout.dll') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $repo 'src/Native/SubtitleTranslation/BilibiliTranslation.dll') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $repo 'src/Native/TimedText/BilibiliTimedText.dll') -Destination $output -Force
Copy-Item -Path (Join-Path $repo 'third_party/FFmpeg/win-x64/*.dll') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $repo 'src/Native/AssRenderer/BilibiliAss.dll') -Destination $output -Force
Copy-Item -Path (Join-Path $repo 'third_party/libass/win-x64/*.dll') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $repo 'src/Native/DanmakuConverter/BilibiliDanmakuFactory.dll') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $repo 'src/Native/RawCompression/BilibiliRawCompression.dll') -Destination $output -Force
if ($Test) {
    & (Join-Path $output 'BilibiliBehaviors.Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Checks failed.' }
}
Write-Output "Built modular runtime: $output"
