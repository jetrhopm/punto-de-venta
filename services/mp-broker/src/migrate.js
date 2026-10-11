// Crea las tablas definidas en schema.sql. Uso: npm run migrate
import { readFile } from 'node:fs/promises';
import { loadConfig } from './config.js';

const config = loadConfig();
const mysql = await import('mysql2/promise');
const connection = await mysql.createConnection({ ...config.db, multipleStatements: true });
const sql = await readFile(new URL('../schema.sql', import.meta.url), 'utf8');
await connection.query(sql);
await connection.end();
console.log('Tablas creadas o ya existentes.');
