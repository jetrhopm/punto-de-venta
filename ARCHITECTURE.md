# Arquitectura

## Decision inicial

El sistema se construira como una aplicacion Windows con cliente WPF y una API local ASP.NET Core. Incluso en una sola caja, el cliente no ejecutara SQL directo contra bases remotas; las reglas criticas viven en la capa de aplicacion.

## Diagrama textual

```text
Pos.Desktop (WPF/MVVM)
  -> Pos.Api (ASP.NET Core local o LAN)
      -> Pos.Application (casos de uso, permisos, transacciones)
          -> Pos.Domain (entidades, reglas, eventos)
          -> Pos.Infrastructure (EF Core, PostgreSQL, outbox)
      -> Pos.Printing (tickets y cola de impresion)
      -> Integraciones modulares
          -> Pos.Integrations.MercadoPago
          -> Pos.Integrations.Taecel
```

## Principios

- PostgreSQL sera la fuente de verdad.
- Las ventas finalizadas seran inmutables.
- Toda operacion critica usara `operation_id` UUID e idempotencia en base de datos.
- Impresion e integraciones externas se procesaran por outbox transaccional.
- Las cajas adicionales se conectaran a la API, no a archivos compartidos ni directamente a PostgreSQL.
