// Entrada compatible con servidores que cargan la aplicación con require()
// (por ejemplo LiteSpeed/Passenger en Hostinger). El código real es un módulo ES.
import('./src/server.js').catch((error) => {
  console.error(JSON.stringify({ at: new Date().toISOString(), event: 'startup_failed', error: error.message }));
  process.exit(1);
});
