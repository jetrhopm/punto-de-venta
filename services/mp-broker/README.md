# Intermediario OAuth de JetVenta para Mercado Pago

Servicio Node.js que permite a cada tienda conectar su cuenta de Mercado Pago solo
iniciando sesión y aceptando. Es el único lugar donde vive el **Client Secret** de la
aplicación de JetVenta. Los cobros **no** pasan por aquí: cada caja habla directo con
Mercado Pago.

- Dominio: `https://mp.gocentersuplementos.com`
- Node.js 20 o superior. Única dependencia: `mysql2`.
- Pruebas: `npm test` (no necesitan MySQL ni Mercado Pago).

## Endpoints

| Método | Ruta | Quién | Autenticación |
| --- | --- | --- | --- |
| POST | `/v1/installations/register` | JetVenta (una vez por tienda) | Ninguna, con límite de 5 por hora por IP |
| POST | `/v1/oauth/start` | JetVenta | Firma HMAC de la instalación |
| GET | `/v1/oauth/callback` | Navegador (regreso de Mercado Pago) | `state` de un solo uso |
| POST | `/v1/oauth/pickup` | JetVenta | Firma HMAC |
| POST | `/v1/oauth/refresh` | JetVenta | Firma HMAC |
| GET | `/v1/health` | Monitoreo | Ninguna |

Firma: `X-JV-Installation`, `X-JV-Timestamp` (segundos UNIX), `X-JV-Nonce` y
`X-JV-Signature` = HMAC-SHA256 hex con el secreto de la instalación sobre
`METHOD\nPATH\nTIMESTAMP\nNONCE\nSHA256(body)`. Se rechazan relojes con más de 5 minutos
de diferencia y nonces repetidos.

Los tokens que entrega Mercado Pago se guardan cifrados (AES-256-GCM) un máximo de 15
minutos, se entregan una sola vez a la instalación que inició la autorización y se borran.
La renovación no guarda nada.

## Despliegue en Hostinger (plan Business)

### 1. Subdominio y SSL
1. hPanel → **Dominios** → crear el subdominio `mp.gocentersuplementos.com`.
2. **Seguridad → SSL**: instalar el certificado gratuito para el subdominio.

### 2. Base de datos
1. hPanel → **Bases de datos → MySQL**: crear una base y un usuario con contraseña fuerte.
2. Abrir **phpMyAdmin** y ejecutar el contenido de `schema.sql`.

### 3. Aplicación Node.js
1. hPanel → **Sitios web → Agregar sitio → Node.js App** (o *Node.js* dentro del sitio) y
   elegir el subdominio.
2. Origen: repositorio de GitHub con **directorio raíz** `services/mp-broker`, o subir un
   `.zip` de esta carpeta **sin** `node_modules`.
3. Versión de Node: 20 o 22. Comando de instalación: `npm install --omit=dev`.
   Comando de inicio: `npm start`. Archivo de entrada: `src/server.js`.
4. **Variables de entorno** (ver `.env.example`):

| Variable | Valor |
| --- | --- |
| `MP_CLIENT_ID` | App ID de la aplicación JetVenta en Mercado Pago |
| `MP_CLIENT_SECRET` | Client Secret de producción |
| `MP_REDIRECT_URI` | `https://mp.gocentersuplementos.com/v1/oauth/callback` |
| `TOKEN_ENC_KEY` | 32 bytes aleatorios en base64 (ver abajo) |
| `DB_HOST`, `DB_PORT`, `DB_USER`, `DB_PASSWORD`, `DB_NAME` | Los de la base del paso 2 |
| `TRUST_PROXY` | `1` |
| `PORT` | El que indique Hostinger (si lo asigna) |

Generar `TOKEN_ENC_KEY` una sola vez y guardarla en un gestor de contraseñas:

```
node -e "console.log(require('crypto').randomBytes(32).toString('base64'))"
```

Si se pierde o se cambia `TOKEN_ENC_KEY`, las instalaciones registradas dejan de validar
su firma y deberán registrarse otra vez (JetVenta lo hace solo al pulsar "Autorizar cuenta").

5. Desplegar y abrir `https://mp.gocentersuplementos.com/v1/health`. Debe responder
   `{"ok":true}`.

### 4. Aplicación en Mercado Pago
1. [Tus integraciones](https://www.mercadopago.com.mx/developers/panel/app) → crear la
   aplicación "JetVenta" para pagos presenciales / Point, como integrador.
2. URL de redireccionamiento: `https://mp.gocentersuplementos.com/v1/oauth/callback`
   (exacta, igual que `MP_REDIRECT_URI`).
3. Activar **PKCE** y el permiso **offline_access** (necesario para el refresh token).
4. Activar credenciales de producción y copiar App ID y Client Secret a las variables
   de entorno. Nunca al repositorio.

### 5. JetVenta
En `appsettings.json` de la API: `"MercadoPago": { "BrokerUrl": "https://mp.gocentersuplementos.com/" }`.
Después, en **Configuración → Mercado Pago Point → Autorizar cuenta**.

## Operación
- Los registros (logs) no incluyen tokens, códigos ni secretos; sólo ids de instalación,
  de intento y de usuario de Mercado Pago.
- Los intentos terminados se borran a los 30 días; los nonces, a los 10 minutos.
- Para bloquear una instalación: `UPDATE installations SET revoked_at = <ms> WHERE id = '<id>';`
