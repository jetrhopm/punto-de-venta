# JetVenta

Sistema de JetVenta de escritorio para Windows 10 y Windows 11 x64, desarrollado por fases verificables con C#, .NET 10 LTS, WPF, ASP.NET Core y PostgreSQL.

## Estado actual

La base técnica de Fase 0 y los módulos principales de Fase 1 y Fase 2 están implementados. El módulo F4 de inventario ya cuenta con catálogo paginado, valoración, alertas, movimientos, importación y exportación:

- Prompt maestro guardado en `PROMPT_MAESTRO.md`.
- Requisitos de desarrollo documentados en `docs/REQUISITOS_DESARROLLO.md`.
- Estructura inicial de solucion .NET creada.
- Iconos iniciales preparados en `src/Pos.Desktop/Assets/Icons/`.
- Nucleo de dominio inicial con dinero decimal, permisos y borradores de venta.
- Cascaron WPF navegable con atajos F1 a F4 y F12.
- Inventario operativo en F4 con páginas de 500 productos, filtros, ordenamiento, límites auditados, movimientos y exportación CSV.
- Importación opcional durante la configuración inicial y disponible posteriormente desde Inventario.

El avance verificable de esta fase se registra en `docs/FASE_0.md`.
El estado completo y los pendientes se mantienen en `docs/PLAN_PROYECTO.md`.

El respaldo, restauracion de prueba y diagnostico estan documentados en `docs/RESPALDO_RESTAURACION_DIAGNOSTICO.md`.
El alcance y la prueba manual de F4 están documentados en `docs/INVENTARIO.md`.

## Compilar

Usa el SDK local instalado en `.tools/dotnet`:

```powershell
.\.tools\dotnet\dotnet.exe build .\PuntoDeVenta.slnx
```

## Ejecutar en desarrollo

El SDK .NET 10 se mantiene dentro de `.tools` para no requerir una instalacion global.

```powershell
.\scripts\dev-up.ps1
```

Para iniciar solo el cliente WPF:

```powershell
.\.tools\dotnet\dotnet.exe run --project .\src\Pos.Desktop\Pos.Desktop.csproj
```

## Requisitos

Consulta `docs/REQUISITOS_DESARROLLO.md` para versiones, herramientas y estado de instalacion.

## Repositorio remoto

Repositorio GitHub configurado:

```text
https://github.com/jetrhopm/punto-de-venta
```

Los commits deben usar mensajes descriptivos en espanol.
## Diagnóstico y báscula

La configuración incluye `Báscula` para equipos RS-232 o USB que Windows exponga como puerto COM, y `Detalles del sistema` para consultar versión, equipo, API y dispositivos sin mostrar secretos. Consulta [docs/CONFIGURACION_BASCULA.md](docs/CONFIGURACION_BASCULA.md) antes de probar un modelo nuevo.
