# Instalador autocontenido

Revisión técnica: 13 de agosto de 2026.

El producto se distribuye en un solo `Setup.exe` autocontenido para Windows x64. El paquete incluye el cliente WPF, la API ASP.NET Core, PostgreSQL portable y Microsoft Visual C++ Redistributable. El usuario no instala .NET, PostgreSQL ni otras dependencias manualmente.

## Orden de instalación

1. Solicitar elevación administrativa.
2. Aceptar términos y elegir accesos directos.
3. Extraer el paquete interno en una carpeta temporal.
4. Detectar Microsoft Visual C++ x64 e instalarlo solo cuando falte.
5. Detectar una instalación anterior y detener sus servicios.
6. Copiar o actualizar archivos en `C:\Program Files\Punto de Venta`.
7. Copiar el propio `Setup.exe` para reparación y desinstalación.
8. Crear o conservar el clúster PostgreSQL en `C:\ProgramData\PuntoDeVenta\postgresql\data`.
9. Crear o conservar la base `punto_venta`, el usuario técnico y la conexión protegida con DPAPI.
10. Crear o actualizar los servicios `PuntoDeVentaPostgreSQL` y `PuntoDeVentaApi`.
11. Pasar al servicio API la ruta absoluta de `connection.bin` y el puerto, sin depender de variables creadas durante la misma instalación.
12. Esperar una respuesta correcta de `http://127.0.0.1:5000/health`.
13. Registrar el producto en Aplicaciones instaladas y crear los accesos elegidos.
14. Mostrar `Abrir Punto de Venta` solamente después de completar toda la instalación.

La aplicación, no el instalador, presenta el asistente inicial. El asistente crea transaccionalmente la tienda, caja y administrador. Después permite decidir si el inventario se importará, se capturará manualmente o se omitirá.

## Reparación y actualización

Ejecutar una versión nueva de `Setup.exe` sobre la instalación existente. No es necesario desinstalar. El proceso conserva:

- `C:\ProgramData\PuntoDeVenta`.
- El clúster y la base PostgreSQL.
- `connection.bin` y `postgres-admin.bin`.
- Tienda, usuarios, productos, ventas y migraciones aplicadas.
- Regla de firewall válida y servicios existentes, actualizando su configuración cuando corresponda.

No se debe rotar la contraseña de `pos_app` si existe una conexión protegida válida.

## Registros

- `setup-launch.log`: elevación y arranque del instalador.
- `setup.log`: progreso visible y archivos procesados.
- `setup-error.log`: excepciones de la interfaz del instalador.
- `setup-update.log`: advertencias al detener servicios.
- `instalacion.log`: PostgreSQL, base, firewall, servicios y comprobación de API.
- `postgresql.log`: servidor PostgreSQL.
- `api-startup.log`: inicio de API y errores al aplicar migraciones.

Todos se almacenan en `C:\ProgramData\PuntoDeVenta\logs`.

## Decisiones verificadas

- La API usa `Microsoft.Extensions.Hosting.WindowsServices` y `UseWindowsService`.
- La raíz de contenido del servicio es `AppContext.BaseDirectory`, no `C:\Windows\System32`.
- El servicio recibe rutas absolutas en su `binPath`.
- El instalador actualiza el `binPath` durante reparación.
- Los permisos usan SID universales y funcionan en Windows en español.
- PostgreSQL se registra con `pg_ctl register` y conserva su clúster existente.

Referencias oficiales revisadas:

- https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/windows-service?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/dotnet/core/deploying/
- https://www.postgresql.org/docs/current/app-pg-ctl.html
- https://www.postgresql.org/docs/current/wal-reliability.html

## Pruebas de liberación pendientes

Antes de declarar el instalador listo para producción deben registrarse resultados en máquinas virtuales limpias:

- Windows 10 x64 y Windows 11 x64.
- Instalación limpia sin .NET ni PostgreSQL global.
- Reparación sobre la misma versión.
- Actualización desde una versión anterior.
- Reinicio y arranque automático de ambos servicios.
- Asistente inicial, login y creación de tienda.
- Desinstalación conservando `ProgramData` y reinstalación reconociendo la tienda.
- Puerto 5000 ocupado, disco insuficiente y servicio detenido abruptamente.
