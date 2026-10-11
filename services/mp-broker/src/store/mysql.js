// Almacenamiento en MySQL (Hostinger). Fechas en milisegundos UNIX (BIGINT)
// para evitar problemas de zona horaria del servidor compartido.
export class MySqlStore {
  static async connect(db) {
    const mysql = await import('mysql2/promise');
    const pool = mysql.createPool({ ...db, waitForConnections: true, connectionLimit: 5, charset: 'utf8mb4' });
    return new MySqlStore(pool);
  }

  constructor(pool) { this.pool = pool; }

  async createInstallation(i) {
    await this.pool.execute(
      'INSERT INTO installations (id, secret_enc, fingerprint, license_id, product_version, created_at, last_seen_at) VALUES (?, ?, ?, ?, ?, ?, ?)',
      [i.id, i.secretEnc, i.fingerprint, i.licenseId, i.productVersion, i.createdAt, i.createdAt]);
  }

  async getInstallation(id) {
    const [rows] = await this.pool.execute('SELECT id, secret_enc, revoked_at FROM installations WHERE id = ?', [id]);
    const row = rows[0];
    return row ? { id: row.id, secretEnc: row.secret_enc, revokedAt: row.revoked_at === null ? null : Number(row.revoked_at) } : null;
  }

  async touchInstallation(id, at) { await this.pool.execute('UPDATE installations SET last_seen_at = ? WHERE id = ?', [at, id]); }

  async useNonce(installationId, nonce, at) {
    try {
      await this.pool.execute('INSERT INTO request_nonces (installation_id, nonce, created_at) VALUES (?, ?, ?)', [installationId, nonce, at]);
      return true;
    } catch (error) {
      if (error.code === 'ER_DUP_ENTRY') return false;
      throw error;
    }
  }

  async createAttempt(a) {
    await this.pool.execute(
      'INSERT INTO oauth_attempts (id, installation_id, state_hash, verifier_enc, status, created_at, updated_at, expires_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?)',
      [a.id, a.installationId, a.stateHash, a.verifierEnc, a.status, a.createdAt, a.createdAt, a.expiresAt]);
  }

  async getAttempt(id) {
    const [rows] = await this.pool.execute('SELECT * FROM oauth_attempts WHERE id = ?', [id]);
    return rows[0] ? mapAttempt(rows[0]) : null;
  }

  async findAttemptByStateHash(stateHash) {
    const [rows] = await this.pool.execute('SELECT * FROM oauth_attempts WHERE state_hash = ?', [stateHash]);
    return rows[0] ? mapAttempt(rows[0]) : null;
  }

  async transitionAttempt(id, from, changes) {
    const columns = { status: 'status', mpUserId: 'mp_user_id', tokensEnc: 'tokens_enc', verifierEnc: 'verifier_enc', error: 'error', updatedAt: 'updated_at', expiresAt: 'expires_at' };
    const sets = [];
    const values = [];
    for (const [key, value] of Object.entries(changes)) {
      if (!columns[key]) throw new Error(`Columna desconocida: ${key}`);
      sets.push(`${columns[key]} = ?`);
      values.push(value ?? null);
    }
    const [result] = await this.pool.execute(
      `UPDATE oauth_attempts SET ${sets.join(', ')} WHERE id = ? AND status IN (${from.map(() => '?').join(', ')})`,
      [...values, id, ...from]);
    return result.affectedRows === 1;
  }

  async expirePendingAttempts(installationId, at) {
    await this.pool.execute("UPDATE oauth_attempts SET status = 'expired', verifier_enc = NULL, updated_at = ? WHERE installation_id = ? AND status = 'pending'", [at, installationId]);
  }

  async purge(at, nonceTtlMs) {
    await this.pool.execute("UPDATE oauth_attempts SET status = 'expired', tokens_enc = NULL, verifier_enc = NULL, updated_at = ? WHERE status IN ('pending', 'exchanging', 'completed') AND expires_at <= ?", [at, at]);
    await this.pool.execute('DELETE FROM request_nonces WHERE created_at < ?', [at - nonceTtlMs]);
    // Los intentos terminados se conservan 30 días sin datos sensibles, para auditoría.
    await this.pool.execute('DELETE FROM oauth_attempts WHERE created_at < ?', [at - 30 * 24 * 60 * 60 * 1000]);
  }

  async close() { await this.pool.end(); }
}

function mapAttempt(row) {
  return {
    id: row.id,
    installationId: row.installation_id,
    stateHash: row.state_hash,
    verifierEnc: row.verifier_enc,
    status: row.status,
    mpUserId: row.mp_user_id === null ? null : String(row.mp_user_id),
    tokensEnc: row.tokens_enc,
    error: row.error,
    createdAt: Number(row.created_at),
    updatedAt: Number(row.updated_at),
    expiresAt: Number(row.expires_at)
  };
}
