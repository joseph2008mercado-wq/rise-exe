param([string]$PublishDirectory = 'artifacts/publish', [string]$OutputDirectory = 'artifacts/installer')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$publishPath = [IO.Path]::GetFullPath((Join-Path $projectRoot $PublishDirectory))
$outputPath = [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
$toolPath = Join-Path $projectRoot '.tools/nsis/nsis-3.12/makensis.exe'
if (-not (Test-Path $toolPath)) {
    $archivePath = Join-Path $projectRoot '.tools/nsis-3.12.zip'
    New-Item -ItemType Directory (Split-Path $archivePath) -Force | Out-Null
    $downloadUri = 'https://downloads.sourceforge.net/project/nsis/NSIS%203/3.12/nsis-3.12.zip'
    Invoke-WebRequest -Uri $downloadUri -OutFile $archivePath
    $bytes = [IO.File]::ReadAllBytes($archivePath)
    if ($bytes[0] -ne 80 -or $bytes[1] -ne 75) {
        # SourceForge sometimes serves its intermediate download page first.
        $page = [IO.File]::ReadAllText($archivePath)
        $downloadUri = [Net.WebUtility]::HtmlDecode([regex]::Match($page, 'https://downloads\.sourceforge\.net/project/nsis/NSIS%203/3\.12/nsis-3\.12\.zip\?[^"<> ]+').Value)
        if (-not $downloadUri) { throw 'Official NSIS download link not found' }
        Invoke-WebRequest -Uri $downloadUri -OutFile $archivePath
    }
    # Published on the official SourceForge nsis-3.12.zip download page.
    if ((Get-FileHash $archivePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne '56581f90db321581c5381193d796fffcf2d24b2f8fed2160a6c6a3baa67f2c4f') { throw 'NSIS archive checksum mismatch' }
    [IO.Compression.ZipFile]::ExtractToDirectory($archivePath, (Join-Path $projectRoot '.tools/nsis'), $true)
}
if (-not (Test-Path (Join-Path $publishPath 'RISE.exe'))) { throw 'Build RISE before packaging' }
New-Item -ItemType Directory $outputPath -Force | Out-Null
[xml]$project = Get-Content (Join-Path $projectRoot 'src/Rise.App/Rise.App.csproj')
$version = [string]$project.Project.PropertyGroup.Version
$setupPath = Join-Path $outputPath 'RISE-Setup.exe'
& $toolPath /V2 "/DAPP_VERSION=$version" "/DPUBLISH_DIR=$publishPath" "/DSETUP_OUTPUT=$setupPath" (Join-Path $projectRoot 'packaging/RISE.nsi')
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed ($LASTEXITCODE)" }
$hash = (Get-FileHash $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  RISE-Setup.exe" | Set-Content (Join-Path $outputPath 'SHA256SUMS.txt')
Write-Output "Windows installer ready: $setupPath"
