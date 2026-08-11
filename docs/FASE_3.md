# Fase 3: multicaja por red local

## Incremento 1 terminado

- El cliente WPF permite configurar IP o nombre del servidor y puerto.
- La direccion se guarda por usuario en `%LocalAppData%\PuntoDeVenta\client-settings.json`.
- El login puede probar la conexion al endpoint publico `/health` antes de autenticar.
- Todas las ventanas WPF usan el cliente HTTP centralizado; no hay URLs locales duplicadas.
- La API expone `/api/lan/info` con version de protocolo y nombre del servidor.
- El instalador configura la API para escuchar en LAN privada y agrega una regla de Firewall de Windows para TCP 5000 en perfil privado.

## Incremento 2 terminado: emparejamiento de cajas

- El administrador puede generar desde la caja principal un codigo temporal de seis digitos.
- El codigo expira en diez minutos, se almacena solamente como hash y solo puede utilizarse una vez.
- La caja adicional permite capturar el codigo, nombre del equipo y nombre de la caja.
- El servidor crea de forma transaccional el identificador de caja y el registro del equipo.
- La identidad persistente de la caja se guarda localmente protegida con DPAPI del usuario de Windows.
- La API valida que el usuario que genera codigos sea administrador y evita nombres de caja duplicados.
- La migracion `AgregaEmparejamientoLan` agrega las tablas `device` y `pairing_code` sin alterar datos existentes.

## Verificacion

- Compilacion de la solucion: 0 advertencias, 0 errores.
- Pruebas unitarias: 6 correctas.
- Pruebas de integracion PostgreSQL: 2 correctas.
- Health comprobado: `/health` responde `status=ok`.
- Compatibilidad LAN comprobada: `/api/lan/info` responde version de protocolo `1`.
- La migracion de emparejamiento fue aplicada y las pruebas de integracion siguen correctas.
- La solucion compila con 0 advertencias y 0 errores despues de agregar la UI de emparejamiento.

## Pendiente del siguiente incremento

- Validacion de version cliente/API durante el emparejamiento.
- SignalR para avisos de cambios, sin usarlo como garantia de consistencia.
- Pruebas con dos cajas y fallas de red.
- Enviar la identidad de caja en las solicitudes protegidas y validar su estado activo en la API.
