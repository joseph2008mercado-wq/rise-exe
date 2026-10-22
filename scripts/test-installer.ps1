param([string]$Setup = 'artifacts/installer/RISE-Setup.exe')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$testRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot ('artifacts/installer-test-' + [Guid]::NewGuid().ToString('N'))))
$testKey = 'HKCU:\Software\RISE\InstallerTest'
if (Test-Path $testKey) { throw 'Previous installer test registry entry exists; inspect it before retrying' }
$results = [Collections.Generic.List[string]]::new()
function Assert-Installer($Condition, $Name) {
    if (-not $Condition) { throw $Name }
    $results.Add("PASS $Name")
}
$installerPath = [IO.Path]::GetFullPath((Join-Path $projectRoot $Setup))
$installedExe = Join-Path $testRoot 'RISE.exe'
$uninstaller = Join-Path $testRoot 'Uninstall.exe'
try {
    # Runtime TESTMODE keeps links within this unique test directory and uses a
    # separate registry key. No real desktop links, settings, or wake tasks change.
    $install = Start-Process -FilePath $installerPath -ArgumentList @('/S', '/TESTMODE', ('/D=' + $testRoot)) -PassThru -WindowStyle Hidden
    if (-not $install.WaitForExit(60000)) { throw 'Installer timed out' }
    Assert-Installer ($install.ExitCode -eq 0 -and (Test-Path $installedExe)) 'installer extracts the Windows application successfully'
    $registration = Get-ItemProperty $testKey
    Assert-Installer ($registration.DisplayName -eq 'RISE.EXE' -and $registration.InstallLocation -eq $testRoot -and $registration.UninstallString.Contains('Uninstall.exe')) 'installer registers app metadata and quoted uninstall command'
    $shell = New-Object -ComObject WScript.Shell
    try {
        foreach ($name in @('RISE.EXE.lnk', 'Start-RISE.EXE.lnk')) {
            $shortcutPath = Join-Path $testRoot ('TestShortcuts/' + $name)
            Assert-Installer (Test-Path $shortcutPath) "installer creates $name"
            $shortcut = $shell.CreateShortcut($shortcutPath)
            try { Assert-Installer ($shortcut.TargetPath -eq $installedExe -and $shortcut.WorkingDirectory -eq $testRoot) "$name targets the installed executable with the correct working directory" }
            finally { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) | Out-Null }
        }
    }
    finally { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null }
    $marker = Join-Path $testRoot 'keep-user-file.txt'
    'This file is not part of the installed app.' | Set-Content $marker
    $uninstall = Start-Process -FilePath $uninstaller -ArgumentList @('/S', ('_?=' + $testRoot)) -PassThru -WindowStyle Hidden
    if (-not $uninstall.WaitForExit(60000)) { throw 'Uninstaller timed out' }
    Assert-Installer ($uninstall.ExitCode -eq 0 -and -not (Test-Path $installedExe) -and -not (Test-Path $testKey)) 'uninstaller removes installed application and registry metadata'
    Assert-Installer (-not (Test-Path (Join-Path $testRoot 'TestShortcuts'))) 'uninstaller removes both shortcuts'
    Assert-Installer ((Test-Path $marker)) 'uninstaller preserves unrelated user files'
    Remove-Item -LiteralPath $marker
    # _?= runs the real uninstaller in place so the test can wait for it. Remove
    # its now-unlocked executable explicitly; never recursively delete this path.
    if (Test-Path $uninstaller) { Remove-Item -LiteralPath $uninstaller }
    Remove-Item -LiteralPath $testRoot
}
finally {
    New-Item -ItemType Directory (Join-Path $projectRoot 'artifacts/smoke') -Force | Out-Null
    $results | Set-Content (Join-Path $projectRoot 'artifacts/smoke/installer-results.txt')
    $results | Write-Output
}
