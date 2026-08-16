# Integración Mercado Pago Point

## Alcance implementado

JetVenta integra terminales Mercado Pago Point mediante la API oficial de Orders. No utiliza la API anterior de intenciones de pago.

El flujo es:

1. El administrador autoriza su cuenta de Mercado Pago mediante OAuth con PKCE, o captura un Access Token de prueba.
2. JetVenta consulta las terminales asociadas a esa cuenta.
3. El administrador asigna a la caja una terminal configurada en modo `PDV`.
4. Al cobrar con tarjeta, JetVenta crea una orden con un `operation_id` e idempotency key únicos.
5. La terminal recibe el importe y procesa la tarjeta de manera presencial.
6. JetVenta consulta el estado autoritativo de la orden. Solo registra la venta cuando Mercado Pago responde `processed`.
7. Un estado rechazado, cancelado, vencido o desconocido deja el ticket abierto y no modifica caja ni inventario.

La impresión ocurre después de confirmar la venta local. Un fallo de impresora no crea otra orden ni otra venta.

## Configuración de desarrollo

No se guardan credenciales reales en Git. Para habilitar OAuth define en el servidor:

```text
MercadoPago__ClientId=<APP_ID>
MercadoPago__ClientSecret=<CLIENT_SECRET>
MercadoPago__RedirectUri=https://<dominio-publico>/api/integrations/mercado-pago/oauth/callback
```

El callback debe ser HTTPS, público y coincidir exactamente con el registrado en la aplicación de Mercado Pago. El Access Token y el refresh token se cifran con DPAPI vinculado al equipo servidor. JetVenta renueva automáticamente el Access Token antes de su vencimiento y conserva el nuevo refresh token.

Para una prueba sin OAuth se puede usar **Configuración > Mercado Pago Point > Access Token de prueba**. Esta opción valida el token contra la lista de terminales antes de guardarlo.

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
