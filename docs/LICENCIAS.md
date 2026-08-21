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

## Continuidad del emisor

El emisor incluye dos mecanismos para no depender de una única computadora:

1. **Autorizar otro emisor.** En la computadora nueva se crea una solicitud. Un emisor existente crea un archivo `.jvissuer` firmado y cifrado para esa computadora. Al importarlo, la llave queda protegida con DPAPI para el usuario local y se elimina la llave temporal de solicitud.
2. **Recuperación de emergencia.** Un emisor autorizado crea un archivo `.jvrecovery`, cifrado con una contraseña de al menos 16 caracteres mediante AES-GCM y PBKDF2-SHA512. Ese archivo debe guardarse en un medio externo cifrado y separado de su contraseña.

No copies manualmente `issuer-private-key.bin`. El archivo está protegido por el perfil de Windows del emisor y no funciona en otro usuario o equipo. Tampoco guardes archivos `.jvissuer`, `.jvrecovery` ni sus contraseñas junto con los respaldos normales de una tienda.

Flujo recomendado:

1. Autoriza una segunda computadora emisora y pruébala creando una licencia de prueba.
2. Crea un respaldo `.jvrecovery` y comprueba su importación en una computadora de contingencia controlada.
3. Conserva el respaldo y la contraseña en custodias separadas.
4. Si se pierde un emisor, recupera o autoriza otro antes de emitir nuevas licencias.
