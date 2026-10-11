// Configuración del intermediario. Todo sale de variables de entorno;
// ningún valor sensible vive en el repositorio.

function required(env, name) {
  const value = env[name];
  if (!value || !value.trim()) throw new Error(`Falta la variable de entorno ${name}.`);
  return value.trim();
}

export function loadConfig(env = process.env) {
  const tokenKey = Buffer.from(required(env, 'TOKEN_ENC_KEY'), 'base64');
  if (tokenKey.length !== 32) throw new Error('TOKEN_ENC_KEY debe ser de 32 bytes codificados en base64.');

  const redirectUri = required(env, 'MP_REDIRECT_URI');
  if (!redirectUri.startsWith('https://')) throw new Error('MP_REDIRECT_URI debe ser HTTPS.');

  return {
    port: Number(env.PORT || 3000),
    clientId: required(env, 'MP_CLIENT_ID'),
    clientSecret: required(env, 'MP_CLIENT_SECRET'),
    redirectUri,
    tokenKey,
    trustProxy: env.TRUST_PROXY === '1',
    allowOpenRegistration: env.ALLOW_OPEN_REGISTRATION !== '0',
    mpApiBase: env.MP_API_BASE || 'https://api.mercadopago.com',
    mpAuthBase: env.MP_AUTH_BASE || 'https://auth.mercadopago.com',
    db: {
      host: env.DB_HOST || 'localhost',
      port: Number(env.DB_PORT || 3306),
      user: env.DB_USER || '',
      password: env.DB_PASSWORD || '',
      database: env.DB_NAME || ''
    }
  };
}

// Tiempos del flujo. El código de autorización de Mercado Pago vence en 10 minutos.
export const LIMITS = {
  attemptTtlMs: 10 * 60 * 1000,        // tiempo para aceptar en Mercado Pago
  pickupTtlMs: 15 * 60 * 1000,         // tiempo para que la tienda recoja los tokens
  signatureSkewSeconds: 300,           // diferencia máxima de reloj en peticiones firmadas
  nonceTtlMs: 10 * 60 * 1000,
  maxBodyBytes: 16 * 1024,
  mpTimeoutMs: 15000
};
