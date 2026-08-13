# Iconografia de la aplicacion

## Biblioteca seleccionada

La interfaz WPF usa `MahApps.Metro.IconPacks.Material` version 6.2.1. El paquete ofrece controles vectoriales nativos para WPF, por lo que los iconos conservan nitidez en diferentes escalas de Windows y no requieren archivos sueltos por cada boton.

- Paquete: https://www.nuget.org/packages/MahApps.Metro.IconPacks.Material/
- Proyecto: https://github.com/MahApps/MahApps.Metro.IconPacks
- Licencia: MIT.
- Fecha de revision: 2026-08-13.

La licencia MIT permite uso, modificacion y distribucion comercial conservando el aviso de licencia correspondiente. La dependencia permanece versionada de forma central en `Directory.Packages.props`.

## Criterios de uso

- Los iconos acompanan comandos existentes; no sustituyen nombres o atajos importantes para el cajero.
- Verde se reserva para confirmar cobros o resultados correctos.
- Rojo se reserva para cancelar, salir o advertir riesgo.
- Azul identifica navegacion y acciones principales.
- Amarillo y morado distinguen devoluciones, kits, vinculacion y funciones secundarias.
- Los iconos de marca del programa siguen siendo los PNG e ICO propios ubicados en `src/Pos.Desktop/Assets/Icons`.

## Mantenimiento

Antes de actualizar el paquete se debe comprobar compatibilidad con la version vigente de .NET/WPF, licencia, vulnerabilidades y compilacion autocontenida `win-x64`. Los nombres `Kind` usados por XAML deben validarse durante la compilacion porque pueden cambiar entre versiones mayores.
