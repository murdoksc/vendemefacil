# Primer despliegue en Azure Container Apps

Este despliegue usa dos Container Apps y construye las imágenes remotamente en Azure Container Registry. Docker Desktop no es necesario.

## 1. Preparar Azure CLI

```powershell
az login
az account list --output table
az account set --subscription "ID O NOMBRE DE LA SUSCRIPCIÓN"
```

Comprueba qué suscripción está activa:

```powershell
az account show --output table
```

## 2. Crear una clave JWT

Genera una clave aleatoria y guárdala temporalmente en un administrador de contraseñas:

```powershell
$bytes = New-Object byte[] 64
[System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
[Convert]::ToBase64String($bytes)
```

No guardes esa clave en Git ni en un archivo del proyecto.

## 3. Ejecutar el despliegue

Desde la raíz del repositorio:

```powershell
cd C:\repos\vendemefacil
.\deploy\azure-deploy.ps1
```

El script solicitará de forma oculta:

- La cadena completa de SQL Server.
- La clave JWT.

Al terminar mostrará la URL pública de la web y la URL de salud del API.

## 4. Probar

1. Abre la URL `ca-vendemefacil-web...azurecontainerapps.io`.
2. Inicia sesión con el negocio existente.
3. Consulta productos y reportes.
4. Registra una venta de prueba y cancélala.
5. Revisa que el inventario se restaure.

## Consideraciones

- Ambas aplicaciones escalan de 0 a 2 réplicas. La primera solicitud tras un periodo sin uso puede tardar algunos segundos.
- SQL Server continúa en GoDaddy. Debe aceptar conexiones desde Azure.
- Los secretos se almacenan como secretos de Container Apps.
- Para una etapa posterior conviene reemplazar las credenciales administrativas de ACR por identidad administrada.
- Configura `vendemefacil.com` solamente después de validar la URL generada por Azure.
