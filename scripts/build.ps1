param([string]$Configuration = 'Release', [string]$OutputDirectory = 'artifacts/publish')
$ErrorActionPreference = 'Stop'
function Invoke-DotNet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $args failed ($LASTEXITCODE)" }
}
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    Invoke-DotNet restore RISE.slnx
    Invoke-DotNet build RISE.slnx -c $Configuration --no-restore
    Invoke-DotNet run --project tests/Rise.Tests -c $Configuration --no-build
    Invoke-DotNet publish src/Rise.App -c $Configuration -r win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:EnableCompressionInSingleFile=true' '-p:DebugType=None' '-p:DebugSymbols=false' -o $OutputDirectory
    $executable = Join-Path $OutputDirectory 'RISE.exe'
    if (-not (Test-Path $executable)) { throw 'RISE.exe missing' }
    $env:RISE_TEST_OUTPUT = Join-Path $PWD 'artifacts/smoke'
    $test = Start-Process -FilePath $executable -ArgumentList '--self-test' -PassThru -WindowStyle Hidden
    if (-not $test.WaitForExit(60000)) { Stop-Process -Id $test.Id; throw 'WPF tests timed out' }
    Get-Content artifacts/smoke/results.txt
    if ($test.ExitCode -ne 0) { throw "WPF integration tests failed ($($test.ExitCode))" }
    Copy-Item LICENSE,THIRD_PARTY_NOTICES.md $OutputDirectory
    Copy-Item licenses $OutputDirectory -Recurse -Force
    $hash = (Get-FileHash $executable -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  RISE.exe" | Set-Content (Join-Path $OutputDirectory 'SHA256SUMS.txt')
    Write-Output "RISE.exe and redistribution notices ready in $OutputDirectory"
}
finally { Pop-Location }
