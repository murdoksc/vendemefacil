@echo off
setlocal
title Instalador de impresion - Vendeme Facil

net session >nul 2>&1
if not "%errorlevel%"=="0" (
  echo Windows solicitara permiso de administrador.
  powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

set "INSTALLER_DIR=%~dp0"
set "CERT_PATH=%INSTALLER_DIR%vendemefacil-qz.crt"
set "SCRIPT_PATH=%INSTALLER_DIR%install-vendemefacil-qz-cert.ps1"

if not exist "%CERT_PATH%" (
  echo No se encontro vendemefacil-qz.crt. Extrae todos los archivos del ZIP.
  pause
  exit /b 1
)
if not exist "%SCRIPT_PATH%" (
  echo No se encontro el instalador. Descarga nuevamente el paquete completo.
  pause
  exit /b 1
)
if not exist "%ProgramFiles%\QZ Tray\qz-tray-console.exe" (
  echo QZ Tray todavia no esta instalado.
  echo Se abrira la descarga oficial. Instalalo y ejecuta este archivo nuevamente.
  start "" "https://qz.io/download/"
  pause
  exit /b 2
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_PATH%" -CertificatePath "%CERT_PATH%"
if errorlevel 1 (
  echo No se pudo completar la configuracion. Toma una foto de este mensaje para soporte.
  pause
  exit /b 1
)

echo.
echo La impresion silenciosa de Vendeme Facil quedo configurada.
start "" "%ProgramFiles%\QZ Tray\qz-tray.exe"
pause
