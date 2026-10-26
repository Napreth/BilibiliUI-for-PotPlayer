param([string]$StateRoot, [switch]$CheckOnly)
. (Join-Path $PSScriptRoot 'installation-common.ps1')
Assert-PlayerClosed
if (-not $StateRoot) { $StateRoot = Get-InstallationStateRoot }
$backupPath = Join-Path $StateRoot 'original-settings.json'
if (-not (Test-Path -LiteralPath $backupPath)) { throw 'Original installation backup is missing; refusing to remove files without restoring settings.' }
$backup = Read-InstallBackup $backupPath
$runtime = Resolve-InstallChild $backup.PlayerRoot 'Extension/BilibiliBehaviors'
$manifest = Join-Path $runtime 'installed-files.json'
if (-not (Test-Path -LiteralPath $manifest)) { throw 'Installed file manifest is missing; installation files were retained for recovery.' }
[string[]]$installedFiles = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
foreach ($relative in $installedFiles) { $null = Resolve-InstallChild $runtime $relative }
foreach ($file in $backup.Files) {
    if ($file.Exists -and -not (Test-Path -LiteralPath (Resolve-InstallChild $StateRoot $file.Saved))) { throw 'A backed-up skin or extension file is missing.' }
}
if ($CheckOnly) { return }
Stop-BehaviorHost $runtime
$translationArguments = '--native-translation-restore "' + $backup.PlayerRoot + '" "' + $StateRoot + '"'
$translation = Start-Process -FilePath (Join-Path $runtime 'BilibiliBehaviors.exe') -ArgumentList $translationArguments -WindowStyle Hidden -Wait -PassThru
if ($translation.ExitCode -ne 0) { throw 'Native translation extension restoration failed; installation files and recovery copies were retained.' }
Restore-PlayerPreferences $backup
foreach ($file in $backup.Files) {
    if ($file.Path -notin @('Extension/Media/PlayParse/MediaPlayParse - BilibiliBehaviors.as','Skins/BilibiliLite.dsf')) { throw 'Invalid backup file path.' }
    $destination = Resolve-InstallChild $backup.PlayerRoot $file.Path
    if ($file.Exists) { Copy-Item -LiteralPath (Resolve-InstallChild $StateRoot $file.Saved) -Destination $destination -Force }
    elseif (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination }
}
# Remove only files recorded by this distribution, never recursively delete a player folder.
if (Test-Path -LiteralPath $manifest) {
    foreach ($relative in $installedFiles) {
        $file = Resolve-InstallChild $runtime $relative
        if (Test-Path -LiteralPath $file -PathType Leaf) { Remove-Item -LiteralPath $file }
    }
    Remove-Item -LiteralPath $manifest
}
# Keep original backup and caption preferences for recovery; mark the installation inactive.
Write-InstallJson (Join-Path $StateRoot 'last-uninstall.json') @{Completed=[DateTime]::UtcNow.ToString('o');PlayerRoot=$backup.PlayerRoot}
Write-Output 'Original player settings restored; caption preferences retained.'
