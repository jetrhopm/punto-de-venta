# Instalador de produccion

El instalador usa WiX Toolset v7. La compilacion requiere ejecutar `scripts/package-production.ps1` desde una terminal con permisos administrativos cuando se instale PostgreSQL como servicio.

Para crear el MSI y `Setup.exe` ejecuta `scripts/build-installer.ps1 -Version 1.0.0`. Cambiar la version produce una actualización mayor; el MSI permite reparar desde Aplicaciones instaladas. WiX debe mostrar `WixToolset.Bal.wixext 7.0.0` sin la marca `damaged`; si aparece dañada, hay que reparar la extensión antes de crear el Bundle.

Antes de publicar una version real se debe:

- firmar `Setup.exe` y los binarios con un certificado Authenticode;
- probar instalacion, reparacion, actualizacion y desinstalacion en Windows 10/11 x64 limpios;
- validar respaldo verificable antes de cada migracion;
- conservar `ProgramData\\PuntoDeVenta` al desinstalar por defecto.
