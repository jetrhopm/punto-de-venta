import crypto from 'node:crypto';
import { LIMITS } from './config.js';
import { MercadoPagoError } from './mercadopago.js';
import { decrypt, encrypt, isUuid, pkcePair, randomToken, RateLimiter, safeEqualHex, sha256Hex, signRequest } from './security.js';

class HttpError extends Error {
  constructor(status, message, code) { super(message); this.status = status; this.code = code; }
}

// Crea el manejador HTTP. Todas las dependencias se inyectan para poder probarlo.
export function createApp({ config, store, mercadoPago, now = () => Date.now(), log = defaultLog }) {
  const limiter = new RateLimiter(now);
  let lastPurge = 0;

  async function maybePurge() {
    const at = now();
    if (at - lastPurge < 60_000) return;
    lastPurge = at;
    try { await store.purge(at, LIMITS.nonceTtlMs); } catch (error) { log('purge_failed', { error: error.message }); }
  }

  function clientIp(req) {
    if (config.trustProxy) {
      const forwarded = req.headers['x-forwarded-for'];
      if (typeof forwarded === 'string' && forwarded.trim()) return forwarded.split(',')[0].trim();
    }
    return req.socket?.remoteAddress || 'unknown';
  }

  function limit(key, max, windowMs) {
    if (!limiter.allow(key, max, windowMs)) throw new HttpError(429, 'Demasiadas solicitudes. Espera un momento e intenta de nuevo.', 'rate_limited');
  }

  // Verifica la firma HMAC de la instalación y devuelve su id.
  async function authenticate(req, path, body) {
    const installationId = header(req, 'x-jv-installation');
    const timestamp = header(req, 'x-jv-timestamp');
    const nonce = header(req, 'x-jv-nonce');
    const signature = header(req, 'x-jv-signature');
    if (!isUuid(installationId) || !/^\d{9,12}$/.test(timestamp || '') || !/^[A-Za-z0-9_-]{16,64}$/.test(nonce || '') || !signature)
      throw new HttpError(401, 'La solicitud no está firmada por una instalación de JetVenta.', 'unsigned');
    if (Math.abs(Math.floor(now() / 1000) - Number(timestamp)) > LIMITS.signatureSkewSeconds)
      throw new HttpError(401, 'El reloj de este equipo no coincide con la hora real. Ajusta la fecha y hora de Windows.', 'clock_skew');

    const installation = await store.getInstallation(installationId.toLowerCase());
    if (!installation || installation.revokedAt) throw new HttpError(401, 'Esta instalación no está registrada o fue revocada.', 'unknown_installation');
    const secret = decrypt(config.tokenKey, installation.secretEnc);
    const expected = signRequest(secret, req.method, path, timestamp, nonce, body);
    if (!safeEqualHex(expected, signature.toLowerCase())) throw new HttpError(401, 'La firma de la solicitud no es válida.', 'bad_signature');
    if (!(await store.useNonce(installation.id, nonce, now()))) throw new HttpError(401, 'La solicitud ya fue utilizada.', 'replayed');
    await store.touchInstallation(installation.id, now());
    limit(`inst:${installation.id}`, 120, 60_000);
    return installation.id;
  }

  // ---------- Endpoints ----------

  async function register(req, body) {
    if (!config.allowOpenRegistration) throw new HttpError(403, 'El registro de instalaciones está cerrado.', 'registration_closed');
    limit(`register:${clientIp(req)}`, 5, 60 * 60 * 1000);
    const input = parseJson(body);
    const fingerprint = typeof input.fingerprint === 'string' ? input.fingerprint.trim() : '';
    if (fingerprint.length < 8 || fingerprint.length > 128) throw new HttpError(400, 'Falta la huella del equipo.', 'invalid_fingerprint');
    const id = crypto.randomUUID();
    const secret = randomToken(32);
    await store.createInstallation({
      id,
      secretEnc: encrypt(config.tokenKey, secret),
      fingerprint,
      licenseId: optionalText(input.licenseId, 128),
      productVersion: optionalText(input.productVersion, 32),
      createdAt: now()
    });
    log('installation_registered', { installationId: id });
    return json(201, { installationId: id, installationSecret: secret });
  }

  async function start(installationId) {
    const at = now();
    await store.expirePendingAttempts(installationId, at);
    const state = randomToken(32);
    const { verifier, challenge } = pkcePair();
    const attemptId = crypto.randomUUID();
    const expiresAt = at + LIMITS.attemptTtlMs;
    await store.createAttempt({ id: attemptId, installationId, stateHash: sha256Hex(state), verifierEnc: encrypt(config.tokenKey, verifier), status: 'pending', createdAt: at, expiresAt });
    const url = new URL('/authorization', config.mpAuthBase);
    url.searchParams.set('client_id', config.clientId);
    url.searchParams.set('response_type', 'code');
    url.searchParams.set('platform_id', 'mp');
    url.searchParams.set('redirect_uri', config.redirectUri);
    url.searchParams.set('state', state);
    url.searchParams.set('code_challenge', challenge);
    url.searchParams.set('code_challenge_method', 'S256');
    log('oauth_started', { installationId, attemptId });
    return json(200, { attemptId, authorizationUrl: url.toString(), expiresAt: new Date(expiresAt).toISOString() });
  }

  async function callback(query) {
    const state = query.get('state') || '';
    const code = query.get('code') || '';
    const providerError = query.get('error');
    if (!state) return page(400, 'Autorización no válida', 'El enlace no contiene los datos de la autorización. Vuelve a pulsar “Autorizar cuenta” en JetVenta.');
    const attempt = await store.findAttemptByStateHash(sha256Hex(state));
    const at = now();
    if (!attempt || attempt.status !== 'pending' || attempt.expiresAt <= at)
      return page(400, 'Autorización vencida', 'Este enlace ya se usó o venció. Regresa a JetVenta y pulsa “Autorizar cuenta” otra vez.');

    if (providerError || !code) {
      await store.transitionAttempt(attempt.id, ['pending'], { status: 'failed', error: providerError === 'access_denied' ? 'El acceso fue rechazado en Mercado Pago.' : 'Mercado Pago no entregó la autorización.', verifierEnc: null, updatedAt: at });
      return page(400, 'Autorización cancelada', 'No se concedió el acceso. Puedes cerrar esta ventana y volver a intentarlo desde JetVenta.');
    }

    // Marca el intento antes de llamar a Mercado Pago: un código sólo se usa una vez.
    if (!(await store.transitionAttempt(attempt.id, ['pending'], { status: 'exchanging', updatedAt: at })))
      return page(400, 'Autorización en proceso', 'Esta autorización ya se está procesando. Regresa a JetVenta.');

    try {
      const tokens = await mercadoPago.exchangeCode(code, decrypt(config.tokenKey, attempt.verifierEnc));
      const done = now();
      await store.transitionAttempt(attempt.id, ['exchanging'], {
        status: 'completed',
        mpUserId: tokens.userId,
        tokensEnc: encrypt(config.tokenKey, JSON.stringify(tokens)),
        verifierEnc: null,
        updatedAt: done,
        expiresAt: done + LIMITS.pickupTtlMs
      });
      log('oauth_completed', { installationId: attempt.installationId, attemptId: attempt.id, mpUserId: tokens.userId });
      return page(200, 'Cuenta autorizada', 'Listo. Regresa a JetVenta: la cuenta se conectará sola y podrás elegir la terminal de tu caja.');
    } catch (error) {
      const message = error instanceof MercadoPagoError ? error.message : 'Error inesperado al completar la autorización.';
      await store.transitionAttempt(attempt.id, ['exchanging'], { status: 'failed', error: message.slice(0, 250), verifierEnc: null, updatedAt: now() });
      log('oauth_exchange_failed', { attemptId: attempt.id, code: error.code, status: error.status });
      return page(502, 'No se pudo completar', 'Mercado Pago no confirmó la autorización. Regresa a JetVenta y vuelve a intentarlo.');
    }
  }

  async function pickup(installationId, body) {
    const input = parseJson(body);
    if (!isUuid(input.attemptId)) throw new HttpError(400, 'Falta el identificador del intento.', 'invalid_attempt');
    const attempt = await store.getAttempt(input.attemptId.toLowerCase());
    if (!attempt || attempt.installationId !== installationId) throw new HttpError(404, 'No se encontró la autorización.', 'attempt_not_found');
    const at = now();
    if (['pending', 'exchanging', 'completed'].includes(attempt.status) && attempt.expiresAt <= at) {
      await store.transitionAttempt(attempt.id, [attempt.status], { status: 'expired', tokensEnc: null, verifierEnc: null, updatedAt: at });
      return json(200, { status: 'expired', message: 'La autorización venció. Vuelve a pulsar “Autorizar cuenta”.' });
    }
    switch (attempt.status) {
      case 'pending':
      case 'exchanging':
        return json(202, { status: 'pending' });
      case 'failed':
        return json(200, { status: 'failed', message: attempt.error || 'La autorización no se completó.' });
      case 'expired':
        return json(200, { status: 'expired', message: 'La autorización venció. Vuelve a pulsar “Autorizar cuenta”.' });
      case 'picked_up':
        throw new HttpError(410, 'Los datos de esta autorización ya se entregaron.', 'already_picked_up');
      case 'completed': {
        // Entrega única: primero se marca y se borra, después se responde.
        const tokensEnc = attempt.tokensEnc;
        if (!(await store.transitionAttempt(attempt.id, ['completed'], { status: 'picked_up', tokensEnc: null, updatedAt: at })))
          throw new HttpError(410, 'Los datos de esta autorización ya se entregaron.', 'already_picked_up');
        const tokens = JSON.parse(decrypt(config.tokenKey, tokensEnc));
        log('oauth_picked_up', { installationId, attemptId: attempt.id });
        return json(200, { status: 'completed', ...tokens });
      }
      default:
        throw new HttpError(500, 'Estado de autorización desconocido.', 'unknown_state');
    }
  }

  async function refresh(installationId, body) {
    const input = parseJson(body);
    if (typeof input.refreshToken !== 'string' || input.refreshToken.length < 10 || input.refreshToken.length > 512)
      throw new HttpError(400, 'Falta el refresh token.', 'invalid_refresh_token');
    limit(`refresh:${installationId}`, 10, 60 * 60 * 1000);
    try {
      const tokens = await mercadoPago.refresh(input.refreshToken);
      log('token_refreshed', { installationId, mpUserId: tokens.userId });
      return json(200, tokens);
    } catch (error) {
      if (error instanceof MercadoPagoError && (error.status === 400 || error.status === 401) )
        throw new HttpError(401, 'Mercado Pago ya no reconoce esta autorización. Vuelve a autorizar la cuenta desde JetVenta.', 'authorization_revoked');
      throw new HttpError(502, 'No se pudo renovar el acceso con Mercado Pago. Se reintentará más tarde.', 'refresh_failed');
    }
  }

  // ---------- Enrutador ----------

  return async function handle(req, res) {
    const started = now();
    let url;
    try { url = new URL(req.url, 'http://localhost'); } catch { return send(res, json(400, { message: 'URL no válida.', code: 'bad_url' })); }
    const path = url.pathname;
    let result;
    try {
      await maybePurge();
      limit(`ip:${clientIp(req)}`, 300, 60_000);
      if (req.method === 'GET' && path === '/v1/health') result = json(200, { ok: true });
      else if (req.method === 'GET' && path === '/v1/oauth/callback') result = await callback(url.searchParams);
      else if (req.method === 'POST' && path === '/v1/installations/register') result = await register(req, await readBody(req));
      else if (req.method === 'POST' && ['/v1/oauth/start', '/v1/oauth/pickup', '/v1/oauth/refresh'].includes(path)) {
        const body = await readBody(req);
        const installationId = await authenticate(req, path, body);
        if (path === '/v1/oauth/start') result = await start(installationId);
        else if (path === '/v1/oauth/pickup') result = await pickup(installationId, body);
        else result = await refresh(installationId, body);
      } else result = json(404, { message: 'Ruta no encontrada.', code: 'not_found' });
    } catch (error) {
      if (error instanceof HttpError) result = json(error.status, { message: error.message, code: error.code });
      else {
        log('unhandled_error', { path, error: error.message });
        result = json(500, { message: 'Error interno del intermediario.', code: 'internal' });
      }
    }
    send(res, result);
    if (path !== '/v1/health') log('request', { method: req.method, path, status: result.status, ms: now() - started });
  };
}

// ---------- Utilidades ----------

function header(req, name) { const value = req.headers[name]; return typeof value === 'string' ? value.trim() : ''; }
function optionalText(value, max) { return typeof value === 'string' && value.trim() ? value.trim().slice(0, max) : null; }

function parseJson(body) {
  if (!body) return {};
  try { const value = JSON.parse(body); if (value && typeof value === 'object' && !Array.isArray(value)) return value; } catch { /* abajo */ }
  throw new HttpError(400, 'El cuerpo de la solicitud no es JSON válido.', 'invalid_json');
}

function readBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    let size = 0;
    req.on('data', (chunk) => {
      size += chunk.length;
      if (size > LIMITS.maxBodyBytes) { reject(new HttpError(413, 'La solicitud es demasiado grande.', 'too_large')); req.destroy(); return; }
      chunks.push(chunk);
    });
    req.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
    req.on('error', reject);
  });
}

function json(status, value) {
  return { status, headers: { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' }, body: JSON.stringify(value) };
}

function page(status, title, message) {
  const escape = (text) => text.replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const ok = status === 200;
  const body = `<!doctype html><html lang="es"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>${escape(title)} · JetVenta</title>
<style>body{margin:0;font-family:"Segoe UI",system-ui,sans-serif;background:#f4f6f8;color:#1c2430;display:flex;min-height:100vh;align-items:center;justify-content:center}
main{background:#fff;max-width:440px;margin:16px;padding:32px;border-radius:12px;box-shadow:0 2px 12px rgba(0,0,0,.08)}h1{font-size:22px;margin:0 0 12px;color:${ok ? '#137333' : '#b3261e'}}p{line-height:1.5;margin:0}</style></head>
<body><main><h1>${escape(title)}</h1><p>${escape(message)}</p></main></body></html>`;
  return { status, headers: { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store', 'x-frame-options': 'DENY', 'referrer-policy': 'no-referrer' }, body };
}

function send(res, result) {
  res.writeHead(result.status, { ...result.headers, 'x-content-type-options': 'nosniff' });
  res.end(result.body);
}

function defaultLog(event, data) {
  console.log(JSON.stringify({ at: new Date().toISOString(), event, ...data }));
}
