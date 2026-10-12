# JetVenta Móvil: checador de productos

## Funciones disponibles

JetVenta Móvil se conecta al servidor de la tienda desde la red local y presenta un menú según los permisos del usuario:

- **Verificador:** escanear o buscar por código, nombre o descripción; consultar existencia, precio y niveles mínimo/máximo.
- **Conteo:** registrar la cantidad física de cada artículo. Cada registro actualiza existencias al momento y conserva existencia del sistema, cantidad contada, diferencia, usuario, teléfono y fecha.
- **Inventario:** consultar artículos y filtrar todos, inventario bajo o agotados.
- **Bitácora:** revisar los conteos recientes y filtrar los que tuvieron diferencias.

El conteo requiere `AdjustInventory`; consultar el verificador e inventario requiere `ViewInventory` (o `AdjustInventory`). La app no vende, no crea turnos, no muestra costos ni opera periféricos.
El conteo se recomienda fuera del horario de ventas. Si una venta ocurre mientras se cuenta, JetVenta detecta que la existencia cambió y pide volver a consultar el artículo antes de registrar el conteo.

## Preparación en JetVenta

1. En la computadora servidor abre **Configuración > JetVenta Móvil**.
2. Confirma la dirección IP privada mostrada y el puerto `5000`.
3. Genera el código temporal de seis dígitos. Vence en diez minutos y se utiliza una sola vez.
4. Conecta el teléfono al mismo Wi-Fi privado de la tienda.
5. En la aplicación escribe la dirección, un nombre para el teléfono y el código temporal.
6. Inicia sesión con el administrador o con un usuario que tenga `ViewInventory` o `AdjustInventory`.
7. Para usar **Conteo**, el usuario necesita `AdjustInventory`; los demás módulos aparecen de acuerdo con sus permisos.

## Seguridad

- La aplicación sólo acepta una IP privada de red local (`10.x.x.x`, `172.16-31.x.x` o `192.168.x.x`).
- Cada teléfono recibe una identidad propia. Emparejar de nuevo el mismo nombre revoca sus sesiones anteriores.
- Las sesiones móviles vencen a las ocho horas y se almacenan en el almacenamiento seguro del teléfono.
- La conexión móvil no reutiliza una caja, dispositivo de venta o turno.
- El servidor verifica el protocolo de red y limita los intentos de emparejamiento e inicio de sesión.

## Prueba manual

1. Emparejar un teléfono y comprobar que no puede conectarse con un código vencido o ya usado.
2. Iniciar sesión con un usuario que tenga permiso de inventario.
3. Escanear un código existente y validar precio y existencia.
4. Buscar un producto por texto y seleccionar un resultado.
5. Con un usuario con `AdjustInventory`, iniciar un conteo, registrar una cantidad igual y otra distinta; verificar que el inventario cambie de inmediato.
6. Reintentar el mismo registro y confirmar que no duplica el movimiento. Abrir **Bitácora** y revisar usuario, teléfono, hora y diferencia.
7. Intentar contar con sólo `ViewInventory` y confirmar que el servidor lo rechaza.
8. Revocar la sesión desde la aplicación y confirmar que solicita iniciar sesión otra vez.
9. Confirmar que el teléfono no aparece como caja y no genera un turno.
