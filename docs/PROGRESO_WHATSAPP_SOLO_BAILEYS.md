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
- **Última actualización:** 2026-10-18, tras la etapa 5 (anti-bloqueo): las cinco etapas están hechas. Lo que falta es tuyo: ver "Pasos manuales para Agustín" y "Qué falta".

## Estado de las etapas

| Etapa | Qué | Estado | Commit |
|---|---|---|---|
| — | Auditoría de todos los flujos de WhatsApp y este archivo | **hecho** | `86be1ac` |
| 1 | Sacar whatsapp-web.js (servicio, API, docs, VPS) | **hecho** | `0ae4dfd` |
| 2 | Eliminar los recordatorios por WhatsApp, con tests | **hecho** | `341807f` |
| 3 | Revisar el resto de los flujos salientes (solo se lista y se recomienda) | **hecho** | `e25061f` |
| 4 | Consentimiento de WhatsApp: opciones y recomendación (sin tocar migraciones ni tablas) | **hecho** | `e9f0962` |
| 5 | Anti-bloqueo de lo que quede | **hecho** | (este commit) |

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
| S1 | **Recordatorio de turno 24 h** | `RecordatorioBackgroundService.EjecutarUnaVez` → `NotificadorTurnos.EnviarRecordatorio24h` → `AvisarPaciente` (canales `CanalesRecordatorio` = Chat, Email, App) → `EnviarChat` | **El reloj**: un ciclo cada `Mensajeria:IntervaloChequeoMinutos` (5), a todo turno Confirmado que cae en las próximas `RecordatorioHorasAntes` (24) horas. También a mano: `POST /mensajeria/recordatorios/ejecutar` | **Automático, programado y masivo** (uno por paciente con turno, sin que nadie escriba). Activo con canal real y paciente con teléfono. Trae "Respondé 1 o 2" y deja la conversación esperando esa respuesta | **Eliminar (etapa 2)** → **hecho**, ver "Etapa 2" |
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
   canales que quedan, con una prueba contra la base. **Resuelto en la etapa 2.**
3. **Hoy `Mensajeria__CanalActivo=WhatsAppWebJs` se acepta a propósito** (para que un servidor que lo tenga siga arrancando). Al sacarlo, **la API no arranca**
   si el `.env` del servidor todavía lo trae (falla con un mensaje que dice qué cambiar). Es el primer paso manual antes de publicar.
4. **El texto de consentimiento `v0-provisorio` promete "un recordatorio antes de la fecha".** Con la etapa 2 deja de ser cierto. No lo edito: una versión
   publicada no se edita, y el texto se tiene que rehacer con el equipo legal según lo que se decida en las etapas 3 y 4.
5. **El mail y la campanita del recordatorio dicen "avisanos respondiendo el mensaje del chat"**, que es el WhatsApp que se saca. Es solo texto: se cambia a
   "avisanos desde la web" sin tocar la lógica de esos recordatorios. **Hecho en la etapa 2.**
6. **En producción hoy (con `webjs`) los recordatorios salen sin ninguna protección**: a todo paciente con teléfono, sin consentimiento, sin topes y sin
   pausas largas. Al quedar solo Baileys, todo lo que salga pasa por el consentimiento, los topes, las pausas y el "escribiendo...".
7. **El chat simulado usa el canal activo** (E4). Con `Baileys`, un pedido local al simulador hace que el asistente le conteste por WhatsApp real a ese
   teléfono. Está acotado (solo desde la propia máquina; el servicio exige constancia de esa persona) pero conviene saberlo.
8. **Cerrar y reabrir una conversación mandan un texto fijo** aunque la persona nunca haya escrito (S8 y S9): se cubre en la etapa 5.
9. **`CanalWhatsAppCloud`** es un enchufe sin usar para la API oficial de Meta. Se deja (está en la hoja de ruta del `README.md`).
10. **El tope de 3 mensajes por persona y por día cortaba al asistente a la mitad** (lo vi en la etapa 5, al revisar los topes contra lo que queda). Contaba también las respuestas del asistente, y pedir un turno lleva unos cinco mensajes. Se corrigió en la etapa 5 con un tope más alto cuando la persona está conversando; los avisos sueltos siguen con el de 3.

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

## Etapa 2: sin recordatorios por WhatsApp

### Qué quedó hecho

- **El recordatorio de 24 h sale solo por mail y campanita.** `NotificadorTurnos.CanalesRecordatorio` pasó de `Chat, Email, App` a **`Email, App`**, y es una lista de solo lectura
  (`IReadOnlyList<string>`): no hay ninguna clave de configuración que la cambie ni un valor por defecto que la reactive. `Mensajeria:RecordatorioHorasAntes` e
  `IntervaloChequeoMinutos` siguen siendo solo el "cuándo", nunca el "por dónde". Lo mismo vale para `POST /mensajeria/recordatorios/ejecutar` (la pasada a mano).
- **Una segunda traba, independiente de la primera.** `AvisarPaciente` recibe ahora el texto de WhatsApp como opcional y el recordatorio no tiene ninguno
  (`textoChat: null`): aunque alguien volviera a poner `"Chat"` en la lista, ese canal queda `Omitido` ("Este aviso no sale por WhatsApp.") **sin** consultar el consentimiento,
  sin armar la conversación y sin llamar al servicio. Para que el recordatorio volviera a salir por WhatsApp habría que cambiar las dos cosas a la vez (y las pruebas fallan).
- **Se borró lo que era solo del recordatorio por WhatsApp:** la plantilla `PlantillasWhatsApp.Recordatorio` (con su "Respondé 1 o 2" y la línea de BAJA) y el bloque de
  `EnviarChat` que dejaba la conversación esperando esa respuesta (`PasoBot.RespuestaRecordatorio`). Quedan cuatro plantillas: solicitud recibida, turno propuesto, confirmado y cancelado.
- **`RecordatorioDAO.TurnosConfirmadosSinRecordar` cuenta solo los canales que se piden** (hallazgo 2). Antes contaba las filas resueltas de cualquier canal y las comparaba con la
  cantidad de canales: sin "Chat" y con una fila vieja de "Chat" ya enviada, un mail que había fallado dejaba de reintentarse. Ahora la consulta filtra por `Canal IN (...)`.
- **Mail y campanita: la misma lógica, un texto distinto.** Cambió una frase, "avisanos respondiendo el mensaje del chat o desde la web" → "avisanos desde la web" (hallazgo 5): ya no hay
  un mensaje de chat al que responder. El texto del mail y el de la campanita siguen siendo el mismo (con quién, cuándo y el link para ingresar).
- Docs: `README.md` (raíz, sección de mensajería: el recordatorio ya no avisa "por chat" ni acepta `1`/`2`) y el comentario del enchufe `CanalWhatsAppCloud`. `DEPLOY.md` y `SERVIDOR.md` ya lo decían desde la etapa 1.

### Decisiones y límites

1. **El historial no se toca.** Las filas viejas de `Recordatorio` con `Canal = 'Chat'` quedan tal cual, y el `CHECK` de `Recordatorio.Canal` sigue admitiendo `Chat` porque los avisos por evento (S2 a S5) lo usan. Sin migración, sin tablas nuevas.
2. **Se conserva `BotReservasService.ResponderRecordatorio`** (el "1 = asisto, 2 = no puedo"), marcado en el código como heredado. Nada deja ya una conversación en ese paso, pero las que quedaron esperando una respuesta con la API anterior
   (la que está hoy en el VPS) conservan su comportamiento si la persona contesta después de actualizar. Se puede borrar más adelante, cuando esas conversaciones ya hayan vencido: queda en "Qué falta".
3. **Un recordatorio que había quedado a medias al actualizar** (por ejemplo: el WhatsApp ya había salido, la campanita también y el mail falló) **sigue reintentando el mail** y no vuelve a mandar nada por WhatsApp. Está probado con la base real.
4. **Consecuencia de producto a tener presente:** un paciente que **no tiene mail** y no entra a la app ya no se entera del recordatorio (antes le llegaba por WhatsApp). El registro público (`POST /pacientes`) exige el mail, pero la columna admite
   `NULL` y puede haber pacientes cargados antes de esa regla o por otra vía: a esos solo les queda la campanita (el mail queda `Omitido`: "El paciente no tiene mail cargado.").
5. **No toqué el texto del consentimiento `v0-provisorio`**, que todavía promete "un recordatorio antes de la fecha" (hallazgo 4): una versión publicada no se edita. Va a la etapa 4.
6. `POST /mensajeria/recordatorios/ejecutar` sigue existiendo (pruebas y demo): corre la misma pasada, ahora solo por mail y campanita.

### Pruebas de esta etapa

- **Archivo nuevo `YoSoy.Tests/RecordatoriosSinWhatsAppTests.cs`: 27 casos.** Arman el `NotificadorTurnos` y el `RecordatorioBackgroundService` **reales**, con el canal `CanalBaileys` **de verdad** (el que se registra con `Mensajeria__CanalActivo=Baileys`) y un manejador HTTP que
  anota cada pedido que haría a `whatsapp-service` (no hay red: nada sale de la máquina). Comprueban, en los tres estados del consentimiento del paciente (sin decisión, aceptó, revocó):
  - que durante un recordatorio **no llega ni un pedido** al servicio, no se guarda ningún mensaje, no se arma ninguna conversación y ni siquiera se consulta el consentimiento;
  - que se reservan solo las filas `Email` y `App` (nunca `Chat`), que la campanita sale con el texto completo y el mail se intenta como siempre (queda `Omitido` si no está configurado o el paciente no tiene mail);
  - que el servicio de recordatorios **pide solo `Email` y `App`**, con uno y con varios turnos;
  - **el control:** un aviso por evento (`TurnoConfirmado`) con el mismo armado **sí** manda un `POST /enviar`, así se sabe que el espía vería un pedido si el recordatorio lo hiciera;
  - que ninguna opción de `MensajeriaOptions` ni clave de `appsettings.json` mezcla "recordatorio" con "WhatsApp / chat / canal / Baileys", que `CanalesRecordatorio` es de solo lectura y no tiene `Chat`, y que no existe una plantilla de WhatsApp con nombre de recordatorio.
  - **Contra la base real (`yosoy_test`, categoría `Integracion`):** los turnos que faltan recordar con cada combinación de filas (incluida la fila vieja de `Chat` que antes lo rompía); y el recorrido completo con los DAOs reales: un recordatorio deja solo las filas `Email` (`Omitido`) y `App` (`Enviado`), una notificación, ninguna conversación, ningún pedido al servicio, y una segunda pasada no repite nada.
- **Verificadas con mutaciones** (rompo el código a propósito y compruebo que las pruebas fallan; todo se restauró byte a byte). Casos que fallan con cada una: poner `"Chat"` de nuevo en la lista (6), contar las filas de cualquier canal (2),
  que el servicio pida también `Chat` (2), ignorar los intentos agotados (1), volver al texto "respondé el mensaje" (1), darle de nuevo un texto de chat al recordatorio (1) y **la regresión completa** —`"Chat"` en la lista **y** un texto de chat— (12 casos de
  9 pruebas distintas, entre ellas las que miran que no salga ningún pedido al servicio).
- **Adaptadas:** `PlantillasWhatsAppTests` (cuatro plantillas; ya no hay una de recordatorio), `ConsentimientoWhatsAppEnviosTests`, `CorreoNoConfiguradoTests` (espera `Email` y `App`) y los cuatro dobles de `IRecordatorioDAO`.
- `Scripts\correr-tests.ps1` contra `yosoy_test`: **1430 de 1430** (eran 1406: entran los 27 casos del archivo nuevo y salen 3 netos de los que probaban el recordatorio por WhatsApp en las pruebas adaptadas). `npm test` en `whatsapp-service`: sin cambios (389: 383 pasan, 6 saltadas, 0 fallas).

## Etapa 3: el resto de los flujos salientes (solo se listan y se recomienda)

Esta etapa **no cambia código**: clasifica lo que sigue saliendo por WhatsApp después de la etapa 2, dice qué riesgo tiene cada cosa y qué conviene. **No borré ni pausé nada de esto sin avisarte.**

### Qué sigue saliendo por WhatsApp hoy

| # | Flujo | ¿Responde a un mensaje de la persona? | ¿Automático? | ¿Masivo? | Qué hay puesto | Veredicto |
|---|---|---|---|---|---|---|
| S6 | Respuestas del asistente | **Sí**: solo se arma como respuesta a un mensaje entrante (webhook o chat simulado) | Sí, pero dentro de una conversación que la persona empezó | No: uno o pocos mensajes por cada mensaje que ella mandó | Servicio: solo a quien escribió primero, BAJA gana, topes, pausas, "escribiendo..." | **Se deja.** Etapa 5: se agrega una traba más en la API |
| S7 | Respuesta manual del equipo (bandeja) | Sí, normalmente | No: la escribe una persona | No | Las mismas protecciones del servicio | **Se deja** (decide una persona) |
| S8 | Aviso "el equipo cerró esta conversación" | Debería serlo, pero **no lo garantiza hoy**: sale aunque la persona nunca haya escrito (la conversación pudo crearla un aviso de turno) | Sí: texto fijo al apretar "Cerrar" | No (uno por clic) | Solo las del servicio | **Etapa 5:** solo si la persona escribió en esa conversación |
| S9 | Aviso "el equipo reabrió esta conversación" | ídem | ídem | No | ídem | **Etapa 5:** ídem |
| S2 | "Recibimos tu solicitud de turno" | **No** | **Sí, por evento** (`POST /turnos/solicitar`, `POST /public/profesionales/{slug}/reservar`) | No es un envío programado ni un lote: sale de a uno, cuando alguien pide un turno | Consentimiento (ver abajo), topes, pausas, "escribiendo...", textos mínimos (día y hora) | **Decisión tuya** (abajo) |
| S3 | "Te propusieron un turno" (con el link) | **No** | **Sí, por evento** (`POST /turnos/profesional-solicitar`) | No | ídem | **Decisión tuya** |
| S4 | "Tu turno quedó confirmado" | **No** | **Sí, por evento** (`PUT /turnos/{id}`, `PUT /turnos/{id}/confirmar-paciente`) | No, pero **puede salir en ráfaga** (un profesional que confirma una agenda entera) | ídem | **Decisión tuya** |
| S5 | "Tu turno fue cancelado" | **No** | **Sí, por evento** (`PUT /turnos/{id}/cancelar`, `PUT /turnos/{id}`) | No | ídem | **Decisión tuya** |

Ya no queda **nada programado**: sin el recordatorio no hay un reloj que mande mensajes. Lo que queda automático sale siempre porque alguien hizo algo (pidió, confirmó, canceló o escribió).

### Los cuatro avisos de turno (S2 a S5): por qué los marco

Son la única salida que **empieza una conversación** (no responde a nada) y por eso la única que queda fuera de la regla "una respuesta automática solo dentro de una conversación que empezó la persona" de la etapa 5.
Lo que los acota hoy:

- **Quién los recibe:** `IPoliticaEnvioWhatsApp` decide por paciente. *Revocó* → no se manda nada (ni se arma la conversación). *Aceptó* (consentimiento vigente, de este número y de este texto) → el servicio recibe `optIn: true` y **puede escribirle sin que ella haya escrito antes**. *Sin decisión* → no hay `optIn`: el servicio solo le escribe si ella le escribió primero al número (en los últimos 365 días) y si no, lo rechaza (`sin_consentimiento`, el aviso queda `Omitido`).
- **Qué dicen:** solo el día y la hora (nada del profesional, la especialidad ni el motivo): `PlantillasWhatsApp`, vigilado por pruebas.
- **Cuánto:** los topes del servicio (100 por día, 40 por hora, 3 por persona), pausas al azar de 4 a 9 s, una pausa larga cada 8 a 14 mensajes, "escribiendo..." y una cola de 50 que descarta lo que lleva más de 3 horas esperando. Una ráfaga se **espacia o se descarta**; no sale de golpe.
- Si el canal falla (o la persona pidió la baja), el aviso queda `Omitido` o `Error` en la tabla `Recordatorio`; **no hay un reloj que lo reintente** (el único que reintentaba era el del recordatorio).

El riesgo que queda es el de siempre con una API no oficial: **mensajes que abre el negocio a personas que no le escribieron** (aunque hayan aceptado en un formulario). Es poco volumen, pero es justo lo que el bloqueo mira, y vos dijiste que lo saliente se pausa.

### Recomendación

1. **Dejar el código y el consentimiento como están, pero poner un interruptor:** `Mensajeria:AvisosPorWhatsApp` (por defecto **`false`**, variable `Mensajeria__AvisosPorWhatsApp`). Con `false`, `NotificadorTurnos.EnviarChat` devuelve `Omitido` ("Los avisos por WhatsApp están pausados.") **antes** de consultar el consentimiento o armar la conversación,
   igual que la segunda traba del recordatorio (etapa 2). Es una pausa **reversible con una variable y un reinicio**, no se borra nada y las pruebas existentes siguen valiendo (con el interruptor en `true` hacen lo mismo que hoy). Son unas 30 líneas con sus pruebas.
2. Si pausás los avisos, **la campanita queda como único aviso de turno fuera del recordatorio** (los avisos por evento no tienen canal de mail). Si querés que el paciente se entere sin entrar a la app, la solución barata es **agregar `Email` a `CanalesEvento`**: `EnviarMail` ya existe y es el mismo que usa el recordatorio. Es decisión de producto, no la tomé.
3. **Mientras no haya interruptor, nada cambia:** los cuatro avisos siguen saliendo solo a quien aceptó o ya escribió. Si preferís **sacarlos del todo** (en vez de pausar), también es fácil, pero lo dejo para cuando me lo pidas: borrarlos deja sin uso a medio consentimiento (etapa 4).

**No hice nada de esto** (ni el interruptor ni el mail): el encargo era listar y recomendar. Decime y lo agrego en una etapa aparte.

### Lo que no cambia

- Las respuestas del asistente y las de una persona del equipo (S6 y S7) se dejan: responden dentro de una conversación y las acota el servicio.
- El chat simulado (E4) sigue usando el canal activo (H7): está acotado a la propia máquina y el servicio exige constancia de esa persona.
- `CanalWhatsAppCloud` (la API oficial de Meta, sin usar) se deja: es la salida natural si algún día se quiere volver a mandar avisos de forma legítima.

## Etapa 4: ¿tiene sentido seguir con el consentimiento de WhatsApp?

Esta etapa **no cambia nada**: ni código, ni textos, ni migraciones, ni tablas (`ConsentimientoWhatsApp`, `migrations/add_consentimiento_whatsapp.sql` y `docs/produccion/01-produccion.sql` quedan como están).
El consentimiento (PR #43) está en `main` y **no está desplegado**: no hay ninguna aceptación guardada en producción que se pueda perder.

### Qué hay y quién lo usa hoy

| Pieza | Para qué sirve | ¿La usa algo hoy? |
|---|---|---|
| Tabla `ConsentimientoWhatsApp` + migración | Constancia de cada aceptación y revocación: texto exacto (versión + SHA-256), número y quién | Sí: la leen los avisos de turno y la pantalla |
| `ConsentimientoWhatsAppService`, DAO y endpoints (`/consentimiento-whatsapp/texto`, `/`, `/aceptar`, `/revocar`) | Aceptar, revocar, consultar | Sí: registro y perfil |
| Texto `v0-provisorio.txt` (hash trabado por una prueba) | Lo que la persona lee antes de aceptar | Sí, pero **promete "un recordatorio antes de la fecha"** (hallazgo 4) |
| Casilla del registro y tarjeta "Avisos por WhatsApp" del perfil | Pedir la aceptación (nunca tildada de antemano) | Sí |
| `optIn: true` en `POST /enviar` | Deja que el servicio le escriba a quien aceptó **aunque no haya escrito primero** | Solo los cuatro avisos de turno (S2 a S5) |
| BAJA / ALTA (servicio + `ComandoWhatsApp` en la API) | El servicio deja de escribirle; la API revoca la aceptación | Sí: también protege las respuestas del asistente |
| Link `wa.me` (opción B) y `Mensajeria__WhatsAppNumeroClinica` | Que la persona **escriba primero**; no manda nada | Sí, y no depende de ningún aviso |
| `TelefonoArgentino` | Validar el celular al aceptar y normalizar el número del aviso | Sí |

### Qué cambió con las etapas anteriores

1. **El recordatorio ya no sale por WhatsApp**, así que el texto que ve la persona ya no es del todo cierto (promete un recordatorio que no va a llegar). Corregirlo exige una **versión nueva** del texto y revisión del equipo legal (una versión publicada no se edita); por eso no lo toqué.
2. **Pero no es cierto que ya no haya envíos salientes automáticos:** quedan los **cuatro avisos de turno por evento** (etapa 3). El consentimiento hoy es **lo único que les permite abrir una conversación** con alguien que no escribió primero. Mientras esos avisos existan, el consentimiento sigue teniendo un consumidor.
3. **Si esos avisos se pausan** (la recomendación de la etapa 3), el consentimiento queda sin quien lo use y la casilla del registro **prometería avisos que no llegan**. Eso sí hay que resolverlo.
4. Hay cosas que **no dependen de que haya avisos**: la BAJA/ALTA (el servicio la necesita para proteger las respuestas) y el link `wa.me` (la persona escribe primero).

### Opciones

**A. Dejarlo dormido** (recomendada)
Todo el código, la tabla y la migración quedan. Con la llave `Mensajeria:AvisosPorWhatsApp` de la etapa 3 **apagada**: (1) los cuatro avisos quedan `Omitido`; (2) la oferta se esconde: el texto y el estado salen con `disponible: false`, que es lo que la interfaz ya sabe mostrar
(el registro no dibuja el bloque y el perfil dice que los avisos no están disponibles por ahora); (3) la BAJA/ALTA y el link `wa.me` siguen andando.
- **A favor:** no se borra nada (las 386 pruebas que trajo el PR #43, la constancia de auditoría y el trabajo con legal); se prende de nuevo con una variable; sin migraciones ni tablas; se va la promesa vacía; queda listo por si algún día se vuelve a mandar (por ejemplo con la API oficial de Meta).
- **En contra:** código que no corre mientras la llave esté apagada (mantenimiento y pruebas); y **antes de volver a prender hay que rehacer el texto** con legal.
- **Esfuerzo:** chico (la llave de la etapa 3 más que `Texto()` y `Obtener()` la respeten, con pruebas).

**B. Simplificar**
Quedarse solo con lo que sirve sin avisos: la BAJA/ALTA (con su registro en la base), el link `wa.me` y la validación del teléfono. Sacar la casilla del registro, la tarjeta del perfil, el `optIn`, el texto versionado y los endpoints de aceptar/revocar.
- **A favor:** mucha menos superficie; sin avisos no hace falta pedir permiso, así que **no hay un texto legal que mantener**; no queda ninguna casilla que mienta.
- **En contra:** se tira un trabajo recién hecho y probado; si los avisos vuelven hay que reconstruirlo (y es lo más delicado); toca la API, las dos pantallas y buena parte de las pruebas que trajo el PR #43; **la tabla queda sin uso** y no la puedo borrar (sin migraciones).
- **Esfuerzo:** medio a grande.

**C. Sacarlo todo**
Borrar el módulo entero: reglas, DAO, endpoints, pantallas, texto, pruebas y docs, también la BAJA en la base.
- **A favor:** el código más chico.
- **En contra:** lo de B y además que la API ya no se enteraría de una BAJA (el servicio la respeta, pero el perfil, si algún día vuelve, mentiría); la tabla y la migración quedan sin dueño; rehacerlo después es lo más caro.
- **Esfuerzo:** grande. No la recomiendo.

### Recomendación

**A**, y decidiendo primero qué hacer con los cuatro avisos (etapa 3):

- Si los avisos **se pausan** → A: la oferta se esconde con la misma llave y no hay nada que decidir con legal por ahora.
- Si los avisos **se mantienen** → el consentimiento **tiene que quedar prendido** (es lo que los legitima), pero **antes de usarlo con pacientes reales hay que publicar una versión nueva del texto** sin la promesa del recordatorio (y con lo que legal ya tenía pendiente: ver `PROGRESO_CONSENTIMIENTO_WHATSAPP.md`, "Pasos manuales", punto 2).
- Si algún día se decide **no volver a mandar nada saliente por WhatsApp** → B, que ahí sí vale el esfuerzo.

**Nada de esto lo implementé:** el encargo de esta etapa es evaluar y dejar la recomendación.

### Detalles a tener en cuenta

- **No toqué el texto `v0-provisorio.txt`** (ni su prueba de hash). Al publicar un texto nuevo, **los avisos se detienen hasta que cada paciente vuelva a aceptar** (es a propósito; mejor hacerlo antes de que haya aceptaciones).
- `WHATSAPP_CONSENTIMIENTO_DIAS` (por defecto 365) es cuánto vale en el servicio "me escribió primero". Es la perilla para acortar la ventana de las respuestas tardías si se quiere más cuidado (etapa 5).
- Lo que se decidió en el consentimiento sobre el **teléfono sin verificar** (cualquiera puede tildar la casilla con el celular de otro) sigue pendiente (`PROGRESO_CONSENTIMIENTO_WHATSAPP.md`, "Qué falta", punto 1): con los avisos pausados, deja de ser un riesgo.

## Etapa 5: anti-bloqueo de lo que queda

Dos cosas: comprobar que las protecciones del servicio siguen puestas (y probadas) ahora que Baileys es el único motor, y asegurar que **una respuesta automática solo sale dentro de una conversación que empezó la persona**. En el camino apareció un problema real entre los topes y el asistente (hallazgo 10).

### Las protecciones que siguen puestas

Todas viven en `whatsapp-service` y vienen **prendidas por defecto**; la cola no se arma sin ellas (`crearColaParaBackend` lanza un error, etapa 1). Sin cambios de valores, salvo el tope por persona (abajo).

| Protección | Por defecto | Dónde | Quién la traba |
|---|---|---|---|
| Solo a quien escribió primero (365 días) o aceptó (`optIn`); la BAJA gana sobre todo | `WHATSAPP_CONSENTIMIENTO_DIAS=365` | `consentimiento.js` | `consentimiento.test.js` y la prueba de punta a punta de `backend.test.js` |
| Tope por día / por hora | 100 / 40 (lo que pasa la hora espera en la cola) | `limites.js` | `cola.test.js`, `consentimiento.test.js` |
| Tope por persona y por día | 3 (aviso suelto) · **30 si está conversando** | `limites.js` | pruebas nuevas, abajo |
| Pausa entre mensajes | 4 a 9 s al azar | `cola.js` | `backend.test.js` (los valores que arma el backend), `cola.test.js` |
| Pausa larga | 30 a 75 s cada 8 a 14 mensajes | `cola.js` | `backend.test.js`, `cola.test.js` |
| "escribiendo..." antes de cada mensaje | prendido (hasta 4 s; `WHATSAPP_SIMULAR_ESCRITURA=0` lo apaga) | `baileys/servicio.js` | `config.test.js` (por defecto), `backend.test.js`, `baileys-servicio.test.js` |
| Cola | 50 pendientes; lo que lleva más de 3 horas esperando se descarta | `cola.js` | `cola.test.js` |

### Hallazgo 10: el tope por persona (3 por día) cortaba al asistente a la mitad

Con Baileys como único motor, **el tope de 3 mensajes por persona y por día contaba también las respuestas del asistente**. Pedir un turno lleva unos cinco mensajes (menú, profesional, horarios, confirmación...), así que la cuarta respuesta del día recibía un `429 limite_destinatario` y la charla quedaba muerta:
la persona no recibía nunca la confirmación. Hasta ahora no se había visto porque en producción corre el servicio viejo (sin topes) y Baileys no está desplegado. Es exactamente lo que **dejaría de andar el día del cambio**.

**Qué hice (el cambio más chico que lo resuelve):** el servicio ahora distingue si la persona **está conversando** —escribió hace menos de 24 horas, algo que ya sabe por su registro de "escribió primero"— y en ese caso rige un tope más alto (**30 por día**, `WHATSAPP_TOPE_EN_CONVERSACION`, de 1 a 200). Para un **aviso suelto** (la clínica le manda algo a quien no escribió hoy) **sigue el tope de 3**.
Los dos topes cuentan los mismos mensajes del día (no se suman) y los topes por día y por hora siguen valiendo para todo. La API no tiene que avisar nada. Con `WHATSAPP_TOPE_EN_CONVERSACION=3` se vuelve a lo de antes.
Alternativa que **no** tomé: subir `WHATSAPP_TOPE_POR_PERSONA` a 20, que afloja también el tope de los avisos sueltos.

**Ojo con los topes generales:** las respuestas del asistente gastan los mismos topes de día y de hora. Con los iniciales recomendados (30 por día, 10 por hora) alcanza para unas seis charlas de pedir un turno por día. `DEPLOY.md` (paso 5) ya lo avisa: subilos de a poco según lo que reciba el consultorio.

### La regla de la API: una respuesta automática solo dentro de una conversación que empezó la persona

- **Dónde:** en `ServicioMensajes`, el único punto por el que sale todo. Un mensaje de origen **`Bot`** o **`Sistema`** se manda solo si la persona **ya escribió en esa conversación** (hay al menos un mensaje entrante: `IMensajeChatDAO.TieneMensajeEntrante`). Si no, **no se guarda ni se manda**: la llamada devuelve un error con el código `sin_conversacion_iniciada` y queda una línea en el log (solo el número de conversación y el origen; sin teléfono ni texto).
- **A quién alcanza hoy:** el aviso de **"el equipo cerró esta conversación"** y el de **"reabrió"** (S8 y S9), que antes salían aunque la persona nunca hubiera escrito (conversaciones que armó un aviso de turno). Cerrar y reabrir funcionan igual para el equipo (cambia el estado y se audita); solo que a la persona no le llega nada. Las **respuestas del asistente** (S6) siempre cumplen la regla, porque el mensaje de la persona se guarda antes de contestar: la regla es una red de seguridad por si algún camino nuevo se olvidara.
- **Las excepciones, por nombre y una por una:** (1) **el equipo** (`Humano`): lo escribe una persona, que decide, y el servicio de WhatsApp igual exige consentimiento; (2) **los cuatro avisos de turno por evento**, que declaran `OpcionesEnvio.IniciaElNegocio` (S2 a S5, etapa 3). Por defecto `IniciaElNegocio` es `false`: **todo mensaje automático nuevo queda sujeto a la regla** si no se declara. Una prueba falla si otro archivo que no sea `NotificadorTurnos` usa la excepción.
- **`optIn` no es una excepción:** aceptar los avisos no es escribir. Se prueba aparte.
- **Vale igual para el canal simulado** (lo que se prueba es lo que corre).

### Decisiones y límites

1. **"Escribió alguna vez" y no "escribió hace poco"** en la API. La ventana de tiempo ya la pone el servicio (`WHATSAPP_CONSENTIMIENTO_DIAS`, 365 por defecto): si querés que **solo se le pueda contestar dentro de las 24 horas** de su último mensaje, ponelo en `1`. No lo hice por defecto porque una respuesta humana de la secretaría al día siguiente es normal en un consultorio y se rechazaría (`403 sin_consentimiento`).
2. **El mensaje bloqueado no se guarda**: la tabla `MensajeChat` solo admite `Pendiente`, `Enviado` y `Error` (sin migraciones no puedo agregar un estado), y guardarlo como `Error` mostraría en la bandeja un aviso del sistema que la persona nunca iba a recibir.
3. **No toqué** los textos, el consentimiento, los avisos de turno (S2 a S5) ni las tablas.
4. **Los valores por defecto siguen siendo una opinión, no un dato de WhatsApp** (WhatsApp no publica sus umbrales) y **no se probaron contra WhatsApp real**: las pruebas usan reloj falso y socket simulado.

### Pruebas de esta etapa

- **Servicio** (`npm test`): **398 pruebas, 392 pasan, 6 saltadas (permisos POSIX), 0 fallas** (eran 389). Nuevas: el registro dice cuándo una persona está conversando (con y sin `optIn`, el límite exacto de las 24 horas, y que nunca supere la vigencia ni se informe si pidió la baja), los topes distinguen conversación de aviso suelto (se cuentan juntos, lo que espera en la cola cuenta, un envío que falla devuelve el lugar), la cola le pasa a los topes lo que dice el consentimiento, la configuración (valor por defecto y límites de `WHATSAPP_TOPE_EN_CONVERSACION`) y **de punta a punta con los valores por defecto**: una charla de ocho respuestas pasa, y un aviso con `optIn` a otra persona se corta en el cuarto.
  **8 mutaciones**, todas detectadas (la cola que no pasa el dato, la ventana igual a la vigencia, siempre el tope grande o siempre el chico, conversar con un tope menor que un aviso, el backend sin pasar el tope, `optIn` que siempre "conversa", el valor por defecto en 3).
- **API** (`Scripts\correr-tests.ps1`): **1448 de 1448** (eran 1430: entran los 18 casos de `RespuestasAutomaticasTests.cs`). Archivo nuevo `RespuestasAutomaticasTests.cs`: en memoria (con y sin mensaje entrante, para cada origen y cada opción; que lo que mandó antes la clínica no cuente; que cuente solo esa conversación; el log sin teléfono ni texto; que solo `NotificadorTurnos` use la excepción) y **la aplicación entera contra la base de pruebas**, con el canal de WhatsApp reemplazado por uno que anota cada envío:
  el asistente contesta, cerrar y reabrir mandan su aviso si la persona escribió y **no mandan nada si la conversación la armó un aviso** (con otra conversación que sí tiene mensajes de la persona, para que no se confundan), y el equipo puede responder.
  **10 mutaciones**, todas detectadas, incluidas las del acceso a datos (que cuente cualquier dirección o cualquier conversación): sin la regla, la regla también para el equipo, sin la excepción de los avisos, el notificador sin declararla, `Sistema` como no automático, `optIn` como excepción, guardar igual el mensaje bloqueado y perder el código del error.
- **Adaptadas:** `ServicioMensajesOpcionesTests` (modelan opciones y el código de rechazo, no el origen), los dobles de `IMensajeChatDAO` y las pruebas del notificador (ahora también comprueban `IniciaElNegocio`).

## Pasos manuales para Agustín

En este orden. Nada de esto lo hice yo: lo que toca producción o el teléfono del número es tuyo.

1. **Abrir y revisar el PR** (`refactor/whatsapp-solo-baileys` contra `main`; el link de compare, con título y descripción, está en el mensaje final). Son la auditoría y cinco etapas, un commit cada una. No hay migraciones.
2. **Antes de publicar la API: mirá el valor de `Mensajeria__CanalActivo` en el servidor.** Con esto se imprime solo esa línea:
   `sudo grep -n '^Mensajeria__CanalActivo=' /opt/yosoy/config/.env`. Si dice **`WhatsAppWebJs`**, cambialo a `Baileys` (anda igual con el servicio viejo) o a `Simulado`; si no, la API **no arranca**.
   Detalle: `docs/DEPLOY.md`, "WhatsApp en la API".
3. **Publicar la API** con el flujo de siempre. Desde ese momento **el recordatorio de 24 h sale solo por mail y campanita** (nada por WhatsApp), y cerrar o reabrir una conversación le manda el aviso a la persona solo si ella escribió.
4. **Desplegar el servicio nuevo y vincular el número** (`docs/DEPLOY.md`, "Despliegue de whatsapp-service (Baileys)"): instalar al lado, backup, corte, topes iniciales bajos, vincular y probar con tu teléfono.
   **Dos cosas para tener presentes:** (a) **el asistente gasta los mismos topes** de día y de hora (con 30 por día, unas seis charlas de pedir turno): subilos de a poco según lo que reciba el consultorio; (b) hay un tope por persona nuevo, `WHATSAPP_TOPE_EN_CONVERSACION` (30 por defecto; no hace falta tocarlo).
5. **Decidir** qué hacer con los cuatro avisos de turno por WhatsApp (etapa 3: la recomendación es una llave `Mensajeria:AvisosPorWhatsApp` apagada por defecto, y decidir si también salen por mail) y, según eso, con el consentimiento de WhatsApp (etapa 4: dejarlo dormido, simplificarlo o sacarlo). **No hace falta decidir para publicar:** mientras tanto los avisos siguen saliendo solo a quien aceptó o escribió primero.
6. **Cuando lleve unos días estable, limpiar lo viejo** (`DEPLOY.md`, "10. Limpieza de lo viejo"): ya **no hace falta instalar ni mantener** en el VPS
   - **Chromium y sus paquetes** (`libnss3`, `libatk1.0-0t64`, `libatk-bridge2.0-0t64`, `libgtk-3-0t64`, `libgbm1`, `libasound2t64`): opcional, con `apt remove --dry-run` primero (algunas las usan otros programas);
   - **`/opt/yosoy/whatsapp/sesion`**: la sesión vieja de whatsapp-web.js, **una credencial** (desvinculá antes el dispositivo viejo desde el teléfono);
   - **`/opt/yosoy/whatsapp/cache`**: el Chromium de Puppeteer y su perfil (la carpeta se vacía y queda solo para la caché de npm);
   - **`app-anterior-<fecha>`** y el backup de la unit y del `whatsapp.env` viejos, pasados unos días;
   - las **variables viejas** de `whatsapp.env`: `WHATSAPP_BACKEND`, `WHATSAPP_SESSION_PATH`, `WHATSAPP_WEB_VERSION`, `WHATSAPP_DEBUG` y `PUPPETEER_*` (el servicio las nombra en el log al arrancar; solo nombres);
   - cualquier ajuste de AppArmor o de `kernel.apparmor_restrict_unprivileged_userns` que hayas hecho para el sandbox de Chromium (el servicio viejo arrancaba con `--no-sandbox`, así que probablemente ninguno).
7. **En tu PC**: si tenías una carpeta `.wwebjs_auth` o `.wwebjs_cache` de pruebas viejas dentro de `whatsapp-service/`, borrala (era la sesión de una cuenta). No encontré ninguna en este árbol.

## Qué falta / pendiente de decisión

1. **Los cuatro avisos de turno por WhatsApp** (solicitud, propuesta, confirmación, cancelación): son lo único que abre una conversación sin que la persona haya escrito (solo con consentimiento). Opciones y recomendación: etapa 3. Si se pausan, la campanita queda como único aviso de turno (a menos que se agregue el mail a esos avisos).
2. **El consentimiento de WhatsApp** (etapa 4): dejarlo dormido (recomendado), simplificarlo o sacarlo. En cualquier caso, **el texto `v0-provisorio` promete un recordatorio que ya no existe**; antes de usarlo con pacientes reales hay que publicar una versión nueva con el equipo legal.
3. **Un paciente sin mail** (el registro público lo exige, pero la columna admite `NULL`) **ya no se entera del recordatorio** si no entra a la app: antes le llegaba por WhatsApp.
4. **`BotReservasService.ResponderRecordatorio`** (el "1 = asisto, 2 = no puedo") quedó como código heredado para las conversaciones que ya estaban esperando esa respuesta. Se puede borrar cuando esas conversaciones hayan vencido.
5. **Ventana de tiempo para las respuestas automáticas:** la API pide que la persona haya escrito *alguna vez* en la conversación, y el servicio la acota con `WHATSAPP_CONSENTIMIENTO_DIAS` (365). Si querés que solo se pueda contestar dentro de las 24 horas de su último mensaje, ponelo en `1` (el costo: una respuesta humana al día siguiente se rechaza).
6. **Nada de esto se probó contra WhatsApp real** (límite de la tarea): ni los topes, ni las pausas, ni el "escribiendo...". Los valores por defecto son una opinión prudente, no un dato de WhatsApp; el primer despliegue es la primera vez que algo corre contra el servicio de verdad, y hay que hacerlo con el teléfono del número en la mano.
7. **El chat simulado usa el canal activo** (H7): con `Baileys`, un pedido local al simulador hace que el asistente le conteste por WhatsApp real a ese teléfono (acotado: solo desde la propia máquina, y el servicio exige constancia de esa persona).
8. **`CanalWhatsAppCloud`** (la API oficial de Meta) sigue siendo un enchufe sin usar: es la salida natural si algún día se quiere volver a mandar avisos de forma legítima.
9. **En el VPS hoy corre el servicio viejo** (`whatsapp-web.js` + Chromium): todo lo de este PR es código en `main` y no está desplegado.
