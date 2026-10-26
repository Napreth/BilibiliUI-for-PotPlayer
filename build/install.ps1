param(
    [string]$PlayerPath,
    [string]$PayloadPath = (Join-Path $PSScriptRoot 'out/package'),
    [string]$StateRoot,
    [string]$LegacyBackupPath,
    [switch]$PrepareOnly,
    [switch]$ApplyOnly,
    [string]$RegistryPath = 'Software\DAUM\PotPlayerMini64\Settings'
)
. (Join-Path $PSScriptRoot 'installation-common.ps1')
Assert-PlayerClosed
$PlayerPath = Resolve-PlayerFile $PlayerPath
$playerRoot = Split-Path $PlayerPath -Parent
if (-not $StateRoot) { $StateRoot = Get-InstallationStateRoot }
$runtime = Resolve-InstallChild $playerRoot 'Extension/BilibiliBehaviors'
Stop-BehaviorHost $runtime
$profileFile = Join-Path $PayloadPath 'input-profile.json'
if (-not (Test-Path -LiteralPath $profileFile)) { $profileFile = Join-Path $runtime 'input-profile.json' }
$profile = Get-Content -LiteralPath $profileFile -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Path $StateRoot -Force | Out-Null
$backupPath = Join-Path $StateRoot 'original-settings.json'
if ((Test-Path -LiteralPath (Join-Path $StateRoot 'last-uninstall.json')) -and -not $ApplyOnly) {
    $history = Join-Path $StateRoot ('history/' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $history -Force | Out-Null
    if (Test-Path -LiteralPath $backupPath) { Move-Item -LiteralPath $backupPath -Destination $history }
    Get-ChildItem -LiteralPath $StateRoot -Filter 'saved-*' -File | ForEach-Object { Move-Item -LiteralPath $_.FullName -Destination $history }
    Move-Item -LiteralPath (Join-Path $StateRoot 'last-uninstall.json') -Destination $history
}
$bootstrap = 'Extension/Media/PlayParse/MediaPlayParse - BilibiliBehaviors.as'
$legacyBootstrap = Resolve-InstallChild $playerRoot 'Extension/Media/PlayParse/MediaPlayParse - BilibiliInput.as'
if (-not (Test-Path -LiteralPath $backupPath)) {
    if ($ApplyOnly) { throw 'Prepare installation before applying preferences.' }
    if (Test-Path -LiteralPath (Join-Path $runtime 'BilibiliBehaviors.exe')) { throw 'An installation exists without its original settings backup.' }
    $probe = Join-Path $playerRoot ('.bilibili-write-' + [guid]::NewGuid().ToString('N'))
    try { [IO.File]::WriteAllText($probe,''); Remove-Item -LiteralPath $probe }
    catch { throw 'The PotPlayer directory is not writable. Run the installer with /ALLUSERS to request administrator rights, using the same Windows account.' }
    if ($RegistryPath -ne 'Software\DAUM\PotPlayerMini64\Settings' -and $RegistryPath -notlike 'Software\BilibiliBehaviors.Tests\*') { throw 'Invalid registry path.' }
    $legacy = $null
    if (Test-Path -LiteralPath $legacyBootstrap) {
        if (-not $LegacyBackupPath) {
            $candidate = Join-Path (Split-Path $PSScriptRoot -Parent) 'settings/.local/original-input.json'
            if (Test-Path -LiteralPath $candidate) { $LegacyBackupPath = $candidate }
        }
        if (-not $LegacyBackupPath) { throw 'Legacy installation detected. Restore it first, or supply its original-input.json using -LegacyBackupPath (installer: /LEGACYBACKUP="path").' }
        $legacy = Get-Content -LiteralPath $LegacyBackupPath -Raw | ConvertFrom-Json
        if ($legacy.SchemaVersion -ne 1 -or $legacy.RegistryPath -ne $RegistryPath) { throw 'Invalid legacy backup.' }
        $legacyRuntime = Resolve-InstallChild $playerRoot 'Extension/BilibiliInput'
        if ($legacy.RuntimePath -and [IO.Path]::GetFullPath($legacy.RuntimePath) -ne $legacyRuntime) { throw 'Legacy backup belongs to another installation.' }
        Stop-BehaviorHost $legacyRuntime
    }
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($RegistryPath)
    try {
        $names = @($profile.Native.PSObject.Properties.Name) + @('LastSkinName','LastSkinXmlName','LastSkinXmlNameVideo','LastSkinXmlNameAudio')
        $values = @($names | Select-Object -Unique | ForEach-Object {
            $name = $_; $original = if ($legacy) { $legacy.Values | Where-Object Name -eq $name | Select-Object -First 1 }
            if ($original) { $original }
            elseif ($legacy -and $name -eq 'LastSkinName' -and $key.GetValue($name) -eq 'BilibiliLite.dsf') { [ordered]@{Name=$name;Exists=$true;Kind='String';Value='Default.dsf'} }
            else { $exists=$key.GetValueNames() -contains $name; [ordered]@{Name=$name;Exists=$exists;Kind=if($exists){$key.GetValueKind($name).ToString()}else{'DWord'};Value=if($exists){$key.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)}else{$null}} }
        })
    } finally { $key.Dispose() }
    $shortcuts = @()
    $shortcutPath = $RegistryPath -replace '\\Settings$','\Main ShortCutList'
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($shortcutPath)
    if ($key) {
        try { $shortcuts = @($key.GetValueNames() | ForEach-Object { [ordered]@{Name=$_;Kind=$key.GetValueKind($_).ToString();Value=$key.GetValue($_,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)} }) }
        finally { $key.Dispose() }
    }
    if ($legacy) { $shortcuts = @($legacy.Shortcuts) }
    $files = @()
    foreach ($relative in @($bootstrap,'Skins/BilibiliLite.dsf')) {
        $original = Resolve-InstallChild $playerRoot $relative
        $exists = Test-Path -LiteralPath $original
        if ($legacy -and $relative -eq 'Skins/BilibiliLite.dsf') { $exists = $false }
        $savedName = 'saved-' + $files.Count
        if ($exists) { Copy-Item -LiteralPath $original -Destination (Join-Path $StateRoot $savedName) -Force }
        $files += [ordered]@{Path=$relative;Exists=$exists;Saved=$savedName}
    }
    Write-InstallJson $backupPath ([ordered]@{SchemaVersion=2;PlayerRoot=$playerRoot;RegistryPath=$RegistryPath;Values=$values;Shortcuts=$shortcuts;Files=$files;Legacy=($null -ne $legacy)})
}
$backup = Read-InstallBackup $backupPath
if ([IO.Path]::GetFullPath($backup.PlayerRoot) -ne $playerRoot) { throw 'Uninstall the other PotPlayer installation before changing the target directory.' }
if ($PrepareOnly) { Write-Output 'Installation backup is ready.'; return }
if (-not $ApplyOnly) {
    $source = Join-Path $PayloadPath 'runtime'
    if (-not (Test-Path -LiteralPath (Join-Path $source 'BilibiliBehaviors.exe'))) { throw 'Build the installation package first.' }
    New-Item -ItemType Directory -Path $runtime -Force | Out-Null
    Copy-Item -Path (Join-Path $source '*') -Destination $runtime -Recurse -Force
    foreach ($relative in @($bootstrap,'Skins/BilibiliLite.dsf')) {
        $destination = Resolve-InstallChild $playerRoot $relative
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $PayloadPath (Split-Path $relative -Leaf)) -Destination $destination -Force
    }
}
$key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($backup.RegistryPath)
try {
    foreach ($value in $profile.Native.PSObject.Properties) { $key.SetValue($value.Name,[int]$value.Value,[Microsoft.Win32.RegistryValueKind]::DWord) }
    $key.SetValue('LastSkinName','BilibiliLite.dsf',[Microsoft.Win32.RegistryValueKind]::String)
    $key.SetValue('LastSkinXmlName','VideoSkin.xml',[Microsoft.Win32.RegistryValueKind]::String)
    $key.SetValue('LastSkinXmlNameVideo','VideoSkin.xml',[Microsoft.Win32.RegistryValueKind]::String)
} catch { Restore-PlayerPreferences $backup; throw } finally { $key.Dispose() }
$key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey(($backup.RegistryPath -replace '\\Settings$','\Main ShortCutList'))
try { foreach ($name in $key.GetValueNames()) { $key.DeleteValue($name) } } finally { $key.Dispose() }
if ($backup.Legacy -and (Test-Path -LiteralPath $legacyBootstrap)) { Remove-Item -LiteralPath $legacyBootstrap }
$translationArguments = '--native-translation-prepare "' + $playerRoot + '" "' + $StateRoot + '"'
$translation = Start-Process -FilePath (Join-Path $runtime 'BilibiliBehaviors.exe') -ArgumentList $translationArguments -WindowStyle Hidden -Wait -PassThru
if ($translation.ExitCode -ne 0) { throw 'Native translation extension preparation failed; original scripts are retained in the installation recovery folder.' }
Write-Output "Installed BilibiliBehaviors: $runtime"
