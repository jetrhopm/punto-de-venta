# Modelo de datos inicial

La persistencia se implementara con PostgreSQL y migraciones versionadas. Todas las claves de negocio sensibles seran UUID y las cantidades monetarias `numeric`, nunca `real` ni `double precision`.

```text
Store 1---* Register 1---* Shift 1---* Sale 1---* SaleLine
                                      |        1---* Payment
                                      |        1---* InventoryMovement
                                      |        1---* CashMovement
                                      |        1---* AuditEvent
                                      `---* OutboxMessage

Product 1---* ProductBarcode
Product 1---* InventoryMovement
User *---* Permission
```

## Restricciones iniciales

- `sale.operation_id` tendra `UNIQUE` para garantizar idempotencia.
- `sale`, `payment`, `inventory_movement`, `cash_movement` y `audit_event` no se eliminan; las correcciones crean reversos relacionados.
- `inventory_movement` conserva existencia anterior y posterior, usuario, motivo y fecha.
- Solo puede existir un turno abierto por caja mediante indice unico parcial.
- Los importes usan `numeric(18,2)` y las cantidades `numeric(18,3)`.
- Los eventos se guardan como `timestamptz`; la zona horaria de la tienda se configura por separado.

## Indices prioritarios

- `sale(operation_id)` unico.
- `sale(register_id, created_at desc)`.
- `product_barcode(normalized_code)` unico.
- `inventory_movement(product_id, occurred_at desc)`.
- `outbox_message(status, next_attempt_at)` para el trabajador de impresion e integraciones.
