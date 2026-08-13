# Instalador de produccion

El instalador actual es un unico `Setup.exe` autocontenido para Windows x64. Desde la version 2.0 ya no usa Burn, MSI ni una cadena de prerrequisitos. El paquete contiene el cliente WPF, la API, PostgreSQL portable, el script de servicios y Visual C++ Redistributable.

## Construccion

Desde la raiz del repositorio:

```powershell
$env:DOTNET_ROOT = (Resolve-Path .tools\dotnet).Path
& .\scripts\build-installer.ps1 -Version 2.0.0
```

El resultado es `artifacts\installer\Setup.exe`.

El proceso crea un `Payload.zip` temporal, lo incrusta dentro del ejecutable y lo elimina al terminar. No se debe copiar `Payload.zip` a la computadora del usuario.

## Funcionamiento visible

`Setup.exe` solicita elevacion una sola vez y muestra una consola con la carpeta de instalacion, el archivo que se extrae, porcentaje, archivos copiados, Visual C++, PostgreSQL y servicios.

Los registros quedan en:

```text
C:\ProgramData\PuntoDeVenta\logs\setup.log
C:\ProgramData\PuntoDeVenta\logs\instalacion.log
C:\ProgramData\PuntoDeVenta\logs\postgresql.log
```

La instalacion se hace en `C:\Program Files\Punto de Venta`. La base, secretos, logs y respaldos quedan en `C:\ProgramData\PuntoDeVenta`.

## Modos

Instalar o reparar archivos y servicios:

```powershell
.\Setup.exe
```

Eliminar solamente los servicios y conservar datos:

```powershell
.\Setup.exe /uninstall
```

La reinstalacion es idempotente: conserva el cluster PostgreSQL existente y no borra datos por defecto.

## Verificacion en una VM

Comprueba primero el hash que se publico:

```powershell
Get-FileHash .\Setup.exe -Algorithm SHA256
```

No uses el `Setup.exe` de versiones anteriores. La version 2.0 no debe mostrar `Setup Progress` de Burn ni `Archivos de Punto de Venta`; debe abrir su consola propia desde el primer momento.

Antes de declararlo liberado todavía se requiere probar instalación, reparación, actualización y desinstalación con conservación de datos en Windows 10 y Windows 11 x64 limpios.
