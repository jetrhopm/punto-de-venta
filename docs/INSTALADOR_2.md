# Instalador autocontenido 2.0

El flujo nuevo reemplaza Burn y MSI por un solo ejecutable `Setup.exe` autocontenido. Incluye cliente WPF, API, PostgreSQL portable, Visual C++ Redistributable y el script de servicios.

La ventana del instalador muestra en tiempo real el archivo que se extrae o copia y el porcentaje. Los logs se guardan en `C:\ProgramData\PuntoDeVenta\logs\setup-error.log`, `setup-update.log`, `instalacion.log` y `postgresql.log`.

El orden es: instalar dependencias, crear las carpetas protegidas, copiar la aplicación, crear o conservar el clúster PostgreSQL, registrar servicios, crear o conservar la base técnica `punto_venta` y aplicar migraciones.

El instalador no crea la tienda ni un usuario comercial. Al terminar abre Punto de Venta, que muestra el asistente inicial antes del inicio de sesión. Ese asistente crea la tienda, la caja y el administrador dentro de una transacción, con `admin` y `12345` precargados. Después pregunta si el inventario se importará, se capturará manualmente o se omitirá por el momento. No se importa ni modifica un archivo automáticamente.

Se instala en `C:\Program Files\Punto de Venta` y conserva datos en `C:\ProgramData\PuntoDeVenta`. Durante una actualización se detienen primero los servicios existentes para liberar archivos. `/uninstall` elimina servicios, pero conserva la base y los respaldos.

Se genera con `scripts/build-installer.ps1 -Version 2.0.0`. La versión anterior basada en Burn/MSI queda fuera del flujo de publicación.
