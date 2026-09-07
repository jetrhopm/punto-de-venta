# Control de versiones y recuperacion

## Estado actual

| Estado | Version | Commit o rama | Evidencia |
| --- | --- | --- | --- |
| Estable validada | 2.2.6 | `c6982d0` / `codex/rollback-ventas-estable` | El 6 de septiembre de 2026 se valido inicio de sesion, apertura de turno, F6, tickets, productos y ventas. Compila sin advertencias y las dos pruebas de tickets pasan. |
| Artefacto historico no reproducible | 2.6.2 | `artifacts/production/setup/Pos.Setup.exe` | Paquete completo creado el 6 de septiembre de 2026 a las 13:53. Su metadata indica 2.6.2, pero se genero con cambios locales sin commit: contiene el bloqueo de caja por otro usuario. Puede probarse como referencia, pero no se marca como estable ni permite recuperar su fuente exacta. SHA-256: `F6A1CB653FCFF60074CE569DCFAFD7F1E2DB1CA14229FC4F06A2AAFEA056E67F`. |
| No distribuir | 2.6.4 | `b9ebe5e` / `codex/respaldo-2.6.4-antes-rollback` | Contiene desarrollo posterior respaldado antes de la regresion: F6 intenta abrir turno y el flujo de tickets puede bloquear ventas. |

La version estable no se modifica directamente. Cualquier correccion parte de una rama nueva y solo reemplaza esta linea cuando el usuario valida el ejecutable local y las pruebas tecnicas terminan correctamente.

## Auditoria historica

| Rango | Estado de control | Cambios principales | Referencia |
| --- | --- | --- | --- |
| 2.0.0 | Historico local | Dialogos y atajos iniciales. | `b12f851` (objeto local recuperable). |
| 2.1.1 - 2.1.15 | Respaldado en Git | Licencia, restauracion, respaldos, pagos mixtos, tickets, impresion, demo, inicio automatico. | Commits entre `08d855d` y `bf30fc8`. |
| 2.2.0 - 2.2.6 | Respaldado en Git | Rediseño de instalador, identidad visual, descarte y conservacion de tickets. | Commits entre `239eac3` y `c6982d0`. |
| 2.3.0 | Artefactos locales parciales | Importacion y licencia. | Existen compilaciones locales de seguridad; no hay instalador completo publicable asociado a un commit inmutable. |
| 2.4.0 - 2.5.6 | Sin artefacto local ni punto inmutable | Alertas, permisos, conexion, redondeo, producto comun, granel y mejoras de venta. | No existe instalador, publicacion, commit ni etiqueta exactos; los cambios estan integrados dentro de `b9ebe5e`. |
| 2.6.0 - 2.6.4 | Regresion identificada | Catalogo y permisos; despues cambios en relevo de sesion, F6 y acceso a tickets. | Integrado dentro de `b9ebe5e`; no distribuir. |

La siguiente candidata de codigo se nombrara `2.5.7`, no `2.5.6`: se reconstruira desde el respaldo de desarrollo, retirando el flujo regresivo de `2.6.1` a `2.6.4` y conservando las mejoras comprobables de `2.5.x`. Antes de esa reconstruccion se validara el instalador local `2.6.2`, pues podria ser el ultimo paquete completo funcional previo a la regresion.

## Flujo obligatorio desde ahora

1. Crear una rama con el alcance de la modificacion desde la ultima etiqueta estable.
2. Incrementar la version en `Directory.Build.props` y registrar el cambio en `CHANGELOG.md` antes de compilar.
3. Ejecutar `scripts/verify-release.ps1` y corregir cualquier error.
4. Hacer un commit por cambio coherente y subirlo a GitHub en la misma sesion de trabajo.
5. Generar y abrir primero el ejecutable local de pruebas. No generar instalador hasta que se valide el flujo afectado.
6. Tras la validacion del usuario, crear una etiqueta inmutable `vX.Y.Z-validada-AAAAMMDD`, subirla a GitHub y solo entonces generar el instalador.
7. El instalador se genera desde el commit etiquetado. Nunca desde cambios sin commit.
8. La version mostrada por Windows no se considerara evidencia suficiente: debe coincidir con un commit y una etiqueta publicada en GitHub.

## Pruebas minimas de regresion para ventas

- Inicio de sesion con administrador y cajero.
- Apertura de turno con fondo inicial.
- F6 crea un ticket sin volver a solicitar el fondo.
- Agregar, editar y quitar una partida.
- Confirmar venta y comprobar ticket, inventario y movimiento de caja.
- Cerrar turno, volver al inicio de sesion y verificar que los tickets pendientes se conservan cuando corresponde.
- Reiniciar API y confirmar que la alerta indica la recuperacion sin perder el ticket.

## Recuperacion

- Para recuperar la estable actual: cambiar a `codex/rollback-ventas-estable` o a la etiqueta estable publicada.
- Para revisar desarrollo sin distribuir: cambiar a `codex/respaldo-2.6.4-antes-rollback`.
- No usar `reset --hard` sobre una rama con cambios. Primero se crea una rama de respaldo y se publica en GitHub.
