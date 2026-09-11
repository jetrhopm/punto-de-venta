# Instalador autocontenido

Revisión técnica: 13 de agosto de 2026.

El producto se distribuye en un solo `Setup.exe` autocontenido para Windows x64. El mismo ejecutable instala una **caja principal / servidor** o una **caja adicional**, según la modalidad elegida en una instalación nueva.

## Modalidades

### Caja principal / servidor

Instala JetVenta, API ASP.NET Core, PostgreSQL portable, servicios de Windows,
regla de Firewall privada para TCP 5000 y Microsoft Visual C++ Redistributable.
Es la única computadora que conserva la base de datos y atiende las cajas de la
red local.

### Caja adicional

Instala JetVenta y las dependencias de Windows necesarias. No instala ni copia
PostgreSQL, API, scripts de servidor, servicios de Windows o regla de Firewall.
Durante la instalación se solicita la dirección y puerto de la caja principal,
un código temporal de seis dígitos generado por un administrador y el nombre de
la caja. El instalador comprueba `/health`, compatibilidad de protocolo LAN y
empareja el equipo antes de habilitar **Abrir JetVenta**.

La modalidad se guarda en
`C:\ProgramData\PuntoDeVenta\config\installation-mode.json`. Las
actualizaciones la conservan automáticamente. Las instalaciones anteriores a
3.3.6 sin ese archivo se consideran caja principal para preservar su
comportamiento.

## Orden de instalación

1. Solicitar elevación administrativa y aceptar términos.
2. En una instalación nueva, elegir modalidad y accesos directos.
3. Instalar Visual C++ x64 solo cuando falte.
4. Copiar o actualizar los archivos permitidos por la modalidad en `C:\Program Files\JetVenta`.
5. En caja principal, crear o conservar clúster, base, API, servicios, Firewall y comprobar `http://127.0.0.1:5000/health`.
6. En caja adicional, comprobar la caja principal y ejecutar el emparejamiento LAN antes de guardar la identidad DPAPI del equipo.
7. Registrar el producto, tipo de archivo `.jv` y accesos elegidos.
8. Mostrar `Abrir JetVenta` solamente después de completar la modalidad seleccionada.

La aplicación, no el instalador, presenta el asistente inicial. El asistente crea transaccionalmente la tienda, caja y administrador. Después permite decidir si el inventario se importará, se capturará manualmente o se omitirá.

## Reparación y actualización

Ejecutar una versión nueva de `Setup.exe` sobre la instalación existente. No es necesario desinstalar. El proceso conserva:

- `C:\ProgramData\PuntoDeVenta`.
- El clúster y la base PostgreSQL.
- `connection.bin` y `postgres-admin.bin`.
- Tienda, usuarios, productos, ventas y migraciones aplicadas.
- Regla de firewall válida y servicios existentes, actualizando su configuración cuando corresponda.
- Modalidad de instalación y, en caja adicional, identidad emparejada por computadora.

Una actualización de caja adicional no instala ni reinicia servicios del
servidor. Si un primer emparejamiento no llegó a completarse, el instalador
conserva la modalidad pendiente y solicita un código temporal nuevo al volver a
ejecutarse.

Si existe una conexión protegida válida, la reparación recupera su contraseña y la vuelve a aplicar al rol `pos_app`. Esto corrige instalaciones incompletas sin rotar la credencial, recrear la base ni perder datos.

El servicio `PuntoDeVentaApi` se actualiza mediante la API de administración de servicios de Windows. Esto conserva el servicio y evita errores de interpretación de comillas en rutas con espacios.

Si la API no responde por una migración pendiente o permisos heredados al restaurar una base, la ventana inicial de JetVenta ofrece **Reparar servicios**. Esa acción solicita permisos de Windows y ejecuta el reparador incluido; detiene y vuelve a registrar los servicios, corrige los permisos del esquema `pos` y conserva la base, ventas, usuarios y respaldos.

La API depende del servicio `PuntoDeVentaPostgreSQL`, reintenta la conexión mientras PostgreSQL termina de arrancar y tiene recuperación automática configurada en Windows. Después de reiniciar el equipo, el cliente también espera al servidor local antes de habilitar el inicio de sesión.

La pantalla de inicio de sesión mantiene el diagnóstico visible y diferencia credenciales incorrectas, demora, falta de conexión y errores del servidor. Cuando el servicio no puede arrancar indica la ubicación de `api-startup.log`.

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
- El servicio API declara dependencia de PostgreSQL y acciones de reinicio automático.
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
- Caja adicional limpia: confirmar que no aparecen servicios `PuntoDeVentaApi`
  ni `PuntoDeVentaPostgreSQL`, no se copia `postgresql` ni `api`, y el cliente
  inicia con el servidor emparejado.
- Actualización de una caja adicional: confirmar que conserva modalidad,
  servidor y token de dispositivo sin volver a pedir código temporal.
