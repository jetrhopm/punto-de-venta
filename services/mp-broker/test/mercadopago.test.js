import assert from 'node:assert/strict';
import { it } from 'node:test';
import { createMercadoPagoClient, MercadoPagoError } from '../src/mercadopago.js';

const config = { mpApiBase: 'https://api.mercadopago.com', clientId: 'APP', clientSecret: 'SEC', redirectUri: 'https://mp.example.com/v1/oauth/callback' };

it('envía code, redirect y verifier al intercambiar', async () => {
  let sent;
  const client = createMercadoPagoClient(config, async (url, init) => {
    sent = { url, body: JSON.parse(init.body) };
    return new Response(JSON.stringify({ access_token: 'APP_USR-1', refresh_token: 'TG-1', user_id: 55, expires_in: 15552000 }), { status: 200 });
  });
  const tokens = await client.exchangeCode('CODE', 'VERIFIER');
  assert.equal(sent.url, 'https://api.mercadopago.com/oauth/token');
  assert.deepEqual(sent.body, { client_id: 'APP', client_secret: 'SEC', grant_type: 'authorization_code', code: 'CODE', redirect_uri: config.redirectUri, code_verifier: 'VERIFIER' });
  assert.equal(tokens.userId, '55');
});

it('convierte errores de Mercado Pago', async () => {
  const client = createMercadoPagoClient(config, async () => new Response(JSON.stringify({ error: 'invalid_grant', message: 'invalid refresh_token' }), { status: 400 }));
  await assert.rejects(client.refresh('TG-x'), (error) => error instanceof MercadoPagoError && error.status === 400 && error.code === 'invalid_grant');
});

it('convierte fallas de red en 502', async () => {
  const client = createMercadoPagoClient(config, async () => { throw new TypeError('fetch failed'); });
  await assert.rejects(client.refresh('TG-x'), (error) => error.status === 502 && error.code === 'network');
});
