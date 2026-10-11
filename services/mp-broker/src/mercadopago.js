import { LIMITS } from './config.js';

export class MercadoPagoError extends Error {
  constructor(message, status, code) { super(message); this.status = status; this.code = code; }
}

// Cliente mínimo del endpoint /oauth/token de Mercado Pago.
export function createMercadoPagoClient(config, fetchImpl = globalThis.fetch) {
  async function tokenRequest(body) {
    let response;
    try {
      response = await fetchImpl(`${config.mpApiBase}/oauth/token`, {
        method: 'POST',
        headers: { 'content-type': 'application/json', accept: 'application/json' },
        body: JSON.stringify({ client_id: config.clientId, client_secret: config.clientSecret, ...body }),
        signal: AbortSignal.timeout(LIMITS.mpTimeoutMs)
      });
    } catch (error) {
      throw new MercadoPagoError('No se pudo conectar con Mercado Pago.', 502, error.name === 'TimeoutError' ? 'timeout' : 'network');
    }
    const text = await response.text();
    let json = null;
    try { json = text ? JSON.parse(text) : null; } catch { json = null; }
    if (!response.ok) {
      const code = json?.error || json?.code || `http_${response.status}`;
      throw new MercadoPagoError(json?.message || json?.error_description || `Mercado Pago respondió HTTP ${response.status}.`, response.status, String(code));
    }
    if (!json?.access_token) throw new MercadoPagoError('Mercado Pago devolvió una respuesta sin Access Token.', 502, 'empty_token');
    return {
      accessToken: json.access_token,
      refreshToken: json.refresh_token ?? null,
      userId: json.user_id != null ? String(json.user_id) : null,
      expiresIn: Number(json.expires_in ?? 0),
      scope: json.scope ?? null,
      liveMode: json.live_mode ?? null
    };
  }

  return {
    exchangeCode: (code, verifier) => tokenRequest({ grant_type: 'authorization_code', code, redirect_uri: config.redirectUri, code_verifier: verifier }),
    refresh: (refreshToken) => tokenRequest({ grant_type: 'refresh_token', refresh_token: refreshToken })
  };
}
