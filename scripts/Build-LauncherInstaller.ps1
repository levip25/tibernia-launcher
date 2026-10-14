param(
  [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\launcher-installer'),
  [string]$AppVersion = '',
  [switch]$SignArtifacts,
  [string]$SigningCertificatePath = $env:WINDOWS_SIGNING_PFX_PATH,
  [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if (-not [IO.Path]::IsPathRooted($OutputDirectory)) {
  $OutputDirectory = [IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputDirectory))
}
$launcherProject = Join-Path $root 'src\OtsLauncher.Windows\OtsLauncher.Windows.csproj'
$stage = Join-Path $OutputDirectory 'stage'
$installerScript = Join-Path $root 'installer\TiberniaLauncher.iss'
$setup = Join-Path $OutputDirectory 'Tibernia-Setup.exe'

function Get-SignToolPath {
  $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
  if ($command) { return $command.Source }
  $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
  $candidate = Get-ChildItem -LiteralPath $kits -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1
  if (-not $candidate) { throw 'signtool.exe was not found.' }
  return $candidate.FullName
}

function Invoke-CodeSign([string]$Path) {
  if (-not $SignArtifacts) { return }
  if (-not $SigningCertificatePath -or -not (Test-Path -LiteralPath $SigningCertificatePath)) {
    throw 'Production signing requested, but WINDOWS_SIGNING_PFX_PATH does not point to a certificate.'
  }
  if (-not $env:WINDOWS_SIGNING_PFX_PASSWORD) {
    throw 'Production signing requested, but WINDOWS_SIGNING_PFX_PASSWORD is empty.'
  }
  $signTool = Get-SignToolPath
  & $signTool sign /fd SHA256 /td SHA256 /tr $TimestampUrl /f $SigningCertificatePath /p $env:WINDOWS_SIGNING_PFX_PASSWORD $Path
  if ($LASTEXITCODE -ne 0) { throw "Authenticode signing failed for $Path" }
  & $signTool verify /pa /all $Path
  if ($LASTEXITCODE -ne 0) { throw "Authenticode verification failed for $Path" }
}

function Get-InnoCompilerPath {
  $command = Get-Command iscc.exe -ErrorAction SilentlyContinue
  if ($command) { return $command.Source }
  $candidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path $env:USERPROFILE 'AppData\Local\Programs\Inno Setup 6\ISCC.exe')
  )
  $found = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
  if (-not $found) { throw 'Inno Setup 6 compiler (ISCC.exe) was not found.' }
  return $found
}

if (Test-Path -LiteralPath $OutputDirectory) { Remove-Item -LiteralPath $OutputDirectory -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

dotnet publish $launcherProject -c Release -r win-x64 --self-contained true -o $stage `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Launcher publish failed.' }

$brandingSource = Join-Path $root 'assets\branding'
$brandingTarget = Join-Path $stage 'assets\branding'
New-Item -ItemType Directory -Path $brandingTarget -Force | Out-Null
Get-ChildItem -LiteralPath $brandingSource -File |
  Where-Object { $_.Extension -in '.png', '.ico' } |
  Copy-Item -Destination $brandingTarget -Force

$webBrandingSource = Join-Path $brandingSource 'web'
if (Test-Path -LiteralPath $webBrandingSource) {
  Copy-Item -LiteralPath $webBrandingSource -Destination $brandingTarget -Recurse -Force
}

$launcher = Join-Path $stage 'OtsLauncher.exe'
if (-not (Test-Path -LiteralPath $launcher) -or (Get-Item -LiteralPath $launcher).Length -lt 10MB) {
  throw 'Self-contained launcher executable is missing or unexpectedly small.'
}
Invoke-CodeSign $launcher

$iscc = Get-InnoCompilerPath
$icon = Join-Path $root 'assets\branding\ots-network-launcher-icon.ico'
$version = if ($AppVersion) { $AppVersion } else { [DateTime]::UtcNow.ToString('yyyy.MM.dd.HHmm') }
& $iscc "/DSourceDirectory=$stage" "/DOutputDirectory=$OutputDirectory" "/DSetupIcon=$icon" "/DAppVersion=$version" $installerScript
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $setup)) { throw 'Inno Setup installer build failed.' }
Invoke-CodeSign $setup

$signature = Get-AuthenticodeSignature -LiteralPath $setup
$metadata = [ordered]@{
  fileName = 'Tibernia-Setup.exe'
  sizeBytes = (Get-Item -LiteralPath $setup).Length
  sha256 = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
  builtAt = [DateTime]::UtcNow.ToString('o')
  installerEngine = 'Inno Setup 6'
  defaultInstallDirectory = '%LOCALAPPDATA%\Tibernia'
  allowsCustomInstallDirectory = $true
  authenticode = [ordered]@{
    signed = $signature.Status -ne [System.Management.Automation.SignatureStatus]::NotSigned
    status = [string]$signature.Status
    subject = if ($signature.SignerCertificate) { $signature.SignerCertificate.Subject } else { $null }
    thumbprint = if ($signature.SignerCertificate) { $signature.SignerCertificate.Thumbprint } else { $null }
  }
}
$metadata | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'installer.json') -Encoding utf8
Write-Host "INSTALLER_READY path=$setup bytes=$($metadata.sizeBytes) sha256=$($metadata.sha256) signed=$($metadata.authenticode.signed)"
