[CmdletBinding()]
param(
    [string]$ResourceGroup = "rg-vendemefacil-prod",
    [string]$WebApp = "ca-vendemefacil-web",
    [string]$Registry = ""
)

$ErrorActionPreference = "Stop"

function Assert-LastExitCode([string]$Message) {
    if ($LASTEXITCODE -ne 0) { throw $Message }
}

az account show --output none
Assert-LastExitCode "Inicia sesion primero con: az login"

if ([string]::IsNullOrWhiteSpace($Registry)) {
    $Registry = az acr list --resource-group $ResourceGroup --query "[0].name" --output tsv
    Assert-LastExitCode "No se pudo consultar Azure Container Registry."
}

if ([string]::IsNullOrWhiteSpace($Registry)) {
    throw "No se encontro un Azure Container Registry en $ResourceGroup."
}

$loginServer = az acr show --name $Registry --query loginServer --output tsv
Assert-LastExitCode "No se pudo consultar el servidor del registro."

$imageTag = Get-Date -Format "yyyyMMddHHmmss"
$imageName = "vendemefacil-web:$imageTag"

Write-Host "Construyendo $imageName en Azure..." -ForegroundColor Cyan
az acr build --registry $Registry --image $imageName --file apps/web/Dockerfile .
Assert-LastExitCode "Fallo la compilacion del frontend."

Write-Host "Actualizando $WebApp..." -ForegroundColor Cyan
az containerapp update `
    --name $WebApp `
    --resource-group $ResourceGroup `
    --image "$loginServer/$imageName" `
    --output none
Assert-LastExitCode "Fallo la actualizacion del frontend."

$webFqdn = az containerapp show --name $WebApp --resource-group $ResourceGroup --query properties.configuration.ingress.fqdn --output tsv
Write-Host "Frontend actualizado: https://$webFqdn" -ForegroundColor Green
