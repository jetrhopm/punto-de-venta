# Control de cambios desde JetVenta 2.3.2

## Base funcional protegida

La version 2.3.2 es la base validada para tickets y ventas. No se incorporan cambios de la linea 2.6.x en esta rama.

- Rama: `codex/recuperacion-2.3.2`
- Etiqueta: `v2.3.2-recuperada-20260906`
- Commit: `49dd68df6a62d15861a9fc3757e33102e52b73bf`
- Validacion manual realizada: iniciar sesion, abrir turno, crear tickets con F6, agregar productos y confirmar ventas.
- Validacion automatizada: 62 pruebas aprobadas al recuperar la base.

## Regla de versionado

- Correccion pequena y localizada: aumenta el tercer numero, por ejemplo `2.3.3`.
- Cambio funcional amplio, ya validado y cerrado: aumenta el segundo numero, por ejemplo `2.4.0`.
- No se reutiliza una etiqueta publicada ni se modifica una version ya validada.
- El instalador se genera solamente despues de validar el ejecutable local de la version.

## Requisitos de cada version

Antes de modificar logica se debe registrar el bloque de trabajo en este directorio o en `docs/releases/` con:

1. Version objetivo, problema, modulos y flujos afectados.
2. Riesgos funcionales, especialmente para ventas, tickets, caja, inventario, respaldos y licencia.
3. Criterios de aceptacion verificables.
4. Pruebas automaticas y pruebas manuales de regresion requeridas.

Al terminar cada version se debe:

1. Actualizar `CHANGELOG.md` con comportamiento corregido y limitaciones conocidas.
2. Crear una nota de version en `docs/releases/` con archivos modificados, flujos probados y resultado.
3. Compilar la solucion y ejecutar las pruebas automatizadas.
4. Abrir el ejecutable local para validacion manual.
5. Crear un commit cuyo asunto inicie con la version, por ejemplo `v2.3.3 Corrige selector de usuarios`.
6. Crear una etiqueta Git con esa version y subir commit y etiqueta a GitHub.
7. Generar instalador solamente despues de aprobacion expresa del usuario.

## Regresion obligatoria

Estas pruebas se ejecutan antes de considerar estable cualquier version que toque interfaz, sesion, permisos, ventas, caja o API:

1. API local responde y el inicio llega al login.
2. Login valida usuario y contrasena correctos e incorrectos.
3. Abrir turno o caja con fondo inicial cuando aplique.
4. Crear ticket con F6.
5. Agregar producto por busqueda y por codigo, y verificar linea, importe y total.
6. Cambiar entre tickets y descartar solamente el ticket seleccionado.
7. Cobrar una venta de prueba sin duplicarla.
8. Cerrar programa con turno abierto y confirmar que se muestra el flujo de cierre esperado.
9. Reiniciar la aplicacion y confirmar que los tickets pendientes del usuario siguen disponibles cuando corresponde.

## Bloques de trabajo aprobados

### Bloque A: acceso, demo, permisos y comunicacion

- Selector de usuario, aviso de demo, activacion de licencia y vencimiento.
- Autorizaciones temporales con mensajes claros para credencial invalida y permiso insuficiente.
- Alertas comprensibles ante caida de API, reparacion de servicios y diagnostico.

### Bloque B: ventas y calculos visibles

- Redondeo visible en venta, cobro y ticket.
- Cantidades editables, incluido granel decimal.
- Producto comun temporal y producto inexistente.
- Busqueda con flechas, Enter, doble clic y revision completa de F1 a F10.
- Modo sin impresora.

### Bloque C: catalogo, inventario y compras

- Formularios separados para alta y edicion de Productos F3.
- Existencia inicial, minimo, maximo, departamento, mayoreo y mensajes de validacion claros.
- Baja logica de productos sin borrar historial.
- Ajustes de inventario con motivo validado.
- Recepcion de compras que actualiza costo, precio y margen cuando se confirme.

### Bloque D: diseno transversal

- Alertas de exito, informacion, advertencia y error consistentes con JetVenta.
- Cancelar rojo, Guardar o Aceptar verde, Anterior amarillo y Siguiente azul.
- Enter confirma acciones validas, Esc cancela, y los dialogos no se minimizan ni se ocultan.

## Hallazgos pendientes registrados

| Prueba | Problema a corregir | Bloque |
| --- | --- | --- |
| 2.5 | El selector de usuarios no llena el campo al elegir una cuenta. | A |
| 2.7, 2.8, 2.11 | Faltan aviso claro de demo al abrir, acceso de activacion y aviso inmediato de vencimiento. | A |
| 4.4 | Las confirmaciones de configuracion son planas y conservan estados anteriores. | D |
| 4.6 | El redondeo se cobra internamente, pero no se refleja en venta, cobro ni ticket. | B |
| 5.7 | Producto comun solicita permiso de registro antes de elegir entre temporal o permanente. | B |
| 5.8, 5.10, 5.11 | La autorizacion temporal no explica credenciales invalidas ni falta de permiso. | A |
| 6.2, 6.4, 6.5 | Faltan existencia inicial, minimos y maximos; la edicion debe incluir todos los campos. | C |
| 6.3 | Mayoreo y menudeo no se distinguen visualmente. | C |
| 6.6 | Los codigos duplicados muestran JSON tecnico en lugar de un mensaje. | C |
| 6.7 | La baja de producto debe conservar ventas, historial y reportes. | C |
| 6.8 | Inventario F4 no muestra departamento. | C |
| 6.9, 6.10 | El motivo de ajuste requiere validacion de longitud y mensajes correctos. | C |
| 6.11 | F9 es intermitente; F1 a F10 requieren revision conjunta. | B |

## Observaciones de flujo pendientes

- Configuracion debe indicar cuando un cambio requiere reiniciar y cerrar el dialogo cuando el guardado sea correcto.
- Servicios debe advertir antes de abrir PowerShell, informar claramente caidas de API y confirmar que una reparacion termino.
- Diagnostico debe explicar los documentos en cola y permitir limpiarlos sin afectar ventas cuando sea seguro.
- Productos F3 mantiene una lista principal; Agregar producto y Editar se abren como formularios separados y revisan permisos al ejecutar la accion.
- Producto temporal permite codigo opcional; el producto permanente exige codigo y entra al catalogo.
- Producto comun tiene nombre, unidad, cantidad decimal y precio unitario en una ventana propia.
- Recibir compra puede actualizar costo, precio de venta y margen calculado despues de confirmacion.
- Sin impresora configurada, una venta se confirma sin error, sin enviar PDF ni ofrecer impresion inexistente.

## Criterio para volver atras

Se debe volver a la ultima etiqueta validada cuando falle cualquiera de estas condiciones:

- No se puede crear ticket, agregar producto, cobrar o cerrar de forma controlada.
- Una venta, movimiento de caja, inventario o respaldo puede duplicarse, perderse o quedar en estado incierto.
- La aplicacion deja la sesion o los permisos en un estado que impide continuar una operacion normal.

La etiqueta estable anterior se conserva intacta. La correccion se realiza en una rama nueva; no se fuerza ni se reescribe el historial publicado.
