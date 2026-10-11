import crypto from 'node:crypto';

export const sha256Hex = (value) => crypto.createHash('sha256').update(value).digest('hex');
export const randomToken = (bytes = 32) => crypto.randomBytes(bytes).toString('base64url');

export function pkcePair() {
  const verifier = randomToken(48); // 64 caracteres base64url
  const challenge = crypto.createHash('sha256').update(verifier, 'ascii').digest('base64url');
  return { verifier, challenge };
}

// AES-256-GCM. Formato: base64url(iv[12] | tag[16] | ciphertext)
export function encrypt(key, plaintext) {
  const iv = crypto.randomBytes(12);
  const cipher = crypto.createCipheriv('aes-256-gcm', key, iv);
  const data = Buffer.concat([cipher.update(plaintext, 'utf8'), cipher.final()]);
  return Buffer.concat([iv, cipher.getAuthTag(), data]).toString('base64url');
}

export function decrypt(key, payload) {
  const raw = Buffer.from(payload, 'base64url');
  const decipher = crypto.createDecipheriv('aes-256-gcm', key, raw.subarray(0, 12));
  decipher.setAuthTag(raw.subarray(12, 28));
  return Buffer.concat([decipher.update(raw.subarray(28)), decipher.final()]).toString('utf8');
}

// Texto que firma la instalación:
// METHOD \n PATH \n TIMESTAMP \n NONCE \n SHA256(body) en hex minúsculas.
export function canonicalRequest(method, path, timestamp, nonce, body) {
  return `${method.toUpperCase()}\n${path}\n${timestamp}\n${nonce}\n${sha256Hex(body ?? '')}`;
}

export function signRequest(secret, method, path, timestamp, nonce, body) {
  return crypto.createHmac('sha256', secret).update(canonicalRequest(method, path, timestamp, nonce, body)).digest('hex');
}

export function safeEqualHex(a, b) {
  if (typeof a !== 'string' || typeof b !== 'string' || a.length !== b.length || !/^[0-9a-f]+$/i.test(a)) return false;
  return crypto.timingSafeEqual(Buffer.from(a, 'hex'), Buffer.from(b, 'hex'));
}

export const isUuid = (value) => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);

// Limitador simple en memoria (se reinicia con la app, suficiente para este volumen).
export class RateLimiter {
  constructor(now = () => Date.now()) { this.now = now; this.buckets = new Map(); }
  allow(key, limit, windowMs) {
    const at = this.now();
    const bucket = this.buckets.get(key);
    if (!bucket || at - bucket.start >= windowMs) {
      this.buckets.set(key, { start: at, count: 1 });
      if (this.buckets.size > 10000) this.#prune(at, windowMs);
      return true;
    }
    bucket.count++;
    return bucket.count <= limit;
  }
  #prune(at, windowMs) { for (const [k, v] of this.buckets) if (at - v.start >= windowMs) this.buckets.delete(k); }
}
