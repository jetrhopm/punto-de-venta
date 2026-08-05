# Decisiones de arquitectura

## ADR-0001: Usar .NET 10 LTS como objetivo inicial

Fecha: 2026-08-05

Decision: usar .NET 10 LTS para proyectos nuevos (`net10.0` y `net10.0-windows` en WPF).

Motivo: .NET 10 es LTS activo y su soporte oficial llega hasta noviembre de 2028. .NET 8 sigue instalado globalmente en esta maquina, pero entra en fin de soporte en noviembre de 2026.

Riesgo: algunas herramientas o IDEs antiguos pueden requerir actualizacion para .NET 10 y WPF.

## ADR-0002: PostgreSQL real para integracion

Fecha: 2026-08-05

Decision: las pruebas de integracion transaccional usaran PostgreSQL real. SQLite o bases en memoria podran usarse solo para pruebas unitarias no transaccionales cuando aplique.

Motivo: concurrencia, bloqueos, restricciones, WAL e idempotencia deben verificarse contra el motor real.

Riesgo: requiere preparar PostgreSQL local o portable en desarrollo.

## ADR-0003: Redondeo monetario centralizado

Fecha: 2026-08-05

Decision: el dominio usa `decimal` y redondea a dos decimales con `MidpointRounding.AwayFromZero` en cada importe monetario creado.

Motivo: evita diferencias por `float` o `double` y deja una regla uniforme para venta, cambio, descuentos e impuestos. Las reglas fiscales de redondeo por partida se confirmaran antes de habilitar impuestos configurables.

Riesgo: operaciones futuras con divisas o impuestos por linea requieren documentar si redondean por partida o por total.

## ADR-0004: Borrador separado de venta finalizada

Fecha: 2026-08-05

Decision: un borrador no afecta inventario, caja, credito ni auditoria financiera. La venta finalizada se creara por un comando idempotente y una sola transaccion.

Motivo: protege la operacion ante cierres inesperados y permite varias ventas en atencion sin medias ventas contables.

## ADR-0005: PostgreSQL portable para desarrollo

Fecha: 2026-08-05

Decision: el entorno descarga binarios oficiales EDB PostgreSQL 18.4 en `.tools/postgresql-18.4` y crea un cluster aislado en `.postgres`.

Motivo: permite pruebas reales sin privilegios de administrador, Docker ni interferencia con otras instalaciones.

Controles: escucha solo en `127.0.0.1:55432`, usa SCRAM, checksums, `fsync`, `synchronous_commit` y `full_page_writes`.
