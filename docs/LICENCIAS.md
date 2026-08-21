# Licencias locales de JetVenta

JetVenta usa activación local firmada. El programa muestra un código de solicitud en `Configuración > Licencia`; el emisor autorizado lo pega en `JetVenta.LicenseIssuer.exe` y genera el archivo `licencia.jv`. La tienda carga ese archivo desde la misma pantalla.

## Seguridad y operación

- El archivo contiene la huella del equipo, nombre de tienda, identificador, fecha de emisión y, si se seleccionó, fecha de vencimiento.
- Cada archivo está firmado con ECDSA P-256. JetVenta solo contiene la llave pública y rechaza cualquier archivo alterado o emitido para otro equipo.
- Tras importarlo, JetVenta almacena la licencia cifrada con DPAPI de Windows en `C:\ProgramData\PuntoDeVenta\license`. Copiar la base de datos no copia una activación funcional hacia otra computadora.
- La llave privada del emisor vive fuera del repositorio y fuera del instalador. Nunca debe enviarse al cliente, copiarse a carpetas compartidas ni incluirse en respaldos de la tienda.
- Al desinstalar JetVenta se elimina la activación local. La desinstalación normal sigue conservando base de datos, configuración operativa y respaldos.

Una licencia local reduce el uso no autorizado, pero ningún control puramente local puede impedir de forma absoluta que alguien con control administrativo del equipo modifique binarios. Mantener la llave privada aislada, firmar ejecutables, validar en la API y, en una fase posterior, añadir validación remota eleva significativamente el costo de un parche.

## Emisor

Genera el emisor con:

```powershell
.\scripts\build-license-issuer.ps1
```

El resultado queda en `artifacts\license-issuer\JetVenta.LicenseIssuer.exe`. Esta herramienta solo funciona en la computadora autorizada que contiene la llave emisora protegida por Windows. No se incluye en `Setup.exe`.
