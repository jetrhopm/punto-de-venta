# Fase 3: multicaja por red local

## Incremento 1 terminado

- El cliente WPF permite configurar IP o nombre del servidor y puerto.
- La direccion se guarda por usuario en `%LocalAppData%\PuntoDeVenta\client-settings.json`.
- El login puede probar la conexion al endpoint publico `/health` antes de autenticar.
- Todas las ventanas WPF usan el cliente HTTP centralizado; no hay URLs locales duplicadas.
- La API expone `/api/lan/info` con version de protocolo y nombre del servidor.
- El instalador configura la API para escuchar en LAN privada y agrega una regla de Firewall de Windows para TCP 5000 en perfil privado.

## Verificacion

- Compilacion de la solucion: 0 advertencias, 0 errores.
- Pruebas unitarias: 6 correctas.
- Pruebas de integracion PostgreSQL: 2 correctas.
- Health comprobado: `/health` responde `status=ok`.
- Compatibilidad LAN comprobada: `/api/lan/info` responde version de protocolo `1`.

## Pendiente del siguiente incremento

- Emparejamiento de una caja adicional mediante codigo temporal.
- Identificadores persistentes de tienda, equipo y caja.
- Validacion de version cliente/API durante el emparejamiento.
- SignalR para avisos de cambios, sin usarlo como garantia de consistencia.
- Pruebas con dos cajas y fallas de red.
