# Instalador de produccion

El instalador usa WiX Toolset 6. La compilacion publica el cliente WPF y la API como aplicaciones autocontenidas `win-x64`.

Para crear el MSI y `Setup.exe` ejecuta `scripts/build-installer.ps1 -Version 1.0.0`. El script usa WiX 6 y descarga la extensión Bal oficial desde NuGet si no existe. Cambiar la version produce una actualización mayor; el MSI permite reparar desde Aplicaciones instaladas.

El Bundle actual no instala PostgreSQL ni registra todavia la API como servicio de Windows. Por eso es un paquete de revision y no debe declararse liberacion de produccion hasta completar esa integracion.

Antes de publicar una version real se debe:

- firmar `Setup.exe` y los binarios con un certificado Authenticode;
- probar instalacion, reparacion, actualizacion y desinstalacion en Windows 10/11 x64 limpios;
- validar respaldo verificable antes de cada migracion;
- conservar `ProgramData\\PuntoDeVenta` al desinstalar por defecto.
- integrar PostgreSQL dedicado, secretos protegidos y el servicio Windows de la API;
- probar la instalacion en Windows 10/11 x64 limpios, sin .NET ni PostgreSQL previos.
