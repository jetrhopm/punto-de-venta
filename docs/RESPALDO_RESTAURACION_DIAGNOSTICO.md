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

## Diagnostico

```powershell
.\scripts\diagnose.ps1
```

Revisa respuesta de PostgreSQL, `fsync`, `synchronous_commit`, `full_page_writes`, ultima migracion y espacio libre. No muestra contrasenas.

## Limites actuales de Fase 1

La programacion diaria, cifrado de copias fuera del equipo y restauracion desde el instalador de produccion quedan para el instalador y el servicio Windows. El formato de ticket implementado en esta fase es PDF guardado por el usuario, como impresora virtual; la integracion ESC/POS queda para una fase posterior.
