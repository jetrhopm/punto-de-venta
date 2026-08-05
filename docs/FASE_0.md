# Fase 0: descubrimiento y base tecnica

## Terminado en este incremento

- Se definio una matriz de paridad operativa basada en los flujos de eleventa.
- Se documento el modelo relacional inicial y sus restricciones de integridad.
- Se creo el primer nucleo de dominio: importes monetarios, permisos y borradores de venta.
- Se creo una ventana WPF de operacion con navegacion F1 a F4 y una accion F12 controlada.
- Se dejo documentado el arranque con SDK local, sin instalacion global de .NET.

## Como comprobarlo

```powershell
.\.tools\dotnet\dotnet.exe build .\PuntoDeVenta.slnx
.\.tools\dotnet\dotnet.exe test .\PuntoDeVenta.slnx --no-build
.\.tools\dotnet\dotnet.exe run --project .\src\Pos.Desktop\Pos.Desktop.csproj
```

En la ventana, F1, F2, F3 y F4 cambian de modulo. F12 informa que el cobro todavia no esta habilitado por seguridad.

## Pendientes inmediatos

- Preparar las primeras migraciones EF Core sobre PostgreSQL 18 portable.
- Implementar el caso de uso transaccional de primera configuracion sobre el esquema inicial.
- Crear el asistente transaccional de primera configuracion.
- Implementar autenticacion, usuarios, permisos y turno antes de habilitar una venta finalizada.

## Riesgos abiertos

- La disponibilidad y licencia del paquete portable oficial de PostgreSQL para desarrollo debe verificarse al automatizar la descarga.
- El redondeo de impuestos configurables requiere validacion fiscal antes de la Fase 1.
