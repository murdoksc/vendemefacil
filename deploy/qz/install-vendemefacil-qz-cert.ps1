#Requires -RunAsAdministrator
param(
    [Parameter(Mandatory = $true)]
    [string]$CertificatePath
)

$ErrorActionPreference = "Stop"
$qzDirectory = Join-Path $env:ProgramFiles "QZ Tray"
$qzConsole = Join-Path $qzDirectory "qz-tray-console.exe"
$qzApplication = Join-Path $qzDirectory "qz-tray.exe"
$overrideCertificate = Join-Path $qzDirectory "override.crt"

if (-not (Test-Path -LiteralPath $qzConsole)) {
    throw "QZ Tray no esta instalado en $qzDirectory. Instala QZ Tray 2.2 y vuelve a ejecutar este script."
}

$resolvedCertificate = (Resolve-Path -LiteralPath $CertificatePath).Path
Get-Process -Name "qz-tray" -ErrorAction SilentlyContinue | Stop-Process -Force
Copy-Item -LiteralPath $resolvedCertificate -Destination $overrideCertificate -Force
& $qzConsole --whitelist $resolvedCertificate

if ($LASTEXITCODE -ne 0) {
    throw "QZ Tray no pudo autorizar el certificado de VendeMeFacil."
}

Start-Process -FilePath $qzApplication -WindowStyle Hidden
Write-Host "Certificado de VendeMeFacil instalado y autorizado correctamente."
