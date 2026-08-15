# Respaldo, restauracion y diagnostico

## Respaldo

Con PostgreSQL de desarrollo iniciado, ejecuta:

```powershell
.\scripts\backup-database.ps1
```

El script crea un `.dump` en `artifacts/backups` y un manifiesto JSON con fecha, tamano y SHA-256. El respaldo no reemplaza una copia externa: para una instalacion real debe copiarse a otro disco o ubicacion de red protegida.

## Restauracion de prueba

La restauracion usa por defecto una base temporal llamada `punto_venta_restore_test` y requiere confirmacion explicita:

```powershell
.\scripts\restore-database.ps1 -BackupFile .\artifacts\backups\archivo.dump -Confirm
```

No se debe apuntar a la base activa sin un procedimiento de mantenimiento y una copia previa. El script termina mostrando el numero de productos restaurados.

## Restauracion en otra computadora

1. En la computadora anterior, crea el respaldo desde **Configuracion > Respaldos** y usa **Guardar copia externa**. Conserva juntos `archivo.dump` y `archivo.dump.sha256`.
2. Instala JetVenta en la computadora nueva y deja que complete la configuración técnica.
3. Cierra JetVenta. Abre PowerShell como administrador y ejecuta:

```powershell
& "C:\Program Files\JetVenta\restore-production-backup.ps1" -BackupFile "E:\archivo.dump" -Approve
```

El script valida el checksum antes de cambiar nada, hace una copia preventiva de la base de destino en `C:\ProgramData\PuntoDeVenta\backups`, sustituye solo la base de datos JetVenta y reinicia la API. El contenido de la base, incluidos usuarios, permisos, productos, clientes, proveedores, compras, ventas, turnos, cortes y movimientos, queda restaurado. La impresora se configura nuevamente en el equipo destino.

## Diagnostico

```powershell
.\scripts\diagnose.ps1
```

Revisa respuesta de PostgreSQL, `fsync`, `synchronous_commit`, `full_page_writes`, ultima migracion y espacio libre. No muestra contrasenas.

## Limites actuales de Fase 1

La programacion diaria, cifrado de copias fuera del equipo y restauracion desde el instalador de produccion quedan para el instalador y el servicio Windows. El formato de ticket implementado en esta fase es PDF guardado por el usuario, como impresora virtual; la integracion ESC/POS queda para una fase posterior.
