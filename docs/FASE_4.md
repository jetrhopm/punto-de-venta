# Fase 4 - Integraciones externas

## 4.0.0 - Webhooks y conciliación de Mercado Pago Point

### Implementado

- Endpoint público exclusivo para Webhooks:
  `/api/integrations/mercado-pago/webhook`.
- Validación HMAC-SHA256 de `x-signature`, `x-request-id` y `data.id` con
  comparación de tiempo constante y una ventana de diez minutos contra reenvíos.
- Registro idempotente de cada notificación en la tabla
  `pos.mercado_pago_webhook`. Los reintentos de Mercado Pago no duplican
  cobros, ventas ni órdenes.
- Conciliación asíncrona: el endpoint responde después de registrar el evento;
  un servicio en segundo plano consulta la orden de Mercado Pago y actualiza su
  estado local de forma autoritativa. Los fallos transitorios se reintentan y
  los eventos que no pueden asociarse se conservan como historial técnico.
- Mercado Pago Point muestra que el Webhook HTTPS aún está pendiente cuando la
  cuenta y terminal existen, pero el servidor no tiene secreto configurado.

### Configuración pendiente de infraestructura

El servidor que reciba notificaciones debe tener URL HTTPS pública y establecer
estos valores fuera de Git:

```text
MercadoPago__ClientId=<app-id>
MercadoPago__ClientSecret=<secreto-oauth>
MercadoPago__RedirectUri=https://<dominio>/api/integrations/mercado-pago/oauth/callback
MercadoPago__WebhookSecret=<secreto-de-webhook>
```

En Mercado Pago se registra:

```text
https://<dominio>/api/integrations/mercado-pago/webhook
```

con el evento **Order**. El servidor local de una tienda no se expone a
Internet; Hostinger o un servicio HTTPS equivalente debe recibir y reenviar la
notificación de forma segura.

### Pruebas por completar con credenciales de sandbox

1. Configurar el Webhook de la aplicación y usar la simulación de Mercado Pago.
2. Enviar `order.processed`, `order.failed`, `order.canceled` y `order.expired`.
3. Repetir exactamente la misma notificación y confirmar un solo registro.
4. Enviar firma inválida y confirmar respuesta `401` sin crear registros.
5. Interrumpir la consulta de Mercado Pago y confirmar reintentos sin crear
   una segunda venta ni una segunda orden.
