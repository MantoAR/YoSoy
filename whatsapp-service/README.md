# whatsapp-service

Puente HTTP interno entre la API de YoSoy y WhatsApp, usando
[whatsapp-web.js](https://wwebjs.dev/) (no oficial — automatiza WhatsApp Web
por Puppeteer/Chromium, no es la API oficial de Meta). Pensado para un chip
dedicado, solo para esto; hay riesgo de baneo asumido a propósito, no es para
un número de uso personal.

Solo escucha en `127.0.0.1` — nunca se expone a internet. La API .NET lo
llama vía `CanalWhatsAppWebJs` (`Services/Mensajeria/`); este servicio le
reenvía los mensajes entrantes por su cuenta.

## Dos backends: whatsapp-web.js y Baileys

El servicio puede hablar con WhatsApp de dos maneras, y **la API no se entera de cuál**: la interfaz HTTP es la misma
(`/enviar`, `/estado`, `/desvincular`, `/salud`; ver `servidor.js`).

| `WHATSAPP_BACKEND` | Qué usa | Notas |
|---|---|---|
| `webjs` (**por defecto**) | whatsapp-web.js + Chromium | Lo que corre hoy. Sin la variable, el servicio se comporta exactamente como antes. |
| `baileys` | Baileys (WhatsApp Web sin navegador) | Más liviano: sin Chromium. Obligatoria `WHATSAPP_BAILEYS_SESSION_PATH`. |

Cada librería se carga **solo si se elige** (con `baileys` no se carga Puppeteer, y con `webjs` no se carga Baileys). Son
mutuamente excluyentes: usan el mismo puerto, y dos conexiones con la misma cuenta se pisan.

Del lado de la API, `Mensajeria__CanalActivo` acepta `Simulado` (por defecto), `Baileys` y `WhatsAppWebJs`; los dos últimos
son el mismo canal HTTP (se conserva `WhatsAppWebJs` para que un servidor que ya lo tiene configurado siga arrancando).

### Baileys: variables

Van en `/opt/yosoy/config/whatsapp.env` (o en la `Environment=` de la unit). Todas son opcionales salvo la primera.

| Variable | Para qué |
|---|---|
| `WHATSAPP_BACKEND` | `baileys` para usarlo. |
| `WHATSAPP_BAILEYS_SESSION_PATH` | **Obligatoria.** Carpeta de la sesión (es una credencial): ruta absoluta, **fuera del repositorio** y de la carpeta de la aplicación. |
| `WHATSAPP_BAILEYS_VERSION_WA` | Fija la versión de WhatsApp Web, por ejemplo `2.3000.1037753511`. Solo si WhatsApp rechaza la que trae la librería (cierre con 405 al arrancar). |
| `WHATSAPP_BAILEYS_BUSCAR_VERSION` | `1` para pedirle a Baileys la última versión conocida al arrancar (hace un pedido a `raw.githubusercontent.com`). |
| `WHATSAPP_BAILEYS_VENTANAS_QR` | Cuántas ventanas de QR (~2,7 minutos cada una) se abren solas sin número vinculado antes de esperar a que una persona pida otra. De 1 a 5; por defecto 2. |

### Baileys: la carpeta de la sesión

La sesión de Baileys (`creds.json` y las claves de cifrado) **es una credencial de la cuenta**: quien tenga esos archivos
puede usar el número de la clínica como si fuera el teléfono. Por eso, al arrancar, el servicio exige que la carpeta de
`WHATSAPP_BAILEYS_SESSION_PATH`:

- sea **propia**: solo archivos de la sesión. Si hay otra cosa, no arranca y nunca borra nada ajeno (así una ruta mal
  puesta, como `/opt` o la carpeta de la sesión de whatsapp-web.js, se frena en vez de ensuciarse o borrarse);
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
"WhatsApp conectado (número ***1234)").

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

### Baileys: protecciones contra el bloqueo del número

WhatsApp bloquea los números que parecen un bot o que le escriben a quien no quiere recibir mensajes. Con Baileys (no con
whatsapp-web.js, que sigue como siempre) el servicio se protege solo.

**Solo a quien aceptó** (`consentimiento.js`; las reglas, en `privacidad.js`). A una persona se le puede escribir si:
- ella **le escribió primero** al número de la clínica (vale 365 días desde su último mensaje, que lo renueva), o
- la API afirma que aceptó, mandando `"optIn": true` en el `POST /enviar` (solo cuenta el valor `true` exacto, y en cada pedido);

y **nunca** si escribió `BAJA` (o `STOP`, `NO MOLESTAR`, `DAR DE BAJA`...): eso pesa más que todo, aunque la API mande `optIn`,
hasta que escriba `ALTA`. Tiene que ser el mensaje entero: "cancelar" no es una baja (en el bot es cancelar un turno). Sin
constancia no se envía: `403 sin_consentimiento`. El registro (`datos/consentimientos.json`) guarda la **huella** del teléfono, no
el teléfono, y fechas; nunca el texto de un mensaje. Un mensaje de grupo, uno propio o uno que no es texto no cuenta como
constancia.

**Ritmo humano:** una pausa al azar de 4 a 9 segundos entre mensajes, una pausa larga de 30 a 75 segundos cada 8 a 14 mensajes, y
"escribiendo..." antes de cada uno (hasta 4 segundos, según el largo del texto).

**Topes.** Se guardan en disco (`datos/envios.json`): reiniciar el servicio no devuelve el cupo del día.

| Tope | Por defecto | Variable | Al llegar |
|---|---|---|---|
| Mensajes por día (en la zona horaria de la clínica) | 100 | `WHATSAPP_TOPE_DIARIO` (1 a 1000) | `429 tope_diario`, hasta mañana |
| Mensajes por persona y por día | 3 | `WHATSAPP_TOPE_POR_PERSONA` (1 a 20) | `429 limite_destinatario` |
| Mensajes por hora | 40 | `WHATSAPP_TOPE_POR_HORA` (1 a 500) | los aceptados esperan en la cola hasta que se libere lugar |
| Cola | 50 pendientes | — | `503 cola_llena` |

Los mensajes que esperan en la cola ya cuentan contra el tope del día, así que todo lo que se acepta se puede mandar hoy. Un envío
que falla devuelve su lugar. **Para un número nuevo conviene empezar bajo** (por ejemplo `WHATSAPP_TOPE_DIARIO=30`) y subir de a
poco durante varias semanas: un número que de golpe manda cien mensajes por día es justo lo que WhatsApp bloquea.

Otras variables: `WHATSAPP_CONSENTIMIENTO_DIAS` (cuánto vale haber escrito primero; 365), `WHATSAPP_ZONA_HORARIA`
(`America/Argentina/Buenos_Aires`), `WHATSAPP_PAUSA_MIN_S` y `WHATSAPP_PAUSA_MAX_S` (4 y 9), `WHATSAPP_SIMULAR_ESCRITURA=0`
(apaga el "escribiendo...").

**Con WhatsApp sin conexión:** si está reconectando, los mensajes esperan y salen cuando vuelve; si hace falta una persona
(`requiere_vincular`, `requiere_atencion`), no se acepta nada (`503 no_conectado`); y uno que lleva más de 3 horas esperando se
descarta (un recordatorio tan tarde ya no sirve).

Lo que ve la API cuando se rechaza (`POST /enviar` → `{ "exito": false, "error": "...", "codigo": "..." }`):

| HTTP | `codigo` | Significa |
|---|---|---|
| 400 | `telefono_invalido` | el teléfono no puede ser un teléfono |
| 403 | `sin_consentimiento` | no hay constancia de que la persona acepte (ni escribió primero ni vino `optIn:true`) |
| 403 | `baja` | la persona pidió no recibir más mensajes |
| 429 | `tope_diario`, `limite_destinatario` | se llegó a un tope |
| 503 | `no_conectado`, `cola_llena`, `reiniciando` | el servicio no puede aceptarlo ahora |

> **Ojo con el despliegue:** la API todavía **no manda `optIn`**. Con Baileys, entonces, los avisos y recordatorios solo salen
> a quien le escribió primero al número de la clínica; al resto se le rechaza con `403 sin_consentimiento` (queda en el log de la
> API). Cómo se resuelve es una decisión de producto: ver `docs/PROGRESO_BAILEYS.md`, "Pasos manuales".

### Baileys: qué se escribe en el log (y qué nunca)

El log (`journalctl -u whatsapp-service`) sirve para saber qué pasa sin guardar datos de pacientes. **Nunca** se escribe:
- el **texto** de un mensaje, ni el que llega ni el que sale;
- un **teléfono completo**: queda `***4455` (los últimos 4 dígitos), también dentro de los errores que repiten la librería o el sistema;
- el **QR** de vinculación (sale solo por la pantalla del comando o por `GET /estado`, que pide el secreto), ni las **credenciales** de
  la sesión, ni sus claves;
- el **secreto compartido** con la API (`Mensajeria__WhatsApp__Secreto`), ni el que pruebe alguien en `X-Shared-Secret`.

Sí se escriben los estados (`conectado`, `reconectando`...), los códigos de desconexión, los intentos y las esperas, las ventanas de QR,
los rechazos de envío (con el motivo y los 4 últimos dígitos) y los errores.

Se garantiza en tres capas, y las tres se prueban en cada `npm test`: **(1)** cada módulo enmascara lo suyo; **(2)** con Baileys, todo
lo que el proceso escribe pasa por un filtro (`crearLogSeguro`, en `privacidad.js`) que tapa el secreto compartido y el QR vigente y
enmascara cualquier número que parezca un teléfono; **(3)** una guardia revisa el código fuente: ninguna llamada de log puede nombrar un
teléfono, un texto, el QR, credenciales o secretos sin enmascarar, ni volcar un objeto entero, ni escribir en la consola por fuera de
`logs.js`; y escenarios completos con datos marcados comprueban que ni un marcador llegue al log. La versión de WhatsApp Web
(`2.3000.1037753511`) no se enmascara aunque parezca un número: no es de nadie y sirve para diagnosticar.

Con whatsapp-web.js el log queda como estaba (por ejemplo, escribe el número propio de la clínica al conectar). Para revisar a mano
un log de Baileys, esto no tendría que devolver nada: `journalctl -u whatsapp-service -o cat | grep -E '[0-9]{7,}'`.

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

`desde` es desde cuándo está en ese estado y motivo. No trae teléfonos ni textos (lo vigila una prueba), y se puede consultar sin
secreto porque solo escucha en loopback. Con whatsapp-web.js, `/salud` contesta `{ "ok": true }` mientras el proceso responda, como
siempre. **Ojo:** con Baileys, un servicio recién desplegado y sin vincular contesta 503 (con `requiere_vincular`); eso es lo correcto,
no una falla del proceso.

**Apagado ordenado.** systemd manda SIGTERM al hacer `stop` o `restart` (Ctrl+C es SIGINT, lo mismo). En este orden:
1. se cierra el puerto y la cola deja de despachar (lo nuevo recibe `503 reiniciando`);
2. se espera hasta 5 segundos al envío que esté saliendo, para no cortarlo a la mitad y para que cuente en el tope del día;
3. se suelta el socket de WhatsApp **sin cerrar la sesión** (el número sigue vinculado) y se guardan, a la vez, lo que quede por escribir de
   las credenciales, los consentimientos y los contadores;
4. se sale con código 0.

Todo tiene un tope de 10 segundos (la unit da 20): si algo no contesta, se anota y se sale igual con lo que haya, sin bloquear.
Se sale con 0 a propósito: fue un pedido de apagado, no una falla, y con otro código `Restart=on-failure` lo volvería a levantar.

### Baileys: todas las variables, de un vistazo

Las obligatorias van en `/opt/yosoy/config/whatsapp.env` (o en la unit); las opcionales, solo si hace falta cambiar el valor por defecto.
Si una variable está definida en la unit y en `whatsapp.env`, vale la de `whatsapp.env`.

| Variable | Por defecto | Qué hace |
|---|---|---|
| `API_BASE_URL` | — (obligatoria) | Dónde está la API (`http://127.0.0.1:5000`). Con Baileys, si es otra máquina tiene que ser `https`. |
| `Mensajeria__WhatsApp__Secreto` | — (obligatoria) | El secreto compartido con la API (`X-Shared-Secret`). Nunca va en el repo ni en el log. |
| `WHATSAPP_BACKEND` | `webjs` | `baileys` activa este backend; sin la variable, todo sigue como siempre. |
| `WHATSAPP_BAILEYS_SESSION_PATH` | — (obligatoria con Baileys) | Carpeta de la sesión: absoluta, propia, fuera del repo y de la aplicación (ver "la carpeta de la sesión"). |
| `WHATSAPP_SERVICE_PORT` | `3002` | Puerto (siempre en `127.0.0.1`). |
| `WHATSAPP_BAILEYS_VENTANAS_QR` | `2` (1 a 5) | Ventanas de QR que se abren solas sin número vinculado. |
| `WHATSAPP_BAILEYS_VERSION_WA` | la de la librería | Fija la versión de WhatsApp Web (solo si WhatsApp rechaza la de la librería, cierre 405). |
| `WHATSAPP_BAILEYS_BUSCAR_VERSION` | apagado | `1` pide la última versión conocida al arrancar (un pedido a `raw.githubusercontent.com`). |
| `WHATSAPP_TOPE_DIARIO` | `100` (1 a 1000) | Mensajes por día. Para un número nuevo, empezar en 30. |
| `WHATSAPP_TOPE_POR_HORA` | `40` (1 a 500) | Mensajes por hora; los que se pasan esperan en la cola. |
| `WHATSAPP_TOPE_POR_PERSONA` | `3` (1 a 20) | Mensajes por persona y por día. |
| `WHATSAPP_CONSENTIMIENTO_DIAS` | `365` (1 a 3650) | Cuánto vale que la persona le haya escrito primero al número. |
| `WHATSAPP_ZONA_HORARIA` | `America/Argentina/Buenos_Aires` | Zona en la que cambia el día para el tope diario. |
| `WHATSAPP_PAUSA_MIN_S`, `WHATSAPP_PAUSA_MAX_S` | `4` y `9` | Pausa al azar entre mensajes, en segundos. |
| `WHATSAPP_SIMULAR_ESCRITURA` | activo | `0` apaga el "escribiendo..." antes de cada mensaje. |

(`WHATSAPP_SESSION_PATH`, `WHATSAPP_WEB_VERSION` y `WHATSAPP_DEBUG` son del backend de whatsapp-web.js y se explican en sus secciones.)

### Baileys: dependencias y auditoría

- Baileys está fijado en la versión **exacta** `6.7.24` (sin `^`), la última estable, con `"overrides": {"libsignal": "6.0.0"}`: la librería
  depende de `libsignal` por git, que el VPS (sin clave SSH hacia GitHub) no puede bajar; el paquete de npm tiene el mismo código y trae
  hash de integridad. Cambiar de versión es una decisión: ver `docs/PROGRESO_BAILEYS.md` (la 7.x todavía es candidata).
- `npm audit` del servicio: 13 avisos altos, **todos de las cadenas de `puppeteer` (la descarga de Chromium de whatsapp-web.js) y de
  `patch-package`; ninguno viene de Baileys** ni de sus dependencias. Se pueden ver con `npm audit --omit=dev`.
- En el VPS se instala con `PUPPETEER_SKIP_DOWNLOAD=1 npm ci --omit=dev`: no hace falta bajar Chromium para usar Baileys.

## Qué necesita el VPS

- **Node.js 22 LTS.** No estaba instalado al momento de escribir esto
  (confirmado por SSH) — instalar vía NodeSource:
  ```bash
  curl -fsSL https://deb.nodesource.com/setup_22.x | sudo -E bash -
  sudo apt install -y nodejs
  ```
- **Paquetes para Chromium headless.** En Ubuntu 24.04 varios de estos
  cambiaron de nombre por la migración a `time_t` de 64 bits — son los
  nombres con sufijo `t64`, no los de versiones anteriores de Ubuntu:
  ```bash
  sudo apt install -y libnss3 libatk1.0-0t64 libatk-bridge2.0-0t64 \
      libgtk-3-0t64 libgbm1 libasound2t64
  ```
  (Si en el momento de instalar `apt` no encuentra alguno de estos nombres,
  puede que la versión de Ubuntu haya cambiado el sufijo de nuevo —
  `apt-cache search` por el nombre base para encontrar el paquete real.)

## Primer deploy (manual — no entra en el script de deploy de la API)

Hecho así de verdad en la Etapa 2, confirmado en el VPS real:

```bash
sudo mkdir -p /opt/yosoy/whatsapp/{app,sesion,cache}
sudo chown -R yosoy:yosoy /opt/yosoy/whatsapp
sudo chmod 750 /opt/yosoy/whatsapp/sesion /opt/yosoy/whatsapp/cache
```

Empaquetar esta carpeta **sin `node_modules`** (no hace falta — se instala
en el VPS, no se copia desde la PC) y copiarla:
```bash
# en la PC, parado en whatsapp-service/
tar --exclude=node_modules -czf whatsapp-service.tar.gz .
scp whatsapp-service.tar.gz agustin@179.199.155.50:/home/agustin/
# en el VPS
sudo tar -xzf /home/agustin/whatsapp-service.tar.gz -C /opt/yosoy/whatsapp/app
sudo chown -R yosoy:yosoy /opt/yosoy/whatsapp/app
```

Instalar dependencias **como el usuario `yosoy`**, no como root, con
`npm ci` (no `npm install` — usa el `package-lock.json` tal cual quedó
versionado, sin resolver nada de nuevo) y con `HOME`/`PUPPETEER_CACHE_DIR`
apuntando los dos a la carpeta de caché (el home real de `yosoy` es de solo
lectura bajo `ProtectSystem=strict` — mismo motivo que la línea `Environment=HOME=`
de la unit; acá hace falta a mano porque todavía no está corriendo bajo esa
unit cuando se instala):
```bash
sudo -u yosoy bash -c '
  cd /opt/yosoy/whatsapp/app &&
  HOME=/opt/yosoy/whatsapp/cache PUPPETEER_CACHE_DIR=/opt/yosoy/whatsapp/cache \
  npm ci --omit=dev --cache /opt/yosoy/whatsapp/cache/npm
'
```
Este mismo comando aplica solo, en su propio `postinstall`, el parche de
`patches/` (ver "Diagnóstico" más abajo) — no hace falta ningún paso extra.
Verificado con `npm ci --omit=dev` real, dos veces seguidas desde cero, en
una carpeta aparte (no en el VPS).

### Secreto compartido

```bash
SECRETO=$(openssl rand -hex 32)
sudo install -m 600 -o yosoy -g yosoy /dev/null /opt/yosoy/config/whatsapp.env
sudo tee /opt/yosoy/config/whatsapp.env > /dev/null <<EOF
Mensajeria__WhatsApp__Secreto=$SECRETO
API_BASE_URL=http://127.0.0.1:5000
EOF
```
Nunca se escribe el secreto directo en el repo ni en ningún script
versionado — generarlo en el momento, en el VPS.

### Instalar las units

> Para **Baileys** la unit es `deploy/whatsapp-service-baileys.service` (se instala con el mismo nombre, `whatsapp-service.service`) y el
> paso a paso, con backup y rollback, está en `docs/DEPLOY.md`, "Despliegue de whatsapp-service con Baileys". Lo de abajo es
> la instalación de siempre, con whatsapp-web.js.

```bash
sudo cp deploy/whatsapp-service.service /etc/systemd/system/
sudo mkdir -p /etc/systemd/system/yosoy-api.service.d
sudo cp deploy/yosoy-api-whatsapp.conf /etc/systemd/system/yosoy-api.service.d/whatsapp.conf
sudo systemctl daemon-reload
sudo systemctl enable --now whatsapp-service
sudo systemctl restart yosoy-api   # para que levante el drop-in con el secreto
```

## Vincular el chip (QR)

**Forma normal: desde el Configurador** (sección WhatsApp, rol administrador)
— muestra el estado y el QR como imagen, sin SSH. Ahí mismo está "Desvincular
y cambiar número". Lo que sigue es la forma manual/de respaldo, para cuando
no se puede usar el navegador (primer arranque antes de confirmar que el
servicio levantó, diagnóstico por SSH, etc.).

`GET /estado` y `POST /desvincular` son las dos rutas que usa esa pantalla —
ambas piden el mismo `X-Shared-Secret` que `/enviar` (no están pensadas para
pegarles directo salvo para diagnóstico):
```bash
curl -s http://127.0.0.1:3002/estado -H "X-Shared-Secret: $SECRETO"
# { "conectado": true, "numero": "549...", "qrPendiente": false, "qr": null }
```
Mientras falta escanear, `"qr"` trae el código como data-URL
(`data:image/png;base64,...`) en vez de `null` — se puede pegar ese valor en
la barra de direcciones de un navegador para verlo sin decodificar nada a
mano.

El QR también se sigue imprimiendo por journal como respaldo (si el servicio
recién arranca y todavía no hay forma de llegar a la pantalla del
Configurador, por ejemplo):
```bash
sudo journalctl -u whatsapp-service -f
```
Conviene tener la terminal maximizada — el QR como ASCII necesita bastante
ancho para que la cámara lo pueda leer sin que quede cortado o demasiado
chico (confirmado en el VPS real).

Una vez vinculado (por cualquiera de las dos formas), `LocalAuth` guarda la
sesión en `/opt/yosoy/whatsapp/sesion` — no hace falta repetirlo salvo que se
desvincule (desde el Configurador, desde el teléfono en Dispositivos
vinculados, o `POST /desvincular`) o WhatsApp invalide la sesión por su
cuenta (p. ej. por usar el chip también desde el teléfono físico al mismo
tiempo — no hacerlo). Una desconexión transitoria (corte de red) no borra la
sesión guardada — el servicio reintenta solo, sin pedir un escaneo nuevo,
salvo que la sesión en sí ya no sirva.

## Si se desconecta sola

`whatsapp.js` reacciona distinto según el motivo de `disconnected`:
- Motivo distinto de `"LOGOUT"` (por ejemplo, un corte de red): recrea el cliente sin tocar
  la sesión guardada — si el corte fue pasajero, reconecta solo, sin pedir un escaneo nuevo.
- `auth_failure`: la sesión está confirmada muerta — se borra (solo el contenido de
  `/opt/yosoy/whatsapp/sesion`, nunca el directorio en sí) y se fuerza un QR nuevo, igual que
  el botón "Desvincular" del Configurador.
- `disconnected` con motivo `"LOGOUT"` (alguien lo desvinculó desde el teléfono, en
  Dispositivos vinculados): acá **no se toca el cliente para nada** — ni se destruye ni se
  recrea. whatsapp-web.js ya maneja este caso internamente (borra la sesión y vuelve a
  inicializarse sobre la misma página) y hacerlo nosotros también, al mismo tiempo, generaba
  una carrera real: un crash en producción (`Execution context was destroyed`, Puppeteer).
  Solo se limpia el QR viejo que teníamos guardado; la librería termina emitiendo uno nuevo
  por su cuenta.

Por esto, usar el chip también desde el teléfono físico en simultáneo ya no deja el servicio
colgado sin salida — va a pedir un escaneo nuevo solo, sin reiniciar el proceso —, pero
sigue sin ser buena idea: invalida la sesión sin aviso, justo cuando puede estar esperando
salir un recordatorio de turno.

## Si el proceso no responde a nada de esto

Dos redes de seguridad adicionales, para cuando algo revienta del lado de Puppeteer/
whatsapp-web.js que no es ninguno de los eventos de arriba:
- `unhandledRejection`/`uncaughtException` a nivel proceso: se loguean (nunca el objeto
  entero del error, puede traer datos de la cuenta) y se intenta recuperar el cliente por
  el mismo camino que un corte transitorio — no tiran abajo el proceso.
- `SIGTERM`/`SIGINT` (lo manda systemd al hacer `stop`/`restart`): deja de aceptar pedidos
  nuevos, cierra el cliente de WhatsApp con un tope de 10s, y recién ahí sale. Si ese cierre
  se cuelga, sale igual pasado el tope — nunca bloquea un restart/deploy.

## Diagnóstico: vinculado en el teléfono pero la web no llega a "ready"

Si el celular ya muestra el dispositivo como vinculado pero `whatsapp-service` sigue
mostrando (o pidiendo) un QR sin parar, revisar el log por estos eventos, en orden — todos
quedan con una línea propia, sin datos de la cuenta:
```
WhatsApp: authenticated
WhatsApp: loading_screen 0%
...
WhatsApp: loading_screen 100%
WhatsApp conectado (número 549...).     ← esto es "ready"
```

**Historia de este problema puntual** (detalle completo y las fuentes en
`patches/README.md`): primero se vio `loading_screen 100%` y `authenticated` —repetido 2
a 4 veces— sin que `ready` llegue nunca, sin ningún error en el medio. Un primer parche
(vía `patch-package`, ya no está — ver abajo) corrigió una carrera real confirmada
(`hasSynced` ya `true` antes de registrar el listener que lo espera) — pero en
producción, **con ese parche puesto, se siguió viendo el mismo patrón**. Las sondas de
`WHATSAPP_DEBUG` mostraron `AppState` en `CONNECTED` con `hasSynced:true`, pero
`window.WWebJS` seguía `undefined` — la carga de `inject()` nunca llegaba a terminar.

Causa real, confirmada leyendo el código (no supuesta): `inject()` se re-ejecutaba sin
ninguna guarda en **cada** navegación de la página, así que con 2 a 4 `authenticated`
seguidos había 2 a 4 ejecuciones de la misma cadena de inicialización corriendo a la vez,
pisándose entre sí. Por eso `whatsapp-web.js` pasó a instalarse desde un commit puntual
de `main` (ver "Actualizar whatsapp-web.js" más abajo) en vez de la última versión de
npm — ese commit incluye, confirmado leyendo el código instalado:
- El mismo chequeo inmediato de `hasSynced` que tenía el parche viejo.
- Que `framenavigated` ya NO llama a `inject()` en cada navegación — solo si es un logout
  o si `window.WWebJS` todavía no existe.
- Una cancelación cooperativa (`AbortController`) para cuando `inject()` sí se vuelve a
  llamar: la ejecución vieja se cancela limpio en vez de seguir corriendo en paralelo con
  la nueva.
- Limpieza explícita de listeners viejos antes de registrar los nuevos en cada `inject()`.

Fuerte indicio de que esto resuelve el síntoma real (los cuatro puntos de arriba atacan
exactamente la causa encontrada), pero **no confirmado contra WhatsApp real todavía** —
ver "Actualizar whatsapp-web.js".

**`WHATSAPP_DEBUG=1`** (variable de entorno, apagada por default): activa diagnóstico
fino para este caso puntual —
- Loguea `console`/`pageerror` reales de la página (tipos `error` y `warning`,
  truncados a 300 caracteres). Importante: ese contenido lo genera WhatsApp Web, no
  nuestro código — no hay garantía completa de que nunca contenga nada sensible de la
  cuenta, por eso esto queda apagado por default y conviene prenderlo solo mientras se
  esté diagnosticando, no dejarlo siempre encendido.
- Tras `authenticated`, loguea la versión de WhatsApp Web que reporta el cliente
  (`client.getWWebVersion()`).
- Si pasan 60s y de nuevo 180s desde `authenticated` sin que llegue `ready`, sondea
  dentro de la página: estado y `hasSynced` de `AppState`, si `window.Store` y
  `window.WWebJS` llegaron a existir, y cualquier error al evaluar eso. Pensado para
  correlacionar con los pasos de `inject()` que podrían haberse colgado — ver
  `patches/README.md`. **`window.Store` no existe en esta librería desde hace tiempo**
  (confirmado: no se asigna en ningún lado del código fuente instalado) — en la sonda va
  a aparecer `"undefined"` siempre, no es señal de nada roto; lo único que importa ahí es
  `window.WWebJS`.
- Loguea explícito cuando `ready` sí llega (además del log ya existente de "WhatsApp
  conectado").

Prender solo en el VPS real, mientras se esté mirando este problema puntual:
```
WHATSAPP_DEBUG=1
```
en `/opt/yosoy/config/whatsapp.env`, `sudo systemctl restart whatsapp-service`, y volver
a quitarlo cuando ya no haga falta.

Si el log se corta en algún `loading_screen` (sin llegar al 100%) o directamente nunca
aparece `authenticated`, es una situación distinta a la de arriba — señal más genérica de
que whatsapp-web.js y la versión de WhatsApp Web que sirven los servidores en ese momento
no se llevan bien — ver la sección siguiente (`WHATSAPP_WEB_VERSION`).

(`change_state` también se loguea, por completitud; `remote_session_saved` está cableado
pero no lo usa esta configuración — ver el comentario en el código.)

## Prender el canal en la API

Recién con `conectado:true` confirmado, en `/opt/yosoy/config/.env` (el
principal, no `whatsapp.env`):
```
Mensajeria__CanalActivo=Baileys
```
(o `WhatsAppWebJs` si el servicio corre con whatsapp-web.js: la API trata los dos igual) y
`sudo systemctl restart yosoy-api`.

## Problemas esperables (Ubuntu 24.04)

- **AppArmor y el sandbox de Chromium:** Ubuntu 24.04 restringe por default
  la creación de user namespaces sin privilegios, que es lo que usa el
  sandbox normal de Chromium — sin esto, Puppeteer falla al arrancar.
  `whatsapp.js` ya lanza el cliente con `--no-sandbox`; si igual falla,
  revisar `journalctl -u whatsapp-service` por el error exacto de Puppeteer.
- **`ProtectSystem=strict` y la caché de Puppeteer:** con esa directiva la
  unit no puede escribir en `~/.cache/puppeteer` (la ruta por default), por
  eso `PUPPETEER_CACHE_DIR` está fijado a `/opt/yosoy/whatsapp/cache` en la
  unit, que también está en `ReadWritePaths`. Lo mismo pasa con `$HOME` en
  general (no solo la caché de Puppeteer) — por eso la unit también fija
  `Environment=HOME=` al mismo directorio. Si se cambia esa ruta, hay que
  actualizar las tres cosas juntas: `PUPPETEER_CACHE_DIR`, `HOME` y
  `ReadWritePaths`.
- **La sesión no sobrevive un deploy de la API:** a propósito —
  `/opt/yosoy/whatsapp/` está fuera de `/opt/yosoy/app`, que el script de
  deploy de la API borra y reescribe entero en cada publish. Si algún día se
  automatiza el deploy de este servicio también, nunca debe tocar
  `/opt/yosoy/whatsapp/sesion`.
- **Cuánta memoria hace falta:** `MemoryMax` quedó en `2G` (antes `768M`) — con el valor
  viejo, el proceso llegó al tope real (contando swap) durante la vinculación del chip en
  el VPS. Chromium es el grueso de esto, no Node; si se nota que igual anda justo, revisar
  `systemctl status whatsapp-service` por un `OOMKilled` antes de asumir que es otra cosa.

## Actualizar whatsapp-web.js

**Ya no viene de npm.** La última versión publicada ahí (`1.34.7`, abril 2026) se quedó
sin arreglar un problema real en producción (autenticado pero sin llegar nunca a "ready"
— ver `patches/README.md`), y el repo de la librería tenía el arreglo en `main` sin
publicar una versión nueva. `package.json` apunta ahora a un commit exacto de `main`,
bajado como tarball por HTTPS (no un paquete de npm, no una rama flotante):
```
"whatsapp-web.js": "https://github.com/wwebjs/whatsapp-web.js/archive/<hash>.tar.gz"
```
El `<hash>` es un commit puntual, no `main` en sí — actualizar significa cambiar ese
hash a mano (nunca apuntar a la rama pelada `main`, que sí seguiría moviéndose solo en
cada `npm install`/`npm ci`), con el mismo criterio de siempre: probar fuera del chip de
producción antes de redeploy.

**Por qué un tarball por HTTPS y no `git+ssh://`/`github:usuario/repo#hash`:** npm
normaliza esas dos formas a `git+ssh://` en el `package-lock.json` (confirmado:
pasa incluso pidiendo `git+https://` explícito) — el VPS no tiene ninguna clave SSH
autorizada contra GitHub, así que `npm ci` ahí fallaría. La URL de tarball
(`github.com/.../archive/<hash>.tar.gz`, redirige a `codeload.github.com`) es HTTP
llano, sin git de por medio, y además deja un hash de integridad real en el lockfile
(cosa que la instalación por git se salta directamente — "skipping integrity check for
git dependency").

`patches/` queda sin ningún parche por ahora (ver `patches/README.md`, "`main` ya
incluye..." para el detalle de qué se verificó que sí trae resuelto) — `patch-package`
sigue instalado y con su `postinstall`, sin tocar, por si hace falta un parche puntual
de nuevo más adelante: con la carpeta vacía, el `postinstall` no falla
("No patch files found"), confirmado con `npm ci --omit=dev` real.

## Fijar la versión de WhatsApp Web

whatsapp-web.js trae pineada una versión de WhatsApp Web por default (la que probó al
publicar esa versión de la librería), pero WhatsApp cambia su web seguido y a veces rompe
algo del lado de ellos antes de que haya una versión nueva de whatsapp-web.js con el
parche — pasó más de una vez en 2026 (cliente que nunca llega a "ready" aunque el teléfono
ya lo muestre vinculado; ver el diagnóstico más arriba). El mitigador manual, ya usado por
la comunidad de whatsapp-web.js para esto: fijar una build anterior conocida, en vez de la
que sirven los servidores de WhatsApp en ese momento.

Variable opcional `WHATSAPP_WEB_VERSION` (sin definir: comportamiento actual, sin cambios).
Si se define, por ejemplo:
```
WHATSAPP_WEB_VERSION=<version-conocida-buena, ej. 2.3000.1037753511-alpha>
```
el servicio arranca con esa build exacta, bajada de un índice de versiones público
(`wppconnect-team/wa-version` en GitHub — no un servidor propio). Para encontrar una build
conocida como buena: revisar los comentarios de los issues de
[wwebjs/whatsapp-web.js](https://github.com/wwebjs/whatsapp-web.js/issues) sobre el
problema puntual que se esté viendo — suelen mencionar una versión anterior que sigue
andando.

Nunca se aplicó un cambio de versión de WhatsApp Web sin probarlo antes fuera del chip de
producción, mismo criterio que actualizar whatsapp-web.js en sí (sección de arriba).

## Vulnerabilidades conocidas en dependencias transitivas

`npm audit` reporta 13 altas (eran 9 antes de agregar `patch-package`):

- 9 en la cadena de descarga de Chromium de Puppeteer (`extract-zip`,
  `basic-ftp` vía `proxy-agent`/`pac-proxy-agent`) — no en el código de
  WhatsApp en sí, solo en la herramienta que baja el binario de Chromium la
  primera vez que se instala. Riesgo real bajo en este despliegue: no se usa
  proxy PAC, y la descarga es HTTPS directo al CDN de Google.
- 4 nuevas, de `patch-package` → `find-yarn-workspace-root` → `micromatch` →
  `braces` (denegación de servicio por "stack exhaustion" con patrones glob
  anidados). Riesgo real bajo acá también: ese código solo corre durante
  `npm install`/`npm ci` (el propio `postinstall`), nunca en el proceso ya
  arrancado — no hay ninguna entrada externa que le llegue a esos patrones
  glob en este despliegue.

`npm audit fix --force` "arregla" la parte de Puppeteer bajando
`whatsapp-web.js` a una versión anterior (cambio incompatible, deshace el
fix del Puppeteer viejo sin soporte de la sección de arriba) — no se aplicó
a propósito. Revisar `npm audit` de nuevo en cada actualización de
`whatsapp-web.js` o de `patch-package`.

## Qué NO hacer

- No correr esto con el `.env` principal de la API completo — Chromium
  heredaría la cadena de conexión a SQL, la clave de JWT y la contraseña del
  mail. Solo `whatsapp.env`.
- No exponer el puerto 3002 (ni con una regla de UFW ni con `0.0.0.0` en
  `WHATSAPP_SERVICE_PORT`).
- No usar el chip dedicado desde un teléfono físico en simultáneo — invalida
  la sesión vinculada acá.
