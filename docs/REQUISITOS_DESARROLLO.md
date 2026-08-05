# Requisitos de desarrollo

Este documento registra las herramientas necesarias para editar, compilar, probar y mantener el sistema de punto de venta.

## Herramientas requeridas

| Herramienta | Version objetivo | Uso | Estado en esta maquina |
| --- | --- | --- | --- |
| Git | 2.54 o superior | Control de versiones y publicacion en GitHub | Instalado: `git version 2.54.0.windows.1` |
| .NET SDK | .NET 10 LTS x64 | Compilar WPF, API ASP.NET Core, librerias y pruebas | Instalado localmente en `.tools/dotnet`, version `10.0.302` |
| PostgreSQL | 18.x x64 para desarrollo inicial | Base de datos transaccional real para pruebas de integracion | Pendiente de instalacion global; `psql` no esta en PATH |
| PowerShell | 5.1 o 7.x | Scripts de desarrollo | Disponible por Windows |
| Visual Studio 2026 o Rider | Compatible con .NET 10 y WPF | Edicion visual y depuracion de escritorio | Opcional para este arranque |

## Uso de .NET local

La instalacion local se hizo con el script oficial `dotnet-install.ps1` y queda fuera de Git por `.gitignore`.

Comando de verificacion:

```powershell
.\.tools\dotnet\dotnet.exe --list-sdks
```

Para compilar sin instalar .NET global:

```powershell
.\.tools\dotnet\dotnet.exe build .\PuntoDeVenta.slnx
```

## PostgreSQL

Para desarrollo se usara PostgreSQL real, no SQLite ni base en memoria, en pruebas de integracion transaccionales.

Estado actual:

- `psql` no esta disponible en PATH.
- El intento de instalacion silenciosa con `winget install --id PostgreSQL.PostgreSQL.18` no termino dentro de la sesion.
- La estrategia preferida del proyecto sera descargar binarios ZIP oficiales de PostgreSQL para una instancia aislada en `.postgres/`, iniciada por scripts `dev-setup.ps1` y `dev-up.ps1`, sin mezclar datos de desarrollo y produccion.

Instalacion manual alternativa si se requiere de inmediato:

```powershell
winget install --id PostgreSQL.PostgreSQL.18 --exact --accept-source-agreements --accept-package-agreements
```

Si el instalador pide permisos de administrador, debe ejecutarse desde una terminal elevada. No instales pgAdmin ni StackBuilder como requisito del punto de venta.

## Reglas de Git y GitHub

Los commits y PRs deben tener texto descriptivo en espanol, corto y real. Ejemplos validos:

- `Documenta requisitos de desarrollo`
- `Crea estructura inicial de la solucion`
- `Agrega iconos iniciales del punto de venta`
- `Implementa modulo de Taecel`

Evita mensajes que no expliquen el cambio, por ejemplo `update`, `cambios`, `fase1`, `abc123` o solo numeracion.

## Fuentes revisadas

Revisado el 2026-08-05:

- .NET 10 aparece como LTS activo, con soporte hasta noviembre de 2028, segun la politica oficial de soporte de .NET.
- La descarga oficial de .NET 10 publica SDK 10.0.302 para Windows x64.
- PostgreSQL documenta PostgreSQL 18 como version actual estable y ofrece instaladores/binarios Windows certificados por EDB desde la pagina oficial de PostgreSQL.

Fuentes:

- https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core
- https://dotnet.microsoft.com/en-us/download/dotnet/10.0
- https://www.postgresql.org/download/windows/
- https://www.postgresql.org/docs/current/install-binaries.html
