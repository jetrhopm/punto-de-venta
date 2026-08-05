# Punto de Venta

Sistema de punto de venta de escritorio para Windows 10 y Windows 11 x64, desarrollado por fases verificables con C#, .NET 10 LTS, WPF, ASP.NET Core y PostgreSQL.

## Estado actual

Fase 0 iniciada:

- Prompt maestro guardado en `PROMPT_MAESTRO.md`.
- Requisitos de desarrollo documentados en `docs/REQUISITOS_DESARROLLO.md`.
- Estructura inicial de solucion .NET creada.
- Iconos iniciales preparados en `src/Pos.Desktop/Assets/Icons/`.
- Nucleo de dominio inicial con dinero decimal, permisos y borradores de venta.
- Cascaron WPF navegable con atajos F1 a F4 y F12.

El avance verificable de esta fase se registra en `docs/FASE_0.md`.
El estado completo y los pendientes se mantienen en `docs/PLAN_PROYECTO.md`.

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
