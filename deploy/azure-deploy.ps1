[CmdletBinding()]
param(
    [string]$Location = "southcentralus",
    [string]$ResourceGroup = "rg-vendemefacil-prod",
    [string]$Environment = "cae-vendemefacil-prod",
    [string]$ApiApp = "ca-vendemefacil-api",
    [string]$WebApp = "ca-vendemefacil-web",
    [string]$Registry = ""
)

$ErrorActionPreference = "Stop"

function Assert-LastExitCode([string]$Message) {
    if ($LASTEXITCODE -ne 0) { throw $Message }
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw "Azure CLI no está instalado. Instálalo desde https://aka.ms/installazurecliwindows"
}

if ([string]::IsNullOrWhiteSpace($Registry)) {
    $suffix = Get-Random -Minimum 10000 -Maximum 99999
    $Registry = "acrvendemefacil$suffix"
}

az account show --output none
Assert-LastExitCode "Inicia sesión primero con: az login"

Write-Host "Preparando proveedores de Azure..." -ForegroundColor Cyan
az extension add --name containerapp --upgrade --only-show-errors
az provider register --namespace Microsoft.App --wait
az provider register --namespace Microsoft.OperationalInsights --wait
az provider register --namespace Microsoft.ContainerRegistry --wait

Write-Host "Creando recursos base..." -ForegroundColor Cyan
az group create --name $ResourceGroup --location $Location --output none
az acr create --name $Registry --resource-group $ResourceGroup --location $Location --sku Basic --admin-enabled true --output none
az containerapp env create --name $Environment --resource-group $ResourceGroup --location $Location --output none

Write-Host "Construyendo imágenes dentro de Azure..." -ForegroundColor Cyan
az acr build --registry $Registry --image vendemefacil-api:latest --file apps/api/Dockerfile .
Assert-LastExitCode "Falló la compilación del API."
az acr build --registry $Registry --image vendemefacil-web:latest --file apps/web/Dockerfile .
Assert-LastExitCode "Falló la compilación del frontend."

$loginServer = az acr show --name $Registry --query loginServer --output tsv
$registryUser = az acr credential show --name $Registry --query username --output tsv
$registryPassword = az acr credential show --name $Registry --query 'passwords[0].value' --output tsv

Write-Host "Introduce los secretos. No se guardarán en este repositorio." -ForegroundColor Yellow
$dbSecure = Read-Host "Cadena completa de SQL Server" -AsSecureString
$jwtSecure = Read-Host "Clave JWT aleatoria de 64 caracteres o más" -AsSecureString
$dbConnection = [System.Net.NetworkCredential]::new('', $dbSecure).Password
$jwtKey = [System.Net.NetworkCredential]::new('', $jwtSecure).Password
if ($jwtKey.Length -lt 32) { throw "La clave JWT debe tener al menos 32 caracteres." }

Write-Host "Desplegando API..." -ForegroundColor Cyan
az containerapp create `
    --name $ApiApp `
    --resource-group $ResourceGroup `
    --environment $Environment `
    --image "$loginServer/vendemefacil-api:latest" `
    --registry-server $loginServer `
    --registry-username $registryUser `
    --registry-password $registryPassword `
    --ingress external `
    --target-port 8080 `
    --min-replicas 0 `
    --max-replicas 2 `
    --cpu 0.5 `
    --memory 1Gi `
    --secrets "sql-connection=$dbConnection" "jwt-key=$jwtKey" `
    --env-vars "ASPNETCORE_ENVIRONMENT=Production" "ConnectionStrings__VendemeFacilDb=secretref:sql-connection" "Jwt__Key=secretref:jwt-key" `
    --output none
Assert-LastExitCode "Falló el despliegue del API."

$apiFqdn = az containerapp show --name $ApiApp --resource-group $ResourceGroup --query properties.configuration.ingress.fqdn --output tsv

Write-Host "Desplegando frontend..." -ForegroundColor Cyan
az containerapp create `
    --name $WebApp `
    --resource-group $ResourceGroup `
    --environment $Environment `
    --image "$loginServer/vendemefacil-web:latest" `
    --registry-server $loginServer `
    --registry-username $registryUser `
    --registry-password $registryPassword `
    --ingress external `
    --target-port 80 `
    --min-replicas 0 `
    --max-replicas 2 `
    --cpu 0.25 `
    --memory 0.5Gi `
    --env-vars "API_BASE_URL=https://$apiFqdn" `
    --output none
Assert-LastExitCode "Falló el despliegue del frontend."

$webFqdn = az containerapp show --name $WebApp --resource-group $ResourceGroup --query properties.configuration.ingress.fqdn --output tsv

$dbConnection = $null
$jwtKey = $null
$registryPassword = $null

Write-Host "" 
Write-Host "Despliegue terminado" -ForegroundColor Green
Write-Host "Web: https://$webFqdn"
Write-Host "API: https://$apiFqdn/api/health"
Write-Host "ACR: $Registry"
