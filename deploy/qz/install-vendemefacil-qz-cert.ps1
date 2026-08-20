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
$userQzDirectory = Join-Path $env:APPDATA "qz"
$userAllowed = Join-Path $userQzDirectory "allowed.dat"
$systemQzDirectory = Join-Path $env:ProgramData "qz"
$systemAllowed = Join-Path $systemQzDirectory "allowed.dat"

if (-not (Test-Path -LiteralPath $qzConsole)) {
    throw "QZ Tray no esta instalado en $qzDirectory. Instala QZ Tray 2.2 y vuelve a ejecutar este script."
}

$resolvedCertificate = (Resolve-Path -LiteralPath $CertificatePath).Path
Get-Process -Name "qz-tray" -ErrorAction SilentlyContinue | Stop-Process -Force
Get-CimInstance Win32_Process |
    Where-Object { $_.Name -in @("java.exe", "javaw.exe") -and $_.CommandLine -like "*qz-tray.jar*" } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Milliseconds 500
Copy-Item -LiteralPath $resolvedCertificate -Destination $overrideCertificate -Force

if ((Get-FileHash -Algorithm SHA256 -LiteralPath $resolvedCertificate).Hash -ne
    (Get-FileHash -Algorithm SHA256 -LiteralPath $overrideCertificate).Hash) {
    throw "El certificado no se pudo copiar correctamente a QZ Tray."
}

& $qzConsole --whitelist $resolvedCertificate

if ($LASTEXITCODE -ne 0) {
    throw "QZ Tray no pudo autorizar el certificado de VendeMeFacil."
}

if (-not (Test-Path -LiteralPath $userAllowed) -or
    -not (Select-String -LiteralPath $userAllowed -SimpleMatch "VendeMeFacil QZ Signing" -Quiet)) {
    throw "QZ Tray no agrego VendeMeFacil a la lista de sitios permitidos."
}

New-Item -ItemType Directory -Path $systemQzDirectory -Force | Out-Null
Copy-Item -LiteralPath $userAllowed -Destination $systemAllowed -Force
Write-Host "Certificado de VendeMeFacil instalado y autorizado correctamente."
