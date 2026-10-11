// Almacenamiento en memoria con la misma interfaz que MySqlStore.
// Se usa en las pruebas; no debe usarse en producción.
export class MemoryStore {
  constructor() {
    this.installations = new Map();
    this.nonces = new Map();
    this.attempts = new Map();
  }

  async createInstallation(installation) { this.installations.set(installation.id, { ...installation }); }
  async getInstallation(id) { const item = this.installations.get(id); return item ? { ...item } : null; }
  async touchInstallation(id, at) { const item = this.installations.get(id); if (item) item.lastSeenAt = at; }

  async useNonce(installationId, nonce, at) {
    const key = `${installationId}|${nonce}`;
    if (this.nonces.has(key)) return false;
    this.nonces.set(key, at);
    return true;
  }

  async createAttempt(attempt) { this.attempts.set(attempt.id, { ...attempt }); }
  async getAttempt(id) { const item = this.attempts.get(id); return item ? { ...item } : null; }
  async findAttemptByStateHash(stateHash) {
    for (const item of this.attempts.values()) if (item.stateHash === stateHash) return { ...item };
    return null;
  }

  // Cambio atómico: sólo aplica si el estado actual está en `from`.
  async transitionAttempt(id, from, changes) {
    const item = this.attempts.get(id);
    if (!item || !from.includes(item.status)) return false;
    Object.assign(item, changes);
    return true;
  }

  async expirePendingAttempts(installationId, at) {
    for (const item of this.attempts.values()) {
      if (item.installationId === installationId && item.status === 'pending') Object.assign(item, { status: 'expired', verifierEnc: null, updatedAt: at });
    }
  }

  async purge(at, nonceTtlMs) {
    for (const item of this.attempts.values()) {
      if (['pending', 'completed', 'exchanging'].includes(item.status) && item.expiresAt <= at) Object.assign(item, { status: 'expired', tokensEnc: null, verifierEnc: null, updatedAt: at });
    }
    for (const [key, createdAt] of this.nonces) if (at - createdAt > nonceTtlMs) this.nonces.delete(key);
  }

  async close() {}
}
