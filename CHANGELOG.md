# Changelog

## Sin liberar

- Se agrega prompt maestro del proyecto.
- Se documentan requisitos de desarrollo.
- Se crea estructura inicial de solucion .NET.
- Se preparan iconos iniciales de la aplicacion.
- Se agrega el nucleo inicial de dinero, permisos y borradores de venta.
- Se documentan la paridad operativa y el modelo de datos inicial.
- Se automatiza PostgreSQL 18.4 portable para desarrollo.
- Se agrega la primera migracion PostgreSQL para tienda, usuario y caja.
- Se agrega el flujo transaccional de primera configuracion.
- Se conecta la vista WPF al estado real de configuracion local.
- Se agrega autenticacion inicial con sesiones expirables almacenadas como hash.
- Se documenta el plan completo y el estado por fases.
- Se agregan permisos persistentes y apertura de turno autorizada.
- Se cierran los pendientes de Fase 0 con CI, pruebas PostgreSQL y paquete portable.
- Se inicia Fase 1 con alta y edicion de productos protegidas por permiso.
- Se agrega venta idempotente con efectivo, cambio e inventario transaccional.
- Se verifica doble envio de venta sin duplicar operacion.
- Se agrega registro de movimientos de efectivo y cierre de turno con diferencia.
- Se conectan en WPF la apertura de turno, movimientos de efectivo y cierre con diferencia.
- Se implementa kardex transaccional con ajustes autorizados y existencias anterior/posterior.
- Se agrega cola de impresion y ticket PDF guardable como impresora virtual.
- Se agregan scripts de respaldo, restauracion de prueba y diagnostico PostgreSQL.
- Se agregan endpoints administrativos para usuarios, contrasenas, estados y permisos con proteccion del ultimo administrador.
- Se inicia Fase 2 con clientes, ventas a credito, abonos idempotentes y estado de cuenta.
- Se agregan proveedores, recepcion de compras, costo promedio ponderado y pantalla WPF de compras.
- Se implementan cancelaciones idempotentes con reverso de inventario, caja o credito y motivo auditado.
- Se agregan devoluciones parciales por partida con limite vendido, reverso de caja o credito e idempotencia.
- Se agregan reportes de ventas protegidos por permiso y exportacion CSV con proteccion contra formulas.
- Se agregan precio de mayoreo, cantidad minima y migraciones automaticas al iniciar la API.
- Se agregan promociones porcentuales por producto con vigencia, permiso y aplicacion transaccional.
- Se agrega pantalla WPF para administrar promociones y herramienta local de publicación de producción con WiX.
- Se implementan kits con componentes, devolución/cancelación por componentes y configuración persistente de ticket.
- Se genera Bundle final `Setup.exe` y MSI de producción con actualización mayor y reparación mediante WiX.
- Se reorganiza visualmente la ventana de ventas en tres zonas: acciones rápidas, carrito y resumen de cobro.
- Se conecta Enter y botón Buscar al alta rápida de productos y Delete a la eliminación de partidas del carrito.
