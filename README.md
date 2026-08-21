# Véndeme Fácil

Punto de venta e inventario web, responsivo y multiempresa para boutiques, zapaterías, tiendas de regalos, accesorios, papelerías y otros comercios minoristas en México.

## Estructura

- `apps/web`: React, TypeScript y Vite.
- `apps/api`: ASP.NET Core Web API.

## Desarrollo local

Requisitos: .NET 10 y Node.js 24 o posteriores.

```powershell
npm install --prefix apps/web
npm run dev --prefix apps/web
dotnet run --project apps/api --urls http://127.0.0.1:5080
```

El frontend se abre en `http://localhost:5173` y redirige las peticiones `/api` al backend local.

## Base de datos

El modelo inicial incluye negocios, sucursales, usuarios, categorías, productos, variantes, existencias y movimientos de inventario. Las relaciones usan llaves compuestas con `TenantId` para impedir referencias entre negocios.

Cuando tengas la cadena de conexión configurada, crea o actualiza la base con:

```powershell
dotnet ef database update --project apps/api
```

También puedes ejecutar el script idempotente `database/initial.sql` desde SQL Server Management Studio. Los endpoints bajo `/api/v1` requieren temporalmente el encabezado `X-Tenant-Id`; al implementar autenticación, el backend lo obtendrá de la identidad validada.

## Configuración segura

La cadena de SQL Server se proporciona mediante la variable de entorno:

```text
ConnectionStrings__VendemeFacilDb
```

Nunca guardes credenciales reales en `appsettings.json`, archivos `.env` o Git.

## Estado inicial

La entrega actual contiene el shell visual responsivo, el modelo multiempresa en SQL Server, migraciones, catálogo, entrada rápida, costo promedio y endpoints del dashboard. Autenticación, pruebas de integración y los flujos completos de las pantallas se implementarán en las siguientes iteraciones.
