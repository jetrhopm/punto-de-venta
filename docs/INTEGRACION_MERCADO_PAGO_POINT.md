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

## Configuración de desarrollo

No se guardan credenciales reales en Git. Para habilitar OAuth define en el servidor:

```text
MercadoPago__ClientId=<APP_ID>
MercadoPago__ClientSecret=<CLIENT_SECRET>
MercadoPago__RedirectUri=https://<dominio-publico>/api/integrations/mercado-pago/oauth/callback
```

El callback debe ser HTTPS, público y coincidir exactamente con el registrado en la aplicación de Mercado Pago. El Access Token y el refresh token se cifran con DPAPI vinculado al equipo servidor. JetVenta renueva automáticamente el Access Token antes de su vencimiento y conserva el nuevo refresh token.

Para desarrollo local se puede usar **Configuración > Mercado Pago Point > Solo pruebas de desarrollo**. Esa opción valida un Access Token de prueba contra la lista de terminales antes de guardarlo. No debe ser el flujo de una instalación distribuida.

## Requisitos para habilitar el botón de autorización

JetVenta debe tener una sola aplicación OAuth central, propiedad del proyecto, no una aplicación diferente por tienda. El usuario final no necesita conocer ni configurar estos valores:

```text
MercadoPago__ClientId=<APP_ID_DE_JETVENTA>
MercadoPago__ClientSecret=<CLIENT_SECRET_DE_JETVENTA>
MercadoPago__RedirectUri=https://<dominio-publico>/api/integrations/mercado-pago/oauth/callback
```

El `ClientSecret` solo vive en la API desplegada. La URL de retorno debe ser pública, HTTPS y coincidir exactamente con la registrada en la aplicación de Mercado Pago. Para el producto distribuido se requiere alojar la API en un servidor con dominio o subdominio estable; una URL `127.0.0.1`, un callback distinto por cliente o un secreto dentro del cliente WPF no son opciones válidas.

Una vez configurado el servidor, la experiencia del comerciante es: **Configuración > Mercado Pago Point > Autorizar cuenta > iniciar sesión en Mercado Pago > aceptar > seleccionar terminal**. El Access Token y el refresh token se renuevan y conservan automáticamente en la API. Si el comerciante revoca el acceso, JetVenta mostrará que la cuenta necesita autorizarse de nuevo.

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
