# Impresion silenciosa con QZ Tray

VendeMeFacil utiliza una raiz propia para firmar solicitudes de QZ Tray sin exponer la llave privada al navegador.

## Secretos de produccion

- `Qz__CertificateBase64`: certificado publico PEM completo codificado en Base64.
- `Qz__PrivateKeyBase64`: llave privada PKCS#8 DER codificada en Base64.

La llave privada no debe copiarse al repositorio, al frontend ni a las computadoras de los clientes.

## Instalacion en Windows

### Instalacion sencilla para clientes

1. Descargar `impresion-silenciosa-vendemefacil.zip` desde `/impresion`.
2. Elegir **Extraer todo**.
3. Hacer doble clic en `Instalar impresion Vendeme Facil.bat`.
4. Aceptar el permiso de administrador de Windows.

El BAT eleva y ejecuta el mismo script documentado abajo. No contiene la llave
privada y el cliente no necesita abrir PowerShell ni escribir comandos.

### Instalacion manual

1. Instalar QZ Tray 2.2 o posterior desde https://qz.io/download/.
2. Descargar el certificado publico `vendemefacil-qz.crt` proporcionado por VendeMeFacil.
3. Abrir PowerShell como administrador.
4. Ejecutar:

   ```powershell
   Set-ExecutionPolicy -Scope Process Bypass
   .\install-vendemefacil-qz-cert.ps1 -CertificatePath .\vendemefacil-qz.crt
   ```

5. Abrir `https://vendemefacil.com`, iniciar sesion y entrar a Configuracion > Impresion.
6. Seleccionar QZ Tray, detectar la impresora y ejecutar una impresion de prueba.

El instalador copia solamente el certificado publico a QZ Tray y lo agrega a la lista permitida. Nunca distribuye la llave privada.

## Rotacion

Antes del vencimiento se debe generar un nuevo par, desplegar ambos certificados durante una ventana de transicion, actualizar los secretos de la API y reinstalar el certificado publico en cada terminal.
