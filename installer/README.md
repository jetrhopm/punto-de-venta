# Instalador de producción

Generación:

```powershell
.\scripts\build-installer.ps1 -Version 3.4.0.0
```

Resultado:

```text
artifacts\installer\Setup.exe
```

El ejecutable es autocontenido y contiene el cliente, API, PostgreSQL y Visual C++. La interfaz activa es WinForms; los archivos WiX y WPF de prototipos anteriores no forman parte de la compilación actual.

## Detección de actualización

Al iniciar, el instalador revisa `C:\Program Files\JetVenta`, la base local en `C:\ProgramData\PuntoDeVenta` y el registro de desinstalación de Windows. Si encuentra una instalación válida, cambia la acción a **Actualizar** y muestra la versión detectada.

Durante una actualización no descomprime todo el paquete a una carpeta temporal ni reinstala dependencias válidas. Visual C++ se conserva si ya está instalado; los archivos de JetVenta se comparan mediante SHA-256 y solo se copian los nuevos o modificados. PostgreSQL, la configuración, la base de datos, los servicios y los respaldos se conservan; el script de producción aplica únicamente las migraciones pendientes.

El Setup detecta la modalidad activa en la caja principal. Si JetVenta Desktop ya está licenciado, permite seleccionar únicamente una licencia firmada Desktop + Móvil para actualizar la modalidad. La licencia elegida se valida para este equipo y se guarda protegida por Windows; si no se selecciona otra, la existente permanece intacta. Las cajas adicionales heredan la licencia del servidor y no permiten sustituirla localmente.

Si faltan archivos críticos o la instalación está incompleta, el mismo flujo funciona como reparación y vuelve a colocar los componentes faltantes sin borrar los datos.

Instalar, reparar o actualizar:

```powershell
.\Setup.exe
```

Desinstalar la aplicación, servicios y accesos conservando `C:\ProgramData\PuntoDeVenta`:

```powershell
.\Setup.exe /uninstall
```

Consulta `docs/INSTALADOR_2.md` para el flujo, registros y matriz de pruebas de liberación.
