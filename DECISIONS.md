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
