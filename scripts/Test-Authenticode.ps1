param(
  [Parameter(Mandatory = $true)][string]$Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Path)) { throw "File not found: $Path" }
$signature = Get-AuthenticodeSignature -LiteralPath $Path
if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
  throw "Authenticode signature is not valid: $($signature.Status)"
}
if (-not $signature.SignerCertificate) { throw 'Signer certificate is missing.' }
Write-Host "[PASS] Authenticode subject=$($signature.SignerCertificate.Subject) thumbprint=$($signature.SignerCertificate.Thumbprint)"
