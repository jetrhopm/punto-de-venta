# Punto de retorno estable: JetVenta 3.2.12 monocaja

## Estado

`3.2.12` es el punto de retorno estable más reciente de JetVenta antes del
renombre del ejecutable y antes de iniciar la implementación multicaja.

- Versión: `3.2.12`
- Etiqueta de código: `v3.2.12`
- Etiqueta de estabilidad: `stable-v3.2.12-monocaja`
- Modalidad: una caja local por computadora Windows x64.

## Garantías de este punto

- El instalador actual instala cliente, API local y PostgreSQL en el mismo
  equipo para operación monocaja.
- La aplicación de escritorio permite sólo una instancia de JetVenta por
  computadora. Un segundo inicio muestra un aviso y no crea otra sesión ni
  otra ventana de ventas.
- La API continúa como proceso separado del cliente de escritorio.
- No se activa ni se entrega todavía una configuración multicaja por red.

## Recuperación

Para revisar o reconstruir exactamente este punto sin modificar la rama de
trabajo:

```powershell
git switch --detach stable-v3.2.12-monocaja
```

El renombre posterior a `JetVenta.exe` se realizará en una versión nueva y no
modificará esta etiqueta, para que siempre exista un retorno funcional de
monocaja con el ejecutable anterior.
