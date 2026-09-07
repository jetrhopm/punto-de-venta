# Plan de correcciones posteriores a pruebas 2.3.2

## Propósito

Este documento registra el estado de JetVenta antes de atender las observaciones capturadas en el plan de pruebas de usuario final 2.3.0 y en las notas operativas asociadas. Su objetivo es conservar trazabilidad, limitar el alcance de cada entrega y evitar regresiones en funciones que ya fueron verificadas.

## Evidencia de origen

- Plan de pruebas: F:\JetVenta-Plan-de-Pruebas-Usuario-Final-2.3.0.docx.
- Notas operativas: F:\notas para punto de venta.txt.
- Línea base de código: JetVenta 2.3.2.
- Validación automatizada de la línea base: 29 pruebas unitarias, 18 de escritorio y 15 de integración superadas.

## Alcance de pruebas

Las secciones 1 a 7 del plan fueron revisadas por el usuario. Las observaciones documentadas en esas secciones forman el alcance de esta corrección.

Las secciones 8 a 16, desde Clientes y créditos hasta el cierre final de diseño y usabilidad, siguen pendientes de prueba de usuario. No se considerarán defectos confirmados hasta que se ejecuten; sólo se modificarán cuando dependan directamente de una corrección aprobada de las secciones 1 a 7.

La corrección de F10 ya está incluida en la línea base 2.3.2. Debe volver a validarse junto con las demás teclas de función cuando se modifique el enrutamiento global de atajos.

## Política de versión

- Una corrección pequeña y localizada incrementa el último número: 2.3.2 a 2.3.3.
- Un cambio importante de flujo, módulo o experiencia incrementa el número central: 2.3.2 a 2.4.0.
- El primer número se reserva para un hito mayor o cambio incompatible explícitamente aprobado.
- Cada compilación de pruebas y cada instalador indicarán la versión exacta que contienen.

## Observaciones confirmadas

### Inicio, licencia y recuperación

- El selector de usuario no coloca la cuenta elegida en el campo de inicio de sesión.
- La demo no se explica ni muestra el acceso a activación antes de vencer; al vencer, el aviso debe aparecer antes de intentar iniciar sesión.
- La caída y recuperación de API necesitan avisos visibles, confirmación de éxito y una advertencia antes de abrir PowerShell.
- La validación de una conexión configurada debe comprobar la dirección y puerto elegidos, no sólo una respuesta local existente.

### Configuración y permisos

- Las confirmaciones de guardado deben indicar qué cambio se aplicó, renovarse con cada operación y cerrar el diálogo cuando sea correcto.
- El redondeo debe verse en el total de venta, el cobro y el ticket, no únicamente al validar el efectivo recibido.
- Diagnóstico debe explicar la cola de documentos y permitir limpiarla de forma segura cuando proceda.
- Los permisos deben evaluarse al ejecutar la acción elegida. En particular, registrar un producto y vender un producto común son acciones distintas.
- La autorización temporal debe explicar credenciales inválidas y permisos insuficientes.

### Productos, inventario y venta

- Productos F3 necesita formularios separados para agregar y editar, con control de permiso de modificación.
- El alta y edición requieren existencia inicial, inventario mínimo y máximo.
- Menudeo y mayoreo requieren diferenciación visual y textual.
- Los errores de validación, códigos duplicados y motivos de ajuste demasiado largos requieren mensajes legibles y específicos.
- Inventario F4 debe mostrar el departamento asignado.
- Eliminar un producto significa retirarlo de la venta de forma lógica, conservando tickets, devoluciones, reportes e historial.
- La venta requiere edición de cantidad por línea, con valores decimales para granel y recálculo de importes.
- La búsqueda debe permitir flechas, Enter y doble clic para agregar el resultado correcto.
- Producto común requiere una captura propia de nombre, unidad, cantidad y precio unitario.
- Los productos por peso o granel deben solicitar cantidad antes de agregarse.
- Todas las teclas de F1 a F10 se revisarán como un solo flujo para evitar interferencia entre diálogos, permisos y atajos.

### Compras e impresión

- Recibir mercancía debe permitir actualizar costo, precio de venta y margen calculado.
- La impresión debe ser una elección explícita. Sin impresora configurada o con impresión desactivada, la venta debe confirmarse sin intentar generar PDF ni enviar trabajos.

## Secuencia de implementación

1. Preparar utilidades de interacción reutilizables: resultados claros, renovación de estado, atajos de diálogo y autorización temporal con errores explicados. Completado en 2.4.0 a 2.4.2; el diseño visual de alertas quedó aplicado con recursos de JetVenta y los rechazos de autorización distinguen sus causas mediante una alerta visible.
2. Corregir inicio, demo, activación y recuperación de servicios. Completado en 2.5.1: la demo y activación quedan disponibles antes del inicio de sesión; reparar servicios pide confirmación, explica la ventana elevada de Windows, confirma el resultado y Configurar conexión valida API y base de datos antes de guardar una dirección.
3. Corregir venta: cantidades, búsqueda, producto común, producto inexistente, granel y atajos.
4. Reestructurar Productos F3, Inventario F4 y recepción de compras.
5. Ajustar impresión, diagnóstico y redondeo. Completado en 2.5.2: el total redondeado se consulta desde la regla del servidor antes del cobro, las configuraciones confirman sus cambios y Diagnóstico permite cancelar solicitudes pendientes de impresión sin alterar ventas ni tickets.
6. Aplicar el diseño final de botones y alertas según la maqueta aprobada.
7. Ejecutar regresión de secciones 1 a 7, actualizar el plan de pruebas y publicar instalador sólo después de validar la compilación local.

## Reglas de seguridad y regresión

- Ninguna corrección debe borrar productos con movimientos, ventas, tickets o devoluciones históricas.
- Cada cambio de base de datos requiere migración, prueba de actualización y prueba de instalación limpia.
- Ninguna acción de limpieza de cola o impresión puede modificar ventas confirmadas.
- Antes de cada nueva versión se compilan solución, pruebas unitarias, pruebas de escritorio e integración.
- El instalador se genera solamente después de aprobar la versión de pruebas local.

## Decisiones pendientes

- Confirmar el atajo de Producto común: Ctrl+1 actualmente descarta el ticket; Ctrl+P está disponible para Producto común.
- Confirmar si el aviso de demo con espera de 30 segundos debe aparecer en cada apertura o una vez por día.
- La maqueta pendiente definirá la apariencia exacta de alertas y solicitudes de autorización.

## Decisiones confirmadas

- Ctrl+1 conserva el descarte de ticket y Ctrl+P conserva Producto común.
- El aviso de demo se mostrará en cada apertura mientras no exista una licencia válida.
- Las alertas seguirán la paleta y los recursos de JetVenta, con jerarquía por estado y sin alterar la lógica de cada operación.
