import http from 'node:http';
import { createApp } from './app.js';
import { loadConfig } from './config.js';
import { createMercadoPagoClient } from './mercadopago.js';
import { MySqlStore } from './store/mysql.js';

const config = loadConfig();
const store = await MySqlStore.connect(config.db);
const handler = createApp({ config, store, mercadoPago: createMercadoPagoClient(config) });

const server = http.createServer((req, res) => {
  handler(req, res).catch(() => { if (!res.headersSent) { res.writeHead(500); res.end(); } });
});
server.requestTimeout = 30_000;
server.headersTimeout = 20_000;
server.listen(config.port, () => console.log(JSON.stringify({ at: new Date().toISOString(), event: 'listening', port: config.port })));

async function shutdown() {
  server.close();
  await store.close().catch(() => {});
  process.exit(0);
}
process.on('SIGTERM', shutdown);
process.on('SIGINT', shutdown);
