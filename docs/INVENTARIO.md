# Inventario

## Alcance de F4

F4 es el módulo operativo de existencias. Productos (F3) conserva la administración del catálogo, precios, departamentos y promociones; Inventario muestra el estado de ese catálogo desde la perspectiva de existencias y movimientos.

El catálogo usa páginas de 500 productos y consulta los datos de forma paginada para evitar cargar toda la tabla en la interfaz. Incluye búsqueda por código o descripción, filtro por estado, orden ascendente o descendente por encabezado y edición de inventario mínimo y máximo. La existencia actual no se edita directamente: cambia mediante entradas, salidas, ajustes, compras, ventas, cancelaciones o devoluciones, todos registrados en el kardex.

## Indicadores

F4 muestra el total de productos, unidades, valor a costo, valor a precio de venta, utilidad potencial y alertas de productos agotados o bajo mínimo. Los totales se calculan sobre el catálogo activo, aunque la tabla esté filtrada o en otra página.

## Importación y exportación

- Durante la configuración inicial, después de crear la tienda, el usuario puede importar un archivo CSV o XLSX, capturar productos manualmente u omitir el paso.
- La importación nunca modifica el archivo original. Muestra vista previa editable, valida cada fila, crea un respaldo previo y permite repetirla desde Inventario.
- Inventario conserva el botón **Importar** para cargas posteriores y correcciones controladas.
- Inventario agrega **Exportar**, que genera un CSV con código, descripción, departamento, tipo de venta, costo, precio, existencia y límites.
- La opción de Configuración se conserva por compatibilidad con el flujo anterior; el acceso principal para estas operaciones es Inventario.

## Movimientos

La ventana de movimientos muestra fecha, código, producto, cantidad positiva o negativa, existencia anterior y posterior, motivo y usuario. También permite buscar y paginar hasta 500 registros por página.

Los cambios de límites se guardan en `inventory_limit_change` con operación única, usuario, valores anteriores, valores nuevos y fecha. Esto evita duplicar actualizaciones por reintentos y deja rastro del cambio sin inventar un movimiento de mercancía.

## Prueba manual

1. Iniciar la API y el cliente en desarrollo.
2. Abrir **Inventario** o pulsar `F4`.
3. Confirmar que los seis indicadores cargan y que la tabla permite ordenar por encabezados.
4. Editar mínimo y máximo de una fila y verificar el mensaje de confirmación.
5. Abrir **Movimientos** y confirmar el historial.
6. Probar **Importar** con un CSV/XLSX y verificar la vista previa.
7. Usar **Exportar** y abrir el CSV generado; los valores deben conservarse como datos, no como fórmulas.
