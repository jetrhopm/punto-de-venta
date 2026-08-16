# Diagnóstico de JetVenta

El diagnóstico se abre desde **Configuración > Diagnóstico** y requiere el permiso de configuración de la tienda o una cuenta administradora.

## Revisiones actuales

- Disponibilidad de PostgreSQL y lectura del esquema.
- Migraciones pendientes.
- Tienda configurada.
- Productos, usuarios, clientes, proveedores y ventas confirmadas.
- Tickets abiertos con productos que pueden recuperarse. Los borradores vacíos no se consideran pendientes.
- Cola de impresión pendiente.
- Respaldo local más reciente y validación de su SHA-256.
- Número de respaldos y espacio libre del equipo servidor.
- Impresoras instaladas en Windows y la impresora seleccionada para esta caja.

Las ventas cobradas con **Cobrar sin imprimir** no crean trabajos de impresión. Las ventas cobradas e impresas sí dejan un trabajo hasta que el sistema confirma que fue enviado a la impresora.

El reporte no muestra contraseñas, tokens, cadenas de conexión ni claves privadas. Se puede copiar para enviarlo a soporte.

## Interpretación

- **Correcto:** la revisión terminó sin detectar problemas.
- **Aviso:** la operación puede continuar, pero conviene atender la recomendación.
- **Problema:** requiere atención antes de confiar en esa función.
- **Pendiente:** aún no está configurado o el módulo todavía no está habilitado.

## Alcance futuro

Recargas telefónicas, pagos de servicios y terminales de pago se muestran como pendientes hasta definir proveedores, credenciales, documentación vigente y pruebas reales. El diagnóstico no simula disponibilidad de esas integraciones.
