# Integración Mercado Pago Point

## Alcance implementado

JetVenta integra terminales Mercado Pago Point mediante la API oficial de Orders. No utiliza la API anterior de intenciones de pago.

El flujo de producción está pensado para que el usuario final no copie tokens:

1. El administrador pulsa **Autorizar cuenta** en JetVenta.
2. JetVenta abre el inicio de sesión oficial de Mercado Pago en el navegador.
3. El administrador acepta el acceso para la cuenta que recibirá el dinero.
4. Mercado Pago regresa al callback HTTPS de JetVenta. La API valida `state` y PKCE, intercambia el código por Access Token y refresh token, y cifra ambos en el servidor.
5. JetVenta detecta automáticamente la autorización y consulta las terminales asociadas a esa cuenta; no hace falta pegar un Access Token ni pulsar actualizar.
6. El administrador selecciona la terminal de esta caja y JetVenta valida que esté configurada en modo `PDV`.
7. Al cobrar con tarjeta, JetVenta crea una orden con un `operation_id` e idempotency key únicos.
8. La terminal recibe el importe y procesa la tarjeta de manera presencial.
9. JetVenta consulta el estado autoritativo de la orden. Solo registra la venta cuando Mercado Pago responde `processed`.
10. Un estado rechazado, cancelado, vencido o desconocido deja el ticket abierto y no modifica caja ni inventario.

La impresión ocurre después de confirmar la venta local. Un fallo de impresora no crea otra orden ni otra venta.

## Intermediario OAuth central

Desde esta versión la autorización pasa por el intermediario `services/mp-broker`
(Node.js en `https://mp.gocentersuplementos.com`). Es el único que conoce el Client
Secret; la API local de cada tienda ya no expone callback ni webhook a Internet.

1. **Autorizar cuenta** → la API local se registra una vez en el intermediario
   (`installation_id` + secreto, guardados con DPAPI) y pide `POST /v1/oauth/start`.
2. El navegador abre Mercado Pago; al aceptar, Mercado Pago regresa a
   `https://mp.gocentersuplementos.com/v1/oauth/callback`.
3. La ventana de configuración consulta `GET /api/integrations/mercado-pago/oauth/status`;
   la API recoge los tokens con `POST /v1/oauth/pickup` una sola vez y los cifra con DPAPI.
4. Cuando faltan menos de 30 días para que venza el Access Token (dura 180), la API lo
   renueva con `POST /v1/oauth/refresh`. Si el intermediario no responde y el token sigue
   vigente, los cobros continúan con el token actual.
5. Si Mercado Pago rechaza la renovación (permiso revocado), JetVenta borra los tokens y
   pide volver a autorizar.

Configuración de la API local (`appsettings.json`):

```json
"MercadoPago": { "BrokerUrl": "https://mp.gocentersuplementos.com/" }
```

Columnas reutilizadas en `store` (sin migración): `MercadoPagoOAuthState` guarda el id del
intento en el intermediario, `MercadoPagoOAuthStateExpiresAtUtc` su vencimiento y
`MercadoPagoOAuthVerifierProtected` la identidad de la instalación cifrada.

El despliegue del intermediario está en `services/mp-broker/README.md`.

### Solo pruebas de desarrollo

El campo para pegar un Access Token de prueba (`APP_USR-` en la documentación actual de Point, o `TEST-` en integraciones sandbox anteriores) sólo aparece si la API tiene
`"MercadoPago": { "AllowTestToken": true }` (ya incluido en `appsettings.Development.json`).
En producción queda oculto y el endpoint lo rechaza.

### Webhooks

Pendiente para una segunda fase: el intermediario recibirá el evento **Order** y cada
tienda recogerá sus eventos. Mientras tanto la ventana de cobro consulta la orden cada 2
segundos y retoma cobros pendientes.

## Desconectar y eliminar datos locales

El botón **Eliminar datos de JetVenta** requiere permisos de administrador o configuración de tienda y confirmación explícita. Elimina de la instalación los tokens cifrados, usuario OAuth, estado de autorización y terminales asociadas a las cajas. No elimina ventas, órdenes, cobros, devoluciones ni auditoría histórica, porque esos registros son necesarios para la trazabilidad. Tampoco revoca el permiso en Mercado Pago; esa revocación se realiza desde la cuenta del comerciante. Mercado Pago documenta que la revocación de la autorización invalida los tokens y concesiones asociados. ([gestión oficial de Access Token](https://www.mercadopago.com.mx/developers/es/docs/checkout-bricks/additional-content/security/oauth/management))

## Configuración de la terminal

La terminal física debe:

- estar vinculada a la cuenta que recibirá el dinero;
- estar asociada a una sucursal y punto de venta de Mercado Pago;
- aparecer en la lista de terminales de esa cuenta;
- operar en modo `PDV`;
- asignarse a la caja correcta desde Configuración de JetVenta.

JetVenta rechaza terminales que no estén en modo `PDV`.

## Seguridad e idempotencia

- Los tokens viven únicamente en la API local y nunca se entregan al cliente WPF.
- Cada intento usa el mismo UUID para la orden externa y la venta local.
- La base impone unicidad para `operation_id` y para el ID de orden devuelto por Mercado Pago.
- Si se pierde la conexión, JetVenta consulta la orden existente; no crea otra a ciegas.
- Una venta con tarjeta o con componente de tarjeta en pago mixto requiere una orden aprobada por el importe exacto.

## Prueba pendiente con credenciales reales

Antes de producción se debe ejecutar con una terminal física:

1. Autorizar la cuenta que recibirá los cobros.
2. Asociar y seleccionar la terminal PDV.
3. Aprobar y rechazar cobros de prueba.
4. Cortar la red después de aprobar y verificar la reconciliación por `operation_id`.
5. Repetir el botón de cobro y confirmar que existe una sola orden y una sola venta.
6. Probar pago mixto con una parte en tarjeta.
7. Apagar la impresora y confirmar que la venta y el cobro no se duplican.

No se considera validada la integración de producción hasta completar estas pruebas con las credenciales y terminal reales.

## Documentación oficial

- Procesamiento Point con Orders: https://www.mercadopago.com.mx/developers/es/docs/mp-point/payment-processing
- Configuración de terminal PDV: https://www.mercadopago.com.mx/developers/es/docs/mp-point/configure-terminal
- OAuth y PKCE: https://www.mercadopago.com.mx/developers/es/docs/security/oauth/creation
- Renovación OAuth: https://www.mercadopago.com.mx/developers/es/docs/security/oauth/renewal
- Migración desde intenciones de pago: https://www.mercadopago.com.mx/developers/es/docs/mp-point/migrate-payment-intent-to-orders

Documentación revisada el 16 de agosto de 2026.
