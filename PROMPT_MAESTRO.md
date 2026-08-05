# Prompt maestro para desarrollar un punto de venta propio para Windows

## Instrucción principal

Actúa como arquitecto de software, desarrollador senior de .NET, especialista en sistemas de punto de venta, bases de datos transaccionales, seguridad, instalación en Windows e integraciones de pago y servicios en México.

Diseña e implementa, por fases verificables, un sistema de punto de venta de escritorio propio que sustituya a eleventa. Debe ser rápido para el cajero, sencillo de aprender, resistente a apagones y cierres inesperados, fácil de respaldar y restaurar, y preparado para crecer desde una sola computadora hasta varias cajas conectadas por red local o, en una fase posterior, por una API alojada en un VPS de Hostinger.

No entregues únicamente maquetas, pseudocódigo, botones sin funcionamiento ni un proyecto de demostración. El resultado final debe ser un producto instalable, documentado, probado y mantenible. No simules funciones críticas como cobros, inventario, cortes, permisos, respaldos o integraciones externas.

La interfaz puede tomar como referencia el flujo de trabajo y los atajos conocidos de eleventa, pero no debe copiar sus recursos gráficos, marca, código ni diseño propietario. Se busca compatibilidad operativa y familiaridad, no una clonación visual.

## Objetivos obligatorios

- Sistema nativo de escritorio para Windows 10 y Windows 11, exclusivamente de 64 bits.
- Idioma inicial: español de México.
- Moneda inicial: pesos mexicanos, con cantidades monetarias usando decimal, nunca float o double.
- Funcionamiento local completo aun sin Internet, excepto funciones que dependan de proveedores externos.
- Soporte inicial para una computadora y una base de datos local.
- Arquitectura preparada para varias cajas conectadas por IP en la red local.
- Arquitectura preparada para una futura modalidad en la nube mediante un VPS de Hostinger.
- Migración controlada de los archivos CSV exportados desde eleventa.
- Impresión térmica de tickets de 56/58 mm y 80 mm.
- Integraciones modulares con Mercado Pago Point y Taecel.
- Instalador final único que automatice todo lo indispensable.
- Desarrollo y revisiones frecuentes sin instalar y desinstalar el programa en cada cambio.
- Protección estricta contra ventas duplicadas, inventario duplicado, corrupción de datos y pérdida de información después de un apagón.

## Forma de trabajo obligatoria

Trabaja por fases pequeñas. Antes de modificar código:

1. Inspecciona el repositorio y la documentación existente.
2. Expón brevemente qué vas a construir, las decisiones técnicas y los riesgos.
3. Implementa una porción completa y utilizable.
4. Ejecuta pruebas automáticas y una comprobación manual del flujo afectado.
5. Documenta lo terminado, las decisiones pendientes y cómo probarlo.
6. Mantén la rama principal funcionando; usa ramas por función y commits pequeños y descriptivos.
7. No avances a una fase nueva si la anterior rompe el arranque, el esquema, una migración o un flujo crítico.

Cuando exista una decisión que cambie de forma importante el comportamiento del negocio, presenta opciones concretas con ventajas y riesgos. No detengas el trabajo por detalles menores que puedan resolverse con una decisión conservadora y documentada.

Mantén como mínimo estos documentos durante el proyecto:

- README.md.
- ARCHITECTURE.md.
- DECISIONS.md con registros de decisiones de arquitectura.
- CHANGELOG.md.
- CONTRIBUTING.md.
- SECURITY.md.
- Manual de instalación, actualización, respaldo y restauración.
- Manual de administrador y manual de cajero.
- Guía de diagnóstico y recuperación de fallas.
- Diccionario de datos y diagrama de la base de datos.
- Matriz de permisos.
- Matriz de atajos de teclado.

## Tecnología y arquitectura recomendadas

Usa esta arquitectura como punto de partida. Si propones cambiarla, justifica el cambio con evidencia y conserva todos los requisitos funcionales y de seguridad.

### Aplicación y servicios

- Lenguaje: C#.
- Plataforma: .NET 10 LTS o la versión LTS de .NET vigente y compatible con Windows 10 y 11 x64 al iniciar el proyecto.
- Escritorio: WPF con MVVM.
- API y lógica autoritativa: ASP.NET Core.
- Servicio local: ejecutable de ASP.NET Core hospedado como servicio de Windows en instalaciones de producción.
- Cliente: WPF; nunca debe ejecutar SQL directo contra una base remota.
- Tiempo real entre cajas: SignalR para cambios que requieran actualización inmediata, sin convertirlo en la única garantía de consistencia.
- Persistencia: Entity Framework Core con Npgsql para operaciones comunes y SQL parametrizado cuidadosamente revisado para operaciones críticas o reportes que lo necesiten.
- Base de datos principal: PostgreSQL.

En una instalación de una sola caja, el cliente, la API local y PostgreSQL viven en la misma computadora. Aun así, el cliente debe comunicarse con la capa de aplicación y no contener reglas críticas duplicadas. Esta separación permitirá añadir cajas por IP sin reescribir el sistema.

### Bibliotecas sugeridas

- CommunityToolkit.Mvvm para MVVM.
- Microsoft.Extensions.Hosting, configuración, inyección de dependencias y servicios en segundo plano.
- Microsoft.AspNetCore.SignalR.Client para eventos entre cajas.
- Npgsql.EntityFrameworkCore.PostgreSQL para PostgreSQL.
- FluentValidation para validaciones de entrada y reglas de comandos.
- Serilog con registros estructurados y rotación local, sin guardar secretos.
- Polly para reintentos limitados únicamente en operaciones seguras e idempotentes.
- CsvHelper para importación y exportación CSV.
- ClosedXML solo si se necesita exportar a XLSX.
- ZXing.Net para códigos de barras o QR cuando corresponda.
- xUnit y una biblioteca de aserciones con licencia compatible para pruebas.
- Pruebas de interfaz con FlaUI, WinAppDriver u otra opción activa y compatible con WPF.

No agregues paquetes por conveniencia sin comprobar mantenimiento, licencia comercial, vulnerabilidades y compatibilidad. Centraliza versiones de paquetes y activa análisis de dependencias.

### Organización sugerida de la solución

```text
src/
  Pos.Desktop/
  Pos.Application/
  Pos.Domain/
  Pos.Infrastructure/
  Pos.Api/
  Pos.Worker/
  Pos.Integrations.MercadoPago/
  Pos.Integrations.Taecel/
  Pos.Printing/
installer/
tests/
  Pos.UnitTests/
  Pos.IntegrationTests/
  Pos.EndToEndTests/
docs/
scripts/
```

Aplica una arquitectura modular y pragmática. Evita tanto el proyecto monolítico sin límites como una colección innecesaria de microservicios. Los módulos deben compartir una transacción local cuando una operación de negocio lo requiera.

## Desarrollo sin instalar y desinstalar continuamente

Implementa tres formas de ejecución claramente separadas.

### 1. Modo desarrollo, recomendado para el trabajo diario

- La aplicación debe poder iniciarse desde Visual Studio o mediante dotnet run.
- Incluye scripts PowerShell dev-setup.ps1, dev-up.ps1, dev-reset.ps1 y dev-down.ps1.
- dev-setup.ps1 prepara una instancia aislada de PostgreSQL para desarrollo una sola vez. Puede usar el archivo ZIP oficial de binarios de PostgreSQL, inicializar un clúster dentro de una carpeta ignorada por Git y arrancarlo con pg_ctl, sin registrar la aplicación ni la base como programa instalado.
- dev-up.ps1 inicia base de datos, API y los servicios necesarios, carga configuración de desarrollo y deja lista la aplicación para ejecutarse desde el IDE.
- dev-reset.ps1 solo reinicia datos de prueba después de mostrar con claridad el destino exacto y pedir confirmación.
- La información de desarrollo y la de producción nunca deben compartir carpetas, puertos, credenciales o bases.
- Incluye datos semilla ficticios para probar productos, ventas, clientes, inventario y cortes.
- Permite aplicar recarga en caliente cuando la tecnología lo soporte, sin comprometer el estado transaccional.

Docker puede ofrecerse como alternativa para desarrolladores que ya lo tengan, pero no debe ser requisito para usar, probar o instalar el punto de venta.

### 2. Compilación portátil de revisión

- Genera una carpeta o ZIP autocontenido win-x64 que se pueda descomprimir y ejecutar sin registrar componentes en Windows.
- Debe utilizar una base de prueba aislada y mostrar permanentemente la leyenda MODO DE PRUEBA.
- Incluye scripts claros para iniciar y detener sus procesos.
- No se permite usar esta modalidad como producción ni conectarla accidentalmente a la base real.
- Sirve para que el propietario revise pantallas y flujos sin ejecutar un instalador en cada avance.

### 3. Instalador de producción

- Créalo únicamente cuando los módulos del hito correspondiente estén probados.
- Las actualizaciones deben ejecutarse sobre la instalación existente, conservar datos y configuración y aplicar migraciones versionadas.
- Prueba el instalador y el actualizador en máquinas virtuales limpias de Windows 10 x64 y Windows 11 x64.

## Instalador único y requisitos externos

El usuario final debe recibir un solo archivo Setup.exe. Usa WiX Toolset con Burn, o una tecnología equivalente que permita encadenar paquetes, detectar prerrequisitos, reparar, actualizar y desinstalar con seguridad.

La aplicación debe publicarse como win-x64 autocontenida. El equipo no debe requerir que el usuario instale .NET manualmente. No actives recorte agresivo de ensamblados hasta comprobar todas las pantallas, reflexión, serialización, EF Core, controladores e impresión.

El instalador debe ofrecer estos modos:

1. Servidor principal / primera caja: instala cliente WPF, API como servicio de Windows, PostgreSQL dedicado y herramientas internas de mantenimiento.
2. Caja adicional: instala solo el cliente y lo conecta al servidor principal por IP o nombre local después de un asistente de emparejamiento.
3. Reparar o actualizar: detecta la instalación existente, conserva datos y repara solo componentes faltantes o dañados.

### PostgreSQL dentro del instalador

- Incluye el instalador silencioso oficial de PostgreSQL o sus binarios ZIP oficiales destinados a integrarse dentro de otra aplicación.
- Instala únicamente servidor y herramientas de línea de comandos necesarias. No instales pgAdmin ni StackBuilder en una computadora de caja salvo solicitud explícita.
- Usa nombre de servicio, puerto, usuario y clúster dedicados al producto para no interferir con otra instalación de PostgreSQL.
- Genera una contraseña aleatoria fuerte durante la instalación; no uses una contraseña fija ni la escribas en la línea de comandos, registros o archivos de texto abiertos.
- Protege secretos con DPAPI o almacenamiento equivalente vinculado al equipo y aplica ACL restrictivas.
- Coloca binarios en Program Files y datos, registros, configuración y respaldos en subcarpetas protegidas de ProgramData.
- Detecta puertos ocupados, instalaciones parciales, espacio insuficiente y reinicios pendientes.
- Crea el clúster, la base, el usuario de aplicación, extensiones aprobadas, esquema inicial y servicio de Windows de forma idempotente.
- Si falla cualquier etapa, informa la causa y revierte los componentes nuevos sin borrar una base anterior.

## Componentes que podrían ser externos

No obligues al usuario a instalar algo por separado si se puede incluir legal y técnicamente en el paquete. Sin embargo, algunos dispositivos pueden exigir controladores o programas firmados del fabricante, por ejemplo ciertas impresoras USB, cajones conectados a controlador propietario o terminales de pago.

En esos casos el instalador debe:

- Detectar si el controlador indispensable existe.
- Mostrar el nombre exacto del componente, fabricante, versión o requisito y un enlace oficial.
- Explicar si puede continuar con funcionalidad limitada.
- No descargar ni ejecutar software de sitios no oficiales.
- No afirmar que Mercado Pago Point, Taecel o una impresora están listos hasta completar una prueba real.

## Actualización y desinstalación

- Antes de migrar el esquema, crea un respaldo verificable y registra la versión.
- Las migraciones deben ser hacia adelante, repetibles de manera segura e incluir estrategia de recuperación.
- Una actualización nunca debe crear un segundo clúster ni reinicializar la tienda.
- Al desinstalar, conserva por defecto base de datos, configuración, tickets y respaldos.
- Eliminar datos requiere una opción separada, advertencia explícita y confirmación adicional.
- Nunca vincules el acceso a la información a una licencia de hardware. Una falla de disco o cambio de computadora no debe impedir restaurar, leer o exportar datos.

## Configuración inicial después de instalar

En el primer inicio, si la base no está inicializada, abre un asistente obligatorio. Debe ser transaccional: si se cancela o falla, no debe dejar una tienda configurada a medias.

### Paso 1: administrador inicial

Muestra tres campos simples, visibles, ya llenos y editables:

- Usuario para iniciar sesión: admin.
- Contraseña: 12345.
- Nombre del administrador: Administrador.

Permite conservar 12345, porque es un requisito del negocio, pero muestra una advertencia clara de que es insegura y recomienda cambiarla. Nunca guardes contraseñas en texto plano. Usa un algoritmo de hash actual con sal única y parámetros resistentes, por ejemplo el PasswordHasher oficial de ASP.NET Core configurado correctamente o Argon2id mediante una biblioteca mantenida.

Reglas obligatorias:

- No se puede eliminar, desactivar ni quitar permisos al último administrador activo.
- El usuario no distingue mayúsculas y minúsculas; define y documenta la normalización.
- La contraseña sí distingue mayúsculas y minúsculas.
- Agrega bloqueo progresivo temporal, auditoría de intentos y una recuperación administrada.
- No muestres ni registres la contraseña después de finalizar el asistente.

### Paso 2: información de la tienda

Solicita:

- Nombre de la tienda o comercio.
- Giro del negocio mediante una lista desplegable.

Incluye como mínimo estos giros:

- Abarrotes.
- Minisúper.
- Farmacia.
- Papelería.
- Ferretería.
- Refaccionaria.
- Ropa y calzado.
- Electrónica.
- Cosméticos.
- Panadería.
- Carnicería.
- Frutas y verduras.
- Restaurante o alimentos.
- Dulcería.
- Vinos y licores.
- Comercio general.
- Otro, con campo para especificar.

Después se podrá configurar nombre fiscal, RFC, dirección, teléfono, logotipo, moneda, impuestos, zona horaria, folios, impresora y formato de ticket.

## Usuarios, tipos y permisos

Solo existen dos tipos base:

1. Administrador: acceso completo y capacidad de crear o editar usuarios y asignar permisos.
2. Cajero: acceso determinado por permisos individuales; puede ser híbrido sin crear muchos roles rígidos.

El administrador debe poder:

- Crear, editar, activar y desactivar usuarios.
- Restablecer contraseñas y obligar su cambio.
- Revocar sesiones.
- Consultar actividad y auditoría.
- Asignar o retirar permisos granulares.
- Definir si una acción sensible requiere contraseña del propio usuario o autorización momentánea de un administrador.

Permisos mínimos independientes:

- Vender.
- Consultar productos.
- Crear y editar productos.
- Cambiar precio durante la venta.
- Aplicar descuentos.
- Usar precio de mayoreo.
- Consultar inventario.
- Agregar o ajustar inventario.
- Ver costos y utilidades.
- Cancelar partidas.
- Cancelar ventas.
- Procesar devoluciones.
- Reimprimir tickets.
- Abrir cajón.
- Registrar entradas y salidas de efectivo.
- Ver historial de ventas.
- Abrir turno.
- Realizar corte.
- Ver cortes anteriores.
- Ver reportes.
- Administrar clientes y crédito.
- Administrar proveedores y compras.
- Ejecutar recargas y pagos de servicios.
- Configurar impresoras.
- Configurar la tienda.
- Administrar usuarios, solo si se concede expresamente.
- Importar o exportar información.

Un botón visible o atajo de teclado nunca debe evadir un permiso. La API vuelve a validar identidad, permiso, turno, estado y reglas de negocio. Para acciones sensibles permite una autorización de administrador de un solo uso, ligada a la operación específica, con caducidad breve y auditoría.

## Interfaz visual

Usa un tema claro:

- Blanco como color principal.
- Tonos azul muy claro para fondos secundarios y selección.
- Azul medio para acciones principales.
- Gris claro para tablas, divisiones y estados inactivos.
- Texto oscuro con contraste accesible.
- Verde para éxito, amarillo para advertencia y rojo para error, cancelación o riesgo.
- Evita tema oscuro por defecto, colores neón, exceso de gradientes y pantallas saturadas.

La interfaz debe funcionar bien con teclado, lector de códigos y pantalla de resolución modesta. Usa botones grandes, foco visible, orden de tabulación coherente, mensajes concretos y el atajo escrito en el botón o junto a la acción.

Barra principal obligatoria:

- F1 Ventas.
- F2 Clientes.
- F3 Productos.
- F4 Inventario.
- Botón Configuración.
- Botón Corte.
- Botón para minimizar.
- Botón Salir.

Configuración y Corte no deben apropiarse de una tecla F que eleventa usa con otro significado dentro de ventas.

## Atajos de teclado compatibles con la operación conocida

Implementa y documenta estos atajos:

- F1: Ventas.
- F2: Clientes o créditos.
- F3: Productos.
- F4: Inventario.
- En Ventas, F5: cambiar entre tickets o ventas en atención.
- En Ventas, F6: dejar o recuperar venta pendiente.
- En Ventas, F9: verificador de precios.
- En Ventas, F10: buscar producto.
- En Ventas, F11: activar o aplicar mayoreo según las reglas configuradas.
- En Ventas, F12: cobrar.
- En la ventana de cobro, F1: cobrar e imprimir.
- En la ventana de cobro, F2: cobrar sin imprimir.
- En la ventana de cobro, F4: notas de venta.
- Esc: cancelar o cerrar el diálogo actual sin confirmar operaciones.

Para reimpresión usa un botón claramente visible en Historial de ventas y, opcionalmente, Ctrl+P, sin modificar las asignaciones anteriores. Cualquier atajo debe ignorarse de manera segura cuando el contexto no corresponda y debe informar si falta permiso.

## Portapapeles y edición

- Permite Ctrl+C, Ctrl+X, Ctrl+V y Ctrl+A en campos donde corresponda.
- Agrega menú contextual con Copiar, Cortar, Pegar y Seleccionar todo.
- Al copiar filas de una tabla, usa texto tabulado que pueda pegarse en Excel.
- Lo copiado debe permanecer en el portapapeles aunque se cierre el programa. En WPF usa la modalidad persistente del portapapeles, por ejemplo Clipboard.SetDataObject(data, true).
- Todo dato pegado se valida con las mismas reglas que el dato escrito.
- No permitas copiar contraseñas, tokens, claves privadas ni secretos de integración desde pantallas administrativas.
- Nunca ejecutes fórmulas, macros, HTML o comandos contenidos en un texto pegado.

## Salida del programa y manejo de turnos

Al pulsar Salir, si existe un turno abierto muestra exactamente estas alternativas:

1. Cerrar turno y salir.
2. Dejar turno abierto y salir.
3. Cancelar.

Dejar el turno abierto no realiza corte. Al reiniciar, detecta el turno, muestra tienda, caja, usuario y hora de apertura, y permite continuarlo después de autenticar al mismo cajero o a un administrador autorizado. No crees otro turno por accidente.

Si hay ventas aún no cobradas, borradores o trabajos de integración en proceso, informa su estado sin convertirlos en ventas finalizadas. El cierre de ventana, apagado de Windows y cierre desde el administrador de tareas deben tener recuperación segura.

## Módulos funcionales

### Ventas

- Lectura rápida de código de barras y búsqueda por código, descripción, categoría o palabra parcial.
- Varias ventas o tickets simultáneos.
- Venta pendiente y recuperación.
- Productos por pieza, unidad, peso o cantidad fraccionada.
- Productos compuestos, paquetes o kits.
- Producto ocasional con permiso.
- Precios normal y mayoreo.
- Promociones y descuentos con reglas auditables.
- Impuestos configurables.
- Notas.
- Pagos en efectivo, tarjeta, transferencia, vale, crédito y pago mixto.
- Soporte opcional para dólares con tipo de cambio registrado en la operación.
- Cálculo de recibido y cambio.
- Cancelación de partida, cancelación total y devolución con motivos.
- Historial, consulta y reimpresión como copia.

### Productos

- Código principal y múltiples códigos de barras.
- Descripción corta y detallada.
- Departamento o categoría.
- Unidad de medida.
- Costo, precio, precio de mayoreo, margen e impuestos.
- Existencia, mínimo, máximo y control de inventario activable.
- Proveedor principal.
- Estado activo o inactivo.
- No reutilizar identificadores internos de productos eliminados.

### Inventario

- Kardex inmutable de movimientos.
- Entradas, salidas, ajustes, compras, ventas, cancelaciones, devoluciones y mermas.
- Cada ajuste requiere motivo, usuario, fecha, existencia anterior y posterior.
- Alertas de inventario bajo.
- Conteos físicos y conciliación.
- Costo promedio ponderado o política de costo configurable y documentada.
- Reporte de valoración y movimientos.
- Nunca permitas cambiar una existencia histórica editando directamente un número sin generar movimiento.

### Clientes y crédito

- Datos de contacto y fiscales opcionales.
- Saldo, límite de crédito y estado de cuenta.
- Ventas a crédito, abonos, liquidaciones y ajustes autorizados.
- Recibos de abono.
- Historial inmutable y auditoría.
- Evita que una edición o cancelación deje saldos sin correspondencia.

### Proveedores y compras

- Catálogo de proveedores.
- Órdenes de compra.
- Recepción parcial o total.
- Actualización transaccional de costo e inventario.
- Cuentas o referencias del documento del proveedor.
- Historial y devoluciones a proveedor.

### Caja y turnos

- Apertura con fondo inicial.
- Entradas y salidas de efectivo con concepto.
- Corte ciego opcional.
- Totales por forma de pago.
- Efectivo esperado, contado y diferencia.
- Cierre, impresión y consulta de cortes.
- Una caja no puede tener turnos incompatibles abiertos.
- Las correcciones se registran; no se borra el historial.

### Reportes y exportación

- Ventas por fecha, hora, producto, categoría, cajero y caja.
- Utilidad y costo con permisos especiales.
- Inventario y movimientos.
- Productos más vendidos y productos sin movimiento.
- Clientes, crédito y abonos.
- Compras y proveedores.
- Turnos, cortes y diferencias.
- Recargas, pagos de servicios y comisiones.
- Exportación CSV y, cuando proceda, XLSX o PDF.
- Los reportes grandes no deben bloquear la interfaz ni degradar una venta en curso.

### Facturación

Prepara el dominio y la interfaz para un módulo futuro de CFDI, pero mantenlo deshabilitado hasta seleccionar PAC, validar obligaciones fiscales vigentes y completar pruebas de timbrado, cancelación y resguardo. No simules facturas válidas.

## Impresión térmica y ticket

En Configuración permite:

- Elegir cualquier impresora instalada en Windows.
- Detectar y probar impresoras USB, serie, Ethernet o compartidas mediante un adaptador de impresión.
- Elegir perfil 56/58 mm o 80 mm y ajustar caracteres o ancho imprimible real.
- Configurar codificación, densidad, márgenes, avance, corte de papel y apertura de cajón cuando el dispositivo lo soporte.
- Ejecutar una página de prueba sin registrar una venta.
- Tener un modo compatible con controlador de Windows y otro ESC/POS de datos crudos, con detección y diagnóstico.

El editor de ticket debe permitir activar, desactivar y ordenar:

- Logotipo.
- Nombre comercial.
- Razón social y RFC.
- Dirección y teléfono.
- Folio, caja, fecha, hora y cajero.
- Detalle de productos, cantidades, precios, descuentos e impuestos.
- Subtotal, total, formas de pago, recibido y cambio.
- Datos del cliente.
- Código de barras o QR.
- Mensaje final y políticas.
- Márgenes, alineación, tamaño, negritas y separadores.

La venta y la impresión son procesos separados. Primero confirma la venta en la base; después crea un PrintJob con identificador único. Un fallo de papel, controlador o energía no puede provocar una segunda venta. Una reimpresión debe mostrar COPIA y quedar auditada con usuario, fecha, motivo y ticket original.

## Integridad de datos y resistencia a apagones

PostgreSQL es la fuente de verdad. Configura y conserva activados fsync, synchronous_commit y full_page_writes; usa WAL y habilita checksums de páginas al crear el clúster si la versión elegida lo soporta. No uses configuraciones de rendimiento que acepten perder transacciones confirmadas.

Una venta finalizada debe escribirse dentro de una sola transacción de base de datos que incluya, como mínimo:

- Encabezado de venta.
- Partidas.
- Pagos.
- Movimientos de inventario.
- Movimiento de caja.
- Cuentas por cobrar si aplica.
- Evento de auditoría.
- Registro de salida para impresión o integración.

Todo se confirma o todo se revierte.

## Idempotencia obligatoria

- El cliente genera un operation_id UUID antes de enviar una operación final.
- La base tiene una restricción UNIQUE sobre ese identificador en el alcance correcto.
- La API devuelve el resultado existente cuando recibe de nuevo la misma operación válida.
- Deshabilitar el botón después del primer clic mejora la interfaz, pero no sustituye la protección de la base.
- Si se corta la conexión y el cliente no sabe si se confirmó, consulta por operation_id; no repite ciegamente la venta.
- Aplica el mismo patrón a cobros externos, recargas, pagos de servicios, importaciones, abonos y cancelaciones.

## Reglas de persistencia

- Usa claves primarias, foráneas, NOT NULL, UNIQUE y CHECK apropiados.
- Usa timestamps con zona horaria para eventos y conserva la zona de negocio por separado.
- Usa consultas parametrizadas; prohíbe concatenar entrada del usuario en SQL.
- Define niveles de aislamiento y bloqueos de forma explícita para existencias, folios, turnos y saldos.
- Las ventas finalizadas son inmutables. Una corrección crea cancelación, devolución o reverso relacionado.
- No borres movimientos financieros, de inventario o auditoría.
- Separa borradores de ventas confirmadas. Un borrador puede guardarse automáticamente, pero no afecta caja ni inventario.
- Usa una bandeja de salida transaccional para impresión, webhooks y tareas externas.
- Procesa trabajos con estados, intentos, próxima ejecución, error sanitizado y bloqueo contra doble procesamiento.
- Registra toda transición sensible con usuario, caja, equipo, fecha y correlación.

## Validación de entradas

- Valida en interfaz, API, dominio y base de datos según corresponda.
- Rechaza cantidades negativas, NaN, desbordamientos, códigos inválidos y fechas imposibles.
- Normaliza códigos y textos sin destruir datos significativos.
- Define límites de longitud y precisión.
- No permitas que datos importados evadan las reglas normales.
- Devuelve mensajes entendibles al usuario y detalles técnicos solo en el registro protegido.

## Respaldos, restauración y recuperación

- Respaldo automático diario y respaldo adicional antes de una actualización o importación.
- Retención configurable y cifrado cuando el destino salga del equipo.
- Permite guardar una copia en disco externo o ubicación de red; no consideres seguro un respaldo que solo está en el mismo disco.
- Usa herramientas coherentes de PostgreSQL y registra versión, checksum, fecha, tienda y resultado.
- Implementa y prueba restauración completa en una base temporal.
- Incluye verificación periódica de respaldos y alerta si llevan demasiado tiempo fallando.
- Recomienda UPS para la computadora principal, servidor, switch e impresora, sin tratarla como sustituto de transacciones y respaldos.
- Incluye herramienta de diagnóstico para revisar servicio, conexión, espacio, checksums, migraciones, cola de impresión y último respaldo.

## Varias computadoras por red local

En una instalación multicaja:

- Una computadora actúa como servidor principal y aloja API y PostgreSQL.
- Las cajas adicionales se conectan únicamente a la API por IP o nombre local.
- No compartas archivos de base de datos por carpetas de Windows.
- No abras PostgreSQL directamente a todas las cajas si no es indispensable.
- El asistente debe descubrir o permitir escribir el servidor, probar latencia y versión, y emparejar la caja mediante un código temporal.
- Asigna un identificador único a tienda, equipo y caja.
- Crea reglas de Firewall de Windows restringidas al perfil privado y al puerto necesario.
- Rechaza versiones de cliente incompatibles con la API.
- Muestra claramente cuando se pierde la conexión y evita confirmar operaciones cuyo resultado sea desconocido.
- Señaliza cambios con SignalR y confirma el estado autoritativo mediante la API.

No implementes funcionamiento desconectado completo de cajas adicionales en la primera fase. Diseña una futura cola local con SQLite y patrón outbox solo si el negocio acepta reglas claras de conflicto. No prometas sincronización automática de inventario y folios sin un diseño explícito.

## Opción futura en Hostinger

La opción recomendada es un VPS de Hostinger con API ASP.NET Core y PostgreSQL administrado en el propio VPS o en un servicio compatible. No conectes el cliente WPF directamente a una base PostgreSQL publicada en Internet.

- Expón únicamente la API por HTTPS.
- No publiques el puerto 5432 a Internet.
- Usa TLS, firewall, actualizaciones, copias externas, monitoreo y rotación de secretos.
- Considera WireGuard o Tailscale para acceso privado entre tiendas y servidor.
- Separa ambientes de pruebas y producción.
- Empaqueta despliegue reproducible, por ejemplo con contenedores, sin convertir Docker en requisito del cliente Windows.
- Verifica que el plan exacto de Hostinger permita procesos persistentes, puertos y recursos requeridos; un hosting compartido tradicional no debe asumirse adecuado.
- Diseña recuperación ante caída de Internet antes de activar nube en producción.

## Integración con Mercado Pago Point

Implementa la integración detrás de una interfaz de proveedor para poder activarla, sustituirla o deshabilitarla sin afectar una venta en efectivo.

Requisitos:

- Usa la API oficial vigente para Point en México; valida si el flujo aplicable es Orders API.
- Cada terminal debe quedar asociada a la tienda y caja correctas.
- Guarda Access Token, OAuth y secretos solamente en el servicio, cifrados; nunca en el cliente, repositorio o registros.
- Usa una clave de idempotencia por intento de cobro.
- Crea primero una intención local pendiente y relaciona IDs local, de orden, pago, terminal y venta.
- Confirma el pago mediante consulta autoritativa o webhook verificado, no solo por lo que muestre el cliente o la terminal.
- Valida autenticidad y firma de webhooks, evita repetición y registra el cuerpo sanitizado con correlación.
- Modela estados pendiente, enviado, procesando, aprobado, rechazado, cancelado, expirado, desconocido y reembolsado según la documentación vigente.
- Permite reconciliar cobros cuyo resultado quedó desconocido tras una desconexión.
- Nunca marques dos veces una venta ni descuentes dos veces inventario por recibir el mismo evento.
- Incluye sandbox o modo de prueba y una lista de pruebas con terminal física antes de producción.
- Documenta qué modelos de terminal son compatibles; no supongas que cualquier dispositivo Mercado Pago, Sr. Pago o lector NFC sirve como periférico genérico.

## Integración con Taecel

Taecel requiere un proceso de alta e integración que puede incluir cuestionario tecnológico, credenciales de pruebas, verificación y habilitación de producción. No inventes endpoints, parámetros ni respuestas. Solicita y conserva la documentación y credenciales oficiales vigentes cuando estén disponibles.

Crea un módulo desacoplado que contemple:

- Consulta y sincronización de catálogo de operadores, productos, recargas y servicios.
- Recargas telefónicas.
- Pagos de servicios disponibles para la cuenta.
- Consulta de saldo, costo, comisión y límites cuando la API lo permita.
- Registro de folio local, folio Taecel, referencia, teléfono o contrato, producto, monto, comisión, estado, caja y usuario.
- Validación doble de número o referencia antes de enviar.
- Idempotencia para impedir compras duplicadas por doble clic, timeout o reintento.
- Estados iniciado, enviado, pendiente, exitoso, rechazado, desconocido, cancelado o revertido conforme a la API real.
- Consulta posterior de estado antes de volver a enviar una operación dudosa.
- Impresión de comprobante sin duplicar la operación.
- Conciliación diaria de saldo, operaciones y comisiones.
- Restricción por permisos y límites configurables por caja.
- Manejo explícito de falta de Internet o saldo insuficiente.
- Ambiente de pruebas separado de producción.

Los secretos de Taecel viven únicamente en el servicio. Ocúltalos en la interfaz y en los registros. Toda operación externa debe poder auditarse sin guardar datos sensibles innecesarios.

## Migración desde eleventa

Construye un asistente de migración para CSV y otros formatos que eleventa realmente exporte. No presupongas que un solo archivo contiene historial completo.

Flujo obligatorio:

1. Seleccionar una carpeta o varios archivos sin modificar los originales.
2. Detectar codificación, separador, encabezados, comillas, formato decimal y fechas.
3. Permitir asignar columnas de origen a campos de destino.
4. Mostrar una vista previa.
5. Validar todos los registros en modo simulación.
6. Clasificar filas válidas, advertencias, errores y duplicados.
7. Elegir reglas de duplicados por código de barras, código interno y nombre.
8. Crear respaldo previo.
9. Importar dentro de una transacción o por lotes atómicos con punto de recuperación claramente documentado.
10. Mostrar resumen y generar un reporte descargable de todo lo aceptado, transformado, omitido o rechazado.
11. Permitir deshacer una importación identificada mientras no existan operaciones posteriores incompatibles.

Considera productos, códigos, categorías, precios, costos, impuestos, existencias, clientes, saldos, proveedores y cualquier otro dato presente. No inventes información ausente. Cuando eleventa no exporte ventas históricas o movimientos completos, conserva los archivos originales como archivo legado de solo lectura y explica qué sí se migró.

Prueba el importador con:

- UTF-8 y codificaciones comunes de Windows.
- Coma, punto y coma y tabulador.
- Campos con comas, saltos de línea y comillas.
- Códigos con ceros iniciales.
- Acentos y letra ñ.
- Precios o existencias inválidas.
- Filas repetidas.
- Archivos grandes.
- Interrupción de energía simulada durante la importación.

## Seguridad

- Aplica mínimo privilegio a base, servicio, carpetas y usuarios.
- No ejecutes el cliente normalmente como administrador de Windows.
- Firma digitalmente ejecutables e instalador cuando exista certificado.
- Verifica hash o firma de todos los componentes incluidos en el instalador.
- No guardes secretos en Git, appsettings.json, logs, tickets o respaldos sin cifrar.
- Proporciona .env.example o plantillas sin valores reales únicamente para desarrollo.
- Enmascara tokens, contraseñas, teléfonos y referencias sensibles en diagnósticos exportables.
- Protege contra SQL injection, path traversal, CSV formula injection, deserialización insegura y archivos de importación maliciosos.
- La auditoría es append-only para usuarios normales.
- Incluye bloqueo y expiración de sesiones, y registra la caja desde la que se actuó.
- Si se añade licencia comercial, debe estar basada en cuenta o suscripción con periodo de gracia y modo de solo lectura. La exportación y restauración de los datos siempre permanecen disponibles.

## Observabilidad y errores

- Usa IDs de correlación entre cliente, API, base, impresión e integraciones.
- Registra eventos estructurados con nivel y contexto, sin secretos.
- Rota registros y limita espacio.
- Presenta al cajero mensajes cortos con acción recomendada.
- Genera paquetes de diagnóstico sanitizados con consentimiento del administrador.
- No ocultes excepciones ni continúes después de una transacción parcialmente conocida.
- Diferencia claramente error de validación, conexión, impresora, proveedor externo y fallo interno.

## Pruebas mínimas obligatorias

### Unitarias

- Totales, impuestos, descuentos, mayoreo y cambio.
- Reglas de inventario, crédito, permisos y turnos.
- Normalización y validación.
- Transiciones de estado de pagos, recargas e impresión.

### Integración con PostgreSQL real

- Transacciones completas y rollback.
- Restricciones y concurrencia.
- Idempotencia.
- Migraciones hacia adelante.
- Recuperación después de terminar procesos abruptamente.
- Importaciones y respaldos.

No sustituyas todas las pruebas de PostgreSQL por una base en memoria o SQLite, porque su comportamiento transaccional y de concurrencia es diferente.

### Extremo a extremo

- Primera instalación y asistente inicial.
- Inicio de sesión y permisos híbridos.
- Apertura de turno, venta, cobro, ticket y corte.
- Dejar turno abierto y reanudarlo.
- Doble clic en Cobrar.
- Timeout después de confirmar la venta.
- Impresora apagada y reimpresión como copia.
- Dos cajas vendiendo simultáneamente el mismo producto.
- Cancelación, devolución, abono y ajuste.
- Importación de CSV.
- Actualización sobre una versión anterior.
- Desinstalación conservando datos y reinstalación reconociendo la tienda.
- Mercado Pago y Taecel con dobles webhooks, timeouts y resultados desconocidos.

### Pruebas de instalación

- Windows 10 x64 limpio.
- Windows 11 x64 limpio.
- Equipo sin .NET instalado.
- Equipo con otra versión de PostgreSQL.
- Puerto predeterminado ocupado.
- Usuario sin privilegios administrativos.
- Disco casi lleno.
- Reinicio durante instalación o actualización.
- Reparación y desinstalación.

## Fases de implementación

### Fase 0: descubrimiento y base técnica

- Inventario de funciones de eleventa y matriz de paridad.
- Historias de usuario y criterios de aceptación.
- Prototipo navegable de pantallas y atajos.
- Arquitectura, modelo de datos y amenazas.
- Repositorio, CI, estilo, configuración y scripts de desarrollo.

### Fase 1: punto de venta local esencial

- Instalación de desarrollo sin reinstalaciones.
- Primera configuración.
- Usuarios y permisos.
- Productos, ventas, inventario, turnos, cobro en efectivo.
- Impresión térmica básica.
- Transacciones, idempotencia, auditoría y respaldos.
- Importador CSV inicial.

### Fase 2: operación comercial completa

- Clientes, crédito y abonos.
- Proveedores y compras.
- Mayoreo, promociones, kits, devoluciones y reportes.
- Editor completo de ticket.
- Instalador de producción y actualizaciones seguras.

### Fase 3: multicaja por red local

- Instalación servidor/caja adicional.
- Emparejamiento, API en LAN, SignalR y concurrencia.
- Pruebas con varias cajas y fallas de red.

### Fase 4: integraciones

- Mercado Pago Point en sandbox y después terminal real.
- Taecel en pruebas y después producción.
- Conciliación, webhooks, idempotencia y soporte operativo.

### Fase 5: nube y funciones opcionales

- VPS de Hostinger y acceso seguro.
- Estrategia ante caída de Internet.
- CFDI solo después de seleccionar PAC y validar normativa.

Cada fase debe producir una aplicación utilizable, una lista de pruebas ejecutadas, problemas conocidos y un paquete portátil de revisión. No generes el instalador final en cada cambio; genéralo en hitos y en pruebas de liberación.

## Criterios de aceptación no negociables

El sistema no se considera listo si ocurre cualquiera de estos puntos:

- Una venta puede registrarse dos veces por doble clic, reintento o timeout.
- Un ticket fallido causa una segunda venta.
- Un apagón deja media venta registrada.
- Se puede borrar una venta, movimiento de caja, inventario o auditoría sin rastro.
- Un cajero evade permisos mediante un atajo o llamada directa a la API.
- Una actualización borra o reinicializa la base.
- La desinstalación elimina datos por defecto.
- La aplicación exige instalar .NET manualmente.
- El usuario debe instalar PostgreSQL o pgAdmin por su cuenta en la modalidad servidor.
- Una caja adicional accede directamente a archivos compartidos de base de datos.
- PostgreSQL queda expuesto públicamente en Internet.
- Se confirma un pago externo únicamente por la respuesta visual del cliente.
- Se reenvía una recarga o cobro de estado desconocido sin consultar primero.
- El importador modifica el CSV original o deja una importación parcial silenciosa.
- El programa no puede restaurarse en otro equipo después de una falla de hardware.

## Entregables finales

- Código fuente completo y repositorio Git limpio.
- Solución que compila sin errores ni advertencias importantes.
- Scripts de desarrollo y paquete portátil de revisión.
- Un solo Setup.exe firmado cuando sea posible.
- Instalador con modalidades Servidor principal, Caja adicional, Actualizar y Reparar.
- Base de datos versionada y migraciones.
- Pruebas unitarias, de integración, E2E y de instalación.
- Documentación técnica y manuales de usuario.
- Plantillas de respaldo, restauración y diagnóstico.
- Matriz de compatibilidad de impresoras y terminales probadas.
- Lista de dependencias, licencias y versiones.
- Procedimiento de publicación y actualización.
- Paquete de liberación con checksum.

## Cómo debes responder y avanzar

Comienza entregando, en este orden:

1. Resumen ejecutivo y supuestos.
2. Matriz de paridad funcional con eleventa: imprescindible, fase posterior y descartado.
3. Arquitectura propuesta y diagrama textual.
4. Modelo de datos inicial con entidades, relaciones, restricciones e índices.
5. Diseño de ejecución de desarrollo, paquete portátil e instalador único.
6. Plan de fases con criterios de aceptación y riesgos.
7. Wireframes textuales de primera configuración, Ventas, Cobro, Productos, Inventario, Corte y Configuración.
8. Matriz de permisos y atajos.
9. Estructura inicial del repositorio.
10. Implementación de la Fase 0 y después la Fase 1 en incrementos verificables.

No intentes escribir todo el sistema en una sola respuesta. Conserva una lista clara de pendientes. Cuando entregues código, indica exactamente cómo iniciarlo sin instalar, cómo probar el cambio y qué evidencia confirma que funciona.

## Fuentes oficiales que deben revisarse y mantenerse actualizadas

Verifica la documentación vigente antes de implementar; si cambió, registra la fecha y la decisión:

- Publicación autocontenida y de archivo único en .NET: https://learn.microsoft.com/en-us/dotnet/core/deploying/ y https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview.
- WPF en la versión actual de .NET: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/.
- WiX Burn para un instalador que encadena paquetes: https://docs.firegiant.com/wix/tools/burn/.
- Instaladores de PostgreSQL para Windows: https://www.postgresql.org/download/windows/.
- Parámetros de instalación desatendida de PostgreSQL: https://www.enterprisedb.com/docs/supported-open-source/postgresql/installing/command_line_parameters/.
- Fiabilidad WAL de PostgreSQL: https://www.postgresql.org/docs/current/wal-reliability.html.
- Checksums de PostgreSQL: https://www.postgresql.org/docs/current/checksums.html.
- Restricciones de PostgreSQL: https://www.postgresql.org/docs/current/ddl-constraints.html.
- Portapapeles persistente en WPF: https://learn.microsoft.com/en-us/dotnet/api/system.windows.clipboard.setdataobject.
- Flujo de Mercado Pago Point: https://www.mercadopago.com.mx/developers/es/docs/mp-point/payment-processing.
- Configuración de terminal Mercado Pago Point: https://www.mercadopago.com.mx/developers/es/docs/mp-point/configure-terminal.
- Integración de servicios Taecel: https://taecel.com/portal/integracion-web-services.
- Introducción y módulos de eleventa: https://eleventa.com/punto-de-venta/introduccion.
- Navegación y accesos de eleventa: https://eleventa.com/aprender/conociendo-eleventa.
- Cobro en eleventa: https://eleventa.com/aprender/cobrando-una-venta.
- Manejo de varios tickets: https://eleventa.com/aprender/atender-a-varios.
- Verificador, búsqueda y mayoreo: https://eleventa.com/aprender/verificador-de-precios.
- Manejo de turnos: https://eleventa.com/aprender/manejo-de-turnos.
- Respaldos de eleventa: https://eleventa.com/aprender/respaldo-automatico.
- Problemas de base de datos en eleventa: https://eleventa.com/aprender/problema-base-datos.
- Migración desde eleventa: https://eleventa.com/aprender/migrar-abarrotes-punto-de-venta.
- Conexión multicaja en eleventa: https://eleventa.com/aprender/conexion-multicaja.

No uses blogs, videos o respuestas de foros como fuente principal cuando exista documentación oficial. En integraciones externas, no completes huecos inventando contratos: crea adaptadores, pruebas y marcadores explícitos hasta obtener credenciales y documentación reales.
