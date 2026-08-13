# Instalador de producción

Generación:

```powershell
.\scripts\build-installer.ps1 -Version 2.3.0
```

Resultado:

```text
artifacts\installer\Setup.exe
```

El ejecutable es autocontenido y contiene el cliente, API, PostgreSQL y Visual C++. La interfaz activa es WinForms; los archivos WiX y WPF de prototipos anteriores no forman parte de la compilación actual.

Instalar, reparar o actualizar:

```powershell
.\Setup.exe
```

Desinstalar la aplicación, servicios y accesos conservando `C:\ProgramData\PuntoDeVenta`:

```powershell
.\Setup.exe /uninstall
```

Consulta `docs/INSTALADOR_2.md` para el flujo, registros y matriz de pruebas de liberación.
