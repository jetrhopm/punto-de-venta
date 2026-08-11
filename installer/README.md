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

Desde la version 1.0.3, Burn encadena el bootstrap autocontenido despues del MSI. La instalacion normal ya no requiere ejecutar PowerShell ni instalar PostgreSQL o Visual C++ manualmente. La validacion en una VM limpia sigue siendo obligatoria.

El instalador incluye licencia RTF, ruta seleccionable, accesos directos en Escritorio e Inicio, icono del producto, PostgreSQL, Visual C++ Redistributable y el bootstrap automatico de servicios. La interfaz estandar de WiX conserva algunos textos del sistema mientras se incorpora una localizacion completa en espanol.

## Registro visible de instalacion

Durante la etapa de configuracion de PostgreSQL y servicios se abre una consola en espanol con las etapas, comandos, carpetas y archivos que se estan creando. Esa consola no debe cerrarse hasta que indique que la instalacion termino.

Durante la copia de los archivos del MSI se muestra tambien la interfaz interna de Windows Installer. Esa vista permite observar la accion de MSI y el archivo que esta procesando, en lugar del texto general `Archivos de Punto de Venta` de Burn.

Los registros quedan en:

- `C:\ProgramData\PuntoDeVenta\logs\instalacion.log`: etapas del bootstrap, comandos y archivos creados.
- `C:\ProgramData\PuntoDeVenta\logs\instalador-bootstrap.log`: inicio, finalizacion y codigo de salida del ejecutable auxiliar.
- `C:\ProgramData\PuntoDeVenta\logs\postgresql.log`: salida del servidor PostgreSQL.

Si el progreso visual de Setup parece detenerse, espera a que termine la etapa indicada en la consola. Si no cambia durante varios minutos, cierra la instalacion desde el boton Cancelar y revisa `instalacion.log`; el ultimo registro identifica el comando o archivo que estaba en proceso. No repitas la instalacion a ciegas si ya existe `C:\ProgramData\PuntoDeVenta\postgresql\data`, porque el script conserva el cluster existente de forma idempotente.

Antes de publicar una version real se debe:

- firmar `Setup.exe` y los binarios con un certificado Authenticode;
- probar instalacion, reparacion, actualizacion y desinstalacion en Windows 10/11 x64 limpios;
- validar respaldo verificable antes de cada migracion;
- conservar `ProgramData\\PuntoDeVenta` al desinstalar por defecto.
- integrar PostgreSQL dedicado, secretos protegidos y el servicio Windows de la API;
- probar la instalacion en Windows 10/11 x64 limpios, sin .NET ni PostgreSQL previos.
