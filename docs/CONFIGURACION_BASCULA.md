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
- Modelo de protocolo: genérico o **Torrey PCR-40**.

La configuración se guarda localmente en cada computadora/caja y queda disponible después de reiniciar. No modifica la configuración de otras cajas.

## Torrey PCR-40

Selecciona `Torrey PCR-40 (USB/COM)` si Windows muestra la báscula como puerto COM. JetVenta aplica los valores recomendados `115200`, `8N1`, unidad kilogramo y terminador `CR`. Al leer, envía el carácter ASCII `P`, que es el comando de consulta del equipo; no espera que la báscula transmita peso por sí sola.

Prueba primero desde **Configuración > Báscula > Probar lectura**. Después, al agregar o editar un artículo vendido por kilogramo, gramo o granel, usa **Tomar peso de báscula** en la ventana de cantidad. El cajero puede revisar o ajustar el valor antes de aceptarlo.

## Prueba de lectura

`Probar lectura` abre el puerto configurado, limpia el búfer, espera una línea y muestra tanto el texto recibido como el primer número válido encontrado. La lectura no se agrega a una venta durante esta prueba.

JetVenta no declara que una lectura sea estable por el simple hecho de recibir texto. La estabilidad, unidad y formato dependen del protocolo del modelo de báscula. Para integrar automáticamente el peso en una venta se debe validar el modelo y su manual, especialmente cuando el equipo no envía una línea de peso continuamente.

## Controladores

Una báscula USB tipo HID o un dispositivo USB propietario no necesariamente crea un COM y no se puede manejar con esta configuración serial. En ese caso se necesita el controlador o SDK oficial del fabricante. No se instalan controladores genéricos desde JetVenta.

La lectura serial usa `SerialPort` con tiempo de espera para evitar que una báscula desconectada bloquee la interfaz. `DataReceived` se reserva para una futura lectura continua porque sus eventos se ejecutan en un hilo secundario y no garantizan un evento por cada byte recibido.
