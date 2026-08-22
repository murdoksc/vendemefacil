# Véndeme Fácil

Punto de venta e inventario web, responsivo y multiempresa para comercios minoristas en México.

## Capacidades

- Punto de venta, caja, cancelaciones y devoluciones parciales.
- Catálogo, variantes, inventario, conteo físico, kardex e importación.
- Clientes, monedero, apartados y recordatorios.
- Reportes, auditoría, usuarios y permisos por rol.
- Onboarding, suscripciones y administración de plataforma.
- Catálogo público, documentos por correo e impresión con QZ Tray.

## Tecnología y estructura

- `apps/web`: React, TypeScript, Vite y Vitest.
- `apps/api`: ASP.NET Core Minimal APIs, EF Core y SQL Server.
- `apps/api/Features`: módulos verticales del backend por capacidad de negocio.
- `tests/api`: pruebas automatizadas del backend.
- `deploy`: operación, Azure Container Apps e impresión.

El backend es un monolito modular. Comparte proceso y base de datos, pero cada feature registra sus rutas y concentra sus reglas. Consulta [la arquitectura](docs/ARCHITECTURE.md) para conocer el patrón y el plan de migración.

## Desarrollo local

Requisitos: .NET 10, Node.js 24 y SQL Server.

```powershell
npm ci --prefix apps/web
npm run dev --prefix apps/web
dotnet run --project apps/api --urls http://127.0.0.1:5080
```

El frontend abre en `http://localhost:5173` y envía `/api` al backend local.

## Configuración

Copia los archivos `.env.example` como referencia. Los valores sensibles se proporcionan mediante variables de entorno o secretos del despliegue:

```text
ConnectionStrings__VendemeFacilDb
Jwt__Key
Email__ConnectionString
PlatformAdmin__Email
PlatformAdmin__PasswordSha256
Qz__CertificateBase64
Qz__PrivateKeyBase64
```

No guardes credenciales reales en Git. En desarrollo, `X-Tenant-Id` solamente se acepta en el ambiente Development.

## Base de datos

Las relaciones relevantes incluyen `TenantId` y el contexto aplica filtros globales para aislar negocios. Para actualizar el esquema:

```powershell
dotnet ef database update --project apps/api
```

El API también ejecuta migraciones pendientes al iniciar. En producción, revisa las migraciones antes del despliegue y conserva un respaldo verificable.

## Calidad

```powershell
dotnet restore VendemeFacil.slnx --configfile NuGet.Config
dotnet build VendemeFacil.slnx --configuration Release --no-restore
dotnet test tests/api/VendemeFacil.Api.Tests.csproj --configuration Release --no-build
npm ci --prefix apps/web
npm run lint --prefix apps/web
npm test --prefix apps/web
npm run build --prefix apps/web
```

El pipeline ejecuta compilación, lint y pruebas antes de publicar imágenes. El despliegue valida health checks y revierte a las imágenes anteriores si falla.

## Despliegue

Consulta [Azure DevOps](deploy/AZURE_DEVOPS.md) y la guía de [impresión silenciosa](deploy/qz/README.md).
