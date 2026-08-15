# Matriz de permisos

Los administradores tienen acceso completo. Los cajeros reciben únicamente los permisos marcados por un administrador. La API valida cada permiso; ocultar o deshabilitar un botón no sustituye esa validación.

## Ventas

| Permiso | Permite |
| --- | --- |
| `Sell` | Registrar ventas y cobrar. |
| `ChangeSalePrice` | Cambiar el precio durante la venta. |
| `ApplyDiscounts` | Aplicar descuentos y promociones. |
| `UseWholesalePrice` | Usar precio de mayoreo. |
| `CancelSaleLines` | Eliminar productos del ticket. |
| `CancelSales` | Cancelar ventas confirmadas. |
| `ProcessReturns` | Procesar devoluciones. |
| `ReprintTickets` | Reimprimir tickets. |
| `OpenCashDrawer` | Abrir el cajón de dinero. |
| `RecordCashMovements` | Registrar entradas y salidas de efectivo. |
| `ViewSalesHistory` | Consultar historial de tickets. |
| `OpenShift` | Abrir turno. |
| `CloseShift` | Realizar corte de caja. |
| `ViewPreviousShifts` | Consultar cortes anteriores. |

## Clientes

| Permiso | Permite |
| --- | --- |
| `ManageCustomersAndCredit` | Administrar clientes, crédito y abonos. |

## Productos

| Permiso | Permite |
| --- | --- |
| `ViewProducts` | Consultar el catálogo de productos. |
| `ManageProducts` | Crear, editar y eliminar productos, departamentos, kits y promociones. |

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
| `ManageSuppliersAndPurchases` | Administrar proveedores y compras. |
| `ProcessServicePayments` | Realizar recargas y pagos de servicios. |
| `ViewReports` | Consultar reportes y análisis de ventas. |
| `ConfigurePrinters` | Configurar impresoras y formato de ticket. |
| `ConfigureStore` | Configurar datos de la tienda. |
| `ManageUsers` | Administrar usuarios y permisos. |
