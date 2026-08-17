# Publicación en Docker Hub con Azure DevOps

El pipeline `azure-pipelines.yml` valida el API y el frontend y, cuando el cambio
llega a `main`, publica ambas imágenes en un único repositorio privado de Docker
Hub:

- `murdoksc/vendemefacil:api-<Build.BuildId>`
- `murdoksc/vendemefacil:web-<Build.BuildId>`

También actualiza las etiquetas `api-latest` y `web-latest`. Las etiquetas con el
número de compilación son inmutables y se usarán posteriormente para desplegar y
regresar a una versión conocida.

La etapa de despliegue usa el environment `production`, actualiza las dos Azure
Container Apps con las etiquetas del mismo Build ID y comprueba sus endpoints de
salud. Si una comprobación falla, intenta restaurar las imágenes anteriores.

## 1. Preparar Docker Hub

1. Inicia sesión en Docker Hub con la cuenta `murdoksc`.
2. Crea un solo repositorio llamado `vendemefacil` y selecciona visibilidad
   **Private**.
3. En **Account settings > Personal access tokens**, crea un token con permiso
   de lectura y escritura. No guardes el token en este repositorio.

## 2. Crear el proyecto y el pipeline

1. Crea o abre el proyecto de VendemeFacil en Azure DevOps.
2. En **Pipelines > New pipeline**, selecciona **GitHub**.
3. Autoriza la aplicación de Azure Pipelines y selecciona el repositorio
   `murdoksc/vendemefacil`.
4. Selecciona **Existing Azure Pipelines YAML file** y el archivo
   `/azure-pipelines.yml` de la rama `main`.

## 3. Conectar Docker Hub

1. Abre **Project settings > Service connections**.
2. Crea una conexión de tipo **Docker Registry**.
3. Selecciona **Docker Hub**.
4. Usa el Docker ID `murdoksc` y el token creado anteriormente.
5. Asigna exactamente este nombre a la conexión:
   `dockerhub-vendemefacil`.
6. Autoriza la conexión para el pipeline de VendemeFacil.

No es necesario seleccionar **Grant access permission to all pipelines**; se
recomienda autorizar solamente este pipeline.

## 4. Primera ejecución

Ejecuta el pipeline manualmente sobre `main`. Deben completarse las etapas:

1. **Validar aplicaciones**.
2. **Publicar imágenes en Docker Hub**.

Al terminar, comprueba en el repositorio privado de Docker Hub que existan las
etiquetas `api-latest`, `web-latest`, `api-<Build.BuildId>` y
`web-<Build.BuildId>`.

## 5. Conectar y proteger producción

1. Crea una conexión de Azure Resource Manager con federación de identidad,
   limitada al grupo `rg-vendemefacil-prod`.
2. Asigna exactamente el nombre `azure-vendemefacil-production`.
3. Crea el environment `production` en Azure DevOps.
4. Agrega una aprobación manual en **Approvals and checks**.
5. Autoriza este pipeline para utilizar ambas conexiones de servicio.

Cuando un cambio llega a `main`, la publicación termina primero. El deployment
queda esperando la aprobación del environment antes de modificar las Container
Apps. No elimines Azure Container Registry hasta probar el despliegue y su ruta
de recuperación durante varios días.
