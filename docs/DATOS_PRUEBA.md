# Datos realistas de prueba

La base `punto_venta_dev` puede poblarse con informacion ficticia y coherente para revisar reportes, cortes, inventario, compras y rendimiento sin afectar una instalacion de produccion.

## Carga

Con PostgreSQL de desarrollo iniciado, ejecuta desde la raiz del repositorio:

```powershell
.\scripts\seed-realistic-data.ps1
```

El script valida que el destino sea exactamente `punto_venta_dev`, crea primero un respaldo en `.postgres/backups`, calcula su SHA-256 y ejecuta toda la carga dentro de una transaccion PostgreSQL.

## Informacion generada

- Un ano de turnos cerrados y ventas distribuidas por dia.
- Pagos en efectivo, tarjeta y transferencia.
- Partidas de venta e historial de movimientos de inventario.
- Entradas y salidas de efectivo para probar cortes con diferencias.
- Clientes, proveedores, compras y trabajos de impresion completados.
- Minimos y maximos para todos los productos que no tenian valores validos.

Los identificadores son deterministas. Volver a ejecutar el script actualiza los calculos permitidos, pero no duplica ventas, partidas, turnos, pagos ni compras.

Estos datos son exclusivamente de desarrollo y no representan ventas reales del negocio.
