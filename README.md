# Punto de Venta

Sistema de punto de venta de escritorio para Windows 10 y Windows 11 x64, desarrollado por fases verificables con C#, .NET 10 LTS, WPF, ASP.NET Core y PostgreSQL.

## Estado actual

Fase 0 iniciada:

- Prompt maestro guardado en `PROMPT_MAESTRO.md`.
- Requisitos de desarrollo documentados en `docs/REQUISITOS_DESARROLLO.md`.
- Estructura inicial de solucion .NET creada.
- Iconos iniciales preparados en `src/Pos.Desktop/Assets/Icons/`.

## Compilar

Usa el SDK local instalado en `.tools/dotnet`:

```powershell
.\.tools\dotnet\dotnet.exe build .\PuntoDeVenta.slnx
```

## Requisitos

Consulta `docs/REQUISITOS_DESARROLLO.md` para versiones, herramientas y estado de instalacion.

## Repositorio remoto

Repositorio GitHub configurado:

```text
https://github.com/jetrhopm/punto-de-venta
```

Los commits deben usar mensajes descriptivos en espanol.
