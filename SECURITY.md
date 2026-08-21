# Seguridad

## Reglas iniciales

- No guardar secretos en Git.
- No registrar contrasenas, tokens, claves privadas ni credenciales de proveedores.
- Usar hashing resistente para contrasenas.
- Validar permisos en la API, aunque la interfaz oculte botones.
- Usar consultas parametrizadas y restricciones de base de datos.
- Mantener auditoria append-only para operaciones sensibles.
- La llave privada que emite archivos `licencia.jv` nunca se incluye en Git, `Setup.exe`, respaldos ni computadoras de clientes. Solo la llave pública se distribuye con JetVenta.
- La validación de licencia se realiza en la API local; modificar la interfaz no debe habilitar operaciones de negocio.

## Reporte de vulnerabilidades

Mientras el proyecto esta en fase inicial, registra hallazgos de seguridad como issues privados o comunicacion directa con el propietario del repositorio.
