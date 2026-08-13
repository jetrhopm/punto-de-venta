# Instalador autocontenido 2.0

El flujo nuevo reemplaza Burn y MSI por un solo ejecutable `Setup.exe` autocontenido. Incluye cliente WPF, API, PostgreSQL portable, Visual C++ Redistributable y el script de servicios.

La consola muestra en tiempo real el archivo que se extrae y copia, el porcentaje, PostgreSQL, la API y cualquier error. Los logs se guardan en `C:\ProgramData\PuntoDeVenta\logs\setup.log`, `instalacion.log` y `postgresql.log`.

Se instala en `C:\Program Files\Punto de Venta` y conserva datos en `C:\ProgramData\PuntoDeVenta`. `/uninstall` elimina servicios, pero conserva la base y los respaldos.

Se genera con `scripts/build-installer.ps1 -Version 2.0.0`. La versión anterior basada en Burn/MSI queda fuera del flujo de publicación.
