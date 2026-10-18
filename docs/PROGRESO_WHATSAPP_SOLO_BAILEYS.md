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
- **Última actualización:** 2026-10-18, tras la auditoría (antes de tocar código).

## Estado de las etapas

| Etapa | Qué | Estado | Commit |
|---|---|---|---|
| — | Auditoría de todos los flujos de WhatsApp y este archivo | **hecho** | (este commit) |
| 1 | Sacar whatsapp-web.js (servicio, API, docs, VPS) | pendiente | |
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

## Pasos manuales para Agustín

(Se completa etapa por etapa.)

## Qué falta / pendiente de decisión

(Se completa etapa por etapa.)
