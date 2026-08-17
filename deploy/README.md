# Despliegue de VendemeFacil

Producción se publica mediante Azure DevOps y utiliza imágenes privadas de
Docker Hub. Azure Container Registry no forma parte de la arquitectura actual.

## Componentes

- Repositorio de imágenes: `murdoksc/vendemefacil` en Docker Hub.
- API: etiqueta `api-<Build.BuildId>`.
- Frontend: etiqueta `web-<Build.BuildId>`.
- Ejecución: Azure Container Apps en `rg-vendemefacil-prod`.
- Pipeline: `/azure-pipelines.yml`.
- Environment protegido: `production`.

## Publicación normal

Un cambio enviado a `main` inicia el pipeline. El proceso:

1. Compila el API y el frontend.
2. Publica imágenes inmutables con el mismo Build ID.
3. Espera aprobación manual del environment `production`.
4. Despliega primero el API y comprueba su salud y la conexión a la base de
   datos.
5. Despliega el frontend y comprueba `/health`.
6. Si una verificación falla, intenta restaurar las imágenes anteriores.

No es necesario ejecutar comandos de Docker ni Azure CLI para una publicación
normal. Los detalles de conexiones, permisos y configuración inicial están en
[`AZURE_DEVOPS.md`](AZURE_DEVOPS.md).

## Imágenes y recuperación

Para recuperar una compilación anterior, vuelve a desplegar las dos etiquetas
del mismo Build ID:

```text
docker.io/murdoksc/vendemefacil:api-<Build.BuildId>
docker.io/murdoksc/vendemefacil:web-<Build.BuildId>
```

No uses `api-latest` o `web-latest` para producción. Las etiquetas numéricas
permiten identificar exactamente la versión desplegada.

## Secretos

La cadena SQL, la clave JWT y el token de lectura de Docker Hub se almacenan
como secretos de Azure Container Apps. No deben guardarse en Git, Docker Hub ni
variables de texto dentro del pipeline.
