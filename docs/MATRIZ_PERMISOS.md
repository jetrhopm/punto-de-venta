# Matriz de permisos

Los administradores tienen acceso completo. Los cajeros reciben únicamente los permisos marcados por un administrador. La API valida cada permiso; ocultar o deshabilitar un botón no sustituye esa validación.

## Ventas

| Permiso | Permite |
| --- | --- |
| `Sell` | Vender y cobrar tickets. |
| `SellOnCredit` | Cobrar a crédito; además requiere un cliente activo con crédito habilitado. |
| `UseCommonProduct` | Utilizar producto común o registrar rápidamente un código no encontrado. |
| `ChangeSalePrice` | Cambiar el precio durante la venta. |
| `ApplyDiscounts` | Aplicar descuentos. |
| `UseWholesalePrice` | Usar precio de mayoreo. |
| `CancelSaleLines` | Eliminar productos del ticket. |
| `CancelSales` | Cancelar ventas confirmadas. |
| `ProcessReturns` | Procesar devoluciones. |
| `ReprintTickets` | Reimprimir tickets. |
| `OpenCashDrawer` | Abrir el cajón de dinero. |
| `RecordCashMovements` | Registrar entrada F7 y salida F8 de efectivo. |
| `ViewSalesHistory` | Revisar historial de ventas. |

## Clientes

| Permiso | Permite |
| --- | --- |
| `ManageCustomersAndCredit` | Crear, modificar y desactivar clientes; asignarlos a ventas; administrar crédito, cuenta, abonos y reportes. |

## Productos

| Permiso | Permite |
| --- | --- |
| `ViewProducts` | Consultar el catálogo de productos. |
| `ManageProducts` | Crear, modificar y eliminar productos; administrar departamentos, kits y promociones. |

## Inventario

| Permiso | Permite |
| --- | --- |
| `ViewInventory` | Consultar existencias, kardex y reporte de inventario. |
| `AdjustInventory` | Ajustar inventario y modificar mínimos o máximos. |
| `ViewCostsAndProfit` | Ver costos, utilidad y valor del inventario. |
| `ImportOrExportData` | Importar o exportar inventario y crear respaldos. |

## Otros

| Permiso | Permite |
| --- | --- |
| `OpenShift` | Abrir turno. |
| `CloseShift` | Realizar el corte del turno propio y consultar el efectivo esperado. |
| `ViewPreviousShifts` | Consultar cortes de cajeros y el corte consolidado del día. No cierra turnos ajenos. |
| `ManageSuppliersAndPurchases` | Administrar proveedores y compras. |
| `ProcessServicePayments` | Realizar recargas y pagos de servicios. |
| `ViewReports` | Acceder a reportes de ventas, ganancias y análisis. |
| `ConfigurePrinters` | Configurar impresoras y formato de ticket. |
| `ConfigureStore` | Configurar datos de la tienda. |
| `ManageUsers` | Administrar usuarios y permisos. |
