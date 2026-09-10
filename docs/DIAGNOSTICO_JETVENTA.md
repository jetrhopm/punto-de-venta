# Diagnóstico de JetVenta

El diagnóstico se abre desde **Configuración > Diagnóstico** y requiere el permiso de configuración de la tienda o una cuenta administradora.

## Revisiones actuales

- Disponibilidad de PostgreSQL y lectura del esquema.
- Migraciones pendientes.
- Tienda configurada.
- Productos, usuarios, clientes, proveedores y ventas confirmadas.
- Tickets abiertos con productos que pueden recuperarse. Los borradores vacíos no se consideran pendientes.
- Cola de impresión pendiente e historial técnico de impresión.
- Respaldo local más reciente y validación de su SHA-256.
- Número de respaldos y espacio libre del equipo servidor.
- Impresoras instaladas en Windows y la impresora seleccionada para esta caja.

Las ventas cobradas con **Cobrar sin imprimir** no crean trabajos de impresión. Las ventas cobradas e impresas sí dejan un trabajo hasta que el sistema confirma que fue enviado a la impresora.

Cuando Diagnóstico informe documentos de impresión:

1. Si son **trabajos pendientes**, la venta ya fue confirmada. Revisa la impresora desde **Configuración > Impresora**. Si ya no se necesita imprimir esos tickets, usa **Descartar cola pendiente**. Sólo elimina intentos técnicos de impresión.
2. Si son **documentos técnicos**, son registros de comprobantes ya generados o impresos. Usa **Limpiar historial técnico** para retirarlos si no necesitas conservar esa referencia local.

Ninguna de las dos acciones elimina ventas, tickets, pagos, turnos, cortes ni movimientos de inventario.

El reporte no muestra contraseñas, tokens, cadenas de conexión ni claves privadas. Se puede copiar para enviarlo a soporte.

## Interpretación

- **Correcto:** la revisión terminó sin detectar problemas.
- **Aviso:** la operación puede continuar, pero conviene atender la recomendación.
- **Problema:** requiere atención antes de confiar en esa función.
- **Pendiente:** aún no está configurado o el módulo todavía no está habilitado.

## Alcance futuro

Recargas telefónicas, pagos de servicios y terminales de pago se muestran como pendientes hasta definir proveedores, credenciales, documentación vigente y pruebas reales. El diagnóstico no simula disponibilidad de esas integraciones.
