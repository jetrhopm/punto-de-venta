# Iconos del programa

## Archivos fuente

Los iconos iniciales se prepararon desde las dos imagenes proporcionadas por el propietario del proyecto.

Ubicacion:

```text
src/Pos.Desktop/Assets/Icons/
```

Archivos principales:

- `app.ico`: icono principal de la aplicacion WPF.
- `sales.ico`: icono secundario para modulos de ventas, precios o reportes.
- `app-icon-source.png`: fuente PNG recortada del primer icono.
- `sales-icon-source.png`: fuente PNG recortada del segundo icono.
- `app-icon-16.png`, `app-icon-24.png`, `app-icon-32.png`, `app-icon-48.png`, `app-icon-64.png`, `app-icon-128.png`, `app-icon-256.png`: tamanos preparados para Windows.
- `sales-icon-16.png`, `sales-icon-24.png`, `sales-icon-32.png`, `sales-icon-48.png`, `sales-icon-64.png`, `sales-icon-128.png`, `sales-icon-256.png`: tamanos preparados para Windows.
- `app-icon-ai-source.png`: version limpia generada como referencia visual desde las imagenes originales.

## Criterio de preparacion

- Se elimino la barra de tareas visible en las imagenes originales.
- Se centro el recuadro blanco y el simbolo principal.
- Se generaron PNG cuadrados para usos internos y archivos `.ico` para Windows.
- El proyecto WPF usa `app.ico` como `ApplicationIcon`.
