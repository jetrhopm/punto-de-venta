# Matriz de configuracion multicaja

**Estado de control:** 3.3.18
**Objetivo:** separar de forma explicita la configuracion de tienda, de caja y
de equipo. Esta matriz es el requisito previo para cambiar configuraciones sin
afectar ventas, turnos, inventario ni cajas ya emparejadas.

## Principios

1. **Global de tienda:** se guarda en la API/base de datos del servidor. Un
   cambio confirmado por un administrador se refleja en todas las cajas.
2. **Por caja:** se guarda en la API/base de datos, asociado al registro de
   caja emparejado. La API lo valida al cobrar; no depende de un archivo que
   pueda editarse localmente.
3. **Por equipo:** se guarda en el perfil de maquina
   `%ProgramData%\PuntoDeVenta\client\machine-settings.json`. Describe
   dispositivos fisicos y conexion de esa computadora. Se comparte entre los
   usuarios de Windows de la misma caja.
4. **Servidor solamente:** se ejecuta y administra desde la caja principal.
   Una caja adicional nunca instala, repara ni modifica PostgreSQL o la API.
5. Toda pantalla debe mostrar una leyenda antes de guardar:
   **Afecta a todas las cajas**, **Solo esta caja: {nombre}**, o
   **Solo servidor**.

## Matriz funcional

| Area | Configuracion | Alcance objetivo | Ubicacion final | Estado 3.3.16 | Acceso |
| --- | --- | --- | --- | --- | --- |
| Tienda | Nombre, giro, razon social, RFC, direccion, telefono, zona horaria | Global | `store` en servidor | Ya global | Administrador |
| Usuarios | Usuarios, contrasenas, roles y permisos | Global | Base de datos | Ya global | Administrador / permisos delegados |
| Catalogos | Productos, departamentos, proveedores, clientes, creditos, promociones y kits | Global | Base de datos | Ya global | Segun permiso |
| Inventario | Existencias, minimos, maximos, costeo, precios y mayoreo | Global | Base de datos y kardex | Ya global | Segun permiso |
| Opciones de venta | Inventario habilitado, producto comun, credito global, margen automatico, redondeo y avisos | Global | `store` en servidor | Ya global | Administrador |
| Moneda y medida | Simbolo de moneda y unidad predeterminada de granel | Global | `store` en servidor | Ya global | Administrador |
| Folios | Siguiente folio de venta | Global | `store` y transaccion de venta | Ya global; obligatorio conservar asi | Administrador |
| Corte | Conteo, ajuste automatico, limite y bloqueo posterior a excederlo | Global como politica; resultado por caja | Politica en `store`; turnos/cortes por `register` | Ya separado funcionalmente | Administrador |
| Ticket | Encabezado, pie, datos de tienda, promociones, renglones y totales | Global | `store` en servidor | Ya global | Administrador |
| Ticket fisico | Ancho 58/80 mm, fuente, tamano, impresion activa y cola de Windows | Equipo | Perfil local de maquina | Ya local, pero convive con ancho global heredado | Administrador con permiso de impresora |
| Formas de pago | Efectivo, tarjeta, transferencia y credito disponibles para cobrar | Caja | Perfil de pagos por `register` | Implementado en 3.3.17 | Administrador |
| Credito | Permitir ventas a credito en la tienda | Global | `store` | Ya global | Administrador |
| Mercado Pago | Cuenta, ambiente y credenciales | Global / servidor | `store`, cifrado | Ya global | Administrador en servidor |
| Mercado Pago Point | Terminal asignada y habilitada para cobrar | Caja | `register` | Implementado en 3.3.18 | Administrador |
| Futuras terminales | BBVA, Getnet e Inbursa: proveedor, terminal y habilitacion | Caja; credenciales de comercio globales | Perfil por `register` y secretos en servidor | Pendiente de integracion | Administrador |
| Impresora | Cola de Windows, prueba y uso de tickets | Equipo | Perfil local de maquina | Ya local; permisos reparados en 3.3.16 | Permiso `ConfigurePrinters` |
| Lector | Modo teclado/serial, COM, velocidad y terminador | Equipo | Perfil local de maquina | Ya local | Administrador |
| Bascula | Puerto, comunicacion, unidad y tiempo de lectura | Equipo | Perfil local de maquina | Perfil local existe; hay campos globales historicos | Administrador |
| Cajon | Impresora/puerto, modelo y activacion | Equipo | Perfil local de maquina | Perfil local existe; hay campos globales historicos | Administrador |
| Inicio automatico | Ejecutar JetVenta al iniciar sesion en Windows | Equipo | Registro/Inicio de Windows local | Ya local | Administrador de Windows |
| Conexion LAN | URL del servidor, identidad, token y registro emparejado | Equipo | Perfil local de maquina, token DPAPI | Ya local; no editable manualmente | Emparejamiento/reemparejamiento |
| Cajas | Nombre, estado, ultima conexion, usuario y turno abierto | Global de administracion | Base de datos del servidor | Ya global | Administrador en servidor |
| Facturacion | PAC, cuenta, ambiente, CSD y solicitudes CFDI | Global / servidor | Base de datos y secretos cifrados | Configuracion Facturama global | Administrador en servidor |
| Respaldos | Crear, copiar, restaurar y limpiar datos | Servidor | PostgreSQL y `ProgramData` servidor | Ya restringido a servidor | Administrador |
| Diagnostico | API/PostgreSQL/migraciones y mantenimiento | Servidor | Servicios de Windows | Ya restringido a servidor | Administrador |
| Diagnostico | Red, compatibilidad y dispositivos instalados | Equipo | Consulta local | Ya por equipo | Administrador |
| Licencia | Demo, activacion y archivo `.jv` | Equipo servidor | Almacen protegido local | Ya local al equipo | Administrador |

## Casos que requieren consolidacion

### Formas de pago

El catalogo efectivo/tarjeta/transferencia/credito es comun para la tienda,
pero **la disponibilidad efectiva debe ser por caja**. La migracion debe crear
un perfil para cada caja existente con los cuatro valores globales actuales.
No se deben desactivar metodos silenciosamente ni bloquear ventas en cajas ya
operativas.

La API debe recibir la caja desde la sesion y rechazar una forma de pago no
habilitada para esa caja. Un archivo local no sera la fuente de autoridad para
esta regla.

### Ticket e impresora

Se mantienen globales el contenido y la presentacion de negocio: encabezado,
pie, datos de tienda, promociones, columnas y totales. Se mantienen locales
los datos que dependen del hardware: impresora, habilitacion, ancho de papel y
tipografia de impresion. El campo historico global de ancho de ticket se
conserva temporalmente como valor inicial para cajas sin perfil local y se
retirara solo despues de migrar todas las cajas.

### Mercado Pago y futuras terminales

La cuenta comercial y sus secretos pertenecen a la tienda y permanecen en el
servidor cifrados. Cada caja escoge solamente su terminal fisica y si esta
habilitada. Al cerrar, desactivar o reemparejar una caja no se debe borrar la
cuenta global ni sus transacciones historicas.

BBVA, Getnet e Inbursa usaran el mismo modelo: credenciales de comercio
globales, asignacion de terminal por caja, operacion validada en API y nunca
secrets en el escritorio.

### Bascula y cajon

Los campos globales historicos de cajon/bascula seran de solo migracion. La
fuente de operacion sera el perfil local de cada equipo. La consolidacion debe
leer primero el perfil local existente, para no cambiar una configuracion que
ya funciona en una caja.

## Orden de implementacion aprobado para proponer

1. Completado en `3.3.17`: perfil de pagos por caja, migracion de valores
   globales existentes y validacion API por sesion/caja.
2. Completado en `3.3.18`: habilitacion de Mercado Pago Point por caja,
   conservando cuenta, secretos, terminales y ordenes existentes.
3. Consolidar ticket, cajon y bascula, eliminando solo despues los campos
   globales duplicados mediante una migracion reversible.
4. Agregar leyendas de alcance en todas las ventanas de configuracion y
   pruebas multicaja de regresion.

## Pruebas obligatorias antes de cada liberacion

1. Cambiar impresora, lector, bascula o cajon en Caja 2 y verificar que Caja 1
   conserva su perfil.
2. Cambiar datos de tienda, permiso o diseno de ticket en Caja 2 y verificar
   que Caja 1 recibe el cambio al recargar la configuracion.
3. Desactivar tarjeta solo en Caja 2: Caja 1 debe conservar tarjeta y la API
   debe rechazar un cobro con tarjeta originado desde Caja 2.
4. Asignar terminal Point distinta en dos cajas y verificar que cada cobro usa
   su terminal.
5. Actualizar una caja adicional, confirmar que conserva emparejamiento,
   perfil de impresora y formas de pago de esa caja.
6. Restaurar un respaldo en servidor y confirmar que ninguna caja puede
   vender durante mantenimiento; al terminar, cada caja conserva sus perfiles
   de equipo.
