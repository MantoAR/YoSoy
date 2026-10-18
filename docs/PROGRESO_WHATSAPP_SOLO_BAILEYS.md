# Progreso: WhatsApp solo con Baileys y sin recordatorios por WhatsApp

Punto de retoma de la tarea "dejar Baileys como único motor de WhatsApp y eliminar los recordatorios por WhatsApp". Si se corta una
sesión, **este archivo manda**: dice qué hay hoy, qué se decidió, qué está hecho, qué falta y qué pasos manuales le tocan a Agustín.

- **Rama:** `refactor/whatsapp-solo-baileys`, creada desde `origin/main` (ff7e673: el PR #43, consentimiento de WhatsApp). Sin merge a `main`.
- **Decisiones de Agustín:**
  - WhatsApp sigue, pero **solo con Baileys**. WhatsApp Web JS (`whatsapp-web.js`, Puppeteer, Chromium) se saca por completo.
  - **Nada de recordatorios por WhatsApp**, ni automáticos ni programados. Los recordatorios siguen solo por **mail y campanita** de la app.
  - Se pausa el uso saliente por el riesgo de bloqueo de una API no oficial: **lo que quede de WhatsApp tiene que ser de bajo riesgo**.
- **Límites duros:** no se leen ni imprimen `.env`, `whatsapp.env`, `signaling.env` ni `video.env`; sin deploy, sin `sudo`, sin tocar el VPS ni la
  base `yosoy`, sin aplicar migraciones; **nada se conecta ni se manda a WhatsApp real**; sin merge a `main` ni `--force`. Un commit por etapa.
- **Última actualización:** 2026-10-18, tras la etapa 1 (sacar whatsapp-web.js).

## Estado de las etapas

| Etapa | Qué | Estado | Commit |
|---|---|---|---|
| — | Auditoría de todos los flujos de WhatsApp y este archivo | **hecho** | `86be1ac` |
| 1 | Sacar whatsapp-web.js (servicio, API, docs, VPS) | **hecho** | (este commit) |
| 2 | Eliminar los recordatorios por WhatsApp, con tests | pendiente | |
| 3 | Revisar el resto de los flujos salientes (solo se lista y se recomienda) | pendiente | |
| 4 | Consentimiento de WhatsApp: opciones y recomendación (sin tocar migraciones ni tablas) | pendiente | |
| 5 | Anti-bloqueo de lo que quede | pendiente | |

## Cómo retomar

1. `git switch refactor/whatsapp-solo-baileys` y mirar `git log --oneline origin/main..HEAD`: un commit por etapa.
2. Tests del servicio de WhatsApp: `cd whatsapp-service && npm test` (Node 22 o más nuevo; sin red ni WhatsApp: socket simulado).
3. Tests de la API: `Scripts\correr-tests.ps1` (usa la base `yosoy_test`, que tiene que tener las **45 tablas**; el script se niega a usar `yosoy`).
4. Línea base de esta rama, antes de cambiar nada: ver "Línea base" más abajo.
5. Seguir por la primera etapa en `pendiente` de la tabla de arriba.

## Línea base (origin/main, antes de tocar nada)

| Qué | Resultado |
|---|---|
| `npm test` en `whatsapp-service` | 388 pruebas: 382 pasan, 6 saltadas (permisos POSIX), 0 fallas |
| `Scripts\correr-tests.ps1` contra `yosoy_test` (45 tablas, con `ConsentimientoWhatsApp`) | **1407 de 1407**, 0 fallas, 0 omitidas (2 min 28 s) |

## Cómo está armado hoy

```
Paciente ──WhatsApp──▶ [whatsapp-service (Node, 127.0.0.1:3002)] ──POST /mensajeria/whatsapp/entrante──▶ [API .NET] ──▶ BotReservasService
                                  ▲                                                                           │
                                  └────────────── POST /enviar { telefono, texto, optIn? } ◀── CanalWhatsAppWebJs ◀── ServicioMensajes
```

- **whatsapp-service** es un puente HTTP que solo escucha en loopback. Hoy tiene **dos motores** detrás de la misma interfaz: `whatsapp-web.js`
  (el que por defecto arranca si no se define `WHATSAPP_BACKEND`) y Baileys (`WHATSAPP_BACKEND=baileys`). Con `webjs` **no hay consentimiento,
  ni topes, ni pausas largas, ni "escribiendo...": solo 3 a 5 segundos entre mensajes**. Con Baileys sí: solo se le escribe a quien escribió
  primero (365 días) o a quien la API marca con `optIn:true`, BAJA gana sobre todo, topes por día / hora / persona, pausas al azar y "escribiendo...".
- **La API** elige el canal con `Mensajeria:CanalActivo`: `Simulado` (por defecto, no sale nada de la máquina), `Baileys` o `WhatsAppWebJs` (los dos
  últimos son la **misma clase**, `CanalWhatsAppWebJs`: hablan HTTP con el servicio y no saben qué librería hay detrás). Cualquier otro valor frena el arranque.
- Según `docs/SERVIDOR.md` el servicio que corre en el VPS es el de `whatsapp-web.js` + Chromium. **No puedo ver el VPS ni el `.env`** (límite duro):
  el valor real de `Mensajeria__CanalActivo` en producción está **sin verificar**.

## Auditoría: todos los flujos que usan WhatsApp

"Activo" = sale (o entra) por WhatsApp de verdad **si** `Mensajeria__CanalActivo` no es `Simulado` y el servicio está conectado. Con `Simulado` el mismo
código no manda nada: guarda el mensaje en la bandeja como "Enviado" y listo.

### Salientes: la API le pide al servicio que mande un mensaje

Todo mensaje saliente pasa por un único punto: `ServicioMensajes.EnviarAsync` → `IMessageChannel` → `POST /enviar`. Quién lo llama:

| # | Flujo | Dónde está | Qué lo dispara | Qué es | Plan |
|---|---|---|---|---|---|
| S1 | **Recordatorio de turno 24 h** | `RecordatorioBackgroundService.EjecutarUnaVez` → `NotificadorTurnos.EnviarRecordatorio24h` → `AvisarPaciente` (canales `CanalesRecordatorio` = Chat, Email, App) → `EnviarChat` | **El reloj**: un ciclo cada `Mensajeria:IntervaloChequeoMinutos` (5), a todo turno Confirmado que cae en las próximas `RecordatorioHorasAntes` (24) horas. También a mano: `POST /mensajeria/recordatorios/ejecutar` | **Automático, programado y masivo** (uno por paciente con turno, sin que nadie escriba). Activo con canal real y paciente con teléfono. Trae "Respondé 1 o 2" y deja la conversación esperando esa respuesta | **Eliminar (etapa 2)** |
| S2 | Aviso "recibimos tu solicitud de turno" | `NotificadorTurnos.TurnoSolicitado` (canales Chat y App) | `POST /turnos/solicitar`, `POST /public/profesionales/{slug}/reservar`; y el bot (`respondidoPorChat:true`: acá el Chat queda "Omitido", el bot ya contestó) | Automático **por evento**, de a uno; no es respuesta a un mensaje | **Solo listar y recomendar (etapa 3)** |
| S3 | Aviso "tu profesional te propuso un turno" | `TurnoAgendadoPorProfesional` | `POST /turnos/profesional-solicitar` | ídem | etapa 3 |
| S4 | Aviso "turno confirmado" | `TurnoConfirmado` (y `CambioDeEstado`) | `PUT /turnos/{id}/confirmar-paciente`, `PUT /turnos/{id}` (el profesional confirma) | ídem | etapa 3 |
| S5 | Aviso "turno cancelado" | `TurnoCancelado` (y `CambioDeEstado`) | `PUT /turnos/{id}/cancelar`, `PUT /turnos/{id}`; el bot (`respondidoPorChat:true`) | ídem | etapa 3 |
| S6 | **Respuestas del asistente (bot)** | `BotReservasService.ProcesarAsync` → `EnviarAsync(..., "Bot")` | Un **mensaje entrante** de la persona (webhook de WhatsApp o chat simulado) | **Respuesta** a un mensaje entrante: menú, pedir/ver/cancelar turno, preguntas frecuentes, derivación a una persona y el mensaje de emergencia | **Se deja** (etapa 5 lo verifica) |
| S7 | Respuesta manual del equipo | `POST /mensajeria/conversaciones/{id}/responder` (`MensajeriaEndpoints`), origen "Humano" | Una persona del equipo en la bandeja | Respuesta humana dentro de una conversación | Se deja |
| S8 | Aviso "el equipo cerró esta conversación" | `PUT /mensajeria/conversaciones/{id}/cerrar`, origen "Sistema" | Una persona del equipo aprieta "Cerrar" | Texto fijo, **aunque la persona nunca haya escrito** (conversación creada por un aviso) | Etapa 5 (solo si la persona escribió) |
| S9 | Aviso "el equipo reabrió esta conversación" | `PUT /mensajeria/conversaciones/{id}/reabrir`, origen "Sistema" | Una persona del equipo aprieta "Reabrir" | ídem | Etapa 5 |

Lo que **no** sale por WhatsApp, para que no haya dudas:

- Los avisos internos al equipo (`AvisarUrgencia`, `AvisarDerivacion`, `AvisarNoAsistencia`): campanita interna y mail al profesional.
- El mail y la campanita del paciente (recordatorio y avisos), `EmailService`.
- **No hay códigos de verificación ni contraseñas por WhatsApp**: la recuperación de contraseña es por mail (`POST /recuperacion-password`).
- Hay un `CanalWhatsAppCloud` (la API oficial de Meta) que **no está registrado** y lanza `NotImplementedException`: es solo un enchufe para el futuro.

### Entrantes: lo que llega desde WhatsApp

| # | Flujo | Dónde está | Qué hace | Plan |
|---|---|---|---|---|
| E1 | Mensaje de una persona | Baileys `messages.upsert` (`baileys/servicio.js`, `baileys/mensajes.js`) | Solo texto de chats **individuales**: ignora grupos, estados, mensajes propios, historial y todo lo que no es texto. Anota que **escribió primero** (o su BAJA/ALTA) y lo reenvía a la API | Se deja |
| E2 | Webhook de la API | `POST /mensajeria/whatsapp/entrante` (`MensajeriaEndpoints`): solo desde la propia máquina y con el secreto compartido | Pasa el mensaje al asistente (S6) | Se deja |
| E3 | Comando **BAJA** / STOP... | Servicio: `privacidad.js` + `consentimiento.js`. API: `ComandoWhatsApp.EsBaja` → `ConsentimientoWhatsAppService.RegistrarBaja` | El servicio deja de escribirle (hasta que escriba ALTA); la API revoca su aceptación y **el asistente no contesta** | Se deja |
| E4 | Chat simulado | `POST /mensajeria/simulado/entrante` (solo si `ChatSimuladoHabilitado`, y con `SoloLocal`: solo desde la propia máquina) | Mismo asistente. **Usa el canal activo**: con `Baileys`, la respuesta a un teléfono simulado saldría por WhatsApp real (acotado: solo desde la propia máquina y el servicio igual exige constancia de esa persona) | Se deja; queda anotado (H7) |

### Administración y control (no mandan mensajes de pacientes)

| Flujo | Dónde está | Notas |
|---|---|---|
| Estado, QR y desvincular desde el Configurador | `WhatsAppAdminService` + `GET /mensajeria/whatsapp/estado`, `POST /mensajeria/whatsapp/desvincular`, `POST /mensajeria/whatsapp/confirmar-vinculacion`; pantalla en `PANTALLAS/Pantalla_Configurador` | Funciona igual con Baileys: no cambia |
| Vincular por HTTP o por terminal | `POST /vincular` del servicio y `npm run vincular` (`vincular.js`) | Solo Baileys: no cambia |
| `GET /salud` | `servidor.js` | Con `webjs` contestaba `{ok:true}` sin mirar nada; con Baileys da el estado real |
| Link `wa.me` para que el paciente escriba primero | `EnlaceWhatsApp.cs`, perfil y después de un turno | Opción B del consentimiento; no manda nada |
| Consentimiento (registro, perfil, `optIn`) | `ConsentimientoWhatsAppService.cs` y las pantallas | Etapa 4 |

## Lo que existe solo para whatsapp-web.js (se saca en la etapa 1)

- **Servicio:** `whatsapp.js` entero; las dependencias `whatsapp-web.js` y `patch-package` (y el `postinstall`); `patches/` (solo un README histórico);
  la unit `deploy/whatsapp-service.service` (Chromium, `MemoryMax=2G`, `PUPPETEER_CACHE_DIR`, `HOME`, `WHATSAPP_SESSION_PATH`); en `config.js` el selector
  `WHATSAPP_BACKEND` (por defecto `webjs`) y las variables `WHATSAPP_SESSION_PATH`, `WHATSAPP_WEB_VERSION` y `WHATSAPP_DEBUG`; las ramas "webjs" de
  `backend.js`, `logs.js`, `cola.js` y `servidor.js`; unas 45 % del `README.md` (dos backends, paquetes de Chromium, vinculación y diagnóstico de
  webjs, `WHATSAPP_WEB_VERSION`, vulnerabilidades de Puppeteer); y las pruebas que prueban esas ramas.
- **API:** el valor `WhatsAppWebJs` de `Mensajeria:CanalActivo` y el nombre de la clase `CanalWhatsAppWebJs` (es la clase que habla con el servicio,
  sirve igual para Baileys: se renombra), con sus pruebas y comentarios.
- **Docs:** `DEPLOY.md` (la sección de pasar a Baileys y su rollback a webjs), `SERVIDOR.md` (tablas, carpetas y variables), la fila de `CanalActivo` del
  `README.md`, y notas en `PROGRESO_BAILEYS.md` / `PROGRESO_CONSENTIMIENTO_WHATSAPP.md` (históricos).
- **VPS (manual, lo hace Agustín):** los paquetes de Chromium (`libnss3`, `libatk1.0-0t64`, `libatk-bridge2.0-0t64`, `libgtk-3-0t64`, `libgbm1`,
  `libasound2t64`), `/opt/yosoy/whatsapp/cache` (caché de Chromium y de npm, y el `HOME` de la unit vieja), `/opt/yosoy/whatsapp/sesion` (la sesión vieja de
  whatsapp-web.js: **es una credencial**), la unit vieja y las variables viejas de `whatsapp.env`. El detalle va en "Pasos manuales para Agustín".

## Hallazgos de la auditoría (cosas que no estaban en el pedido)

1. **Hay más avisos automáticos por WhatsApp que los recordatorios.** Son cuatro, por evento (S2 a S5). No los toco (el pedido es no borrar sin avisar): se
   listan y se recomienda en la etapa 3. Importa para la etapa 4, que supone que "no hay envíos salientes automáticos": **sí los hay**.
2. **Sacar el canal Chat del recordatorio rompería el reintento del mail** en un caso borde. `RecordatorioDAO.TurnosConfirmadosSinRecordar` cuenta las filas
   resueltas de **cualquier** canal y las compara con la cantidad de canales: con 2 canales en vez de 3 y una fila vieja de "Chat" ya enviada, un mail pendiente
   de reintento dejaría de reintentarse (pasa solo con los turnos cuyo recordatorio estaba a medias al desplegar). Se corrige en la etapa 2 contando solo los
   canales que quedan, con una prueba contra la base.
3. **Hoy `Mensajeria__CanalActivo=WhatsAppWebJs` se acepta a propósito** (para que un servidor que lo tenga siga arrancando). Al sacarlo, **la API no arranca**
   si el `.env` del servidor todavía lo trae (falla con un mensaje que dice qué cambiar). Es el primer paso manual antes de publicar.
4. **El texto de consentimiento `v0-provisorio` promete "un recordatorio antes de la fecha".** Con la etapa 2 deja de ser cierto. No lo edito: una versión
   publicada no se edita, y el texto se tiene que rehacer con el equipo legal según lo que se decida en las etapas 3 y 4.
5. **El mail y la campanita del recordatorio dicen "avisanos respondiendo el mensaje del chat"**, que es el WhatsApp que se saca. Es solo texto: se cambia a
   "avisanos desde la web" sin tocar la lógica de esos recordatorios (se aclara en la etapa 2).
6. **En producción hoy (con `webjs`) los recordatorios salen sin ninguna protección**: a todo paciente con teléfono, sin consentimiento, sin topes y sin
   pausas largas. Al quedar solo Baileys, todo lo que salga pasa por el consentimiento, los topes, las pausas y el "escribiendo...".
7. **El chat simulado usa el canal activo** (E4). Con `Baileys`, un pedido local al simulador hace que el asistente le conteste por WhatsApp real a ese
   teléfono. Está acotado (solo desde la propia máquina; el servicio exige constancia de esa persona) pero conviene saberlo.
8. **Cerrar y reabrir una conversación mandan un texto fijo** aunque la persona nunca haya escrito (S8 y S9): se cubre en la etapa 5.
9. **`CanalWhatsAppCloud`** es un enchufe sin usar para la API oficial de Meta. Se deja (está en la hoja de ruta del `README.md`).

## Etapa 1: sacar whatsapp-web.js

### Qué quedó hecho

**El servicio (`whatsapp-service`)**

- Se borró `whatsapp.js` (el motor viejo, 395 líneas), `patches/` (solo un README histórico), la unit vieja y las dependencias `whatsapp-web.js` y
  `patch-package` (con su `postinstall`). Quedan **cuatro dependencias de producción**: Baileys, `express`, `qrcode` y `qrcode-terminal`. El árbol de npm
  pasó de **429 a 207 paquetes** (los 207 que quedan, idénticos en versión e integridad) y `npm audit --omit=dev` de **13 avisos altos a 0**.
- `config.js`: ya no hay selector de motor. **`WHATSAPP_BACKEND`**: sin la variable, o con `baileys` (como la dejaba la unit propuesta), todo sigue igual; con otro
  valor (`webjs`, `oficial`...) el servicio **no arranca** y dice por qué. **`WHATSAPP_SESSION_PATH`, `WHATSAPP_WEB_VERSION`, `WHATSAPP_DEBUG` y `PUPPETEER_*`** ya no se
  leen; si el entorno todavía las trae, el arranque lo avisa en el log **con los nombres y nunca con los valores**. `API_BASE_URL` ahora se valida siempre (antes solo con
  Baileys): con `http` solo vale contra la propia máquina.
- `backend.js`, `index.js`, `logs.js`: un solo camino. **El log siempre pasa por el filtro de privacidad** (con webjs no pasaba: escribía, por ejemplo, el número propio completo).
- `cola.js`: **`crearColaParaBackend` ya no arma una cola sin protecciones** (antes, un backend sin consentimiento ni topes, como era webjs, recibía una cola "pelada"): sin ellos
  lanza un error. `servidor.js`: se sacaron los "por si el backend no tiene..." que existían para webjs (`/salud` contestando `{ok:true}` sin mirar nada y `/vincular` con 404).
- **Una sola unit**: `deploy/whatsapp-service.service` (era `whatsapp-service-baileys.service`; la de whatsapp-web.js se borró), sin `WHATSAPP_BACKEND`.
- `README.md` reescrito para un solo motor (de 631 a unas 380 líneas): sin Chromium, sin Puppeteer, sin las secciones de diagnóstico de webjs; con la tabla de variables retiradas.

**La API**

- **`Mensajeria__CanalActivo` solo acepta `Simulado` y `Baileys`.** `WhatsAppWebJs` ya no existe: la aplicación **no arranca** con ese valor y el mensaje dice qué poner
  (`Mensajeria__CanalActivo=Baileys`, o `Simulado`). Cualquier otro valor desconocido sigue frenando el arranque, listando los válidos.
- **`CanalWhatsAppWebJs` pasó a llamarse `CanalBaileys`** (clase, archivo, pruebas y comentarios). Es la misma clase que ya hablaba con el servicio por HTTP para los dos motores. El `Nombre` del canal
  sigue siendo "WhatsApp": el historial de conversaciones no se parte.
- Comentarios al día en `MensajeriaOptions`, `WhatsAppAdminService`, `IMessageChannel`, `MensajeriaEndpoints` y `shell.js`.

**Docs**

- `DEPLOY.md`: sección nueva **"WhatsApp en la API: `Mensajeria__CanalActivo` y recordatorios"** (con el comando para mirar el valor del `.env` sin imprimir nada más) y la sección del servicio
  reescrita: **"10. Limpieza de lo viejo (whatsapp-web.js y Chromium)"** (desvincular el dispositivo viejo del teléfono, borrar el código, la sesión y la caché viejos, sacar las variables viejas sin imprimir
  `whatsapp.env`, los paquetes de Chromium con un `--dry-run` primero) y **"11. Rollback"** (al servicio viejo, solo mientras no se haya hecho la limpieza).
- `SERVIDOR.md`: la tabla de servicios, la sección "WhatsApp (whatsapp-service con Baileys)" (hoy en el VPS vs. versión nueva), las carpetas, las variables y los pendientes. `README.md` (raíz): la fila de `CanalActivo`.
- Notas históricas arriba de `PROGRESO_BAILEYS.md` y `PROGRESO_CONSENTIMIENTO_WHATSAPP.md`. `whatsapp-service/.gitignore`: se siguen ignorando las carpetas de sesión viejas (eran una credencial).

### Decisiones y límites

1. **`WHATSAPP_BACKEND` se tolera con `baileys` y se rechaza con cualquier otro valor.** Si se hubiera ignorado por completo, un `whatsapp.env` con `WHATSAPP_BACKEND=webjs` haría arrancar Baileys sin que nadie lo note
   (con un QR para escanear). Rechazarlo obliga a mirar.
2. **`Mensajeria__CanalActivo=WhatsAppWebJs` frena el arranque de la API** en vez de caer a `Simulado`. Es lo que pide el encargo ("quedan Simulado y Baileys") y es lo coherente con cómo ya trata la API un valor mal escrito. El costo: **si el `.env` del
   servidor todavía tiene `WhatsAppWebJs`, la API no arranca al publicar** (se ve enseguida en el `journalctl`, y el rollback de la API está documentado). Caer a `Simulado` habría sido peor: los mensajes quedarían guardados como "Enviado"
   sin salir por ningún lado. Por eso es el **primer paso manual** (abajo).
3. **El servicio viejo del VPS sigue andando con la API nueva** (la interfaz HTTP es la misma): por eso el valor a poner mientras tanto es `Baileys`, y por eso el orden recomendado es: `.env` → API → servicio nuevo.
4. **La caché de npm (`/opt/yosoy/whatsapp/cache`) se conserva solo para instalar**: la unit nueva ya no la tiene en `ReadWritePaths` ni la usa al correr.
5. **No se tocó** `CanalWhatsAppCloud` (el enchufe sin usar para la API oficial de Meta), el consentimiento (etapa 4) ni los textos de los mensajes.
6. **No se corrió nada contra WhatsApp real** ni se abrió ningún socket: las pruebas usan la fachada simulada y la red bloqueada.

### Pruebas de esta etapa

- `npm test` en `whatsapp-service`: **389 pruebas, 383 pasan, 6 saltadas (permisos POSIX), 0 fallas** (eran 388). Se sacaron las que probaban webjs (la cola "de siempre", la interfaz de `whatsapp.js`, el log sin filtro, `/salud` y
  `/vincular` sin soporte) y entraron: el rechazo de `WHATSAPP_BACKEND=webjs`, el aviso de variables retiradas (solo nombres), la cola que no se arma sin protecciones, la unit única sin rastros de Chromium, y una guardia nueva
  (`sin-whatsapp-web-js.test.js`) que vigila que `whatsapp-web.js`, Puppeteer y `patch-package` no vuelvan por un descuido: ni en `package.json`, ni en el lock, ni en el código, ni como archivo.
- `Scripts\correr-tests.ps1` contra `yosoy_test`: **1406 de 1406** (eran 1407: se sacó un caso por cada valor `WhatsAppWebJs` que dejó de ser válido y entraron el rechazo de ese valor y un caso más de valor desconocido).

## Pasos manuales para Agustín

En este orden. Nada de esto lo hice yo: lo que toca producción o el teléfono del número es tuyo.

1. **Antes de publicar la API: mirá el valor de `Mensajeria__CanalActivo` en el servidor.** Con esto se imprime solo esa línea:
   `sudo grep -n '^Mensajeria__CanalActivo=' /opt/yosoy/config/.env`. Si dice **`WhatsAppWebJs`**, cambialo a `Baileys` (anda igual con el servicio viejo) o a `Simulado`; si no, la API **no arranca**.
   Detalle: `docs/DEPLOY.md`, "WhatsApp en la API".
2. **Después, desplegar el servicio nuevo y vincular el número** (`docs/DEPLOY.md`, "Despliegue de whatsapp-service (Baileys)"): instalar al lado, backup, corte, topes iniciales bajos, vincular y probar con tu teléfono.
3. **Cuando lleve unos días estable, limpiar lo viejo** (`DEPLOY.md`, "10. Limpieza de lo viejo"): ya **no hace falta instalar ni mantener** en el VPS
   - **Chromium y sus paquetes** (`libnss3`, `libatk1.0-0t64`, `libatk-bridge2.0-0t64`, `libgtk-3-0t64`, `libgbm1`, `libasound2t64`): opcional, con `apt remove --dry-run` primero (algunas las usan otros programas);
   - **`/opt/yosoy/whatsapp/sesion`**: la sesión vieja de whatsapp-web.js, **una credencial** (desvinculá antes el dispositivo viejo desde el teléfono);
   - **`/opt/yosoy/whatsapp/cache`**: el Chromium de Puppeteer y su perfil (la carpeta se vacía y queda solo para la caché de npm);
   - **`app-anterior-<fecha>`** y el backup de la unit y del `whatsapp.env` viejos, pasados unos días;
   - las **variables viejas** de `whatsapp.env`: `WHATSAPP_BACKEND`, `WHATSAPP_SESSION_PATH`, `WHATSAPP_WEB_VERSION`, `WHATSAPP_DEBUG` y `PUPPETEER_*` (el servicio las nombra en el log al arrancar; solo nombres);
   - cualquier ajuste de AppArmor o de `kernel.apparmor_restrict_unprivileged_userns` que hayas hecho para el sandbox de Chromium (el servicio viejo arrancaba con `--no-sandbox`, así que probablemente ninguno).
4. **En tu PC**: si tenías una carpeta `.wwebjs_auth` o `.wwebjs_cache` de pruebas viejas dentro de `whatsapp-service/`, borrala (era la sesión de una cuenta). No encontré ninguna en este árbol.

## Qué falta / pendiente de decisión

(Se completa etapa por etapa.)
