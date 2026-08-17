# Integraciones de recargas y servicios

JetVenta mantiene cada proveedor separado. La tienda puede activar Taecel, La Red+, ambos o ninguno. Cada proveedor tendrá credenciales, catálogo, saldo, folios, estados e idempotencia independientes.

## Taecel

Taecel publica una integración API para puntos de venta y solicita un levantamiento tecnológico, códigos de prueba, verificación y posteriormente accesos de producción. La cuenta se configura con la URL oficial asignada por Taecel, Key y NIP protegidos en el servicio, nunca en el cliente WPF ni en los registros.

La operación debe consultar primero el catálogo oficial de la cuenta. JetVenta no enviará el nombre comercial de un operador como si fuera un código: usará el código de producto que devuelva Taecel. Esto también determina si `Red+` está habilitada en esa cuenta.

Fuente oficial: <https://taecel.com/portal/integracion-web-services>.

## La Red+

La Red+ es un proveedor independiente, operado por Red de Prepago de México. Su plataforma publica integración para puntos de venta mediante web service y ofrece recargas, pines y pagos de servicios. No se reutilizarán la Key/NIP de Taecel ni se asumirán sus endpoints.

Fuente del proveedor: <https://rpm-mx.net/plataforma/>.

Antes de habilitar operaciones reales de La Red+ se necesita solicitar al proveedor:

- URL de pruebas y producción.
- Usuario, contraseña, token o mecanismo de autenticación.
- Catálogo y códigos de productos, incluyendo operadores, montos y comisiones.
- Método de consulta de saldo.
- Método de venta y consulta de estado.
- Reglas para operaciones desconocidas, cancelaciones y reversos.
- Ambiente de pruebas y casos de certificación.

Hasta recibir esa información, el adaptador de La Red+ queda visible como proveedor independiente pero rechaza operaciones con un mensaje claro. No se simulan recargas ni se marca una venta como exitosa.

## Reglas comunes

- El cliente genera un `operation_id` antes de enviar una operación.
- La API guarda el intento local como pendiente antes de contactar al proveedor.
- Un timeout se consulta por el identificador del proveedor antes de reintentar.
- Una respuesta desconocida no se reenvía automáticamente.
- El cobro local y la confirmación externa quedan relacionados, pero no se descuenta inventario por una recarga externa.
- La impresión del comprobante es posterior a la confirmación y no repite la operación.
- Los secretos se cifran y solo viven en el servicio/API.

## Estado

La solución contiene contratos comunes y proyectos separados para Taecel y La Red+. La conexión real queda pendiente de los contratos y credenciales de prueba de cada proveedor. El siguiente incremento debe agregar la configuración administrativa, catálogo sincronizado y pruebas con credenciales reales de prueba.
