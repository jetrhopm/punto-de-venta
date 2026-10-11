-- Esquema del intermediario OAuth de JetVenta (MySQL 8 / MariaDB 10.6+).
-- Ejecutar una vez desde phpMyAdmin de Hostinger o con `npm run migrate`.
-- Fechas en milisegundos UNIX.

CREATE TABLE IF NOT EXISTS installations (
  id CHAR(36) NOT NULL PRIMARY KEY,
  secret_enc VARCHAR(255) NOT NULL,
  fingerprint VARCHAR(128) NOT NULL,
  license_id VARCHAR(128) NULL,
  product_version VARCHAR(32) NULL,
  created_at BIGINT NOT NULL,
  last_seen_at BIGINT NOT NULL,
  revoked_at BIGINT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS oauth_attempts (
  id CHAR(36) NOT NULL PRIMARY KEY,
  installation_id CHAR(36) NOT NULL,
  state_hash CHAR(64) NOT NULL,
  verifier_enc VARCHAR(255) NULL,
  status VARCHAR(16) NOT NULL,
  mp_user_id BIGINT NULL,
  tokens_enc TEXT NULL,
  error VARCHAR(255) NULL,
  created_at BIGINT NOT NULL,
  updated_at BIGINT NOT NULL,
  expires_at BIGINT NOT NULL,
  UNIQUE KEY ux_oauth_attempts_state (state_hash),
  KEY ix_oauth_attempts_installation (installation_id, status),
  KEY ix_oauth_attempts_expires (status, expires_at),
  CONSTRAINT fk_oauth_attempts_installation FOREIGN KEY (installation_id) REFERENCES installations (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS request_nonces (
  installation_id CHAR(36) NOT NULL,
  nonce VARCHAR(64) NOT NULL,
  created_at BIGINT NOT NULL,
  PRIMARY KEY (installation_id, nonce),
  KEY ix_request_nonces_created (created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
