$ErrorActionPreference = 'Stop'
$global:LASTEXITCODE = 0
$repo = Split-Path $PSScriptRoot -Parent
$id = [guid]::NewGuid().ToString('N')
$sandbox = Join-Path $repo ".local/installation-check-$id"
$playerRoot = Join-Path $sandbox 'player'
$stateRoot = Join-Path $sandbox 'state'
$registryPath = "Software\BilibiliBehaviors.Tests\$id\Settings"
$shortcutPath = "Software\BilibiliBehaviors.Tests\$id\Main ShortCutList"
function Check($Condition,[string]$Message) { if (-not $Condition) { throw $Message }; Write-Output "PASS: $Message" }
try {
    New-Item -ItemType Directory -Path (Join-Path $playerRoot 'Skins'),(Join-Path $playerRoot 'Extension/Media/PlayParse') -Force | Out-Null
    $player = Join-Path $playerRoot 'PotPlayerMini64.exe'
    Copy-Item -LiteralPath (Join-Path $repo 'build/out/runtime/BilibiliBehaviors.exe') -Destination $player
    $skin = Join-Path $playerRoot 'Skins/BilibiliLite.dsf'
    $bootstrap = Join-Path $playerRoot 'Extension/Media/PlayParse/MediaPlayParse - BilibiliBehaviors.as'
    [IO.File]::WriteAllText($skin,'original skin')
    [IO.File]::WriteAllText($bootstrap,'original extension')
    $translateFolder = Join-Path $playerRoot 'Extension/Subtitle/Translate'
    New-Item -ItemType Directory -Path $translateFolder -Force | Out-Null
    $translator = Join-Path $translateFolder 'SubtitleTranslate - OfflineTest.as'
    $translatorSource = 'string GetTitle(){return "Offline test";} string Translate(string Text,string &in SrcLang,string &in DstLang){return Text;}'
    [IO.File]::WriteAllText($translator,$translatorSource,[Text.UTF8Encoding]::new($false))
    $bridge = Join-Path $translateFolder 'BilibiliTranslation.dll'
    [IO.File]::WriteAllText($bridge,'original companion')
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($registryPath)
    $key.SetValue('MouseRightClick',7,[Microsoft.Win32.RegistryValueKind]::DWord)
    $key.SetValue('LastSkinName','BeforeTest.dsf',[Microsoft.Win32.RegistryValueKind]::String)
    $key.Dispose()
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($shortcutPath)
    $key.SetValue('binary',[byte[]]@(1,2,128,255),[Microsoft.Win32.RegistryValueKind]::Binary)
    $key.SetValue('expand','%TEMP%\original',[Microsoft.Win32.RegistryValueKind]::ExpandString)
    $key.Dispose()
    $arguments = @{PlayerPath=$player;StateRoot=$stateRoot;RegistryPath=$registryPath;PayloadPath=(Join-Path $repo 'build/out/package')}
    & (Join-Path $repo 'build/install.ps1') @arguments
    Check ($LASTEXITCODE -eq 0) 'script installation succeeds in an isolated player/registry fixture'
    Check ((Get-Content -LiteralPath $translator -Raw).StartsWith('// Bilibili native translation bridge')) 'installation adapts native translators under their original filenames'
    Check ((Get-FileHash -LiteralPath $bridge).Hash -eq (Get-FileHash -LiteralPath (Join-Path $playerRoot 'Extension/BilibiliBehaviors/BilibiliTranslation.dll')).Hash) 'installation deploys the matching companion bridge beside native translators'
    $backupPath = Join-Path $stateRoot 'original-settings.json'
    $originalHash = (Get-FileHash -LiteralPath $backupPath).Hash
    & (Join-Path $repo 'build/install.ps1') @arguments -PrepareOnly
    Check ((Get-FileHash -LiteralPath $backupPath).Hash -eq $originalHash) 'upgrade preparation preserves the first original backup'
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryPath)
    Check ($key.GetValue('LastSkinName') -eq 'BilibiliLite.dsf' -and $key.GetValue('MouseRightClick') -eq 22) 'installation selects the skin and applies native preferences'
    $key.Dispose()
    & (Join-Path $repo 'build/uninstall.ps1') -StateRoot $stateRoot
    Check ($LASTEXITCODE -eq 0) 'script uninstallation succeeds'
    Check ((Get-Content -LiteralPath $translator -Raw) -eq $translatorSource -and (Get-Content -LiteralPath $bridge -Raw) -eq 'original companion') 'uninstallation restores original translators and preexisting companion bytes'
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryPath)
    Check ($key.GetValue('LastSkinName') -eq 'BeforeTest.dsf' -and $key.GetValue('MouseRightClick') -eq 7) 'uninstallation restores original preferences'
    Check (-not ($key.GetValueNames() -contains 'UseMediaKey')) 'uninstallation removes values originally absent'
    $key.Dispose()
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($shortcutPath)
    Check (([BitConverter]::ToString($key.GetValue('binary'))) -eq '01-02-80-FF') 'binary keyboard shortcuts round-trip through JSON'
    Check ($key.GetValue('expand',$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) -eq '%TEMP%\original') 'expandable settings preserve their unexpanded text'
    $key.Dispose()
    Check ((Get-Content -LiteralPath $skin -Raw) -eq 'original skin' -and (Get-Content -LiteralPath $bootstrap -Raw) -eq 'original extension') 'existing skin and extension files are restored'
    Check (-not (Test-Path -LiteralPath (Join-Path $playerRoot 'Extension/BilibiliBehaviors/BilibiliBehaviors.exe'))) 'uninstallation removes the installed host'
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($registryPath)
    $key.SetValue('MouseRightClick',9,[Microsoft.Win32.RegistryValueKind]::DWord); $key.Dispose()
    Remove-Item -LiteralPath $bridge
    & (Join-Path $repo 'build/install.ps1') @arguments
    & (Join-Path $repo 'build/uninstall.ps1') -StateRoot $stateRoot
    Check (-not (Test-Path -LiteralPath $bridge)) 'uninstallation removes the companion when it was originally absent'
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryPath)
    Check ($key.GetValue('MouseRightClick') -eq 9) 'a fresh installation after removal captures new original preferences'; $key.Dispose()
} finally {
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree("Software\BilibiliBehaviors.Tests\$id",$false)
    $safeRoot = [IO.Path]::GetFullPath((Join-Path $repo '.local')).TrimEnd('\') + '\'
    $absolute = [IO.Path]::GetFullPath($sandbox)
    if (-not $absolute.StartsWith($safeRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test cleanup path.' }
    if (Test-Path -LiteralPath $sandbox) { Remove-Item -LiteralPath $sandbox -Recurse -Force }
}
