# Punto de retorno estable: JetVenta 3.2.11

## Estado publicado

La versión `3.2.11` queda identificada como punto de retorno estable para el
instalador actual de JetVenta en modalidad **monocaja**. El código funcional
está etiquetado en Git como `v3.2.11`.

Este documento no cambia el programa ni su número de versión. Registra el
alcance validado antes de iniciar cambios posteriores.

## Alcance de la instalación estable

- Un equipo Windows x64 opera como una sola caja local.
- El instalador instala en ese mismo equipo el cliente de escritorio, la API
  local y PostgreSQL.
- La base de datos, los turnos, la sesión de caja y la configuración se usan
  localmente en ese equipo.
- La impresora, lector de código de barras, cajón de dinero, báscula y demás
  periféricos se configuran por equipo.
- El instalador reconoce una instalación existente para actualizar JetVenta
  conservando los datos locales, en lugar de tratarla como una instalación
  inicial.

## Fuera de alcance por ahora

- Operación en varias cajas conectadas a una misma base de datos por red local.
- Instalador reducido para cajas adicionales conectadas a un servidor.
- Control de sesión por estación, emparejamiento de cajas y reserva de
  inventario entre estaciones.
- Actualizaciones automáticas remotas.

La modalidad multicaja se implementará como un módulo posterior y no debe
activarse parcialmente sobre esta instalación monocaja.

## Validación previa a cambios posteriores

Antes de declarar estable una nueva versión se debe comprobar, como mínimo:

1. Compilación sin errores y pruebas automatizadas aprobadas.
2. Inicio de API y cliente de escritorio.
3. Inicio de sesión, apertura de turno, creación de ticket, venta, cobro y
   cierre de turno.
4. Inventario, productos, promociones, clientes, créditos y permisos según el
   alcance de la versión.
5. Instalación limpia y actualización sobre una instalación existente.
6. Respaldo y restauración de la base de datos cuando el cambio afecte datos o
   migraciones.

Las pruebas físicas de cada periférico se conservan como validación final en
el equipo donde se usará: impresora de 56 u 80 mm, lector, cajón, báscula y
terminal de pago.

## Recuperación del código

Para inspeccionar o compilar el estado estable sin alterar las ramas de
trabajo, usar la etiqueta:

```powershell
git switch --detach v3.2.11
```

Para volver a la rama de desarrollo:

```powershell
git switch codex/recuperacion-2.3.2
```

No se deben eliminar las etiquetas ni los commits de versiones anteriores.
Cada versión posterior conservará su propio commit, documentación, etiqueta y
publicación en GitHub para poder regresar a un punto conocido.

## Identificación

- Versión de aplicación: `3.2.11`
- Etiqueta de código: `v3.2.11`
- Modalidad: instalador estable monocaja
- Fecha de registro: 2026-09-11
