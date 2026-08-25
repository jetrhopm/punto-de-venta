# Administracion local

Este documento describe las opciones disponibles en **Configuracion** antes de habilitar la operacion multicaja.

## Datos de la tienda

Permite guardar nombre comercial, giro, razon social, RFC, direccion, telefono y zona horaria. Requiere el permiso `ConfigureStore`; la API vuelve a comprobarlo aunque se invoque directamente.

## Usuarios y permisos

Un administrador puede crear administradores o cajeros, activar o desactivar usuarios, restablecer contrasenas y asignar permisos individuales. Nunca se puede desactivar al ultimo administrador activo.

Un cajero con `ManageUsers` puede administrar otros cajeros. No puede crear, desactivar, cambiar la contrasena ni modificar permisos de un administrador.

Las contrasenas se procesan con `PasswordHasher` de ASP.NET Core y no se guardan ni registran como texto plano.

## Impresora de esta caja

La pantalla consulta las colas instaladas en Windows, guarda la impresora elegida en la configuracion local de la caja y permite enviar una pagina de prueba mediante el controlador de Windows. La seleccion es local: otra caja puede usar una impresora distinta.

La prueba confirma que Windows acepta el trabajo. La validacion fisica de papel, corte y cajon debe realizarse con cada modelo real.

## Respaldos

La API ejecuta el `pg_dump.exe` incluido con PostgreSQL y guarda un respaldo en formato personalizado dentro de `C:\ProgramData\PuntoDeVenta\backups`. Cada respaldo incluye:

- archivo `.dump`;
- checksum SHA-256 en `.sha256`;
- manifiesto JSON con fecha y tamano.

La API crea una copia automática diaria sin interrumpir al cajero y mantiene las cinco copias locales más recientes. También crea una copia antes de importar inventario. No depende del cierre del programa: un apagón o cierre forzado puede impedir ese evento, mientras que PostgreSQL conserva las transacciones ya confirmadas.

Desde la pantalla se puede crear un respaldo y guardar una copia en disco externo o ubicacion de red. La copia externa lleva el `.dump` y su archivo vecino `.dump.sha256`; ambos deben conservarse juntos. El permiso requerido es `ImportOrExportData`.

La restauración se ejecuta desde **Configuración > Respaldos > Cargar respaldo**. El usuario selecciona el archivo `.dump`; JetVenta exige que el archivo `.dump.sha256` esté junto a él, valida el SHA-256 y solicita confirmación. Después ejecuta el restaurador protegido con permisos de Windows, crea una copia preventiva de la base de destino, reemplaza la base y reinicia la API. La impresora se selecciona de nuevo porque es una preferencia local de cada caja. El comando PowerShell incluido queda como herramienta técnica de recuperación, no como requisito para el usuario común.

Desde la misma pantalla existe **Limpiar datos**. Esta acción requiere una cuenta administradora y dos confirmaciones, crea primero un respaldo preventivo y elimina los datos de operación para iniciar una tienda limpia. Conserva el nombre y la configuración de la tienda, la cuenta administradora, la licencia local y todos los respaldos. Para recuperar los datos eliminados se debe cargar el respaldo preventivo.

## Importacion de productos e inventario

Se admiten `.xlsx`, `.csv` y `.txt`. El lector reconoce UTF-8 y Windows-1252, detecta el separador CSV y conserva codigos con ceros iniciales cuando el archivo los contiene como texto.

Columnas reconocidas del archivo real exportado por eleventa:

| Eleventa | Destino |
| --- | --- |
| Codigo | Codigo de producto |
| Producto | Descripcion |
| P. Costo | Costo |
| P. Venta | Precio de venta |
| P. Mayoreo | Precio de mayoreo |
| Departamento | Categoria |
| Existencia | Existencia actual mediante movimiento de inventario |
| Inv. Minimo | Inventario minimo |
| Inv. Maximo | Inventario maximo |
| Tipo de Venta | Unidad o tipo de venta |
| Proveedor | Proveedor principal |

Eleventa no incluye en ese archivo la cantidad minima para aplicar mayoreo. La pantalla la solicita y la muestra antes de importar; no se inventa silenciosamente.

Antes de escribir se valida todo el archivo, se muestran hasta 1000 filas de vista previa y se exige corregir todos los errores. Al confirmar:

1. se crea un respaldo verificable;
2. la API procesa la importacion en una transaccion;
3. la existencia cambia mediante kardex, nunca editando el historial;
4. `operation_id` impide repetir la misma importacion;
5. se genera un reporte CSV del resultado;
6. el archivo original permanece intacto.

La regla de duplicados puede omitir productos existentes o actualizar sus datos. Los codigos repetidos dentro del mismo archivo se rechazan.

## Comprobacion de desarrollo

```powershell
dotnet build PuntoDeVenta.slnx
dotnet test tests\Pos.UnitTests\Pos.UnitTests.csproj
dotnet test tests\Pos.DesktopTests\Pos.DesktopTests.csproj
dotnet test tests\Pos.IntegrationTests\Pos.IntegrationTests.csproj
```

Las pruebas de integracion requieren el PostgreSQL de desarrollo iniciado. Incluyen migracion, importacion idempotente, kardex, proveedor principal y creacion real de un respaldo con checksum.
