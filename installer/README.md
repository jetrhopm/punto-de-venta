# Instalador de produccion

El instalador usa WiX Toolset v7. La compilacion requiere ejecutar `scripts/package-production.ps1` desde una terminal con permisos administrativos cuando se instale PostgreSQL como servicio.

Antes de publicar una version real se debe:

- firmar `Setup.exe` y los binarios con un certificado Authenticode;
- probar instalacion, reparacion, actualizacion y desinstalacion en Windows 10/11 x64 limpios;
- validar respaldo verificable antes de cada migracion;
- conservar `ProgramData\\PuntoDeVenta` al desinstalar por defecto.
