# Punto de control: JetVenta 2.4.4

Fecha: 2026-09-07  
Base estable: etiqueta Git `v2.4.4` (`899afe3`)

## Validado por pruebas manuales

- Productos F3: existencia inicial, mínimo y máximo; actualización de límites sin alterar existencia; mayoreo y retiro lógico; validación de código duplicado.
- Alertas y confirmaciones: diseño JetVenta, confirmación y cancelación por teclado.
- Impresión deshabilitada: la venta se confirma sin impresión ni diálogo de PDF.
- Compras: captura con Enter o lector, y actualización de costo, precio de venta y margen.

## Defecto diferido

### Producto común desde código no encontrado

Al escanear o escribir un código inexistente, elegir `Producto común (temporal)`, seleccionar una unidad de granel e indicar la cantidad, todavía puede abrirse la ventana `Cantidad de la partida` por segunda vez.

Comportamiento esperado:

- El diálogo de producto no encontrado captura descripción, precio, unidad y cantidad una sola vez.
- Al confirmar, agrega directamente esa cantidad al ticket.
- La ventana `Cantidad de la partida` se reserva para productos existentes de inventario vendidos por peso o granel.

No continuar modificando este flujo dentro del siguiente bloque salvo que se solicite explícitamente. Debe investigarse con una prueba reproducible de eventos de teclado/lector y trazabilidad de la llamada a `SaleQuantityWindow`.

## Próximo bloque del plan grande

Productos e inventario:

1. Separar definitivamente la lista principal de Productos F3 de los formularios de alta y edición.
2. Completar edición de todos los campos de producto.
3. Confirmar departamento visible y asignable en catálogo e Inventario F4.
4. Consolidar diferenciación de menudeo y mayoreo.
5. Reforzar validaciones y bajas lógicas, preservando ventas y movimientos históricos.

## Regla de control

- Cambios pequeños: incrementar `x.x.n`, documentar en `docs/releases`, crear commit y etiqueta Git, y publicar en GitHub.
- Cambios importantes: incrementar `x.n.x` después de la validación funcional del bloque.
- Generar primero el ejecutable Debug local. El instalador se genera sólo después de validación solicitada.
- No mezclar el defecto diferido de producto común con cambios no relacionados.
