# Arquitectura

## Decisión

Véndeme Fácil utiliza un **monolito modular con vertical slices**. Mantiene una sola aplicación y una sola transacción de base de datos, pero evita que todas las rutas y reglas vivan en `Program.cs`.

No se adoptan microservicios en esta etapa: ventas, caja e inventario requieren consistencia transaccional y el costo operativo de servicios separados no aporta una ventaja proporcional todavía.

## Dependencias

```text
HTTP endpoint -> regla de la feature -> VendemeFacilDbContext -> SQL Server
                         |                       |
                    contratos              filtro TenantId
```

`Program.cs` es el composition root: configura infraestructura, middleware y grupos de rutas. Los módulos dentro de `Features` registran endpoints con métodos de extensión.

## Convenciones

Cada capacidad nueva debe vivir en una carpeta propia:

```text
Features/
  Customers/
    CustomerEndpoints.cs
    CustomerContracts.cs
    CustomerService.cs
```

- El endpoint traduce HTTP a la operación y devuelve `IResult`.
- La validación pequeña puede residir junto al endpoint; reglas reutilizables van en un servicio de la feature.
- Las consultas usan proyecciones y `AsNoTracking` cuando no modifican entidades.
- Toda lectura o escritura de negocio debe respetar el tenant autenticado.
- No se crean repositorios genéricos sobre EF Core; el contexto ya cumple esa función.
- Los contratos públicos no exponen directamente entidades de EF.

## Migración incremental

`Features/Business`, `Features/Sales`, `Features/Layaways`, `Features/Inventory`, `Features/Reporting`, `Features/Identity` y `Features/Account` aplican el patrón modular.

Los únicos endpoints que permanecen activos directamente en `Program.cs` son infraestructura, health checks y catálogo público. Pueden extraerse posteriormente, aunque no contienen el núcleo transaccional del sistema.

Los bloques heredados fueron eliminados después de validar los módulos. `Program.cs` conserva únicamente la composición de infraestructura y el registro de rutas de nivel superior.

Cada extracción debe incluir pruebas y pasar las comprobaciones del README. Así la reestructuración no requiere una reescritura de alto riesgo.

## Estrategia de pruebas

- **Unitarias:** reglas puras, planes, fechas, validadores y cálculos.
- **Integración API:** autenticación, autorización, aislamiento de tenants y el ciclo comercial completo por HTTP.
- **Frontend:** cliente HTTP, sesión, transformaciones y componentes críticos.
- **End-to-end:** apertura de caja, venta, devolución, cierre y actualización de existencias.

La suite HTTP cubre registro, producto con existencia inicial, apertura de caja, venta, devolución parcial, apartado, cancelación del apartado y cierre de caja. La siguiente ampliación prioritaria es ejecutar estos escenarios también contra SQL Server efímero; EF InMemory no sustituye la validación de transacciones y restricciones específicas de SQL Server.
