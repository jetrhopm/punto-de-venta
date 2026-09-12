# Fase 3: multicaja por red local

## Incremento 1 terminado

- El cliente WPF permite configurar IP o nombre del servidor y puerto.
- Desde `3.3.0`, la direccion, impresora, lector y emparejamiento se guardan por computadora en `%ProgramData%\PuntoDeVenta\client\machine-settings.json`.
- El login puede probar la conexion al endpoint publico `/health` antes de autenticar.
- Todas las ventanas WPF usan el cliente HTTP centralizado; no hay URLs locales duplicadas.
- La API expone `/api/lan/info` con version de protocolo y nombre del servidor.
- El instalador configura la API para escuchar en LAN privada y agrega una regla de Firewall de Windows para TCP 5000 en perfil privado.

## Incremento 2 terminado: emparejamiento de cajas

- El administrador puede generar desde la caja principal un codigo temporal de seis digitos.
- El codigo expira en diez minutos, se almacena solamente como hash y solo puede utilizarse una vez.
- La caja adicional permite capturar el codigo, nombre del equipo y nombre de la caja.
- El servidor crea de forma transaccional el identificador de caja y el registro del equipo.
- La identidad persistente de la caja se guarda localmente protegida con DPAPI de la computadora; cualquier usuario de Windows que inicie JetVenta en ese equipo usa la misma caja y periféricos.
- La API valida que el usuario que genera codigos sea administrador y evita nombres de caja duplicados.
- La migracion `AgregaEmparejamientoLan` agrega las tablas `device` y `pairing_code` sin alterar datos existentes.

## Verificacion

- Compilacion de la solucion: 0 advertencias, 0 errores.
- Pruebas unitarias: 6 correctas.
- Pruebas de integracion PostgreSQL: 2 correctas.
- Health comprobado: `/health` responde `status=ok`.
- Compatibilidad LAN comprobada: `/api/lan/info` responde versión de protocolo `2`.
- La migracion de emparejamiento fue aplicada y las pruebas de integracion siguen correctas.
- La solucion compila con 0 advertencias y 0 errores despues de agregar la UI de emparejamiento.

## Pendiente del siguiente incremento

- Enlazar turnos, tickets y efectivo a la caja emparejada.
- Validacion de version cliente/API durante el emparejamiento.
- SignalR para avisos de cambios, sin usarlo como garantia de consistencia.
- Pruebas con dos cajas y fallas de red.
- Enviar la identidad de caja en las solicitudes protegidas y validar su estado activo en la API.

## Incremento 3 terminado: sesiones por caja

- La sesión incluye la caja y, cuando aplica, el dispositivo emparejado que la inició.
- Un usuario puede volver a entrar en la misma caja; su sesión anterior de esa caja se revoca de forma controlada.
- JetVenta bloquea iniciar sesión en otra caja si el usuario tiene sesión o turno abierto en una distinta, e informa el nombre de la caja que debe atender.
- Una conexión por red sin emparejar no puede iniciar sesión. La caja principal local conserva la operación monocaja mediante conexión de bucle local.

## Incremento 4 terminado: operación aislada por caja

- La API obtiene la caja exclusivamente de la sesión autenticada; abrir turno
  ya no acepta una caja elegida desde el escritorio.
- Turnos, efectivo, borradores, ventas, cancelaciones, devoluciones, datos de
  ticket e impresión se resuelven contra la caja de la sesión.
- Los borradores pendientes pueden recuperarse al regresar al mismo equipo,
  pero no se transfieren a otra caja.
- Las existencias continúan siendo únicas para la tienda; cada venta conserva
  la transacción serializable existente para descontarlas.
- La prueba de integración de dos cajas verifica el aislamiento de borrador,
  efectivo y último ticket.

## Incremento 5 terminado: inventario concurrente

- Los movimientos de inventario compartido se serializan por producto dentro
  de la transacción PostgreSQL, en orden estable para evitar interbloqueos.
- Ventas, kits, ajustes, compras, importaciones, cancelaciones y devoluciones
  usan el mismo control antes de cambiar existencias.
- La venta sigue permitida cuando la existencia registrada es insuficiente;
  JetVenta informa que debe revisarse el inventario y conserva el kardex
  acumulado correctamente.
- El consecutivo de venta se protege por tienda para impedir folios repetidos
  cuando dos cajas cobran al mismo tiempo.

## Incremento 6 terminado: dispositivos por computadora

- Impresora, lector, cajón y báscula se guardan en el perfil de la computadora
  y se comparten entre los usuarios que inicien JetVenta en ella.
- La terminal Mercado Pago Point queda ligada al registro de caja de la sesión
  autenticada, no a la primera caja que encuentre la API.
- La cuenta de Mercado Pago permanece como información protegida de la tienda;
  sólo la terminal física se decide por caja/equipo.

## Incremento 7 terminado: comunicación y seguridad LAN

- La versión de protocolo LAN se valida antes de login y emparejamiento.
- Las sesiones de cajas remotas se validan con el token de su dispositivo en
  cada solicitud; la sesión local del servidor no es reutilizable por red.
- La API acepta sólo loopback y redes privadas, limita intentos de login y
  emparejamiento por IP y consume códigos de emparejamiento atómicamente.
- HTTP sigue destinado exclusivamente a una LAN privada; no se publica el
  puerto de JetVenta a Internet.

## Incremento 8 terminado: instalador por modalidad

- Una instalación nueva permite elegir **Caja principal / servidor** o
  **Caja adicional** antes de instalar archivos.
- La caja principal conserva el flujo completo: cliente, PostgreSQL, API,
  servicios de Windows y regla de Firewall de red privada.
- La caja adicional instala únicamente el cliente de escritorio y Microsoft
  Visual C++; no copia API, PostgreSQL ni scripts de servidor, y no crea ni
  detiene servicios o reglas de Firewall.
- El instalador de caja adicional solicita dirección, puerto, código temporal
  y nombre de caja. Antes de terminar valida `/health`, protocolo LAN `2` y
  ejecuta el emparejamiento seguro contra el servidor.
- La identidad resultante se guarda por computadora con DPAPI en el perfil de
  máquina usado por JetVenta. Las actualizaciones conservan la modalidad y el
  emparejamiento existentes.
- Si falla el emparejamiento después de copiar los archivos, se conserva la
  modalidad como pendiente y la siguiente ejecución solicita otro código sin
  convertir la computadora en servidor.

## Incremento 9 terminado: administración y recuperación de cajas

- La caja principal permite consultar cajas activas e inactivas con su nombre,
  equipo, última conexión, usuario con sesión vigente y turno abierto.
- Un administrador puede renombrar, desactivar, reactivar o volver a emparejar
  una caja. La desactivación se bloquea mientras tenga un turno abierto, no
  borra ventas ni inventario y revoca las sesiones vigentes de esa caja.
- Volver a emparejar genera un código temporal para la misma caja: invalida la
  identidad anterior y no crea una caja duplicada.
- Cada solicitud autenticada de una caja emparejada actualiza su última
  conexión como máximo una vez por minuto.
- Diagnóstico identifica la modalidad: el servidor muestra sus tareas locales;
  la caja adicional revisa su conexión y periféricos, sin ofrecer reparación de
  API local.
- Respaldos, restauración, PostgreSQL y limpieza de datos sólo están expuestos
  desde la caja principal. La interfaz de caja adicional los oculta y la API
  rechaza cualquier llamada de red remota a esas operaciones.
- Antes de restaurar, el servidor entra en mantenimiento persistente. Bloquea
  nuevos logins y operaciones hasta que el script termina o falla, y el script
  libera el modo de mantenimiento de forma garantizada.
