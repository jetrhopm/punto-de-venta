import assert from 'node:assert/strict';
import { it } from 'node:test';
import { loadConfig } from '../src/config.js';

const key = Buffer.alloc(32, 7).toString('base64');

it('acepta valores entre comillas como los guarda Hostinger', () => {
  const config = loadConfig({ TOKEN_ENC_KEY: `'${key}'`, MP_CLIENT_ID: "'123'", MP_CLIENT_SECRET: '"abc"', MP_REDIRECT_URI: "'https://mp.example.com/v1/oauth/callback'", DB_PASSWORD: "'p*ss'", TRUST_PROXY: "'1'" });
  assert.equal(config.clientId, '123');
  assert.equal(config.clientSecret, 'abc');
  assert.equal(config.redirectUri, 'https://mp.example.com/v1/oauth/callback');
  assert.equal(config.db.password, 'p*ss');
  assert.equal(config.trustProxy, true);
  assert.equal(config.tokenKey.length, 32);
});

it('explica qué variable falta', () => {
  assert.throws(() => loadConfig({}), /TOKEN_ENC_KEY/);
});
