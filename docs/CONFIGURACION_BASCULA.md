# Configuración de báscula

JetVenta admite básculas que Windows expone como un puerto serial COM. Esto incluye una báscula conectada por RS-232 y algunos modelos USB que instalan un controlador del fabricante y crean un puerto COM virtual.

## Configuración

En `Configuración > Báscula` se puede activar la báscula y elegir:

- Puerto COM detectado por Windows.
- Velocidad: 1200 a 115200 baudios.
- Paridad, bits de datos y bits de parada.
- Terminador de lectura `CRLF`, `CR` o `LF`.
- Unidad de lectura: kilogramo, gramo o libra.
- Tiempo de espera entre 200 y 5000 milisegundos.

La configuración se guarda en la base de datos de la tienda y queda disponible después de reiniciar o migrar la instalación.

## Prueba de lectura

`Probar lectura` abre el puerto configurado, limpia el búfer, espera una línea y muestra tanto el texto recibido como el primer número válido encontrado. La lectura no se agrega a una venta durante esta prueba.

JetVenta no declara que una lectura sea estable por el simple hecho de recibir texto. La estabilidad, unidad y formato dependen del protocolo del modelo de báscula. Para integrar automáticamente el peso en una venta se debe validar el modelo y su manual, especialmente cuando el equipo no envía una línea de peso continuamente.

## Controladores

Una báscula USB tipo HID o un dispositivo USB propietario no necesariamente crea un COM y no se puede manejar con esta configuración serial. En ese caso se necesita el controlador o SDK oficial del fabricante. No se instalan controladores genéricos desde JetVenta.

La lectura serial usa `SerialPort` con tiempo de espera para evitar que una báscula desconectada bloquee la interfaz. `DataReceived` se reserva para una futura lectura continua porque sus eventos se ejecutan en un hilo secundario y no garantizan un evento por cada byte recibido.
