# Manual de hosting y dominio de JetVenta (intermediario de Mercado Pago)

Última revisión: 2026-10-11 · Aplica a JetVenta 3.4.0.0+ y `services/mp-broker` 1.0.0

Este manual explica cómo montar desde cero, mover o cambiar de dominio el único componente
de JetVenta que vive en Internet: el **intermediario OAuth de Mercado Pago**
(`services/mp-broker`). También reúne los errores que ya tuvimos y cómo resolverlos.

> **Nunca pongas valores reales** (Client Secret, `TOKEN_ENC_KEY`, contraseñas) en este
> documento, en el repositorio ni en chats. Viven solo en el panel de Hostinger y en tu
> gestor de contraseñas.

---

## 1. Qué vive dónde

| Pieza | Dónde corre | Qué hace |
| --- | --- | --- |
| **JetVenta** (cliente WPF + API local + PostgreSQL) | En cada tienda, en Windows | Ventas, inventario y caja. La API es el servicio de Windows `PuntoDeVentaApi`. |
| **Intermediario `mp-broker`** (Node.js + MySQL) | Hostinger, en `https://mp.gocentersuplementos.com.mx` | Guarda el **Client Secret** de Mercado Pago y conecta la cuenta de cada tienda con "Autorizar cuenta". |
| **Mercado Pago** | Internet | Cobros con la terminal Point. **Los cobros no pasan por el intermediario**: cada caja habla directo con `api.mercadopago.com`. |

Si el intermediario se cae, **las tiendas siguen cobrando** con su token vigente (dura 180 días
y se renueva cuando faltan 30). Lo que deja de funcionar es "Autorizar cuenta" y la renovación.

### Las 4 piezas que tienen escrito el dominio

Cuando cambie el dominio, **las cuatro deben coincidir exactamente**, con `https`, sin espacios y con la misma terminación (`.com.mx` ≠ `.com`):

1. **Hostinger, variable de entorno** `MP_REDIRECT_URI` = `https://DOMINIO/v1/oauth/callback`, y `PUBLIC_BASE_URL` = `https://DOMINIO`.
2. **Mercado Pago, panel de la aplicación**: URL de redireccionamiento = `https://DOMINIO/v1/oauth/callback`.
3. **JetVenta, código**: `src/Pos.Api/appsettings.json` → `"MercadoPago": { "BrokerUrl": "https://DOMINIO/" }`.
4. **JetVenta, tiendas ya instaladas**: `C:\Program Files\JetVenta\api\appsettings.json`, la misma clave.

En la documentación también aparece en `services/mp-broker/.env.example`, `services/mp-broker/README.md` y `docs/INTEGRACION_MERCADO_PAGO_POINT.md`.

---

## 2. Empezar desde cero en Hostinger (plan Business)

Requisitos: el plan **Business Web Hosting** o un plan Cloud (los que permiten "Web App" con
Node.js), Node 20 o 22, y una base MySQL del mismo plan.

### 2.1 Subdominio y SSL
1. hPanel → **Dominios** → **Subdominios** → crear `mp` dentro de `gocentersuplementos.com.mx`.
2. hPanel → **Seguridad → SSL** → instalar el certificado gratuito para `mp.gocentersuplementos.com.mx`.
3. Si el dominio **no** está registrado en Hostinger, crea en el DNS de tu registrador un registro
   `A` para `mp` que apunte a la IP del plan (está en hPanel → **Detalles del plan**). Espera a que
   propague (ver §6.1) **antes** de pedir el SSL.

### 2.2 Base de datos MySQL
1. hPanel → **Bases de datos → Administración** → crear la base, el usuario y una contraseña fuerte.
   Hostinger les pone un prefijo (`u123456789_...`); anota el nombre **completo**.
2. Crear las tablas desde **phpMyAdmin** (pegar `schema.sql`) o por SSH, como en §3.3.

### 2.3 Generar `TOKEN_ENC_KEY` (solo una vez en la vida del servicio)
En **tu PC** (PowerShell, con Node instalado):

```powershell
node -e "console.log(require('crypto').randomBytes(32).toString('base64'))"
```

Si no tienes Node, PowerShell lo genera igual:

```powershell
$b = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Fill($b); [Convert]::ToBase64String($b)
```

Guárdala en el gestor de contraseñas. **Si cambia o se pierde**, el intermediario ya no puede
descifrar el secreto de las instalaciones registradas y todas las tiendas tendrán que volver a
pulsar "Autorizar cuenta" (ver §5.2).

### 2.4 Empaquetar el intermediario (en tu PC)
El zip **no** debe llevar `node_modules` ni `.env`. Usa `tar.exe` de Windows: genera un zip con
rutas `/`, que Linux entiende. **No uses `Compress-Archive`** de Windows PowerShell 5, porque guarda
rutas con `\` y en Hostinger aparece "Cannot find module ./src/...".

```powershell
cd "C:\Users\jethr\Desktop\proyectos\punto de venta\services\mp-broker"
npm test                                   # opcional: no necesita MySQL ni Mercado Pago
Remove-Item ..\mp-broker-hostinger.zip -ErrorAction SilentlyContinue
tar.exe -a -c -f ..\mp-broker-hostinger.zip package.json app.cjs schema.sql README.md .env.example src
tar.exe -tf ..\mp-broker-hostinger.zip     # verifica: package.json y app.cjs en la raíz, src/... con "/"
```

### 2.5 Crear la Web App de Node.js
1. hPanel → **Sitios web → Crear sitio web → Web App** → **Subir los archivos del sitio** y elegir
   `mp-broker-hostinger.zip`. También puede ser GitHub con directorio raíz `services/mp-broker`.
2. Dominio: `mp.gocentersuplementos.com.mx`.
3. **Ajustes de compilación**:
   - Framework: **Other** (u "Otro").
   - Node.js **20.x o 22.x**.
   - **Archivo de entrada: `app.cjs`** (no `src/server.js`; ver error E2).
   - El panel ejecuta `npm install` y `npm run build`; el `package.json` ya trae un `build` que no hace nada.
4. **Variables de entorno**. Escribe los valores **sin comillas**, aunque el código ya las tolera:

| Variable | Valor |
| --- | --- |
| `MP_CLIENT_ID` | App ID de la aplicación JetVenta en Mercado Pago |
| `MP_CLIENT_SECRET` | Client Secret de **producción** |
| `MP_REDIRECT_URI` | `https://mp.gocentersuplementos.com.mx/v1/oauth/callback` |
| `PUBLIC_BASE_URL` | `https://mp.gocentersuplementos.com.mx` (informativa) |
| `TOKEN_ENC_KEY` | La de §2.3 |
| `DB_HOST` / `DB_PORT` | `localhost` / `3306` (salvo que hPanel indique otro host) |
| `DB_USER` / `DB_PASSWORD` / `DB_NAME` | Los de §2.2, con prefijo |
| `TRUST_PROXY` | `1` |
| `ALLOW_OPEN_REGISTRATION` | `1` (con `0` ninguna tienda nueva puede registrarse) |
| `PORT` | Solo si Hostinger lo pide; normalmente lo asigna solo |

5. **Desplegar** y abrir `https://mp.gocentersuplementos.com.mx/v1/health`. Debe responder `{"ok":true}`.

### 2.6 Aplicación en Mercado Pago
1. [Tus integraciones](https://www.mercadopago.com.mx/developers/panel/app) → aplicación **JetVenta**
   (pagos presenciales / Point, como integrador).
2. **URL de redireccionamiento**: `https://mp.gocentersuplementos.com.mx/v1/oauth/callback`, exacta.
3. Activar **PKCE** y el permiso **offline_access**, necesario para el refresh token.
4. Copiar App ID y Client Secret de producción a las variables de §2.5.

### 2.7 JetVenta
1. Confirmar `src/Pos.Api/appsettings.json` → `"BrokerUrl": "https://mp.gocentersuplementos.com.mx/"`.
2. En cada tienda: **Configuración → Mercado Pago Point → Autorizar cuenta**.

---

## 3. Operación por SSH (Hostinger)

> En el plan Business, **SSH sirve para revisar, no para desplegar**: Hostinger no permite correr
> `npm` por SSH. Instalar, compilar, reiniciar y redesplegar se hace en hPanel
> (**Settings & Redeploy** y el botón **Restart** de la Web App). Todo lo demás sí se hace por SSH.

### 3.1 Activar y conectarse
hPanel → **Avanzado → Acceso SSH** → **Activar** y copiar el **comando SSH**. El usuario es
`u` + números, el puerto suele ser `65002` y la contraseña es la de FTP o la que definas ahí.

```powershell
ssh -p 65002 u123456789@IP_DEL_SERVIDOR      # usa exactamente el comando que muestra hPanel
```

Si la sesión se cierra justo después de escribir la contraseña: desactiva y vuelve a activar SSH en hPanel.

Opcional, para entrar con llave en lugar de contraseña:
```powershell
ssh-keygen -t ed25519 -f $HOME\.ssh\hostinger_jetventa
Get-Content $HOME\.ssh\hostinger_jetventa.pub   # pégala en hPanel → Acceso SSH → Llaves SSH
ssh -i $HOME\.ssh\hostinger_jetventa -p 65002 u123456789@IP_DEL_SERVIDOR
```

### 3.2 Dónde están los archivos y los logs
```bash
cd ~/domains/mp.gocentersuplementos.com.mx/nodejs   # build en ejecución (Hostinger la reemplaza en cada deploy)
ls -la                                              # debe verse package.json, app.cjs, src/
tail -n 100 stderr.log                              # errores al arrancar (config_error, startup_failed...)
tail -f stderr.log                                  # en vivo; Ctrl+C para salir
grep -c '"event":"request"' stdout.log 2>/dev/null  # peticiones registradas, si existe stdout.log
```
Si la ruta no existe, búscala con `ls ~/domains/` y `find ~/domains -maxdepth 3 -name app.cjs`.
**No edites archivos ahí**: se pierden en el siguiente deploy. Corrige en el repositorio y vuelve a desplegar.

### 3.3 Base de datos por SSH
```bash
# Crear o actualizar tablas (seguro de repetir: usa CREATE TABLE IF NOT EXISTS)
mysql -h localhost -u u123456789_jvbroker -p u123456789_jvbroker < ~/domains/mp.gocentersuplementos.com.mx/nodejs/schema.sql

# Revisar instalaciones registradas
mysql -h localhost -u u123456789_jvbroker -p u123456789_jvbroker -e \
  "SELECT id, product_version, FROM_UNIXTIME(created_at/1000) creada, FROM_UNIXTIME(last_seen_at/1000) ultima, revoked_at FROM installations ORDER BY last_seen_at DESC;"

# Bloquear una instalación (por ejemplo, un equipo robado)
mysql ... -e "UPDATE installations SET revoked_at = UNIX_TIMESTAMP()*1000 WHERE id = 'ID';"

# Respaldo completo (hazlo antes de cualquier migración)
mysqldump -h localhost -u u123456789_jvbroker -p --single-transaction u123456789_jvbroker > ~/jvbroker-$(date +%F).sql
```

### 3.4 Pruebas desde cualquier terminal
```bash
curl -s https://mp.gocentersuplementos.com.mx/v1/health                      # {"ok":true}
curl -sI https://mp.gocentersuplementos.com.mx/v1/health | head -1           # HTTP/2 200
curl -vI https://mp.gocentersuplementos.com.mx 2>&1 | grep -i -E 'subject|expire'   # certificado
```
En Windows: `Invoke-RestMethod https://mp.gocentersuplementos.com.mx/v1/health` y `nslookup mp.gocentersuplementos.com.mx`.

---

## 4. Cambiar de dominio (por ejemplo, a `mp.NUEVODOMINIO.mx`)

Sigue el orden. Así las tiendas instaladas no pierden el servicio mientras migras.

1. **DNS y SSL del nuevo dominio** (§2.1). Comprueba con `nslookup mp.NUEVODOMINIO.mx` y espera el SSL.
2. **Hostinger**: en la Web App cambia o agrega el dominio, o crea una Web App nueva con el
   **mismo zip, la misma base y la misma `TOKEN_ENC_KEY`**. Actualiza las variables:
   - `MP_REDIRECT_URI=https://mp.NUEVODOMINIO.mx/v1/oauth/callback`
   - `PUBLIC_BASE_URL=https://mp.NUEVODOMINIO.mx`

   Redespliega y prueba `/v1/health`.
3. **Mercado Pago**: **agrega** la nueva URL de redireccionamiento. Deja la vieja hasta terminar.
4. **JetVenta, código**:
   ```powershell
   cd "C:\Users\jethr\Desktop\proyectos\punto de venta"
   git grep -n "mp.gocentersuplementos.com.mx"     # lista todo lo que hay que cambiar
   ```
   Cambia `src/Pos.Api/appsettings.json` (`BrokerUrl`, con `/` final), `services/mp-broker/.env.example`,
   `services/mp-broker/README.md` y `docs/INTEGRACION_MERCADO_PAGO_POINT.md`. Luego compila, empaqueta
   la versión (`scripts\package-production.ps1` y `scripts\build-installer.ps1`) y **revisa el
   `appsettings.json` del paquete antes de distribuirlo** (ver error E1):
   ```powershell
   Get-Content .\artifacts\production\win-x64\api\appsettings.json | Select-String BrokerUrl
   ```
5. **Tiendas ya instaladas** (PowerShell **como administrador**, en cada tienda):
   ```powershell
   $f = 'C:\Program Files\JetVenta\api\appsettings.json'
   Copy-Item $f "$f.bak-$(Get-Date -Format yyyyMMdd)"
   $j = Get-Content $f -Raw | ConvertFrom-Json
   $j.MercadoPago.BrokerUrl = 'https://mp.NUEVODOMINIO.mx/'
   $j | ConvertTo-Json -Depth 10 | Set-Content $f -Encoding UTF8
   Restart-Service PuntoDeVentaApi
   Invoke-RestMethod 'https://mp.NUEVODOMINIO.mx/v1/health'
   ```
   La API solo acepta `https`. Con `http` o con una URL mal escrita, "Autorizar cuenta" responde
   "Falta configurar la dirección del intermediario".
6. **Prueba real**: en una tienda, **Autorizar cuenta**, aceptar en Mercado Pago y comprobar que aparecen las terminales.
7. Cuando todas las tiendas usen el dominio nuevo, **quita la URL vieja** en Mercado Pago y el dominio viejo de la Web App.

Si conservas la misma base y la misma `TOKEN_ENC_KEY`, **las tiendas no necesitan volver a autorizar**.
Sus instalaciones siguen siendo válidas y solo cambia la dirección.

---

## 5. Cambiar de host

### 5.1 A otra cuenta o plan de Hostinger
1. En el host viejo: `mysqldump ... > jvbroker.sql` (§3.3) y descárgalo con `scp`:
   ```powershell
   scp -P 65002 u123456789@IP_VIEJA:~/jvbroker-2026-10-11.sql .
   scp -P 65002 .\jvbroker-2026-10-11.sql u987654321@IP_NUEVA:~/
   ```
2. En el host nuevo: crea la base (§2.2) e importa con `mysql -u ... -p BASE_NUEVA < ~/jvbroker-2026-10-11.sql`.
3. Crea la Web App (§2.5) con **la misma `TOKEN_ENC_KEY`** y las credenciales de la base nueva.
4. Apunta el DNS del subdominio a la IP nueva y emite el SSL. Si el dominio no cambia, ni Mercado Pago ni JetVenta necesitan cambios.

### 5.2 Si se perdió la base o la `TOKEN_ENC_KEY`
No hay forma de recuperar las instalaciones registradas. El servicio arranca, pero las tiendas recibirán
`unknown_installation` o un error 500 al renovar. Qué hacer:
- Despliega con base y llave nuevas (§2).
- En cada tienda: **Autorizar cuenta**. JetVenta detecta `unknown_installation`, se registra de nuevo
  y pide aceptar en Mercado Pago. Mientras tanto, los cobros siguen con el token local vigente.

### 5.3 A un VPS (Hostinger VPS u otro, Ubuntu 22.04/24.04)
Aquí **todo** se hace por SSH. Reemplaza `DOMINIO`.
```bash
ssh root@IP_VPS
apt update && apt -y upgrade
curl -fsSL https://deb.nodesource.com/setup_22.x | bash - && apt -y install nodejs mysql-server nginx certbot python3-certbot-nginx
adduser --system --group --home /opt/jvbroker jvbroker

# Base de datos
mysql -e "CREATE DATABASE jvbroker CHARACTER SET utf8mb4; CREATE USER 'jvbroker'@'localhost' IDENTIFIED BY 'CAMBIAR'; GRANT ALL ON jvbroker.* TO 'jvbroker'@'localhost';"

# Código (desde tu PC: scp mp-broker-hostinger.zip root@IP_VPS:/opt/jvbroker/)
apt -y install unzip && cd /opt/jvbroker && unzip -o mp-broker-hostinger.zip && npm install --omit=dev
mysql jvbroker < schema.sql

# Variables (sin comillas). chmod 600: sólo el servicio las lee
cat > /etc/jvbroker.env <<'ENV'
PORT=3000
MP_CLIENT_ID=
MP_CLIENT_SECRET=
MP_REDIRECT_URI=https://DOMINIO/v1/oauth/callback
TOKEN_ENC_KEY=
DB_HOST=localhost
DB_PORT=3306
DB_USER=jvbroker
DB_PASSWORD=CAMBIAR
DB_NAME=jvbroker
TRUST_PROXY=1
ENV
chmod 600 /etc/jvbroker.env

cat > /etc/systemd/system/jvbroker.service <<'UNIT'
[Unit]
Description=JetVenta mp-broker
After=network.target mysql.service
[Service]
User=jvbroker
WorkingDirectory=/opt/jvbroker
EnvironmentFile=/etc/jvbroker.env
ExecStart=/usr/bin/node app.cjs
Restart=always
[Install]
WantedBy=multi-user.target
UNIT
chown -R jvbroker:jvbroker /opt/jvbroker && systemctl daemon-reload && systemctl enable --now jvbroker
journalctl -u jvbroker -n 50 --no-pager          # logs

# Nginx + HTTPS
cat > /etc/nginx/sites-available/jvbroker <<'NGX'
server { listen 80; server_name DOMINIO;
  location / { proxy_pass http://127.0.0.1:3000; proxy_set_header Host $host;
               proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for; proxy_set_header X-Forwarded-Proto $scheme; } }
NGX
ln -sf /etc/nginx/sites-available/jvbroker /etc/nginx/sites-enabled/ && nginx -t && systemctl reload nginx
certbot --nginx -d DOMINIO --redirect -m TU_CORREO --agree-tos -n
ufw allow OpenSSH && ufw allow 'Nginx Full' && ufw --force enable
curl -s https://DOMINIO/v1/health
```
Para actualizar el VPS: subir el zip nuevo, `unzip -o`, `npm install --omit=dev`, `systemctl restart jvbroker`.

---

## 6. Errores que ya tuvimos y cómo resolverlos

| # | Síntoma | Causa | Solución |
| --- | --- | --- | --- |
| **E1** | "Autorizar cuenta" falla. Mercado Pago dice que la URL de redirección no es válida, o JetVenta no llega al intermediario. | **Dominio mal escrito**: se usó `gocentersuplementos.com` en vez de `.com.mx`. Los paquetes **3.3.54 y 3.3.55** (`artifacts/production/.../api/appsettings.json`) salieron con `https://mp.gocentersuplementos.com/`. | Revisar las 4 piezas de §1. En tiendas instaladas con esos paquetes, corregir `BrokerUrl` con el script de §4 paso 5. Antes de publicar una versión, revisar el `appsettings.json` del paquete. |
| **E2** | La Web App no arranca en Hostinger (`ERR_REQUIRE_ESM` o "require() of ES Module" en `stderr.log`), o arranca y no responde. | Hostinger (LiteSpeed/Passenger) carga la entrada con `require()`, pero el código es módulo ES (`"type": "module"`). | Archivo de entrada **`app.cjs`**: es un puente que importa `src/server.js`. No uses `src/server.js` como entrada. |
| **E3** | `stderr.log` muestra `config_error` con "TOKEN_ENC_KEY debe ser de 32 bytes" o "MP_REDIRECT_URI debe ser HTTPS" aunque el valor se ve bien. | El panel guardó el valor **entre comillas** (`"https://..."`). | El código ya quita comillas (`clean()` en `src/config.js`). Aun así, captura los valores sin comillas y sin espacios. Para `TOKEN_ENC_KEY`, revisa que sea base64 de 32 bytes (44 caracteres terminados en `=`). |
| **E4** | La app se cae al arrancar sin decir por qué. | No había log de configuración. | Ahora `src/server.js` escribe `{"event":"config_error",...}` y `app.cjs` escribe `startup_failed`. Revisa `tail -n 50 stderr.log` (§3.2). |
| **E5** | El deploy falla en el paso de compilación ("Missing script: build"). | El panel ejecuta `npm run build`. | `package.json` trae `"build": "echo \"Sin paso de compilación\""`. No lo quites. |
| **E6** | En Hostinger aparece "Cannot find module './src/server.js'" aunque el archivo está en el zip. | Zip hecho con `Compress-Archive` de PowerShell 5, que usa rutas con `\`. | Empaquetar con `tar.exe -a -c -f` (§2.4) y verificar con `tar.exe -tf`. |
| **E7** | En JetVenta faltan funciones nuevas (historial de compras, retiro de caja) aunque el cliente sí es nuevo. | Seguía corriendo **una API vieja**: la terminal o el servicio no se reinició y la compilación no pudo reemplazar archivos bloqueados. | Cerrar JetVenta y la API (`Get-Process JetVenta, Pos.Api` y los `dotnet.exe` de `Pos.Api`), compilar y arrancar con `scripts\dev-up.ps1`. En producción: `Restart-Service PuntoDeVentaApi`. Compras ya avisa con "La API está desactualizada". |

### Otros mensajes posibles

| Mensaje o código del intermediario | Qué significa | Qué hacer |
| --- | --- | --- |
| `clock_skew` ("El reloj de este equipo no coincide…") | El reloj de Windows de la tienda está desfasado más de 5 minutos | Ajustar la fecha y hora, activando la sincronización automática |
| `unknown_installation` | La base del intermediario no conoce esa tienda: base nueva, perdida o instalación revocada | Volver a pulsar **Autorizar cuenta**. JetVenta se registra solo. |
| `bad_signature` / `unsigned` / `replayed` | Firma inválida o repetida | Normalmente se resuelve reintentando. Si persiste, revisa la hora y vuelve a autorizar. |
| `registration_closed` (403) | `ALLOW_OPEN_REGISTRATION=0` | Ponerlo en `1` mientras se instalan tiendas nuevas |
| `rate_limited` (429) | Más de 5 registros por hora desde la misma IP | Esperar una hora |
| `authorization_revoked` | La cuenta de Mercado Pago quitó el permiso | Volver a autorizar desde JetVenta |
| `refresh_failed` (502) | Mercado Pago no respondió al renovar | Se reintenta solo. Si dura días, revisa las credenciales de la app de Mercado Pago. |
| 503 o página de Hostinger en lugar de JSON | La app no está corriendo | hPanel → Web App → **Restart**; revisar `stderr.log` |
| SSL no se emite | El DNS aún no apunta a Hostinger | `nslookup`; esperar la propagación (minutos a 24 h) y reintentar |
| `/v1/health` responde, pero "Autorizar cuenta" no abre Mercado Pago | `MP_CLIENT_ID` o `MP_CLIENT_SECRET` incorrectos, o la app de Mercado Pago sin PKCE ni `offline_access` | Revisar §2.6 y las variables |

---

## 7. Lista de verificación final
- [ ] `https://DOMINIO/v1/health` → `{"ok":true}`
- [ ] `MP_REDIRECT_URI` (Hostinger) = URL de redireccionamiento (Mercado Pago) = `BrokerUrl` + `v1/oauth/callback`
- [ ] `TOKEN_ENC_KEY` guardada en el gestor de contraseñas
- [ ] Respaldo de MySQL reciente (`mysqldump`)
- [ ] `appsettings.json` del paquete de producción con el dominio correcto
- [ ] Una tienda probó **Autorizar cuenta** y ve sus terminales Point

## Referencias
- `services/mp-broker/README.md` (endpoints y firma)
- `docs/INTEGRACION_MERCADO_PAGO_POINT.md` (flujo en JetVenta)
- Hostinger: [Web App Node.js](https://www.hostinger.com/support/?p=6553) · [Conectar por SSH](https://www.hostinger.com/support/1583245-how-to-connect-to-a-hosting-plan-via-ssh-in-hostinger/) · [Errores de despliegue Node.js](https://www.hostinger.com/support/how-to-troubleshoot-a-failed-node-js-deployment-using-build-logs/)
