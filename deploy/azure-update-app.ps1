[CmdletBinding()]
param(
    [string]$ResourceGroup = "rg-vendemefacil-prod",
    [string]$ApiApp = "ca-vendemefacil-api",
    [string]$WebApp = "ca-vendemefacil-web",
    [string]$Registry = ""
)
$ErrorActionPreference = "Stop"
function Assert-Exit([string]$message) { if ($LASTEXITCODE -ne 0) { throw $message } }
az account show --output none; Assert-Exit "Inicia sesion con az login."
if ([string]::IsNullOrWhiteSpace($Registry)) { $Registry = az acr list --resource-group $ResourceGroup --query "[0].name" --output tsv }
if ([string]::IsNullOrWhiteSpace($Registry)) { throw "No se encontro el registro de contenedores." }
$server = az acr show --name $Registry --query loginServer --output tsv
$tag = Get-Date -Format "yyyyMMddHHmmss"
Write-Host "Construyendo API y frontend..." -ForegroundColor Cyan
az acr build --registry $Registry --image "vendemefacil-api:$tag" --file apps/api/Dockerfile .; Assert-Exit "Fallo la compilacion del API."
az acr build --registry $Registry --image "vendemefacil-web:$tag" --file apps/web/Dockerfile .; Assert-Exit "Fallo la compilacion del frontend."
Write-Host "Publicando nuevas revisiones..." -ForegroundColor Cyan
az containerapp update --name $ApiApp --resource-group $ResourceGroup --image "$server/vendemefacil-api:$tag" --output none; Assert-Exit "Fallo la actualizacion del API."
az containerapp update --name $WebApp --resource-group $ResourceGroup --image "$server/vendemefacil-web:$tag" --output none; Assert-Exit "Fallo la actualizacion del frontend."
$fqdn = az containerapp show --name $WebApp --resource-group $ResourceGroup --query properties.configuration.ingress.fqdn --output tsv
Write-Host "Actualizacion terminada: https://$fqdn" -ForegroundColor Green
