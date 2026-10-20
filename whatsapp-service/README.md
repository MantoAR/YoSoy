# whatsapp-service

Puente HTTP interno entre la API de YoSoy y WhatsApp, usando [Baileys](https://github.com/WhiskeySockets/Baileys) (WhatsApp Web sin
navegador; no oficial, no es la API de Meta). Pensado para un chip dedicado, solo para esto; hay riesgo de bloqueo asumido a propósito,
no es para un número de uso personal.

Solo escucha en `127.0.0.1` — nunca se expone a internet. La API .NET lo llama vía `CanalBaileys` (`Services/Mensajeria/`); este servicio
le reenvía los mensajes entrantes por su cuenta.

> **Un solo motor.** `whatsapp-web.js` (Puppeteer y Chromium) se sacó por completo: Baileys es el único motor. Qué quedó, qué se sacó y qué
> hay que limpiar en el VPS: [`docs/PROGRESO_WHATSAPP_SOLO_BAILEYS.md`](../docs/PROGRESO_WHATSAPP_SOLO_BAILEYS.md). Las variables del motor
> viejo están en "Variables retiradas", más abajo.
>
> **Sin recordatorios por WhatsApp.** La API ya no manda recordatorios de turno por WhatsApp (siguen por mail y campanita). Este servicio
> nunca manda nada por su cuenta: solo lo que la API le pide con `POST /enviar`, y reenvía a la API lo que llega.

## La interfaz HTTP (la que usa la API)

| Ruta | Para qué | Pide secreto |
|---|---|---|
| `GET /salud` | 200 si está conectado y 503 si no, con el estado, el motivo, la cola y los topes (ver "`/salud` y apagado ordenado"). Solo loopback | no |
| `GET /estado` | `{ conectado, numero, qrPendiente, qr, estado, motivo }`. El QR viaja acá, como imagen | sí |
| `POST /enviar` | `{ telefono, texto, optIn? }` → `{ exito, idExterno }` o `{ exito:false, error, codigo }`. Responde apenas encola | sí |
| `POST /desvincular` | Cierra la sesión y deja un QR nuevo. Responde apenas valida, sin esperar al reinicio | sí |
| `POST /vincular` | Abre una ventana de vinculación por QR a pedido (el QR se ve en `GET /estado`). 409 si no corresponde | sí |

El secreto es `X-Shared-Secret` (`Mensajeria__WhatsApp__Secreto`), comparado en tiempo constante. La app está en `servidor.js`.

### Lo que se le reenvía a la API (contrato v2 del webhook)

Cada mensaje de una persona (de un chat de a uno: no los grupos, las listas de difusión, los estados ni los canales, ni lo que se escribe desde el teléfono del número) va a
`POST {API_BASE_URL}/mensajeria/whatsapp/entrante`, con el secreto compartido:

```json
{ "telefono": "5491122334455", "texto": "[Imagen] mi receta", "tipo": "imagen", "id": "3EB0A1B2C3D4E5F6A7B8", "ts": 1760000123, "nombre": "Juan Pérez" }
```

| Campo | Qué es |
|---|---|
| `telefono`, `texto` | De siempre. Con una foto, un audio... el `texto` es un **aviso del tipo** (abajo) |
| `tipo` | `texto`, `audio`, `imagen`, `video`, `documento`, `sticker`, `ubicacion` o `contacto` |
| `id` | El id del mensaje en WhatsApp. La API lo usa para no guardar dos veces el mismo mensaje (este servicio reintenta si la API falla) |
| `ts` | La hora a la que se escribió, en segundos desde 1970 (la API la usa si es razonable: ni futura ni de un día o más atrás) |
| `nombre` | El nombre del perfil de WhatsApp (decisión D5), sin saltos de línea ni invisibles y de hasta 100 caracteres. La API lo guarda en la conversación; **nunca** se escribe en un log |

Los cuatro campos nuevos son opcionales: una API que no los conoce los ignora, y la API nueva acepta el cuerpo de siempre (`{ telefono, texto }`).

**Fotos, audios y documentos (decisión D4):** no se descargan ni se guardan. Viaja un aviso con el tipo (`[Imagen]`, `[Video]`, `[Audio]`, `[Documento]`, `[Sticker]`,
`[Ubicación]`, `[Contacto]`) y, si la trae, la leyenda a continuación (`[Imagen] mi receta`). Ni el nombre de un archivo ni lo que trae una ubicación o un contacto compartido
viajan. Cuenta como "escribió primero" para el consentimiento, igual que un texto, y la API puede contestarle; pero su aviso **no** se interpreta como BAJA ni como ALTA. No viajan
las reacciones, las ediciones, los borrados, las encuestas ni los mensajes de protocolo.

**Si la API falla (decisión D6)** el reenvío se reintenta **solo en memoria**: el texto de un mensaje nunca se escribe a disco.
- Se reintenta ante un error de red, un tiempo de espera agotado (5 s), un 5xx o un 429. **No** ante otro 4xx (un secreto que no coincide o un cuerpo inválido daría lo mismo).
- Hasta 3 reintentos, a los 5 s, 30 s y 2 minutos del intento anterior.
- Los mensajes de una misma persona salen de a uno y en orden; los de otra persona no esperan.
- Hasta 200 mensajes esperando a la vez: el resto se pierde (y se cuenta).
- El reintento manda exactamente lo mismo (el mismo `id`): si la API ya lo había recibido, no lo guarda dos veces.
- **Lo que está esperando se pierde si se agotan los reintentos o si se apaga el servicio** (no hay otro lugar donde guardarlo, y escribirlo a disco está descartado): el log dice
  cuántos, nunca cuáles. Quedan en el teléfono del número dedicado, si es el dispositivo principal.

## Baileys: variables

Van en `/opt/yosoy/config/whatsapp.env` (o en la `Environment=` de la unit). Todas son opcionales salvo la primera.

| Variable | Para qué |
|---|---|
| `WHATSAPP_BAILEYS_SESSION_PATH` | **Obligatoria.** Carpeta de la sesión (es una credencial): ruta absoluta, **fuera del repositorio** y de la carpeta de la aplicación. |
| `WHATSAPP_BAILEYS_VERSION_WA` | Fija la versión de WhatsApp Web, por ejemplo `2.3000.1037753511`. Solo si WhatsApp rechaza la que trae la librería (cierre con 405 al arrancar). |
| `WHATSAPP_BAILEYS_BUSCAR_VERSION` | `1` para pedirle a Baileys la última versión conocida al arrancar (hace un pedido a `raw.githubusercontent.com`). |
| `WHATSAPP_BAILEYS_VENTANAS_QR` | Cuántas ventanas de QR (~2,7 minutos cada una) se abren solas sin número vinculado antes de esperar a que una persona pida otra. De 1 a 5; por defecto 2. |

### Variables retiradas (eran de whatsapp-web.js)

Ya no se leen. Si el entorno todavía las trae, el servicio arranca igual y **avisa en el log con los nombres** (nunca con los valores); se pueden
borrar de `whatsapp.env` y de la unit. `WHATSAPP_BACKEND` es la única que además se controla: sin la variable, o con `baileys`, todo sigue igual;
con otro valor (por ejemplo `webjs`) el servicio **no arranca**, para que nadie crea que está usando un motor que ya no existe.

| Variable | Qué era |
|---|---|
| `WHATSAPP_BACKEND` | Elegía el motor (`webjs` por defecto o `baileys`). Ya no hay nada que elegir |
| `WHATSAPP_SESSION_PATH` | La carpeta de la sesión de whatsapp-web.js (`/opt/yosoy/whatsapp/sesion`) |
| `WHATSAPP_WEB_VERSION` | Fijaba la build de WhatsApp Web de whatsapp-web.js (en Baileys es `WHATSAPP_BAILEYS_VERSION_WA`) |
| `WHATSAPP_DEBUG` | Diagnóstico de la página de WhatsApp Web (Puppeteer) |
| `PUPPETEER_CACHE_DIR`, `PUPPETEER_SKIP_DOWNLOAD`, `PUPPETEER_EXECUTABLE_PATH` | Chromium de Puppeteer |

### Baileys: la carpeta de la sesión

La sesión de Baileys (`creds.json` y las claves de cifrado) **es una credencial de la cuenta**: quien tenga esos archivos
puede usar el número de la clínica como si fuera el teléfono. Por eso, al arrancar, el servicio exige que la carpeta de
`WHATSAPP_BAILEYS_SESSION_PATH`:

- sea **propia**: solo archivos de la sesión. Si hay otra cosa, no arranca y nunca borra nada ajeno (así una ruta mal
  puesta, como `/opt` o la carpeta de la sesión vieja de whatsapp-web.js, se frena en vez de ensuciarse o borrarse);
- esté **fuera de cualquier repositorio git y de la carpeta de la aplicación** (`/opt/yosoy/whatsapp/app`): un despliegue
  reemplaza `app/` y se llevaría la sesión puesta. Sugerida: `/opt/yosoy/whatsapp/sesion-baileys`, al lado de `app/`;
- sea del usuario que corre el servicio, con permisos **700** en la carpeta y **600** en los archivos (el servicio los
  corrige si encuentra otros, y fija un `umask` de 077 para todo lo que cree);
- se **pueda escribir**: lo comprueba escribiendo y borrando un archivito. Con `ProtectSystem=strict`, la carpeta tiene que
  estar en `ReadWritePaths` de la unit; si no, el servicio dice por qué y no arranca (si arrancara, una vinculación
  exitosa se perdería al reiniciar).

`creds.json` tiene un **respaldo** (`creds.json.bak`, la versión anterior). Baileys escribe ese archivo sin cuidado y, si lo
encuentra cortado (corte de luz, disco lleno), lo da por inexistente y arma una identidad nueva sin avisar: habría que
volver a escanear el QR. Con el respaldo, el servicio lo restaura solo y deja una línea de error en el log. Al desvincular, o
cuando WhatsApp cierra la sesión, se borran las credenciales **y** el respaldo.

El `.gitignore` de la raíz frena por nombre (`creds.json`, `pre-key-*.json`, ...) cualquier archivo de sesión, por si alguien
apuntara la variable adentro del repositorio o copiara la carpeta: es solo una red de seguridad, la regla es que no esté
ahí. **No copiar esa carpeta a ningún lado** (ni a backups sin cifrar, ni a un chat, ni a un ticket).

### Baileys: vincular el número (QR)

Para vincular el número dedicado de la clínica hay tres formas, de la más cómoda a la de respaldo. En ninguna el QR queda en un
log ni en un archivo: vive en memoria mientras vale (WhatsApp lo renueva cada ~20 segundos) y sale solo por pantalla o por
`GET /estado`, que pide el secreto. Es de un solo uso, pero mientras vale **quien lo escanee conecta SU cuenta de WhatsApp al
servicio**: mostrarlo solo a quien lo va a escanear con el teléfono del número de la clínica.

1. **Desde el Configurador** (sección WhatsApp, administrador). Sin número vinculado, el servicio abre solo una **ventana de QR**
   al arrancar. Si nadie la escanea abre una segunda (`WHATSAPP_BAILEYS_VENTANAS_QR`) y después **se queda esperando**, en
   estado `requiere_vincular` con motivo `qr_vencido`: no le pide QR a WhatsApp sin parar. Para abrir otra: "Desvincular y
   cambiar número" en el Configurador, o la forma 2.
2. **Por HTTP, en la propia máquina** (diagnóstico). `POST /vincular` abre una ventana nueva y el QR se ve en `GET /estado`
   (campo `qr`, una imagen). Responde 409 si ya hay un número vinculado o el servicio todavía se está conectando; es idempotente
   si ya hay un QR esperando.
   ```bash
   curl -s -X POST http://127.0.0.1:3002/vincular -H "X-Shared-Secret: $SECRETO"
   curl -s http://127.0.0.1:3002/estado -H "X-Shared-Secret: $SECRETO"
   ```
3. **Desde la terminal** (por SSH, sin el Configurador). `vincular.js` dibuja el QR en la pantalla, reemplazándolo cada vez que
   WhatsApp lo renueva, y termina cuando el número queda vinculado. Corre **con el servicio apagado** (dos conexiones con la
   misma cuenta se pisan) y **como el usuario del servicio** (no root: la sesión quedaría de root):
   ```bash
   sudo systemctl stop whatsapp-service
   cd /opt/yosoy/whatsapp/app
   sudo -u yosoy env WHATSAPP_BAILEYS_SESSION_PATH=/opt/yosoy/whatsapp/sesion-baileys node vincular.js
   sudo systemctl start whatsapp-service
   ```
   Se niega si la salida no es una terminal (para que el QR no termine en un archivo ni en el journal), si corre como root o
   si hay algo escuchando en el puerto del servicio. No necesita el secreto compartido ni reenvía mensajes. Códigos de salida:
   0 vinculado (o ya lo estaba), 1 no se pudo, 2 el QR venció o se agotó el tiempo, 130 Ctrl+C.

Para escanear: en el teléfono del número de la clínica, WhatsApp → Ajustes → Dispositivos vinculados → Vincular un dispositivo.
Al terminar, WhatsApp pide reiniciar la conexión y el servicio la reabre solo con la sesión ya guardada (en el log aparece
"WhatsApp conectado (número ***1234)"). Una vez vinculado no hace falta repetirlo salvo que se desvincule (desde el Configurador, desde el
teléfono en Dispositivos vinculados, o `POST /desvincular`) o WhatsApp cierre la sesión por su cuenta (por ejemplo, si el teléfono principal pasa
unos 14 días sin conectarse). **No usar el chip también desde un teléfono físico en simultáneo**: invalida la sesión vinculada acá.

### Baileys: si se desconecta sola

El servicio mira el código con el que WhatsApp cortó la conexión (`baileys/reconexion.js`) y decide. Hay cortes que se arreglan
reintentando y cortes que **no**: reintentar estos últimos en bucle no los arregla y, contra WhatsApp, es justo lo que puede
terminar en un bloqueo del número.

| Código | Qué significa | Qué hace el servicio |
|---|---|---|
| 428, 408, 503, sin código... | Corte común: la red, el ping, los servidores de WhatsApp | **Reintenta con espera creciente**: 2 s, 4 s, 8 s ... hasta 5 minutos, con ±20 % al azar. La espera vuelve a 2 s cuando una conexión se sostiene un minuto; una que abre y se cae enseguida sigue espaciándose. |
| 515 | WhatsApp pide abrir la conexión de nuevo (pasa al terminar de vincular) | Reabre enseguida, hasta 3 veces por minuto; después espacia como un corte común. |
| 401 (`loggedOut`) | Se desvinculó desde el teléfono, o WhatsApp cerró la sesión | **No reintenta.** Borra las credenciales muertas, queda en `requiere_vincular` (motivo `sesion_cerrada`) y lo avisa por `/salud`. |
| 403 | El número está restringido o bloqueado | **No reintenta.** `requiere_atencion` (motivo `cuenta_restringida`). No borra nada. |
| 440 | Otra conexión usa la misma sesión | **No reintenta**, para no pelear con la otra. `requiere_atencion` (`sesion_reemplazada`). Buscar otra copia del servicio o el comando de vinculación corriendo. |
| 405 | WhatsApp rechaza la versión de WhatsApp Web de la librería | **No reintenta.** `requiere_atencion` (`version_rechazada`). Actualizar Baileys, o fijar `WHATSAPP_BAILEYS_VERSION_WA` / `WHATSAPP_BAILEYS_BUSCAR_VERSION=1`. |
| 500 | WhatsApp rechaza la sesión guardada | Reintenta; a la tercera vez seguida, `requiere_atencion` (`sesion_invalida`), **sin borrar nada**. |

- **El proceso no se cae** en los casos que "no reintentan": sigue vivo y contesta `/salud`. Si saliera con error, systemd
  (`Restart=on-failure`) lo reiniciaría cada pocos segundos y volvería a golpear la puerta de WhatsApp: justo lo que se evita.
- **`GET /salud`** (ver "`/salud` y apagado ordenado", más abajo) contesta 200 solo si está `conectado`; si no, 503 con el estado.
  Con `loggedOut`: `requiere_vincular` + `sesion_cerrada`.
- **Para salir de `requiere_atencion`:** resolver lo que dice el log y `sudo systemctl restart whatsapp-service`. Para cambiar de
  número (por ejemplo, si el bloqueado es definitivo): "Desvincular y cambiar número" en el Configurador y vincular el nuevo.
- Los mensajes del log dicen el código, el intento y la espera (`se reintenta en 8 s (intento 3)`), nunca un teléfono.
- Además, dos redes de seguridad a nivel proceso: un `unhandledRejection` o `uncaughtException` se loguea (nunca el objeto entero del
  error) y se intenta recuperar la conexión por el mismo camino que un corte transitorio, sin tirar abajo el proceso.

### Baileys: protecciones contra el bloqueo del número

WhatsApp bloquea los números que parecen un bot o que le escriben a quien no quiere recibir mensajes. El servicio se protege solo, y **todo**
lo que sale pasa por estas protecciones (la cola no se arma sin ellas).

**Solo a quien aceptó** (`consentimiento.js`; las reglas, en `privacidad.js`). A una persona se le puede escribir si:
- ella **le escribió primero** al número de la clínica (vale 365 días desde su último mensaje, que lo renueva), o
- la API afirma que aceptó, mandando `"optIn": true` en el `POST /enviar` (solo cuenta el valor `true` exacto, y en cada pedido);

y **nunca** si escribió `BAJA` (o `STOP`, `NO MOLESTAR`, `DAR DE BAJA`...): eso pesa más que todo, aunque la API mande `optIn`,
hasta que escriba `ALTA`. Tiene que ser el mensaje entero: "cancelar" no es una baja (en el bot es cancelar un turno). Sin
constancia no se envía: `403 sin_consentimiento`. El registro (`datos/consentimientos.json`) guarda la **huella** del teléfono, no
el teléfono, y fechas; nunca el texto de un mensaje. Un mensaje de grupo, uno propio o uno que no es texto no cuenta como
constancia. **Para que por "escribió primero" solo salga algo dentro de una conversación reciente**, se puede acortar la vigencia con
`WHATSAPP_CONSENTIMIENTO_DIAS` (por ejemplo `1`: solo dentro de las 24 horas del último mensaje de la persona). No cambia lo que se manda
con `optIn:true`: eso lo decide la API.

**Ritmo humano:** una pausa al azar de 4 a 9 segundos entre mensajes, una pausa larga de 30 a 75 segundos cada 8 a 14 mensajes, y
"escribiendo..." antes de cada uno (hasta 4 segundos, según el largo del texto).

**Topes.** Se guardan en disco (`datos/envios.json`): reiniciar el servicio no devuelve el cupo del día.

| Tope | Por defecto | Variable | Al llegar |
|---|---|---|---|
| Mensajes por día (en la zona horaria de la clínica) | 100 | `WHATSAPP_TOPE_DIARIO` (1 a 1000) | `429 tope_diario`, hasta mañana |
| Mensajes por persona y por día (un aviso suelto) | 3 | `WHATSAPP_TOPE_POR_PERSONA` (1 a 20) | `429 limite_destinatario` |
| Mensajes por persona y por día, **si ella está conversando** (escribió hace menos de 24 horas) | 30 | `WHATSAPP_TOPE_EN_CONVERSACION` (1 a 200) | `429 limite_destinatario` |
| Mensajes por hora | 40 | `WHATSAPP_TOPE_POR_HORA` (1 a 500) | los aceptados esperan en la cola hasta que se libere lugar |
| Cola | 50 pendientes | — | `503 cola_llena` |

**Dos topes por persona, según si está conversando.** Un aviso suelto (algo que la clínica le manda a quien no le escribió hoy) se corta
en 3 por día: ninguna persona recibe una ráfaga, aunque la API se equivoque y repita un envío. Pero una charla con el asistente
(pedir un turno lleva cinco mensajes o más) pasa de 3 enseguida, y un tope de 3 la cortaría a la mitad. Por eso, si la persona **escribió
hace menos de 24 horas**, rige el tope de una conversación (30 por día). Los dos topes cuentan los mismos mensajes del día: no se suman. Lo
decide el servicio con lo que ya sabe (cuándo le escribió la persona): la API no tiene que avisarle nada. Los topes por día y por hora
siguen valiendo para todo.

Los mensajes que esperan en la cola ya cuentan contra el tope del día, así que todo lo que se acepta se puede mandar hoy. Un envío
que falla devuelve su lugar. **Para un número nuevo conviene empezar bajo** (por ejemplo `WHATSAPP_TOPE_DIARIO=30`) y subir de a
poco durante varias semanas: un número que de golpe manda cien mensajes por día es justo lo que WhatsApp bloquea.

Otras variables: `WHATSAPP_CONSENTIMIENTO_DIAS` (cuánto vale haber escrito primero; 365), `WHATSAPP_ZONA_HORARIA`
(`America/Argentina/Buenos_Aires`), `WHATSAPP_PAUSA_MIN_S` y `WHATSAPP_PAUSA_MAX_S` (4 y 9), `WHATSAPP_SIMULAR_ESCRITURA=0`
(apaga el "escribiendo...").

**Con WhatsApp sin conexión:** si está reconectando, los mensajes esperan y salen cuando vuelve; si hace falta una persona
(`requiere_vincular`, `requiere_atencion`), no se acepta nada (`503 no_conectado`); y uno que lleva más de 3 horas esperando se
descarta (un aviso tan tarde ya no sirve).

Lo que ve la API cuando se rechaza (`POST /enviar` → `{ "exito": false, "error": "...", "codigo": "..." }`):

| HTTP | `codigo` | Significa |
|---|---|---|
| 400 | `telefono_invalido` | el teléfono no puede ser un teléfono |
| 403 | `sin_consentimiento` | no hay constancia de que la persona acepte (ni escribió primero ni vino `optIn:true`) |
| 403 | `baja` | la persona pidió no recibir más mensajes |
| 429 | `tope_diario`, `limite_destinatario` | se llegó a un tope |
| 503 | `no_conectado`, `cola_llena`, `reiniciando` | el servicio no puede aceptarlo ahora |

> **Lo que manda la API (consentimiento de WhatsApp):** `"optIn": true` **solo cuando el paciente aceptó** recibir avisos (tabla
> `ConsentimientoWhatsApp`: lo acepta al registrarse o desde su perfil, para ese número y con el texto que vio, y lo puede revocar; ver
> `docs/PROGRESO_CONSENTIMIENTO_WHATSAPP.md`). A quien **nunca decidió** le manda el aviso **sin** `optIn`: acá decide este servicio, y solo sale
> si esa persona le escribió primero al número de la clínica (por ejemplo desde el link `wa.me` que muestra el panel); si no, se rechaza con
> `403 sin_consentimiento` y la API lo anota como "Omitido". A quien **revocó** (o escribió BAJA) la API ni lo intenta. Qué mensajes salen por
> WhatsApp (respuestas del asistente y del equipo y avisos por evento; **recordatorios, no**): `docs/PROGRESO_WHATSAPP_SOLO_BAILEYS.md`.

### Baileys: qué se escribe en el log (y qué nunca)

El log (`journalctl -u whatsapp-service`) sirve para saber qué pasa sin guardar datos de pacientes. **Nunca** se escribe:
- el **texto** de un mensaje, ni el que llega ni el que sale (ni la leyenda de una foto, ni el nombre de un archivo: las fotos, los audios y los documentos no se descargan);
- el **nombre del perfil** de WhatsApp de quien escribe (decisión D5);
- un **teléfono completo**: queda `***4455` (los últimos 4 dígitos), también dentro de los errores que repiten la librería o el sistema;
- el **QR** de vinculación (sale solo por la pantalla del comando o por `GET /estado`, que pide el secreto), ni las **credenciales** de
  la sesión, ni sus claves;
- el **secreto compartido** con la API (`Mensajeria__WhatsApp__Secreto`), ni el que pruebe alguien en `X-Shared-Secret`.

Sí se escriben los estados (`conectado`, `reconectando`...), los códigos de desconexión, los intentos y las esperas, las ventanas de QR,
los rechazos de envío (con el motivo y los 4 últimos dígitos), los errores y, al arrancar, el aviso de variables retiradas (solo sus nombres).

Se garantiza en tres capas, y las tres se prueban en cada `npm test`: **(1)** cada módulo enmascara lo suyo; **(2)** todo
lo que el proceso escribe pasa por un filtro (`crearLogSeguro`, en `privacidad.js`) que tapa el secreto compartido y el QR vigente y
enmascara cualquier número que parezca un teléfono; **(3)** una guardia revisa el código fuente: ninguna llamada de log puede nombrar un
teléfono, un texto, el QR, credenciales o secretos sin enmascarar, ni volcar un objeto entero, ni escribir en la consola por fuera de
`logs.js`; y escenarios completos con datos marcados comprueban que ni un marcador llegue al log. La versión de WhatsApp Web
(`2.3000.1037753511`) no se enmascara aunque parezca un número: no es de nadie y sirve para diagnosticar.

Para revisar a mano un log, esto no tendría que devolver nada: `journalctl -u whatsapp-service -o cat | grep -E '[0-9]{7,}'`.

### Baileys: `/salud` y apagado ordenado

**`GET /salud`** no pide secreto y solo escucha en loopback (`127.0.0.1:3002`). Dice si el servicio está en condiciones de mandar
mensajes y, si no, por qué:

```bash
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:3002/salud     # 200 conectado · 503 todo lo demás
curl -s http://127.0.0.1:3002/salud
```
```json
{ "ok": false, "canal": "baileys", "estado": "requiere_vincular", "motivo": "sesion_cerrada", "desde": "2026-10-16T15:00:00.000Z",
  "conectado": false, "qrPendiente": false, "requiereVincular": true, "requiereAtencion": false, "reconectando": false,
  "intentosDeReconexion": 0, "uptimeSegundos": 5231,
  "entrantes": { "recibidos": 42, "duplicados": 1, "ignorados": { "propio": 3, "grupo": 5, "no_textual": 2 },
                 "reenviados": 41, "reintentados": 2, "fallidos": 0, "perdidos": 0, "pendientes": 1 },
  "cola": { "pendientes": 0, "dia": "2026-10-16", "enviadosHoy": 3, "enEspera": 0, "topeDiario": 100,
            "enviadosUltimaHora": 3, "topePorHora": 40, "topePorDestinatario": 3 } }
```

| `estado` | HTTP | Qué significa | Qué hacer |
|---|---|---|---|
| `conectado` | 200 | listo para enviar y recibir | nada |
| `iniciando` | 503 | abriendo la sesión (pasa en segundos) | esperar |
| `reconectando` | 503 | se cortó y se reintenta solo (`intentosDeReconexion`, `desde`) | esperar; si pasa de unos minutos, mirar el log |
| `requiere_vincular` | 503 | falta escanear un QR; `motivo`: `esperando_escaneo` (hay un QR, `qrPendiente: true`), `qr_vencido`, `sesion_cerrada` | vincular (ver arriba) |
| `requiere_atencion` | 503 | WhatsApp cortó por algo que reintentar no arregla; `motivo`: `cuenta_restringida`, `sesion_reemplazada`, `version_rechazada`, `sesion_invalida` | mirar el log y reiniciar el servicio |
| `detenido` | 503 | todavía no arrancó, o ya se apagó | — |

`desde` es desde cuándo está en ese estado y motivo. No trae teléfonos, textos ni nombres (lo vigila una prueba), y se puede consultar sin
secreto porque solo escucha en loopback.

`entrantes` cuenta, desde que arrancó el servicio y solo con números, lo que entró: `recibidos` (los que se procesaron), `duplicados` (WhatsApp entregó dos veces lo mismo),
`ignorados` por motivo (`propio`, `grupo`, `difusion`, `canal`, `no_textual`, `vacio`, `sin_numero`, `sin_clave`) y lo que pasó al reenviarlos a la API: `reenviados`,
`reintentados` (cuántos reintentos hubo), `fallidos` (la API los rechazó con un 4xx: no se reintentan), `perdidos` (se agotaron los reintentos, no había lugar o se apagó el
servicio) y `pendientes` (esperando). Si `perdidos` o `fallidos` crecen, mirar el log de la API y este. **Ojo:** un servicio recién desplegado y sin vincular contesta 503 (con `requiere_vincular`); eso
es lo correcto, no una falla del proceso.

**Apagado ordenado.** systemd manda SIGTERM al hacer `stop` o `restart` (Ctrl+C es SIGINT, lo mismo). En este orden:
1. se cierra el puerto y la cola deja de despachar (lo nuevo recibe `503 reiniciando`);
2. se espera hasta 5 segundos al envío que esté saliendo, para no cortarlo a la mitad y para que cuente en el tope del día;
3. se suelta el socket de WhatsApp **sin cerrar la sesión** (el número sigue vinculado) y se guardan, a la vez, lo que quede por escribir de
   las credenciales, los consentimientos y los contadores. Los mensajes entrantes que esperaban un reintento hacia la API **se pierden** (eran solo memoria): el log dice cuántos;
4. se sale con código 0.

Todo tiene un tope de 10 segundos (la unit da 20): si algo no contesta, se anota y se sale igual con lo que haya, sin bloquear.
Se sale con 0 a propósito: fue un pedido de apagado, no una falla, y con otro código `Restart=on-failure` lo volvería a levantar.

### Baileys: todas las variables, de un vistazo

Las obligatorias van en `/opt/yosoy/config/whatsapp.env` (o en la unit); las opcionales, solo si hace falta cambiar el valor por defecto.
Si una variable está definida en la unit y en `whatsapp.env`, vale la de `whatsapp.env`.

| Variable | Por defecto | Qué hace |
|---|---|---|
| `API_BASE_URL` | — (obligatoria) | Dónde está la API (`http://127.0.0.1:5000`). Si es otra máquina tiene que ser `https`. |
| `Mensajeria__WhatsApp__Secreto` | — (obligatoria) | El secreto compartido con la API (`X-Shared-Secret`). Nunca va en el repo ni en el log. |
| `WHATSAPP_BAILEYS_SESSION_PATH` | — (obligatoria) | Carpeta de la sesión: absoluta, propia, fuera del repo y de la aplicación (ver "la carpeta de la sesión"). |
| `WHATSAPP_SERVICE_PORT` | `3002` | Puerto (siempre en `127.0.0.1`). |
| `WHATSAPP_BAILEYS_VENTANAS_QR` | `2` (1 a 5) | Ventanas de QR que se abren solas sin número vinculado. |
| `WHATSAPP_BAILEYS_VERSION_WA` | la de la librería | Fija la versión de WhatsApp Web (solo si WhatsApp rechaza la de la librería, cierre 405). |
| `WHATSAPP_BAILEYS_BUSCAR_VERSION` | apagado | `1` pide la última versión conocida al arrancar (un pedido a `raw.githubusercontent.com`). |
| `WHATSAPP_TOPE_DIARIO` | `100` (1 a 1000) | Mensajes por día. Para un número nuevo, empezar en 30. |
| `WHATSAPP_TOPE_POR_HORA` | `40` (1 a 500) | Mensajes por hora; los que se pasan esperan en la cola. |
| `WHATSAPP_TOPE_POR_PERSONA` | `3` (1 a 20) | Mensajes por persona y por día (un aviso suelto). |
| `WHATSAPP_TOPE_EN_CONVERSACION` | `30` (1 a 200) | Mensajes por persona y por día si ella está conversando (escribió hace menos de 24 horas). Nunca menos que el de arriba. |
| `WHATSAPP_CONSENTIMIENTO_DIAS` | `365` (1 a 3650) | Cuánto vale que la persona le haya escrito primero al número. |
| `WHATSAPP_ZONA_HORARIA` | `America/Argentina/Buenos_Aires` | Zona en la que cambia el día para el tope diario. |
| `WHATSAPP_PAUSA_MIN_S`, `WHATSAPP_PAUSA_MAX_S` | `4` y `9` | Pausa al azar entre mensajes, en segundos. |
| `WHATSAPP_SIMULAR_ESCRITURA` | activo | `0` apaga el "escribiendo..." antes de cada mensaje. |

### Baileys: dependencias y auditoría

- Baileys está fijado en la versión **exacta** `6.7.24` (sin `^`), la última estable, con `"overrides": {"libsignal": "6.0.0"}`: la librería
  depende de `libsignal` por git, que el VPS (sin clave SSH hacia GitHub) no puede bajar; el paquete de npm tiene el mismo código y trae
  hash de integridad. Cambiar de versión es una decisión: ver `docs/PROGRESO_BAILEYS.md` (la 7.x todavía es candidata).
- Las dependencias de producción son cuatro: Baileys, `express`, `qrcode` y `qrcode-terminal` (el QR de `vincular.js`). Sin Puppeteer, sin
  `whatsapp-web.js` y sin `patch-package`, el árbol pasó de 429 a **207 paquetes**.
- `npm audit --omit=dev`: **0 vulnerabilidades** (antes eran 13 altas, todas de las cadenas de `puppeteer` y de `patch-package`; ninguna de Baileys).
  Revisarlo de nuevo en cada actualización.
- En el VPS se instala con `npm ci --omit=dev` (usa el `package-lock.json` tal cual quedó versionado, sin resolver nada de nuevo).

## Qué necesita el VPS

- **Node.js 22 LTS.** Si no estuviera instalado, vía NodeSource:
  ```bash
  curl -fsSL https://deb.nodesource.com/setup_22.x | sudo -E bash -
  sudo apt install -y nodejs
  ```
- **Nada más.** No hace falta Chromium ni ninguno de sus paquetes (`libnss3`, `libatk1.0-0t64`, `libgtk-3-0t64`, `libgbm1`, `libasound2t64`...), ni
  ajustar AppArmor para el sandbox de un navegador. Si el VPS los tiene de la versión vieja, se pueden sacar: `docs/DEPLOY.md`, "Limpieza de lo
  viejo (whatsapp-web.js y Chromium)".

## Primer deploy (manual — no entra en el script de deploy de la API)

El paso a paso completo, con backup, verificación y rollback, está en `docs/DEPLOY.md`, "Despliegue de whatsapp-service (Baileys)". En resumen,
para un servidor donde el servicio nunca se instaló:

```bash
sudo mkdir -p /opt/yosoy/whatsapp/{app,cache}
sudo install -d -m 700 -o yosoy -g yosoy /opt/yosoy/whatsapp/sesion-baileys
sudo chown -R yosoy:yosoy /opt/yosoy/whatsapp/app /opt/yosoy/whatsapp/cache
```

(`cache/` es solo la caché de npm para la instalación: el servicio no la usa al correr, y no está en `ReadWritePaths` de la unit.)

Empaquetar esta carpeta **sin `node_modules`** (no hace falta — se instala en el VPS, no se copia desde la PC) y copiarla:
```bash
# en la PC, parado en whatsapp-service/
tar --exclude=node_modules -czf whatsapp-service.tar.gz .
scp whatsapp-service.tar.gz agustin@179.199.155.50:/home/agustin/
# en el VPS
sudo tar -xzf /home/agustin/whatsapp-service.tar.gz -C /opt/yosoy/whatsapp/app
sudo chown -R yosoy:yosoy /opt/yosoy/whatsapp/app
```

Instalar dependencias **como el usuario `yosoy`**, no como root, con `npm ci` (no `npm install` — usa el `package-lock.json` tal cual quedó
versionado, sin resolver nada de nuevo) y con `HOME` apuntando a la carpeta de caché (el home real de `yosoy` puede ser de solo lectura):
```bash
sudo -u yosoy bash -c '
  cd /opt/yosoy/whatsapp/app &&
  HOME=/opt/yosoy/whatsapp/cache npm ci --omit=dev --cache /opt/yosoy/whatsapp/cache/npm
'
```

### Secreto compartido

```bash
SECRETO=$(openssl rand -hex 32)
sudo install -m 600 -o yosoy -g yosoy /dev/null /opt/yosoy/config/whatsapp.env
sudo tee /opt/yosoy/config/whatsapp.env > /dev/null <<EOF
Mensajeria__WhatsApp__Secreto=$SECRETO
API_BASE_URL=http://127.0.0.1:5000
EOF
```
Nunca se escribe el secreto directo en el repo ni en ningún script versionado — generarlo en el momento, en el VPS.

### Instalar la unit

```bash
sudo cp deploy/whatsapp-service.service /etc/systemd/system/
sudo mkdir -p /etc/systemd/system/yosoy-api.service.d
sudo cp deploy/yosoy-api-whatsapp.conf /etc/systemd/system/yosoy-api.service.d/whatsapp.conf
sudo systemctl daemon-reload
sudo systemctl enable --now whatsapp-service
sudo systemctl restart yosoy-api   # para que levante el drop-in con el secreto
```

La unit (`deploy/whatsapp-service.service`) pone la carpeta de la sesión (`/opt/yosoy/whatsapp/sesion-baileys`), `MemoryMax=512M`, el
endurecimiento de systemd y el límite de reinicios (si falla 5 veces en 5 minutos, systemd deja de intentarlo: un proceso que cae en bucle volvería
a conectarse a WhatsApp cada pocos segundos, que es justo lo que no hay que hacer). Después de instalarla hay que vincular el número (ver arriba).

## Prender el canal en la API

Recién con `"estado":"conectado"` confirmado en `GET /salud`, en `/opt/yosoy/config/.env` (el principal, no `whatsapp.env`):
```
Mensajeria__CanalActivo=Baileys
```
y `sudo systemctl restart yosoy-api`. Los valores válidos de la API son `Simulado` (por defecto: no sale nada de la máquina) y `Baileys`;
`WhatsAppWebJs` ya no existe y **la API no arranca** si el `.env` todavía lo trae.

## Problemas esperables

- **La sesión no sobrevive un deploy de la API:** a propósito — `/opt/yosoy/whatsapp/` está fuera de `/opt/yosoy/app`, que el script de
  deploy de la API borra y reescribe entero en cada publish. Si algún día se automatiza el deploy de este servicio también, nunca debe tocar
  `/opt/yosoy/whatsapp/sesion-baileys`.
- **Cuánta memoria hace falta:** `MemoryMax=512M` es una estimación (sin navegador, el proceso ocupa unos pocos cientos de MB). En el VPS, el 20/10/2026, con el
  servicio recién arrancado, `Memory:` marcó 56.3M (pico 63.5M); seguir mirándola (`Memory:` en `systemctl status whatsapp-service`) y revisar
  `systemctl status` por un `OOMKilled` antes de asumir que es otra cosa.

## Qué NO hacer

- No correr esto con el `.env` principal de la API completo: heredaría la cadena de conexión a SQL, la clave de JWT y la contraseña del
  mail. Solo `whatsapp.env`.
- No exponer el puerto 3002 (ni con una regla de UFW ni con `0.0.0.0` en `WHATSAPP_SERVICE_PORT`).
- No usar el chip dedicado desde un teléfono físico en simultáneo — invalida la sesión vinculada acá.
- No copiar la carpeta de la sesión ni pegarla en un chat o un ticket: es una credencial.
