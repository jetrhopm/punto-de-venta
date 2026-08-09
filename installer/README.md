# Instalador de produccion

El instalador usa WiX Toolset 6. La compilacion publica el cliente WPF y la API como aplicaciones autocontenidas `win-x64`.

Para crear el MSI y `Setup.exe` ejecuta `scripts/build-installer.ps1 -Version 1.0.0`. El script usa WiX 6 y descarga la extensión Bal oficial desde NuGet si no existe. Cambiar la version produce una actualización mayor; el MSI permite reparar desde Aplicaciones instaladas.

El paquete incluye los binarios oficiales portables de PostgreSQL y `install-production.ps1`. Ese bootstrap crea el clúster dedicado con checksums, registra PostgreSQL y la API como servicios de Windows, genera las credenciales y protege la cadena de conexión con DPAPI.

Después de instalar el MSI, ejecuta una vez desde PowerShell como administrador:

```powershell
& 'C:\Program Files\Punto de Venta\install-production.ps1'
```

El script es idempotente. Para quitar solo los servicios sin borrar datos:

```powershell
& 'C:\Program Files\Punto de Venta\install-production.ps1' -Uninstall
```

El Bundle actual todavía no ejecuta automáticamente ese bootstrap durante la cadena Burn; la ejecución automática queda sujeta a la prueba en Windows limpio para no arriesgar datos durante una actualización.

Antes de publicar una version real se debe:

- firmar `Setup.exe` y los binarios con un certificado Authenticode;
- probar instalacion, reparacion, actualizacion y desinstalacion en Windows 10/11 x64 limpios;
- validar respaldo verificable antes de cada migracion;
- conservar `ProgramData\\PuntoDeVenta` al desinstalar por defecto.
- integrar PostgreSQL dedicado, secretos protegidos y el servicio Windows de la API;
- probar la instalacion en Windows 10/11 x64 limpios, sin .NET ni PostgreSQL previos.
