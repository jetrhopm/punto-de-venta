# Plan completo del punto de venta

Estado actualizado: 2026-08-11

## Terminado

### Base tecnica

- Solucion .NET 10 x64 con WPF, API ASP.NET Core, dominio, infraestructura, impresion e integraciones separadas.
- SDK .NET local en `.tools/dotnet` para trabajar sin instalacion global.
- PostgreSQL 18.4 portable en `.tools/postgresql-18.4`.
- Cluster de desarrollo en `.postgres`, puerto local `55432`, SCRAM, checksums, `fsync`, `synchronous_commit` y `full_page_writes`.
- Migraciones EF Core versionadas y aplicadas contra PostgreSQL real.
- Dependencias centralizadas y sin advertencias de compilacion.
- Git y GitHub con commits descriptivos en espanol.

### Funcionalidad implementada

- Iconos del programa preparados.
- Ventana WPF inicial con navegacion F1 a F4 y F12.
- Dinero monetario con `decimal` y redondeo centralizado.
- Borrador de venta separado de una venta finalizada.
- Matriz de paridad, modelo de datos y decisiones de arquitectura.
- Configuracion inicial de tienda, administrador y primera caja.
- Hash de contrasenas con el hasher oficial de ASP.NET Core.
- Inicio de sesion API y sesiones expirables almacenadas como hash.
- Catalogo persistente de productos.
- Busqueda asincrona por codigo o descripcion.
- Campo compatible con lectores de codigo de barras que escriben y confirman con Enter.

## Pendiente por fases

### Fase 0: base tecnica restante

- Completado: inicio de sesion conectado a la ventana WPF.
- Completado: permisos de navegacion verificados en cliente y API.
- Completado: pruebas de integracion reales contra PostgreSQL, sin `Skip`.
- Completado: CI con compilacion, pruebas y analisis de vulnerabilidades.
- Completado: paquete portatil autocontenido `win-x64` con marca de prueba.

### Fase 1: punto de venta local esencial

- Asistente WPF completo de primera configuracion.
- Administracion de usuarios y permisos en API; bloqueo progresivo y recuperacion administrada quedan para el siguiente incremento de seguridad.
- Turnos: apertura, entradas, salidas y corte conectados en WPF.
- Alta y edicion de productos protegidas por permiso; pendiente importacion CSV.
- Venta finalizada con `operation_id` idempotente, efectivo y cambio.
- Cliente WPF con carrito, seleccion de resultados y ventana de cobro en efectivo.
- Descuento de existencia y kardex con ajustes autorizados, existencias anterior/posterior y auditoria de operacion.
- Cobro en efectivo transaccional con recibido/cambio y movimientos detallados de caja.
- Ticket PDF guardable mediante dialogo de Windows y cola transaccional; ESC/POS fisico queda posterior.
- Respaldo con checksum, restauracion de prueba y diagnostico local documentados; restauracion requiere credenciales administrativas PostgreSQL.
- Importador CSV real bloqueado hasta recibir el archivo de productos.

### Fase 2: operacion comercial completa

- Completado: clientes, credito, abonos, proveedores, compras con costo promedio, cancelacion total, devoluciones parciales y reportes de ventas con exportacion CSV.
- Completado adicional: precio de mayoreo y cantidad minima persistidos y aplicables en la venta.
- Completado adicional: promociones porcentuales por producto con vigencia.
- Completado adicional: pantalla WPF de promociones y preparación local de publicación de producción con WiX.
- Completado adicional: kits con componentes y reversos, configuración WPF de ticket y MSI de producción con actualización mayor/reparación.
- Completado adicional: Bundle `Setup.exe` y MSI generados reproduciblemente con WiX 6, actualización mayor y reparación.
- Completado adicional: pantalla WPF para configurar kits y componentes.
- Pendiente de validación externa: integrar PostgreSQL/servicio Windows en el Bundle y probar instalación, actualización, reparación y conservación de datos en Windows 10/11 limpios.
- Verificado: compilacion de la solucion sin advertencias ni errores.
- Verificado: pruebas unitarias e integracion contra PostgreSQL real, incluyendo migraciones y lectura del catalogo.
- Verificado: publicacion autocontenida `win-x64` y generacion reproducible de MSI y `Setup.exe`.
- Pendiente externo para declarar liberacion de produccion: ejecutar el bootstrap durante Burn y probar instalacion, actualizacion, reparacion y conservacion de datos en Windows 10/11 limpios.

La implementacion local de la Fase 2 esta terminada. La validacion externa requiere la maquina virtual o equipos limpios y permisos administrativos; no debe simularse en desarrollo.

### Fase 3: multicaja LAN

- Incremento 1 completado: servidor configurable por IP/nombre, prueba de conexion, endpoint de compatibilidad, API preparada para escuchar en LAN privada y regla de Firewall restringida al perfil privado.
- Pendiente: emparejamiento por codigo temporal e identificadores persistentes de tienda, equipo y caja.
- Pendiente: SignalR para avisos y concurrencia real sobre PostgreSQL.
- Pendiente: pruebas con dos cajas y fallas de red.

### Fase 4: integraciones externas

- Mercado Pago Point con sandbox, idempotencia, webhooks y conciliacion.
- Taecel solo con documentacion y credenciales oficiales vigentes.

### Fase 5: nube y opcionales

- VPS Hostinger por HTTPS, sin PostgreSQL publico.
- Recuperacion ante perdida de Internet.
- CFDI solo despues de seleccionar PAC y validar timbrado real.

## Criterio para avanzar

No se habilita una venta real hasta que existan usuario autenticado, permiso, turno abierto, transaccion atomica, idempotencia y prueba de rollback contra PostgreSQL.
