# Progreso: canal de WhatsApp con Baileys

Punto de retoma de la tarea larga "canal de WhatsApp con Baileys". Si se corta una sesión, **este archivo manda**: dice qué
está hecho, qué decidí y por qué, qué falta y qué pasos manuales le tocan a Agustín.

- **Rama:** `feat/whatsapp-baileys`, creada desde `origin/main` (b3c94af, PR #41). Sin merge a `main`.
- **Decisión de producto (Agustín):** Baileys (WhatsApp Web no oficial) en vez de la API oficial: más simple y menos RAM.
  Riesgo asumido: WhatsApp puede bloquear el número, por eso va un **número dedicado de la clínica**, nunca uno personal.
- **Límites duros:** nada de `.env`/secretos; sin deploy, sin `sudo`, sin tocar el VPS ni la base `yosoy`; **ningún test ni
  comando abre una conexión real a WhatsApp ni genera o escanea un QR**; sin merge a `main`; sin `--force`.
- **Última actualización:** 2026-10-16, tras la etapa 5 (anti-bloqueo y consentimiento).

## Estado de las etapas

La numeración es la del pedido. El orden de ejecución fue: primero los commits previos, después la **9** (la dependencia
condiciona cómo se escribe el resto) y recién ahí la 1 a la 8.

| Etapa | Qué | Estado | Commit |
|---|---|---|---|
| previo | Frase de `DESARROLLO_LOCAL.md` (base de pruebas), pedida aparte | hecho | `6e846be` |
| previo | Línea suelta ` main` al final de `DEPLOY.md` (resto de un conflicto de merge) | hecho | `c384e1c` |
| — | Este archivo | hecho | `892ae1e` |
| 9 | Dependencia: paquete, versión exacta, `npm audit`, red de seguridad de los tests | **hecho** | `4928d2f` |
| 1 | Canal Baileys en `whatsapp-service` + `Mensajeria__CanalActivo=Baileys` en la API | **hecho** | `bf0c4bd` |
| 2 | Sesión persistente (ruta configurable, fuera del repo, permisos, `.gitignore`) | **hecho** | `7e95b94` |
| 3 | Vinculación por QR (comando y endpoint local) | **hecho** | `a5001e0` |
| 4 | Reconexión con backoff y manejo de `loggedOut` | **hecho** | `d5039bf` |
| 5 | Anti-bloqueo (ritmo, pausas, tope diario, consentimiento) | **hecho** | (este commit) |
| 6 | Privacidad en logs | pendiente | — |
| 7 | Salud y apagado ordenado | pendiente | — |
| 8 | Unidad systemd, `DEPLOY.md`, `SERVIDOR.md` | pendiente | — |

## Cómo retomar

1. `git switch feat/whatsapp-baileys` y mirar `git log --oneline origin/main..HEAD`: un commit por etapa.
2. Tests del servicio: `cd whatsapp-service && npm test` (Node 22 o más nuevo; no necesita red ni WhatsApp).
3. Si se tocó la API: `Scripts\correr-tests.ps1` contra `yosoy_test` (**nunca** contra `yosoy`).
4. Seguir por la primera etapa en `pendiente` de la tabla de arriba.

## Hallazgos de la lectura inicial (antes de tocar nada)

- **El servicio ya existe y usa `whatsapp-web.js`** (Puppeteer + Chromium, `MemoryMax=2G`). Baileys se agrega como un
  **segundo backend** detrás de la misma interfaz HTTP; el actual queda como está.
- **La interfaz que usa la API hoy** es HTTP en loopback: `POST /enviar` (`{telefono, texto}` → `{exito, idExterno, error}`),
  `GET /estado` y `POST /desvincular`, los tres con `X-Shared-Secret`; el servicio le reenvía los mensajes entrantes a
  `POST {API_BASE_URL}/mensajeria/whatsapp/entrante`. `CanalWhatsAppWebJs` y `WhatsAppAdminService` (API) hablan eso y no
  saben qué librería hay detrás.
- **`Mensajeria:CanalActivo` hoy acepta `Simulado` y `WhatsAppWebJs`**; cualquier otro valor frena el arranque. El pedido dice
  "Simulado y Baileys": se **agrega** `Baileys` y se **conserva** `WhatsAppWebJs`, porque el servidor puede tener esa
  variable cargada y sacarla rompería el arranque de la API en el próximo deploy.
- **`privacidad.js` no tenía ninguna regla de consentimiento**: solo `ocultarNumeros` (enmascarar teléfonos en logs). El pedido
  habla de "consentimiento/opt-in según privacidad.js"; la política de consentimiento se **escribe** ahí (etapa 5).
- **Hoy la API le manda WhatsApp a cualquier paciente con teléfono cargado** (`NotificadorTurnos.EnviarChat`): avisos de
  turno y recordatorios, sin ningún chequeo de consentimiento propio del canal. Es justo lo que la etapa 5 acota.
- **`YoSoy_Handoff_Migracion.md` no está** en el repo ni en `docs/` (ni en `Documents`, `Downloads`, `Desktop` ni bajo
  `D:\Claude`). El diseño sale del pedido y del código existente.
- **`DEPLOY.md` terminaba con una línea suelta ` main`** (resto del conflicto de merge del PR #34): se sacó en un commit mínimo.
- **El nodo local es Node 24 y el VPS usa Node 22**: el código no puede usar nada que exista solo en 24. Baileys exige Node >= 20.

## Dependencia (etapa 9): lo que encontré en npm

Consultado el 2026-10-16 con `npm view` (solo lectura) y con instalaciones de prueba en carpetas aparte (fuera del repo, con
`--package-lock-only --ignore-scripts`).

| | `@whiskeysockets/baileys@6.7.24` | `@whiskeysockets/baileys@7.0.0-rc14` |
|---|---|---|
| Estado | **última estable** (tag `legacy`) | candidata (RC), tag `latest` |
| Publicada | 2026-07-29 | 2026-07-29 |
| `engines` | Node >= 20 | Node >= 20 |
| Módulo | **ESM puro** (`"type": "module"`) | ESM puro |
| Paquetes en el árbol | 115 | 95 |
| Dependencias por git | **sí: `libsignal`** (`git+ssh://`, sin integridad en el lock) | no |
| Paquete nuevo | — | `whatsapp-rust-bridge@0.5.4` (WASM, un solo mantenedor) |
| `npm audit` | 0 vulnerabilidades | 0 vulnerabilidades |
| Advisory crítico GHSA-qvv5-jq5g-4cgg | parcheado (desde 6.7.22) | parcheado (desde rc12) |

- **Nombre del paquete:** `@whiskeysockets/baileys` es el que indica el README del proyecto. El nombre sin scope `baileys`
  publica lo mismo (mismos mantenedores: `purpshell`, `auties00`, `shelltear`), ambos desde GitHub Actions con atestación de
  procedencia SLSA. Se usa el que figura en el README. Ojo con los paquetes de nombre parecido (hay forks y copias que no son
  del proyecto, y esta librería maneja las credenciales de la cuenta): el nombre exacto importa, y por eso la versión va fija.
- **Elegí `6.7.24`** porque el pedido dice "última versión estable" y la 7.x todavía es RC. Pero está en **modo mantenimiento**:
  los mantenedores recomiendan pasar a la 7.x.
- **El problema de `libsignal` por git:** el lock lo deja como `git+ssh://git@github.com/...#<commit>` sin hash de integridad,
  y el VPS no tiene clave SSH contra GitHub: `npm ci` fallaría (es el mismo problema que ya documentó el README del servicio
  con `whatsapp-web.js`). Comparé archivo por archivo el commit de git con `libsignal@6.0.0` publicado en npm: `index.js`,
  `index.d.ts`, `package.json` y todo `src/` son **idénticos**. Se resuelve con `"overrides": {"libsignal": "6.0.0"}` y queda
  todo instalado desde el registro con integridad.
- **Scripts de instalación:** `preinstall` de Baileys es `node ./engine-requirements.js` (solo chequea Node >= 20); `protobufjs`
  tiene su `postinstall` conocido. `prepare` (que compila) no corre al instalar desde el registro.
- **Para pasar a la 7.x** alcanza con cambiar la versión exacta y sacar el `override`; la capa de integración (`baileys/cargar.js`)
  usa solo un subconjunto chico de la librería y una prueba de contrato lo vigila.

### Qué quedó hecho en la etapa 9

- `whatsapp-service/package.json`: `"@whiskeysockets/baileys": "6.7.24"` (**exacta, sin `^`**) y
  `"overrides": {"libsignal": "6.0.0"}`. Instalado con `npm install --save-exact --ignore-scripts`.
- `package-lock.json`: solo **altas** (99 paquetes), ningún paquete existente cambió de versión, origen ni hash; `libsignal` y
  Baileys salen de `registry.npmjs.org` y **todo paquete con origen trae `integrity`** (sin dependencias por git).
- **Instalación como en el VPS reproducida** en una carpeta aparte: `npm ci --omit=dev` desde el lock → 405 paquetes, 147 MB,
  sin errores (con `--ignore-scripts` y `PUPPETEER_SKIP_DOWNLOAD=1`, para no bajar Chromium).
- **`npm audit` del servicio: 13 altas, exactamente las mismas de antes** (9 de la cadena de descarga de Chromium de
  `puppeteer`/`whatsapp-web.js` y 4 de `patch-package`); **ninguna viene de Baileys ni de sus dependencias**. Si algún día se
  saca `whatsapp-web.js`, quedan solo las 4 de `patch-package`.
- **Costo de memoria medido:** importar Baileys suma ~48 MB de RSS (31 → 79 MB) y tarda ~0,5 s; una conexión activa suma lo que
  use la sesión, pero no hay navegador. `whatsapp-web.js` lleva Chromium (el README del servicio ya documentaba que llegó a
  `MemoryMax=2G`).
- `baileys/cargar.js`: **único lugar** que importa la librería (con `import()` dinámico: es ESM y el servicio es CommonJS) y
  devuelve una "fachada" con lo que se usa; si la versión instalada no expone algo, el servicio no arranca y lo dice.
- **Red de seguridad de los tests** (`test/helpers/sin-red.js`): `npm test` ahora carga con `--require` un bloqueo que hace
  fallar cualquier conexión o consulta DNS que no sea loopback (`ERR_RED_BLOQUEADA`), en cada archivo de test. Así ningún test
  puede hablar con WhatsApp ni con internet, ni por error. Sus pruebas usan direcciones de documentación (RFC 5737), que
  no existen en internet.
- **Pruebas** (`npm test`, 29 en total, 17 nuevas): contrato con la librería real (carga el paquete **sin conectar**), versión
  exacta, `override` y lock sin git, bloqueo de red, y una guardia que verifica que ningún test importa el paquete real ni
  llama a `makeWASocket`. Verificadas con mutaciones (`^` en la versión, sin `override`, `libsignal` por git, un test que importa
  el paquete real): cada una hace fallar la prueba correspondiente.

## Etapa 1: el canal Baileys

### Qué quedó hecho

- **`whatsapp-service` se partió en piezas** (antes todo estaba en `index.js`, que ahora es solo el arranque):
  `config.js` (lee y valida las variables **antes** de cargar nada: si falta algo, frena con un mensaje que dice qué),
  `backend.js` (arma el backend elegido, con carga perezosa: con Baileys no se carga `whatsapp-web.js`/Puppeteer, y al revés),
  `servidor.js` (el Express: **mismas rutas y mismo contrato HTTP** que usa la API, todo detrás de `X-Shared-Secret` menos
  `/salud`), `reenvio.js` (entrantes hacia `POST {API_BASE_URL}/mensajeria/whatsapp/entrante`, con tope de 5 s).
- **`baileys/`**: `servicio.js` (`ServicioBaileys`: ciclo de vida, estados, envío, entrantes, desvincular), `numeros.js`,
  `mensajes.js`, `logger.js`, `sesion.js` (mínima; la etapa 2 la endurece) y `cargar.js` (etapa 9).
- **Estados** que informa `/salud` y `/estado`: `detenido`, `iniciando`, `conectado`, `reconectando`, `requiere_vincular`.
- **API (.NET):** `Mensajeria:CanalActivo` acepta `Simulado`, `Baileys` y `WhatsAppWebJs`. Los dos últimos usan la misma clase
  (`CanalWhatsAppWebJs`): habla HTTP con el servicio y no sabe qué librería hay detrás. Con `Simulado` nada cambia. El error de
  un valor inválido ahora lista los tres válidos.
- **`README.md` del servicio:** sección "Dos backends" y tabla de variables de Baileys.

### Decisiones de diseño de esta etapa

- **Un socket cerrado no se reutiliza** (la librería lo deja inservible): cada conexión crea uno nuevo. Los eventos de un socket
  descartado se ignoran **por identidad** y además se le sacan los listeners (doble defensa: se probó con un emisor que no
  deja quitarlos). Un intento de conectar que quedó a medias cuando llega `desvincular()` o `cerrar()` se descarta.
- **Parecer una persona, no un servicio:** `markOnlineOnConnect: false`, sin bajar el historial de chats, sin vistas previas
  de links, navegador `Ubuntu / YoSoy`.
- **Número de destino:** WhatsApp en Argentina acepta el celular con o sin el `9`; se le pregunta a WhatsApp (`onWhatsApp`)
  cuál de las dos formas existe y se recuerda la respuesta (24 h si existe, 1 h si no), para no repetir consultas.
- **Entrantes:** solo mensajes nuevos (`notify`, no el historial); se ignoran los propios, grupos, difusiones, canales y lo que
  no es texto; los repetidos se descartan por id; los chats con identificador `@lid` se resuelven al teléfono real con
  `senderPn`/`remoteJidAlt`. El texto pasa por memoria y **nunca por el log**.
- **QR:** vive solo en memoria y sale únicamente por `GET /estado` (que pide el secreto); no se escribe en ningún log.
- **Logger de la librería:** Baileys es muy conversador; solo `warn` y `error` llegan al log, como una línea de texto con los
  números enmascarados (nunca el objeto entero).
- **`desvincular()`** (el botón del Configurador): cierra la sesión en WhatsApp (con tope de 15 s), espera lo que hubiera por
  guardar, borra las credenciales y deja un QR nuevo. Si no se puede borrar, **no sigue como si nada**: queda en
  `requiere_vincular` con motivo `error_borrando_sesion` y el error sube.
- **Reconexión en esta etapa:** espera fija de 5 s (inmediata para el reinicio que WhatsApp pide al terminar de vincular). El
  backoff exponencial y el trato de cada motivo de desconexión son la etapa 4; `loggedOut` ya **no** se reintenta.
- **`POST /enviar` acepta `optIn`** (solo si es exactamente `true`) y lo pasa a la cola; la política que lo usa es la etapa 5.

### Pruebas de esta etapa

- **`npm test`: 138 pruebas, 0 fallas, ~2 s**, sin red (el bloqueo de la etapa 9 las protege). Todas con el socket de Baileys
  simulado: ninguna abre una conexión ni envía nada. Hay un reloj falso (la espera de una reconexión de 5 minutos se prueba en
  un instante) y carreras con compuertas, para que el orden sea siempre el mismo.
- **Pruebas de mutación:** apliqué a mano 45 cambios de comportamiento al código (por ejemplo: reintentar `loggedOut`, loguear
  el QR o el texto de un paciente, escuchar en `0.0.0.0`, aceptar `/enviar` sin secreto, cargar los dos backends a la vez) y
  verifiqué que **cada uno hace fallar alguna prueba**. Las que al principio no fallaban (eventos de un socket viejo,
  `desvincular` en medio de una conexión, `iniciar` dos veces, borrar antes de terminar de guardar) se cubrieron con pruebas
  nuevas y se volvieron a probar. Los archivos quedaron idénticos tras cada mutación.
- **API:** `CanalActivoTests` (nuevo) y `WhatsAppSecretoHeaderTests` (ahora con `WhatsAppWebJs` y `Baileys`): 33 pruebas
  relacionadas, verdes contra `yosoy_test`; sacar el `case "Baileys"` hace fallar 3.

## Etapa 2: la sesión persistente

### Qué quedó hecho

- **`baileys/sesion.js` se endureció.** La sesión (`creds.json` + claves de cifrado) es una credencial de la cuenta; al arrancar,
  **antes de abrir el puerto y de cargar la librería**, `crearBackend` llama a `sesion.preparar()`, que:
  1. exige que la carpeta esté **fuera de cualquier repositorio git** (busca un `.git` hacia arriba, también si es un archivo)
     **y de la carpeta de la aplicación** (ni adentro ni conteniéndola; se resuelven los enlaces simbólicos), sin crear nada si no;
  2. fija `umask 077` (todo lo que cree el proceso, incluidos los archivos de la librería, queda solo para su usuario);
  3. crea la carpeta en **700**, y si existía con permisos de más deja la carpeta en 700 y sus archivos en **600**;
  4. exige que sea **dedicada**: si hay archivos que no son de la sesión (o subcarpetas, o enlaces), **no arranca** y no toca nada;
  5. comprueba que **se puede escribir** (escribe y borra un archivito): con `ProtectSystem=strict` y sin `ReadWritePaths`,
     el servicio lo dice al arrancar en vez de perder la vinculación al reiniciar.
  Cualquier problema corta el arranque con un mensaje que nombra `WHATSAPP_BAILEYS_SESSION_PATH`.
- **Respaldo de `creds.json`.** Hallazgo al leer la librería: `useMultiFileAuthState` escribe sin cuidado y, si `creds.json`
  queda cortado, **lo da por inexistente y arma una identidad nueva sin avisar** (hay que volver a escanear el QR). Ahora:
  cada guardado deja antes la versión anterior como `creds.json.bak` (escritura atómica: temporal + `fsync` + renombrado);
  si después de guardar el archivo quedó inservible, se vuelve a la versión anterior (y el error original, si lo hubo,
  sube); y al abrir la sesión, si `creds.json` falta o está cortado y hay respaldo válido, se restaura. Un respaldo que no se
  puede escribir **no impide** guardar las credenciales nuevas (se avisa).
- **Borrado:** `limpiar()` (desvincular o `loggedOut`) borra credenciales, respaldo y temporales, y **nada más**: si hay algo
  ajeno, no borra nada. No sigue enlaces simbólicos.
- **`.gitignore`:** en la raíz, por nombre (`creds.json`, `creds.json.*`, `pre-key-*.json`, `session-*.json`,
  `sender-key-*.json`, `app-state-sync-*`, `lid-mapping-*`, `device-list-*`, `tctoken-*`) y por carpetas típicas
  (`sesion-baileys/`, `auth_info_baileys/`); en `whatsapp-service/.gitignore`, una nota que remite a la raíz. Son una red de
  seguridad: la regla es que la sesión esté fuera del repo (y `preparar()` lo exige).
- **README del servicio:** sección "Baileys: la carpeta de la sesión".

### Decisiones y límites

- **Ruta sugerida: `/opt/yosoy/whatsapp/sesion-baileys`**, al lado de `app/` (el despliegue reemplaza `app/`, así que nunca la
  toca). No se reutiliza `/opt/yosoy/whatsapp/sesion` (la de whatsapp-web.js, con el perfil de Chromium): el chequeo de
  carpeta dedicada la rechazaría.
- **El puerto es el candado:** `preparar()` no restaura nada del respaldo (eso lo hace `abrirAuth`, que corre recién después de
  que `iniciar()` tiene el puerto), así un segundo proceso que arranca por error no puede pisar un guardado del primero.
- **No se implementó un almacén propio de credenciales** (con escritura atómica de todos los archivos): se envuelve el de la
  librería. Solo `creds.json` es crítico; las claves sueltas (`pre-key-*`, `session-*`) que queden cortadas las trata la
  librería como inexistentes y se renegocian con cada contacto. Si algún día se quiere más, el camino es una base de datos.
- **Límite de lo que pude verificar:** estas pruebas corrieron en Windows, donde **5 pruebas se saltean** (permisos 700/600 y
  `umask`, no poder crear la carpeta, y dos de enlaces simbólicos) porque Windows no tiene permisos POSIX. La lógica POSIX
  son ~15 líneas y está revisada, pero **hay que correr `npm test` una vez en Linux** (ver "Pasos manuales").

### Pruebas de esta etapa

- `test/sesion.test.js` (nuevo, 38 pruebas) y ampliaciones en `backend.test.js` y `baileys-servicio.test.js`: ubicación (repo,
  `.git` como archivo, aplicación, vecinos de nombre parecido, enlaces), carpeta dedicada, permisos, escritura, respaldo
  (6 formas de `creds.json` dañado, sin respaldo, respaldo viejo que no debe pisar uno sano, falla del respaldo, disco lleno
  a mitad de un guardado), borrado, y que el `.gitignore` frene cada nombre de archivo de sesión (con `git check-ignore`) y
  que ninguno esté versionado.
- **`npm test`: 179 pruebas, 174 pasan, 5 saltadas (solo POSIX), 0 fallas.**
- **Mutaciones:** 34 cambios de comportamiento a propósito en `sesion.js` y `backend.js`; **30 detectados** y 4 que solo se pueden
  detectar en POSIX (permisos, `umask`, enlaces simbólicos): ver arriba. (Dos de las que primero se escaparon, `..sesion`
  dentro de la aplicación y `deps.appDir`, llevaron a pruebas nuevas.)

## Etapa 3: vinculación por QR

### Qué quedó hecho

- **Tres formas de vincular, ninguna con el QR en un log o un archivo** (ver README, "Baileys: vincular el número (QR)"):
  1. el **Configurador** (el flujo de siempre: muestra el QR de `GET /estado`);
  2. **`POST /vincular`** (loopback, con secreto): abre una ventana nueva; el QR se ve en `GET /estado`. 409 si no corresponde;
  3. **`npm run vincular`** / `node vincular.js` (**el comando**): dibuja el QR en la terminal, reemplazándolo en cada renovación, y
     termina cuando el número queda vinculado. Corre con el servicio apagado, como el usuario del servicio, y solo en una
     terminal real. No necesita el secreto compartido ni reenvía mensajes.
- **Ventanas de QR acotadas.** Hallazgo: Baileys cierra cada ventana (~2,7 min) con un 408 y, con la reconexión de la etapa 1, el
  servicio habría pedido un QR nuevo cada ~3 minutos para siempre, sin nadie mirando. Ahora, sin número vinculado, se abren
  hasta `WHATSAPP_BAILEYS_VENTANAS_QR` ventanas solas (1 a 5, por defecto 2) y después el servicio queda en `requiere_vincular` /
  `qr_vencido`, esperando que una persona pida otra (`POST /vincular`, "Desvincular y cambiar número" o el comando). Cada
  pedido de una persona renueva el cupo.
- **"Vinculado" se decide por `creds.me`, no por `creds.registered`.** Hallazgo al leer la librería: con el QR, Baileys deja
  `me` y **nunca** marca `registered` (eso solo pasa con el emparejamiento por código de teléfono). Usar `registered` habría
  tomado una sesión válida por vacía. Los dobles de prueba ahora se comportan como la librería real en esto.
- **`ServicioBaileys.vincular()`** (nuevo): `ya_vinculado` si hay número, `en_curso` si todavía conecta, `detenido`,
  `error_borrando_sesion` si la sesión vieja no se pudo borrar; idempotente si ya hay un QR esperando. `qrTexto()` entrega el
  texto crudo **solo al comando** (mismo proceso): ninguna ruta HTTP lo devuelve (hay una prueba que lo verifica).
- **`config.js`:** `leerConfigBaileys()` y `leerPuerto()` salen aparte para que el comando no pida la API ni el secreto.

### Decisiones y límites

- **El comando es autónomo** (abre su propio socket sobre la misma carpeta de sesión) en vez de hablarle al servicio por HTTP:
  así el texto crudo del QR nunca viaja por HTTP. A cambio hay que parar el servicio antes (lo verifica por el puerto).
- **No cambié la API ni el Configurador.** Hoy el Configurador solo sabe `GET /estado` y `POST /desvincular`; con Baileys alcanza
  (el QR aparece solo al arrancar sin número, y "Desvincular y cambiar número" abre otra ventana). Un botón "Vincular" que llame
  a `POST /vincular` sería un cambio chico en la API, **no hecho** (queda como mejora; ver "Qué falta").
- **Lo que no probé, por el límite de no tocar WhatsApp real:** que el QR que genera la librería real se vea bien en una terminal
  de SSH, y el ciclo de ~2,7 minutos con tiempos reales. Los tiempos se prueban con reloj falso y el dibujo con `qrcode-terminal`
  sobre un texto de ejemplo.

### Pruebas de esta etapa

- `test/vincular.test.js` (nuevo, 16), más pruebas de ventanas y `vincular()` en `baileys-servicio.test.js`, de `/vincular` (409,
  500, sin QR crudo) en `servidor.test.js`, y de configuración y cableado.
- **`npm test`: 213 pruebas, 208 pasan, 5 saltadas (solo POSIX), 0 fallas.** Además arreglé una prueba intermitente de la etapa 1
  (leía `creds.json` mientras se escribía).
- **Mutaciones:** 36 cambios a propósito (servicio, comando, configuración, endpoint, cableado); **todos detectados.** Para que se
  note que `vincular`/`desvincular` renuevan el cupo, esas pruebas usan el cupo por defecto (2 ventanas): con 1 el resultado
  visible es el mismo. Una mutación dejó una carpeta vacía dentro del repo; las pruebas que apuntan una sesión adentro del repo ahora la
  borran si el código la llegara a crear.

## Etapa 4: reconexión con backoff

### Qué quedó hecho

- **`baileys/reconexion.js` (nuevo):** `PlanDeReconexion` y `clasificarDesconexion`, lógica pura.
  - **Backoff exponencial:** 2 s, 4 s, 8 s ... tope de 5 minutos, con ±20 % al azar (nunca pasa del tope). Reemplaza los 5 s fijos
    de la etapa 1, también para los errores al crear el socket y para `recuperar()`.
  - **Cuándo vuelve a empezar:** cuando una conexión se sostiene un minuto, o cuando una persona actúa (arranque, `vincular`,
    `desvincular`). Una conexión que abre y se cae enseguida **no** reinicia la espera (si el servidor la acepta y la tira, sigue
    espaciándose).
  - **Qué cortes se reintentan y cuáles no** (tabla completa en el README, "Baileys: si se desconecta sola"): 401 (`loggedOut`) →
    `requiere_vincular` y borra la sesión muerta, **sin reintentar** (era el pedido); 403, 440 y 405 → estado nuevo
    **`requiere_atencion`**, sin reintentar y sin borrar nada; 500 → se reintenta y a la tercera seguida pide atención, sin
    borrar; 515 → se atiende enseguida hasta 3 veces por minuto; el resto, backoff.
- **Estado nuevo `requiere_atencion`** (motivos `cuenta_restringida`, `sesion_reemplazada`, `version_rechazada`,
  `sesion_invalida`): `/salud` lo informa con `requiereAtencion: true` y 503. `vincular()` se niega ahí; `desvincular()` sirve
  para cambiar de número. El proceso **sigue vivo** (no sale con error): si saliera, systemd lo reiniciaría cada pocos segundos y
  volvería a golpear la puerta de WhatsApp.
- **`/salud` ahora informa** `requiereAtencion` e `intentosDeReconexion`.
- `clasificarDesconexion` ignora lo que no sea un código numérico (un `undefined` que coincidiera con un motivo que la librería
  no trae habría clasificado mal).

### Decisiones y límites

- **El 405 (versión rechazada) no figura en `DisconnectReason`** de Baileys 6.7.24; se define en el módulo. Por la lectura de la
  librería, un `<failure>` sin `reason` se toma como 500: por eso el 500 se reintenta y solo pide atención si se repite.
- **No se borra la sesión salvo en `loggedOut`.** Un 403, un 440 o un 500 no prueban que las credenciales estén muertas; borrarlas
  obligaría a vincular de nuevo y, con un número bloqueado, no arregla nada. Esa decisión queda en una persona.
- **La espera no se configura por variable de entorno** (base, factor, tope y azar son parámetros del módulo): no vi motivo para
  que alguien los cambie en producción, y menos opciones son menos formas de equivocarse.
- **No pude probar contra WhatsApp real** qué códigos manda de verdad ante cada situación (límite de la tarea). La tabla sale de
  la lectura de Baileys 6.7.24 y de su documentación; si WhatsApp usara otro código para algo, cae en "corte común" (backoff), que
  es el lado seguro.

### Pruebas de esta etapa

- `test/reconexion.test.js` (nuevo, 15) y 18 pruebas nuevas en `baileys-servicio.test.js`: secuencia exacta de esperas (2 s ... 300 s),
  tope, azar en los extremos, reinicio por conexión estable y no por una que dura 59 s, cada código terminal (sin reintento, sin
  borrar la sesión, `recuperar()` y `enviar` no lo pisan), los 500 seguidos, los 515, `vincular`/`desvincular` desde
  `requiere_atencion`, y la forma completa de `salud()`.
- **`npm test`: 246 pruebas, 241 pasan, 5 saltadas (solo POSIX), 0 fallas.**
- **Mutaciones:** 50 cambios a propósito; **49 detectados** y 1 equivalente (sacar `plan.reiniciar()` de `iniciar()`, que solo corre
  con el plan recién creado). Cuatro se escaparon primero y llevaron a pruebas nuevas: que un 403/500 no borre la sesión
  (había que esperar al borrado asíncrono antes de mirar), que `recuperar()` no pise `requiere_atencion` y que `desvincular` y
  `vincular` reinicien la espera.

## Etapa 5: anti-bloqueo y consentimiento

### Qué quedó hecho

- **Solo se escribe a quien aceptó** (`consentimiento.js`, con las reglas en `privacidad.js`, como pedía el encargo). Una persona
  acepta si **le escribió primero** al número de la clínica (vale 365 días desde su último mensaje) o si la API manda
  `optIn: true` en ese pedido. Si escribió **BAJA** (o STOP, NO MOLESTAR...) no se le manda nada, ni con `optIn`, hasta que escriba
  **ALTA**. Sin constancia: **`403 sin_consentimiento`** (el valor por defecto es no enviar). "Cancelar" no es una baja, porque
  en el bot es cancelar un turno; el comando tiene que ser el mensaje entero.
- **Qué se guarda de cada persona:** la **huella** de su teléfono (HMAC-SHA256 con una clave derivada del secreto compartido,
  que no está en ningún archivo de datos) y tres fechas. Ni el teléfono ni el texto de un mensaje.
- **Topes** (`limites.js`, guardados en disco: reiniciar no devuelve el cupo): por día en la zona horaria de la clínica (100), por
  persona y por día (3), por hora (40, los que se pasan esperan en la cola) y capacidad de la cola (50). Lo que espera en la cola
  ya cuenta contra el tope del día; un envío que falla devuelve su lugar. Se rechaza con `429 tope_diario` o
  `429 limite_destinatario`.
- **Ritmo humano** (`cola.js`): pausa al azar de 4 a 9 s entre mensajes, una pausa larga de 30 a 75 s cada 8 a 14, y "escribiendo..."
  antes de cada mensaje (hasta 4 s según el largo; si falla, el mensaje sale igual).
- **Conexión:** si WhatsApp está reconectando los mensajes esperan; si hace falta una persona, `503 no_conectado`; lo que lleva más
  de 3 horas esperando se descarta.
- **`datos/`** (subcarpeta de la carpeta de la sesión, 700): ahí viven `consentimientos.json` y `envios.json`. Es la única subcarpeta
  que `sesion.js` acepta, sobrevive a "desvincular" y a un cierre de sesión (el número y sus pacientes son los mismos) y se
  respalda/borra a mano. Para empezar de cero con otro número: borrar `datos/`.
- **Arreglo de un riesgo de la etapa 2:** `preparar()` le cambiaba los permisos (700) a la carpeta **antes** de comprobar que fuera
  propia; si alguien apuntaba la variable a, digamos, `/opt/yosoy`, le habría hecho `chmod 700`. Ahora primero mira que sea propia.
- **Nada cambia con whatsapp-web.js:** la cola de siempre (3 a 5 s entre mensajes, sin consentimiento ni topes) sigue igual; las
  protecciones llegan solo con el backend Baileys (`crearColaParaBackend`).
- Módulos nuevos: `limites.js`, `consentimiento.js`, `almacen.js` (JSON atómico que se junta y se guarda), `archivos.js` (escritura
  atómica compartida con la sesión). Variables nuevas: `WHATSAPP_TOPE_DIARIO`, `_TOPE_POR_HORA`, `_TOPE_POR_PERSONA`,
  `_CONSENTIMIENTO_DIAS`, `_ZONA_HORARIA`, `_PAUSA_MIN_S`, `_PAUSA_MAX_S`, `_SIMULAR_ESCRITURA` (todas opcionales; README).

### Decisiones y límites

- **La API todavía no manda `optIn`.** Hoy `CanalWhatsAppWebJs` manda solo `{telefono, texto}` y la API no sabe si un paciente aceptó
  recibir WhatsApp (el "consentimiento" que existe es el informado de telemedicina, otra cosa). Consecuencia, **con Baileys**: los
  avisos y recordatorios **solo salen a quien le escribió primero al número de la clínica**; al resto se le rechaza con 403 y
  queda en el log de la API. Es el lado seguro, pero hay que decidir cómo se resuelve: ver "Pasos manuales".
- **El consentimiento por "escribió primero" lo juzga el servicio, no la API:** funciona aunque la API esté caída, y el pedido de BAJA
  se respeta siempre. A cambio, una baja no llega a la base de la API: el bot seguirá viendo el mensaje "BAJA" (se sigue reenviando
  como cualquier otro) y sus respuestas se rechazarán con `403 baja`.
- **Las bajas se recuerdan para siempre** (no vencen con el año); lo demás se olvida a los 365 días. Si se rota el secreto
  compartido, las huellas dejan de coincidir y el registro "se olvida" (se vuelve a no enviar a nadie hasta que escriban de nuevo).
- **Los valores por defecto son una opinión, no un dato de WhatsApp.** WhatsApp no publica sus umbrales. Los topes (100/día, 40/hora,
  3 por persona) y las pausas son prudentes para un consultorio, no una garantía: lo que más pesa es que **las personas esperen los
  mensajes** (el consentimiento) y arrancar bajo con un número nuevo.
- **Las pausas y los topes no se probaron contra WhatsApp** (límite de la tarea); se probaron con reloj falso, exactos al milisegundo.

### Pruebas de esta etapa

- Nuevas: `consentimiento.test.js` (36: reglas, almacén, registro, límites), `cola.test.js` (23) y ampliaciones en
  `baileys-servicio.test.js` ("escribiendo..."), `config.test.js`, `backend.test.js` (de punta a punta: quien escribe primero queda
  anotado antes de reenviar a la API; la baja; sin teléfono ni texto en el log ni en los archivos) y `sesion.test.js` (`datos/`).
- **`npm test`: 322 pruebas, 316 pasan, 6 saltadas (solo POSIX), 0 fallas.**
- **Mutaciones:** 87 cambios a propósito (reglas de consentimiento, huellas, topes, cola, configuración, cableado, "escribiendo..."):
  **todos detectados.** Cuatro se escaparon primero: el `optIn` estricto en la cola, que el apagado espere a los dos archivos, que
  la cola mire el estado real del servicio (las tres llevaron a pruebas nuevas) y un filtro redundante de `limites.cargar()` que se
  sacó del código. Otras (el orden "primero ver si hay lugar en la cola, después reservar cupo", o que un recordatorio no se
  descarte antes de las 3 horas) se reforzaron antes de correr las mutaciones.

## Decisiones tomadas (para revisar)

1. **Dos backends, `webjs` por defecto.** `WHATSAPP_BACKEND=baileys` activa el nuevo; sin la variable, el servicio se comporta
   exactamente como hoy. Así el próximo deploy no cambia nada hasta que Agustín lo decida.
2. **`WhatsAppWebJs` se conserva en la API** además de `Baileys` (ver arriba).
3. **Variable propia para la sesión de Baileys** (`WHATSAPP_BAILEYS_SESSION_PATH`), obligatoria, sin valor por defecto: así
   nunca puede terminar dentro del repo por omisión ni mezclarse con la carpeta de `whatsapp-web.js`.
4. **El mismo nombre de canal ("WhatsApp") en la bitácora de la API** para los dos backends: cambiar de librería no parte el
   historial de mensajes en dos.

## Pasos manuales para Agustín

(Se completa etapa por etapa; nada de esto lo hice ni lo voy a hacer yo.)

- **Correr `npm test` una vez en Linux** (por ejemplo en una copia aparte, o en el VPS tras `npm ci`): en Windows se saltean 5
  pruebas de permisos y enlaces simbólicos. Las pruebas usan carpetas temporales y no tocan la sesión real.
- **DECISIÓN TUYA, importante antes de usar Baileys en producción: cómo llega el consentimiento al servicio.** Con la API como está, solo
  reciben WhatsApp los pacientes que le escribieron primero al número de la clínica. Tres caminos (se pueden combinar):
  (A) **Guardar la aceptación en la API**: un campo en `Paciente` (por ejemplo `AceptaWhatsApp`) que se tilda al registrarse, en el
  perfil o en recepción, y que `NotificadorTurnos`/`CanalWhatsAppWebJs` manden como `"optIn": true`. Es un cambio de API + base +
  pantallas y de texto legal; no lo hice porque requiere tu decisión sobre dónde y con qué leyenda se acepta.
  (B) **Que el primer contacto sea del paciente**: un link `wa.me/<número>?text=Hola` en la web y en el mail de confirmación; al
  escribir, queda con constancia por 365 días y recibe respuestas y recordatorios. Sin cambios de código.
  (C) **Nada de recordatorios por WhatsApp a quien no haya escrito** y usar mail para el resto.
  Mientras no se elija, `NotificadorTurnos` va a registrar fallos de envío para los pacientes sin constancia.
- **Arrancar el número nuevo con topes bajos** (por ejemplo `WHATSAPP_TOPE_DIARIO=30` y `WHATSAPP_TOPE_POR_HORA=10`) y subirlos de a poco
  durante semanas; es lo que más baja el riesgo de bloqueo.
- **Vincular el número dedicado (yo no lo hago nunca):** tener el chip del número dedicado de la clínica con WhatsApp ya activo en un
  teléfono, y escanear el QR vos, por la forma que prefieras (README, "Baileys: vincular el número (QR)"). Después de escanear,
  no usar ese WhatsApp en otro teléfono al mismo tiempo: invalida la sesión.
- **Crear la carpeta de la sesión en el VPS** (cuando se vaya a usar Baileys; el detalle completo va en `DEPLOY.md`, etapa 8):
  `sudo install -d -m 700 -o yosoy -g yosoy /opt/yosoy/whatsapp/sesion-baileys` y poner
  `WHATSAPP_BAILEYS_SESSION_PATH=/opt/yosoy/whatsapp/sesion-baileys` en `/opt/yosoy/config/whatsapp.env`. Esa carpeta también
  tiene que figurar en `ReadWritePaths` de la unit.

## Qué falta / pendiente de decisión

- Etapas 6 a 8 (ver la tabla). Lo que las etapas 1 a 5 dejan a propósito para después: revisión de privacidad de todo el log (6),
  `/salud` más completo (con los contadores de la cola y los topes, que ya existen en `cola.resumen()`) y apagado ordenado
  (extraer y probar el cierre de `index.js`, incluido `datos.cerrar()`) (7), unidad systemd y docs de despliegue (8).
- **Mejora opcional (no bloquea):** un botón "Vincular" en el Configurador que llame a `POST /vincular` (hoy, con el QR vencido,
  se usa "Desvincular y cambiar número"). Es un cambio chico en `WhatsAppAdminService` y la pantalla; lo dejé sin tocar para no
  mezclar un cambio de API/UI en esta tarea.
- **Decisión pendiente (no bloquea nada): ¿se respalda la sesión de Baileys?** `SERVIDOR.md` ya pide respaldar fuera del
  servidor la sesión de WhatsApp. Con Baileys, perderla significa volver a escanear un QR (unos minutos, con el teléfono del
  número dedicado), no perder el número; el costo de respaldarla es que son credenciales de la cuenta. Si se respalda: cifrada
  y con acceso restringido, nunca en un chat, un ticket ni el repo. El flujo de despliegue (`/opt/yosoy/backups`: app,
  configuración, UFW, base) no la menciona.

