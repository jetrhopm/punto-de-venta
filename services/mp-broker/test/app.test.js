import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import http from 'node:http';
import { after, before, beforeEach, describe, it } from 'node:test';
import { createApp } from '../src/app.js';
import { LIMITS } from '../src/config.js';
import { MercadoPagoError } from '../src/mercadopago.js';
import { signRequest } from '../src/security.js';
import { MemoryStore } from '../src/store/memory.js';

const config = {
  clientId: 'APP123',
  clientSecret: 'SECRET',
  redirectUri: 'https://mp.example.com/v1/oauth/callback',
  tokenKey: crypto.randomBytes(32),
  trustProxy: false,
  allowOpenRegistration: true,
  mpAuthBase: 'https://auth.mercadopago.com'
};

let clock;
let store;
let mp;
let server;
let base;
let logs;

function fakeMercadoPago() {
  const calls = { exchange: [], refresh: [] };
  return {
    calls,
    exchangeResult: { accessToken: 'APP_USR-access', refreshToken: 'TG-refresh', userId: '987', expiresIn: 15552000, scope: 'offline_access read write', liveMode: true },
    exchangeError: null,
    refreshError: null,
    async exchangeCode(code, verifier) { calls.exchange.push({ code, verifier }); if (this.exchangeError) throw this.exchangeError; return this.exchangeResult; },
    async refresh(token) { calls.refresh.push(token); if (this.refreshError) throw this.refreshError; return { accessToken: 'APP_USR-new', refreshToken: 'TG-new', userId: '987', expiresIn: 15552000, scope: null, liveMode: true }; }
  };
}

before(async () => {
  server = http.createServer((req, res) => handler(req, res));
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  base = `http://127.0.0.1:${server.address().port}`;
});
after(() => server.close());

let handler;
beforeEach(() => {
  clock = Date.UTC(2026, 8, 25, 12, 0, 0);
  store = new MemoryStore();
  mp = fakeMercadoPago();
  logs = [];
  handler = createApp({ config, store, mercadoPago: mp, now: () => clock, log: (event, data) => logs.push({ event, ...data }) });
});

async function request(method, path, { body, headers } = {}) {
  const response = await fetch(base + path, { method, headers: { 'content-type': 'application/json', ...headers }, body, redirect: 'manual' });
  const text = await response.text();
  let json = null;
  try { json = JSON.parse(text); } catch { /* html */ }
  return { status: response.status, json, text };
}

async function register() {
  const res = await request('POST', '/v1/installations/register', { body: JSON.stringify({ fingerprint: 'ABCDEF0123456789', productVersion: '3.4.0' }) });
  assert.equal(res.status, 201);
  return res.json;
}

function signed(installation, method, path, payload, overrides = {}) {
  const body = payload === undefined ? '' : JSON.stringify(payload);
  const timestamp = String(overrides.timestamp ?? Math.floor(clock / 1000));
  const nonce = overrides.nonce ?? crypto.randomBytes(16).toString('base64url');
  const signature = overrides.signature ?? signRequest(installation.installationSecret, method, path, timestamp, nonce, body);
  return request(method, path, { body: body || undefined, headers: { 'x-jv-installation': installation.installationId, 'x-jv-timestamp': timestamp, 'x-jv-nonce': nonce, 'x-jv-signature': signature } });
}

async function startFlow(installation) {
  const res = await signed(installation, 'POST', '/v1/oauth/start', {});
  assert.equal(res.status, 200);
  const url = new URL(res.json.authorizationUrl);
  return { attemptId: res.json.attemptId, url, state: url.searchParams.get('state') };
}

describe('registro e identidad', () => {
  it('registra una instalación y no guarda el secreto en claro', async () => {
    const inst = await register();
    assert.match(inst.installationId, /^[0-9a-f-]{36}$/);
    const saved = await store.getInstallation(inst.installationId);
    assert.ok(!saved.secretEnc.includes(inst.installationSecret));
  });

  it('rechaza huella vacía', async () => {
    const res = await request('POST', '/v1/installations/register', { body: '{}' });
    assert.equal(res.status, 400);
  });

  it('rechaza firma inválida, reloj desfasado y nonce repetido', async () => {
    const inst = await register();
    assert.equal((await signed(inst, 'POST', '/v1/oauth/start', {}, { signature: 'ab'.repeat(32) })).json.code, 'bad_signature');
    assert.equal((await signed(inst, 'POST', '/v1/oauth/start', {}, { timestamp: Math.floor(clock / 1000) - 600 })).json.code, 'clock_skew');
    const nonce = 'nonce-repetido-123456';
    assert.equal((await signed(inst, 'POST', '/v1/oauth/start', {}, { nonce })).status, 200);
    assert.equal((await signed(inst, 'POST', '/v1/oauth/start', {}, { nonce })).json.code, 'replayed');
  });

  it('rechaza peticiones sin firma', async () => {
    const res = await request('POST', '/v1/oauth/start', { body: '{}' });
    assert.equal(res.status, 401);
    assert.equal(res.json.code, 'unsigned');
  });
});

describe('flujo OAuth', () => {
  it('genera URL con PKCE S256, state y redirect exacto', async () => {
    const inst = await register();
    const { url, state } = await startFlow(inst);
    assert.equal(url.origin + url.pathname, 'https://auth.mercadopago.com/authorization');
    assert.equal(url.searchParams.get('client_id'), 'APP123');
    assert.equal(url.searchParams.get('redirect_uri'), config.redirectUri);
    assert.equal(url.searchParams.get('code_challenge_method'), 'S256');
    assert.ok(state.length >= 40);
  });

  it('completa, entrega tokens una sola vez y los borra', async () => {
    const inst = await register();
    const { attemptId, url, state } = await startFlow(inst);

    assert.equal((await signed(inst, 'POST', '/v1/oauth/pickup', { attemptId })).status, 202);

    const cb = await request('GET', `/v1/oauth/callback?code=TG-code&state=${encodeURIComponent(state)}`);
    assert.equal(cb.status, 200);
    assert.match(cb.text, /Cuenta autorizada/);

    // El verifier enviado a Mercado Pago corresponde al challenge de la URL.
    const verifier = mp.calls.exchange[0].verifier;
    assert.equal(crypto.createHash('sha256').update(verifier).digest('base64url'), url.searchParams.get('code_challenge'));

    const first = await signed(inst, 'POST', '/v1/oauth/pickup', { attemptId });
    assert.equal(first.status, 200);
    assert.equal(first.json.status, 'completed');
    assert.equal(first.json.accessToken, 'APP_USR-access');
    assert.equal(first.json.refreshToken, 'TG-refresh');
    assert.equal((await store.getAttempt(attemptId)).tokensEnc, null);

    assert.equal((await signed(inst, 'POST', '/v1/oauth/pickup', { attemptId })).status, 410);
    assert.ok(!JSON.stringify(logs).includes('APP_USR-access'));
  });

  it('no permite reutilizar el callback', async () => {
    const inst = await register();
    const { state } = await startFlow(inst);
    await request('GET', `/v1/oauth/callback?code=TG-code&state=${encodeURIComponent(state)}`);
    const second = await request('GET', `/v1/oauth/callback?code=TG-code&state=${encodeURIComponent(state)}`);
    assert.equal(second.status, 400);
    assert.equal(mp.calls.exchange.length, 1);
  });

  it('otra instalación no puede recoger los tokens', async () => {
    const owner = await register();
    const other = await register();
    const { attemptId, state } = await startFlow(owner);
    await request('GET', `/v1/oauth/callback?code=TG-code&state=${encodeURIComponent(state)}`);
    assert.equal((await signed(other, 'POST', '/v1/oauth/pickup', { attemptId })).status, 404);
    assert.equal((await signed(owner, 'POST', '/v1/oauth/pickup', { attemptId })).status, 200);
  });

  it('marca como fallido si el comercio rechaza el acceso', async () => {
    const inst = await register();
    const { attemptId, state } = await startFlow(inst);
    const cb = await request('GET', `/v1/oauth/callback?error=access_denied&state=${encodeURIComponent(state)}`);
    assert.equal(cb.status, 400);
    const res = await signed(inst, 'POST', '/v1/oauth/pickup', { attemptId });
    assert.equal(res.json.status, 'failed');
    assert.match(res.json.message, /rechazado/);
  });

  it('reporta falla si Mercado Pago no acepta el código', async () => {
    const inst = await register();
    const { attemptId, state } = await startFlow(inst);
    mp.exchangeError = new MercadoPagoError('invalid_grant', 400, 'invalid_grant');
    assert.equal((await request('GET', `/v1/oauth/callback?code=bad&state=${encodeURIComponent(state)}`)).status, 502);
    assert.equal((await signed(inst, 'POST', '/v1/oauth/pickup', { attemptId })).json.status, 'failed');
  });

  it('vence el intento si no se acepta a tiempo', async () => {
    const inst = await register();
    const { attemptId, state } = await startFlow(inst);
    clock += LIMITS.attemptTtlMs + 1000;
    assert.equal((await request('GET', `/v1/oauth/callback?code=TG-code&state=${encodeURIComponent(state)}`)).status, 400);
    assert.equal((await signed(inst, 'POST', '/v1/oauth/pickup', { attemptId })).json.status, 'expired');
  });

  it('borra los tokens no recogidos a tiempo', async () => {
    const inst = await register();
    const { attemptId, state } = await startFlow(inst);
    await request('GET', `/v1/oauth/callback?code=TG-code&state=${encodeURIComponent(state)}`);
    clock += LIMITS.pickupTtlMs + 1000;
    const res = await signed(inst, 'POST', '/v1/oauth/pickup', { attemptId });
    assert.equal(res.json.status, 'expired');
    assert.equal((await store.getAttempt(attemptId)).tokensEnc, null);
  });

  it('un nuevo inicio invalida el intento pendiente anterior', async () => {
    const inst = await register();
    const firstFlow = await startFlow(inst);
    await startFlow(inst);
    assert.equal((await request('GET', `/v1/oauth/callback?code=TG-code&state=${encodeURIComponent(firstFlow.state)}`)).status, 400);
  });
});

describe('renovación', () => {
  it('renueva sin guardar tokens', async () => {
    const inst = await register();
    const res = await signed(inst, 'POST', '/v1/oauth/refresh', { refreshToken: 'TG-refresh-old' });
    assert.equal(res.status, 200);
    assert.equal(res.json.accessToken, 'APP_USR-new');
    assert.equal(mp.calls.refresh[0], 'TG-refresh-old');
    assert.equal(store.attempts.size, 0);
  });

  it('informa autorización revocada', async () => {
    const inst = await register();
    mp.refreshError = new MercadoPagoError('invalid_grant', 400, 'invalid_grant');
    const res = await signed(inst, 'POST', '/v1/oauth/refresh', { refreshToken: 'TG-refresh-old' });
    assert.equal(res.status, 401);
    assert.equal(res.json.code, 'authorization_revoked');
  });

  it('distingue falla temporal de Mercado Pago', async () => {
    const inst = await register();
    mp.refreshError = new MercadoPagoError('timeout', 502, 'timeout');
    const res = await signed(inst, 'POST', '/v1/oauth/refresh', { refreshToken: 'TG-refresh-old' });
    assert.equal(res.status, 502);
    assert.equal(res.json.code, 'refresh_failed');
  });
});

describe('general', () => {
  it('health responde sin datos sensibles', async () => {
    const res = await request('GET', '/v1/health');
    assert.deepEqual(res.json, { ok: true });
  });

  it('JSON inválido devuelve 400 con mensaje en español', async () => {
    const res = await request('POST', '/v1/installations/register', { body: 'no-json' });
    assert.equal(res.status, 400);
    assert.match(res.json.message, /JSON/);
  });
});
