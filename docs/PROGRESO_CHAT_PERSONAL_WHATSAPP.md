# Chat de WhatsApp para el personal: diagnóstico y diseño (fase 1)

**Estado (2026-10-19):** la fase 1 (diagnóstico, solo lectura) está terminada y **Agustín aprobó el diseño el 2026-10-19** con las ocho decisiones de la sección 7
(la D2 corregida: **los profesionales no ven ningún chat**; solo el Configurador ve y responde). La fase 2 (código) empieza por el **hito 1** (ramas 1 y 2 de la
sección 5.10), una rama por vez. Rama `docs/chat-personal-whatsapp-diagnostico`, desde `main` `ca77360` (PR #45).
En la fase 1 no se cambió código, no se tocó `yosoy`, el VPS ni ningún `.env`, y no se conectó nada a WhatsApp real.

**El objetivo:** ya no hay avisos ni recordatorios automáticos por WhatsApp (`Mensajeria:AvisosPorWhatsApp` sigue apagada). En su lugar, un
**chat para el personal**: el paciente le escribe al número dedicado, el Configurador (que hace de recepción) ve la conversación en el sistema y responde. Más adelante,
posiblemente una IA que sugiera borradores, siempre con aprobación humana. Por ahora, solo entorno de prueba, nada a producción.

## Resumen

1. **La bandeja ya existe.** Hoy el Configurador y los profesionales tienen una bandeja de chats completa: lista con no leídos, hilo, notas internas,
   etiquetas, respuestas rápidas, Tomar/Devolver/Asignar, cerrar/reabrir, panel del paciente, auditoría y permisos por rol (matriz en
   `docs/MATRIZ_ENDPOINTS.md`). Lo que falta no es la bandeja: es adaptarla a "el paciente le escribe a un número real y responde el Configurador"
   (y, por la D2, **sacarles los chats a los profesionales**).
2. **Los entrantes llegan a la API, pero incompletos y sin red de seguridad.** Solo viaja el texto de los chats 1 a 1. Fotos, audios, documentos y
   hasta la leyenda de una foto se descartan sin dejar rastro. No viaja el id ni la hora del mensaje. Si la API falla o tarda más de 5 segundos, el servicio no
   reintenta: el mensaje puede perderse.
3. **Hoy contesta el asistente automático.** Cada mensaje entrante lo procesa el asistente de reservas (menú, pide el DNI), salvo que la conversación
   esté escalada a una persona. Para este objetivo se apaga (D3), con el mensaje de emergencia **desactivado por defecto** hasta que haya líneas oficiales verificadas.
4. **El vínculo teléfono ↔ paciente lo hace el asistente, como efecto de contestar.** Sin asistente nadie vincula, y no existe el vínculo manual ni el
   "no es paciente". Un número desconocido queda sin paciente y solo lo ve el Configurador (salvo que esté escalado).
5. **Los topes sí distinguen "responder" de "avisar", pero solo en uno de los tres.** Por persona y por día: 30 si escribió hace menos de 24 horas,
   3 si no. El tope diario (100) y el horario (40) son globales. Además, el motivo de un rechazo no llega a la pantalla (el personal ve "no entregado"
   sin saber por qué) y un mensaje que la cola acepta pero después no puede mandar figura como "Enviado".
6. **Para probar en tu PC alcanza con 3 variables en el servicio y 3 en la API** (más `Jwt:Key`, que ya tendrías, y `Email__Password` en blanco para que no salgan
   mails; sección 6). `yosoy_test` está vacía (45 tablas, 0 usuarios): hay que crear un Configurador. Los comandos están verificados sin red (sintaxis de
   PowerShell 5.1, y la validación de la configuración y de la carpeta de sesión de whatsapp-service en Windows).
7. **El flujo se puede probar hoy, sin código** (6.8): escribirle al número "quiero hablar con una persona" escala la conversación, el asistente se
   calla y el equipo responde desde Chats. Es un paliativo: el primer mensaje igual recibe una respuesta automática.

**Diseño (sección 5):** una llave `Mensajeria:ModoAtencion` (`Asistente` = lo de hoy, por defecto; `Personal` = lo nuevo), una migración
aditiva (la 17), un contrato v2 del webhook retrocompatible y **7 ramas en 3 hitos**. Con el hito 1 (dos ramas) ya se prueba con un teléfono real.

**Decisiones tomadas** (sección 7): las ocho, con lo que implican y en qué rama se implementa cada una. Dos cambian lo que yo había recomendado: la **D2**
(solo el Configurador ve los chats) y la **D3** (el mensaje de emergencia queda desactivado por defecto).

---

## 1. Cómo maneja hoy whatsapp-service los mensajes entrantes

### El camino

```
Teléfono de la persona --> WhatsApp --> Baileys (evento messages.upsert) --> baileys/servicio.js  _alRecibir()
   --> baileys/mensajes.js  interpretarEntrante()   (qué se ignora, de quién es, qué texto trae)
   --> descarta repetidos por id (en memoria)
   --> backend.js  reenviar():
         (a) consentimiento.alMensajeEntrante(telefono, texto)   anota "escribió primero" / BAJA / ALTA  (datos/consentimientos.json)
         (b) reenvio.js  POST {API_BASE_URL}/mensajeria/whatsapp/entrante   { telefono, texto }  +  X-Shared-Secret
                                                                             (un intento, 5 s de límite, el error solo se loguea)
```

Sí llegan a la API. El webhook solo contesta si el pedido viene de la propia máquina **y** trae el secreto compartido (si no, 404 sin decir cuál
falló). Después lo procesa `BotReservasService.ProcesarAsync` (sección 2).

### Qué llega y qué no

Solo se miran los eventos de tipo `notify` (mensajes nuevos); `append` (historial y sincronización) se ignora.

| Lo que manda la persona | ¿Llega a la API? | Detalle |
|---|---|---|
| Texto simple o con formato/enlace | Sí | Se recorta a 4000 caracteres, como la API |
| Texto dentro de un envoltorio (efímero, "ver una vez") | Sí | Se desenvuelve hasta 5 niveles |
| Foto, video o documento **con leyenda** | **No** | Se ignora entero, también la leyenda (`no_textual`) |
| Audio, nota de voz, sticker, ubicación, contacto, encuesta, reacción, botón/lista | **No** | `no_textual`, sin dejar rastro |
| Edición o borrado de un mensaje | **No** | El sistema se queda con el texto original |
| Grupos (`@g.us`), difusión y estados (`@broadcast`), canales (`@newsletter`) | No | A propósito |
| Mensajes propios (`fromMe`), **también los que se escriben desde el teléfono del número** | No | Lo que el equipo conteste desde el teléfono no queda en el sistema |
| Remitente con identificador anónimo (`@lid`) sin número resoluble | No | `sin_numero`: se ignora en silencio |
| El mismo mensaje dos veces | Una sola | Se recuerdan hasta 1000 ids, **en memoria**: no sobrevive a un reinicio |

### Qué guarda el servicio

- **Del texto: nada.** Pasa por memoria y se reenvía. No se escribe en disco ni en el log (hay pruebas que lo vigilan: `privacidad-logs.test.js`).
- `datos/consentimientos.json` (dentro de la carpeta de la sesión): por persona, la **huella** de su teléfono (HMAC-SHA256 de los últimos 10 dígitos,
  con una clave derivada del secreto compartido) y tres fechas (desde, último mensaje, baja). No guarda el teléfono.
- `datos/envios.json`: los contadores de envío (por día, por hora, por persona-huella).
- Los logs enmascaran los teléfonos (`***1234`) y tapan el secreto y el QR.

### Qué NO reenvía a la API (y haría falta)

- El **id** del mensaje de WhatsApp (sin él la API no puede evitar duplicados) y su **hora** real.
- El **tipo** de contenido y el **nombre del perfil** de la persona.

### Cuando algo falla

- La API caída, lenta (más de 5 s) o que rechaza: el servicio escribe una línea de error y sigue. **No reintenta ni guarda el mensaje: se pierde.**
  (Queda en el teléfono del número dedicado, si es el dispositivo principal.)
- WhatsApp desconectado: no llegan mensajes; cuando reconecta, WhatsApp entrega lo pendiente. **Sin verificar** si lo entrega como `notify` (que se
  procesa) o como `append` (que se descarta): hay que probarlo con un teléfono real (sección 8).
- Los motivos de cada mensaje ignorado existen en el código (`sin_clave`, `propio`, `grupo`, `difusion`, `canal`, `no_textual`, `vacio`,
  `sin_numero`) pero **no se cuentan ni se informan**: no hay forma de saber cuántos se descartaron.

---

## 2. Qué existe en la API y en la base

### Tablas (ya existen, de las migraciones 5, 13 y posteriores; producción tiene 44 tablas)

| Tabla | Para qué | Lo que importa |
|---|---|---|
| `Conversacion` | un hilo por teléfono y canal | `Telefono` (10 dígitos), `Canal` (`WhatsApp` o `Simulado`), `IdPaciente` (NULL si no se identificó), `Estado` (`Activa`, `DerivadaHumano`, `Urgente`, `Cerrada`), `PasoActual` y `DatosContexto` (estado del asistente), `IdProfesionalAsignado` (dueño del hilo), `IdUltimoMensajeLeido` (la lectura es **compartida por todo el equipo**, no por persona), `UNIQUE (Telefono, Canal)` |
| `MensajeChat` | cada mensaje | `Direccion` (`Entrante`/`Saliente`), `Origen` (`Paciente`, `Bot`, `Humano`, `Sistema`), `Texto` (4000), `FechaHora`, `EstadoEnvio` (`Pendiente`/`Enviado`/`Error`). **Sin** id de WhatsApp, tipo, autor ni motivo de error |
| `NotificacionInterna` | la campanita | `Destinatario` (`Administrador`/`Profesional`), `IdConversacion`, `Tipo`, `Titulo`, `Mensaje`, `Leida`. La de urgencia guarda **un extracto de 300 caracteres del mensaje y el nombre del paciente** |
| `NotaInternaConversacion` | notas del equipo (nunca llegan al paciente) | `Texto` (1000), `IdUsuario` |
| `EtiquetaConversacion`, `ConversacionEtiqueta` | etiquetas: catálogo y asignación | |
| `RespuestaRapida` | plantillas del composer, globales o de un profesional | |
| `PreguntaFrecuente` | las del menú del asistente | |
| `LogAuditoria` | auditoría | acción, entidad, id, usuario |
| `ConsentimientoWhatsApp` | consentimiento de avisos (migración 16) | **dormida**: no está aplicada en producción ni se toca con la llave apagada |

### Endpoints (detalle por rol en `docs/MATRIZ_ENDPOINTS.md`)

| Grupo | Rutas | Quién |
|---|---|---|
| Webhook de WhatsApp | `POST /mensajeria/whatsapp/entrante` | Solo `whatsapp-service`: pedido local **y** secreto compartido. Sin JWT |
| Chat simulado | `POST /mensajeria/simulado/entrante`, `GET /mensajeria/simulado/conversacion` | Anónimo, pero solo con `ChatSimuladoHabilitado` y (con `SoloLocal`) desde la propia máquina |
| Bandeja | `GET /mensajeria/conversaciones` (filtros `estado`, `desdeMensaje`, `idEtiqueta`), `GET /{id}`, `GET /{id}/paciente`, `GET /{id}/mensajes` (`desde`, `antes`, `limite`), `PUT /{id}/leida`, `POST /{id}/responder`, `PUT /{id}/cerrar`, `PUT /{id}/reabrir` | Profesional y Configurador, filtrado por pertenencia (404 si no se puede ver) |
| Propiedad del hilo | `PUT /{id}/tomar` (Profesional), `PUT /{id}/devolver` (dueño o Configurador), `PUT /{id}/asignar` (Configurador) | Solo en conversaciones escaladas |
| Notas y etiquetas | `GET/POST/DELETE /{id}/notas`, `POST/DELETE /{id}/etiquetas/{idEtiqueta}`, catálogo `/etiquetas` | Catálogo: alta/baja/cambio solo Configurador |
| Respuestas rápidas y preguntas frecuentes | `/respuestas-rapidas`, `/preguntas-frecuentes` | Respuestas: globales del Configurador y propias de cada profesional. Preguntas frecuentes: cualquiera de los dos |
| Campanita | `GET /notificaciones-internas`, `PUT /{id}/leida` | Filtrada por la pertenencia actual de la conversación |
| WhatsApp (administración) | `GET /mensajeria/whatsapp/estado` (con el QR), `POST /desvincular`, `POST /confirmar-vinculacion` | **Solo Configurador**. No depende de `ChatSimuladoHabilitado` |
| Capacidades | `GET /capacidades` → `{ mensajeria: true/false }` | Anónimo; decide si las pantallas muestran Chats |

### El chat simulado y `Mensajeria__ChatSimuladoHabilitado`

- El nombre engaña: la llave **apaga todo el grupo `/mensajeria`** (la bandeja entera, no solo el simulador), la carpeta estática
  `/pantallas/Pantalla_ChatSimulado/` y lo que dice `/capacidades`. Por defecto está en `true` (`appsettings.json`).
- `Mensajeria__SoloLocal` (por defecto `true`): rechaza con 404 todo pedido a `/mensajeria` que no venga de la propia máquina. Un pedido que pasó por
  un proxy (Caddy, con `X-Forwarded-For`) cuenta como remoto aunque llegue desde 127.0.0.1. En tu PC no hay proxy y anda; **en el VPS la bandeja
  daría 404 desde internet con estos valores** (no leí el `.env` del servidor: hay que verificar qué valen allá).
- `POST /mensajeria/simulado/entrante` corre el mismo asistente que el webhook real, pero con el canal activo: con `CanalActivo=Baileys` la respuesta
  a un teléfono simulado saldría por WhatsApp real (el servicio igual exige que esa persona haya escrito antes). No usar el simulador con Baileys activo,
  salvo con tu propio número.

### Pantallas y roles

| Pantalla | Quién la ve | Cómo se llega | Condición |
|---|---|---|---|
| CRM → **Chats** (bandeja a página completa, con panel del paciente) | Configurador | menú CRM, `#chats` | `/capacidades.mensajeria = true` |
| CRM → **Escalamientos** (la misma bandeja con el filtro "Escaladas a persona") | Configurador | `#escalamientos` | idem |
| CRM → **Etiquetas de chats** (catálogo) | Configurador | `#etiquetas` | idem |
| **WhatsApp** (estado, QR, "Desvincular y cambiar número") | Configurador | `#whatsapp` | siempre: no depende de la llave de chats |
| Botón **Chats** (cajón lateral, expandible) | Profesional | barra superior | idem; arranca con el filtro "Mías" prendido. **Se saca en el hito 1 (D2)** |
| Campanita **Avisos** | quien tenga la bandeja | barra superior | |
| `/pantallas/Pantalla_ChatSimulado/` (bandeja + simulador del paciente) | Configurador | URL directa | `ChatSimuladoHabilitado` (si no, 404) |
| Paciente | ninguna | | el rol paciente recibe 403 en `/mensajeria/*` |

Qué hace la bandeja: actualiza por **polling** (cada 3 s, hasta 12 s si no hay novedades, con el chat a la vista; cada 20 a 45 s cerrado; nada con la pestaña
oculta; una vuelta completa cada 20 s), cuenta no leídos, avisa con un toast y sonido opcional, busca por nombre o teléfono, filtra (todas, no leídas,
escaladas, del bot, cerradas, etiqueta, "Mías"), agrupa los mensajes por día, pagina el historial hacia arriba (50 en 50), tiene composer con respuestas
rápidas (`/`), reintento si el envío falla, panel de contexto (datos del paciente, próximo turno, consentimiento, caso) y notas internas.

**Quién ve qué conversación** (`Services/VisibilidadConversacion.cs`, un único punto de decisión; 404 y no 403 para no confirmar que existe):

| Estado | Configurador | Profesional |
|---|---|---|
| Escalada (`DerivadaHumano`/`Urgente`) y sin dueño | la ve | la ve **cualquiera** (para poder tomarla) |
| Escalada y con dueño | la ve | solo el dueño |
| `Activa` o `Cerrada`, con paciente identificado | la ve | solo los profesionales tratantes del paciente (`PacienteProfesional`) |
| `Activa` o `Cerrada`, **sin paciente identificado** | la ve | **nadie** |

Esto es lo que hay hoy. **Con la D2 (decidida) la tabla se reduce a una fila:** solo el Configurador ve y contesta chats; los profesionales reciben 403 en todo
`/mensajeria`. Se descartan la visibilidad por paciente y Tomar/Devolver/Asignar a profesionales (el código queda inalcanzable para ellos; ver 5.9).

### Lo que hoy NO sirve para un chat de personal

| # | Qué pasa hoy | Dónde |
|---|---|---|
| a | El asistente contesta solo a cada mensaje (menú, pide el DNI, "No te entendí") | `BotReservasService.Procesar` |
| b | Cerrar y reabrir mandan un texto automático a la persona ("escribí *menu* y el asistente te va a ayudar") | `MensajeriaEndpoints` (`/cerrar`, `/reabrir`) |
| c | `Activa` se muestra como "Bot". Un mensaje nuevo avisa con contador, toast y sonido opcional, pero **solo con la pestaña abierta**; la campanita (que queda guardada) solo registra derivaciones y urgencias: nada avisa a quien no está mirando | `bandeja.js`, `NotificadorTurnos` |
| d | El motivo de un envío fallido no llega a la pantalla (`MensajeChat.CodigoDeError` es `[JsonIgnore]`): solo se ve "no entregado" | `MensajeChat.cs` |
| e | `POST /responder` contesta 200 aunque el envío haya fallado; el error va dentro del cuerpo (`estadoEnvio`) | `MensajeriaEndpoints` |
| f | La lista `GET /conversaciones` no pagina: trae todas en cada vuelta completa (cada 20 s) | `ConversacionDAO.ListarResumen` |
| g | "Mías" filtra por **dueño del hilo**, pero el tooltip dice "tus pacientes": con el filtro prendido (el predeterminado), un profesional no ve las conversaciones `Activa` de sus pacientes si no son suyas por asignación | `bandeja.js` (`visibles`) |
| h | La etiqueta de canal es fija: "Chat simulado" en la lista y un selector "Enviar por" con una sola opción | `bandeja.js` |
| i | No hay vínculo manual con un paciente, ni "no es paciente", ni archivar | — |
| j | Tres auditorías (responder, cerrar, reabrir) guardan el **nombre del paciente** en el detalle (las demás, solo ids) | `Auditar` vs `AuditarConIds` |
| k | Los **profesionales** ven y contestan chats (botón Chats, filtro "Mías", Tomar/Devolver, visibilidad por paciente) y reciben la campanita de los chats. **Contradice la D2** | `MensajeriaEndpoints`, `VisibilidadConversacion`, `shell.js`, `bandeja.js` |

---

## 3. Cómo se vincula un teléfono entrante con un paciente

**Paso a paso, hoy:**

1. El teléfono se **normaliza** a los últimos 10 dígitos (`Telefono.Normalizar`): `5491155550001` → `1155550001`. `Paciente.Telefono` es texto libre y se
   normaliza en memoria al comparar (`DatosMensajeriaDAO.PacientesPorTelefono`: lee **todos** los pacientes activos con teléfono y compara; sin índice).
2. La conversación se busca o se crea por (teléfono de 10 dígitos, canal `WhatsApp`).
3. Si la conversación no está escalada y no tiene paciente, **el asistente intenta identificar**:
   - **un solo paciente** con ese teléfono: lo vincula (`Conversacion.IdPaciente`) y saluda por su nombre;
   - **ninguno o más de uno**: pide el DNI (hasta 3 intentos por conversación); si el DNI existe y el paciente está activo, lo vincula; si no, le dice que se
     registre en la web.
4. El vínculo queda guardado y **no se revisa más**: si el paciente cambia de teléfono, no se desvincula; no hay forma de desvincular.

**Número desconocido:** la conversación se crea igual (con su teléfono y sus mensajes). Queda con `IdPaciente = NULL`. La bandeja la muestra como
"Paciente sin identificar" y el panel dice "el teléfono no coincide con ningún paciente y no pasó su DNI", **sin ninguna acción**. La ve solo el Configurador
(salvo que esté escalada y sin dueño: entonces, cualquier profesional). Se guardan el teléfono y los mensajes de personas que no son pacientes, **sin ninguna
política de retención**.

**Limitaciones del vínculo actual:**

- Un celular cargado con el "15" delante no coincide. Dos pacientes con el mismo teléfono (una familia) nunca se vinculan solos.
- **Si el primer mensaje ya pide una persona (o trae una palabra de riesgo), la conversación se escala antes de intentar identificar y queda sin vínculo para
  siempre**: en una conversación escalada el asistente se calla, y la identificación es lo que hace el asistente. (Un `hola` antes sí vincula por teléfono.)
- Vincular por DNI **no verifica identidad**: quien sepa el DNI de un paciente "se vuelve" ese paciente ante el asistente, que le muestra sus turnos y el nombre
  de sus profesionales. Con el asistente apagado (modo `Personal`) esto desaparece, pero entonces el vínculo tiene que salir de otro lado.
- Vincular por teléfono tampoco verifica identidad (un número reciclado, un familiar con el teléfono del paciente): hay que tratarlo como "probable".

---

## 4. Cómo quedan hoy los topes anti-baneo para respuestas

**Dónde viven:** todos en `whatsapp-service` (`consentimiento.js`, `limites.js`, `cola.js`); la API solo ve el resultado del envío.

| Protección | Valor por defecto | Variable | Al llegar |
|---|---|---|---|
| Consentimiento: solo a quien le escribió primero (vale 365 días desde su último mensaje **de texto**) o si la API manda `optIn` | 365 días | `WHATSAPP_CONSENTIMIENTO_DIAS` | 403 `sin_consentimiento`; 403 `baja` si escribió BAJA |
| Tope diario global (el día cambia a medianoche de Buenos Aires) | 100 | `WHATSAPP_TOPE_DIARIO` (1 a 1000) | 429 `tope_diario` |
| Tope por persona y día: **aviso suelto** | 3 | `WHATSAPP_TOPE_POR_PERSONA` (1 a 20) | 429 `limite_destinatario` |
| Tope por persona y día: **conversando** (escribió hace menos de 24 h) | 30 | `WHATSAPP_TOPE_EN_CONVERSACION` (1 a 200; nunca menos que el de arriba) | 429 `limite_destinatario` |
| Tope por hora global | 40 | `WHATSAPP_TOPE_POR_HORA` (1 a 500) | los aceptados esperan en la cola |
| Pausa entre mensajes | 4 a 9 s al azar | `WHATSAPP_PAUSA_MIN_S`, `WHATSAPP_PAUSA_MAX_S` | espera |
| Pausa larga | cada 8 a 14 mensajes, de 30 a 75 s | (fija) | espera |
| "Escribiendo…" antes de cada mensaje | 0,8 s + 20 ms por carácter, hasta unos 4 s (con una variación del 20 %) | `WHATSAPP_SIMULAR_ESCRITURA=0` lo apaga | demora |
| Cola | 50 pendientes; lo que espera más de 3 h se descarta | (fijas) | 503 `cola_llena`; el descarte solo va al log |
| Conexión | `requiere_vincular`, `requiere_atencion` y `detenido` rechazan | | 503 `no_conectado` |

**Dónde se distingue "responder a quien escribió hace menos de 24 h" de "aviso aislado":** en **un solo lugar**. `consentimiento.permite()` calcula
`enConversacion` (la persona mandó un texto hace 24 horas o menos) y `limites.reservar()` elige con eso el tope por persona y día: **30 o 3**. Los topes
diario y horario, las pausas y el "escribiendo…" son iguales para una respuesta que para un aviso.

**Qué pasa hoy con una respuesta del equipo:**

1. `POST /mensajeria/conversaciones/{id}/responder` → `ServicioMensajes.EnviarAsync(c, texto, "Humano")`. El origen `Humano` está exento de la regla "un mensaje
   automático solo sale si la persona escribió en esa conversación".
2. Se guarda el `MensajeChat` (`Pendiente`) y `CanalBaileys` llama a `POST 127.0.0.1:3002/enviar { telefono, texto }`, sin `optIn`: decide el servicio.
3. El servicio acepta si la persona le escribió en los últimos 365 días; si no, rechaza con 403. Si acepta, encola.
4. La API marca `Enviado` (= "aceptado en la cola") o `Error`. El código del rechazo no se guarda ni se muestra.
5. En segundo plano la cola respeta pausas y topes y manda por Baileys.

**Brechas para un chat de personal:**

| # | Brecha | Efecto |
|---|---|---|
| 1 | El presupuesto global (100 por día, 40 por hora) es compartido por todas las respuestas | Con 20 conversaciones activas y 5 respuestas cada una se agota el día. Los valores están pensados para un número nuevo; subirlos es una decisión de riesgo |
| 2 | El motivo de un rechazo no llega a la pantalla | El personal ve "no entregado" sin saber si es el tope, la baja o que WhatsApp está desconectado |
| 3 | "Enviado" no es "salió" | Los errores del envío real (el número no tiene WhatsApp, el límite de 20 s) y los descartes (más de 3 h en cola) solo van al log del servicio |
| 4 | Lo que no es texto no cuenta como "escribió primero" ni abre la ventana de 24 h | Quien solo manda un audio o una foto no puede recibir respuesta (403 `sin_consentimiento`), y el equipo ni se entera |
| 5 | Responder desde el teléfono del número no pasa por los topes ni queda en el sistema | El teléfono no está controlado por ninguna de las protecciones |
| 6 | El tope por persona es por día calendario, la ventana de conversación es móvil (24 h desde el último mensaje) | Una conversación que cruza la medianoche recupera cupo; es inofensivo pero conviene saberlo |

Lo que **ya está bien** para el nuevo objetivo: la clínica no puede iniciar una conversación con alguien que nunca le escribió (403 `sin_consentimiento`), la BAJA se
respeta hasta que la persona escriba ALTA, y una respuesta a quien escribió hace menos de 24 horas tiene un tope razonable (30).

---

## 5. Diseño mínimo propuesto

**Principios:** reusar lo que existe; todo lo nuevo detrás de una llave que por defecto deja las cosas como hoy; cambios de base solo aditivos; nada al
servidor; ninguna prueba contra WhatsApp real.

### 5.1 Llaves

| Llave | Valores | Por defecto | Para qué |
|---|---|---|---|
| `Mensajeria:ModoAtencion` | `Asistente` / `Personal` | `Asistente` (= hoy) | En `Personal` el asistente no contesta y todo entrante queda para el Configurador |
| `Mensajeria:Personal:RespuestaDeEmergencia` | `true` / `false` | **`false`** (D3) | En `Personal`, si el mensaje trae palabras de riesgo, se manda el mensaje de emergencia. Apagada, igual se marca `Urgente` y se avisa en la campanita. **Aunque se ponga en `true`, no se manda mientras alguna línea de `Mensajeria:LineasEmergencia` diga `[COMPLETAR Y VALIDAR]`** (se avisa en el log). Pasos para activarla: sección 7 |

`/capacidades` pasa a informar también el modo (`{ mensajeria, modoAtencion }`), para que las pantallas rotulen bien.

### 5.2 Flujo

```
Paciente --> WhatsApp --> numero dedicado --> whatsapp-service
     [tipo, id, hora, texto o aviso del tipo; reintenta en memoria; no guarda nada]
         --> POST /mensajeria/whatsapp/entrante          (loopback + secreto)
              ModoAtencion = Personal:
                1. guarda el MensajeChat (sin duplicar: IdExterno)
                2. busca paciente por telefono: un solo candidato => vinculo "telefono" (a confirmar por una persona)
                3. la conversacion queda DerivadaHumano: sin dueno, o con el que ya tenia
                4. palabras de riesgo => Urgente + campanita (el mensaje de emergencia solo si D3 se activo a mano)
                5. campanita "chat nuevo" (una por conversacion mientras no se lea)
Configurador --> Chats (polling) --> lista --> hilo --> responder --> ServicioMensajes("Humano") --> CanalBaileys --> /enviar
                                                                          ^                                        |  cola, topes, pausas
                                                                          +------- estado real del envio <---------+  (callback con referencia = IdMensaje)
```

**Un estado nuevo o no.** Se **reusa `DerivadaHumano`** como el estado de todo chat en modo `Personal`: no cambia el esquema ni los filtros. Su costo es semántico
(la pantalla dice "Escalada a persona"): se corrige con rótulos según el modo. Con la D2 ya no importa que ese estado traiga las reglas de Tomar/Devolver/Asignar y
de visibilidad por profesional: no se usan. La alternativa (un estado `Abierta`) obliga a cambiar el CHECK de la base, los filtros y los paneles; migrar a ella
después es mecánico. Una conversación `Cerrada` que recibe un mensaje vuelve a `DerivadaHumano`.

### 5.3 Modelo de datos: migraciones aditivas e idempotentes (no tocan filas existentes)

La **17** (rama 2, hito 1) trae solo lo que usa el hito 1. Lo que usan las ramas 3 y 4 va en migraciones propias (18 y 19), para que cada rama se pueda
revisar, aplicar o descartar sola.

| Migración | Tabla | Columna | Tipo | Para qué |
|---|---|---|---|---|
| **17** | `MensajeChat` | `IdExterno` | `nvarchar(64)` NULL | id del mensaje en WhatsApp. Índice único filtrado `(IdConversacion, IdExterno)` donde no sea NULL: evita duplicados |
| **17** | `MensajeChat` | `Tipo` | `nvarchar(12)` NOT NULL, por defecto `texto` | `texto`, `audio`, `imagen`, `video`, `documento`, `sticker`, `ubicacion`, `contacto`, `otro` (CHECK) |
| **17** | `Conversacion` | `NombreContacto` | `nvarchar(100)` NULL | nombre del perfil de WhatsApp (D5) |
| 18 (rama 3) | `Conversacion` | `VinculoOrigen` | `nvarchar(10)` NULL | `telefono` o `manual`; NULL = sin vínculo, o anterior a la migración |
| 19 (rama 4) | `MensajeChat` | `IdUsuario` | `int` NULL, FK a `Usuario` | quién respondió (mensajes `Humano`) |
| 19 (rama 4) | `MensajeChat` | `CodigoError` | `nvarchar(30)` NULL | motivo del fallo, de una lista cerrada (`sin_consentimiento`, `baja`, `tope_diario`, `limite_destinatario`, `no_conectado`, `cola_llena`, `sin_whatsapp`, `descartado`...) |

No hay tablas nuevas ni se guarda contenido multimedia. La 17 es independiente de la 16 (que sigue sin aplicarse). **Mientras `ModoAtencion` sea `Asistente`, la API no
usa ninguna columna nueva** (ni para leer ni para escribir): se puede publicar sin haber aplicado la 17. Con `Personal` hace falta aplicarla **antes**. Orden de
despliegue, **a futuro y no ahora**: backup, migración 17, API, whatsapp-service, y recién después `Mensajeria__ModoAtencion=Personal`. La API vieja sigue andando con
las columnas nuevas. El script va en la rama 2, con su verificación de idempotencia y su versión en `01-produccion.sql`, como las migraciones anteriores.

### 5.4 whatsapp-service: contrato v2 del webhook

El cuerpo agrega campos **opcionales** (la API vieja ignora los desconocidos, y la nueva acepta el cuerpo viejo):

```
POST /mensajeria/whatsapp/entrante
{ telefono, texto, tipo?, id?, ts?, nombre? }
```

- `tipo` e `id` (el de WhatsApp), `ts` (hora real del mensaje: la API la usa si es razonable, no futura y de menos de un día; si no, usa la suya) y `nombre` (D5).
- Lo que no es texto viaja como **aviso del tipo**: `texto` = `[Audio]`, `[Imagen]`, `[Documento]`..., con la leyenda a continuación si la trae. El contenido no se descarga.
  Cuenta como "escribió primero" para el consentimiento y la ventana de 24 h (brecha 4).
- **Reintentos** si la API falla (error de red, timeout, 5xx): 3 intentos en unos 3 minutos, **solo en memoria** (los textos no se escriben a disco). Sin reintento en 4xx.
- **Contadores** en `GET /salud` (solo números): reenviados, ignorados por motivo, fallidos y perdidos. Sin teléfonos ni textos.
- `POST /enviar` acepta una `referencia` (el `IdMensaje` de la API) y, cuando el mensaje sale, se descarta o falla, el servicio avisa a la API
  (`POST /mensajeria/whatsapp/envio { referencia, estado, codigo }`), que actualiza `EstadoEnvio` y `CodigoError`. Resuelve la brecha 3.

### 5.5 API: endpoints nuevos y cambiados

| Endpoint | Cambio | Quién |
|---|---|---|
| `POST /mensajeria/whatsapp/entrante` | Campos nuevos; en `Personal` no corre el asistente; responde `{ idConversacion, idMensaje }` | `whatsapp-service` (igual que hoy) |
| `POST /mensajeria/whatsapp/envio` | **Nuevo**: estado real de un envío (por `referencia`) | `whatsapp-service` (igual candado) |
| `GET /mensajeria/conversaciones/{id}/candidatos?q=` | **Nuevo** (rama 3): pacientes que coinciden por teléfono (hasta 5) o por la búsqueda `q` (nombre o DNI, hasta 10); DNI y teléfono **enmascarados** | Configurador |
| `PUT /mensajeria/conversaciones/{id}/paciente` | **Nuevo** (rama 3): vincula (`{ idPaciente }`, `VinculoOrigen = manual`); audita ids | Configurador |
| `DELETE /mensajeria/conversaciones/{id}/paciente` | **Nuevo** (rama 3): desvincula ("no es esa persona") | Configurador |
| `POST /mensajeria/conversaciones/{id}/responder` | Devuelve `codigoError` cuando `estadoEnvio = Error`; guarda `IdUsuario` (rama 4) | Configurador |
| `GET /mensajeria/conversaciones` | Pagina (`limite`, `antes`); agrega `ultimoEntrante` (ventana de 24 h) y `tipoUltimoMensaje` (ramas 4 y 5) | Configurador |
| `PUT /{id}/cerrar`, `PUT /{id}/reabrir` | En `Personal` no mandan texto automático (rama 1) | Configurador |
| `GET /capacidades` | Agrega `modoAtencion` (rama 1) | anónimo |
| **Todo `/mensajeria`** (bandeja, notas, etiquetas, respuestas rápidas, preguntas frecuentes, campanita, recordatorios) | **D2** (rama 1): solo Configurador; un Profesional recibe **403** en cada ruta | Configurador |

El banner de "WhatsApp desconectado" (rama 4) usa el `GET /mensajeria/whatsapp/estado` que ya existe (solo Configurador): ya no hace falta un endpoint nuevo `/conexion`.

### 5.6 Pantalla (se reusa `bandeja.js`; no se escribe una nueva)

| Cambio | Detalle |
|---|---|
| Sin chats para el Profesional (D2) | Se saca el botón **Chats**, el cajón y la campanita de chats de la pantalla del Profesional; no se carga `bandeja.js` |
| Rótulos según el modo | En `Personal`: "Esperando al equipo" en vez de "Escalada a persona"; sin el filtro "Del bot"; sin "Atendida por" ni "Asignar" (no hay dueño: D2); el icono dice "WhatsApp", no "Chat simulado"; se saca el selector "Enviar por" |
| Conversación sin identificar | Panel con los candidatos por teléfono, buscador por nombre o DNI, "Vincular" y "No es paciente". Lo vinculado por teléfono se marca "Probable: confirmá la identidad" |
| Tipos de mensaje | Un aviso con icono para audio, imagen, documento...: "Mirá el teléfono del número" |
| Envío visible | Cada mensaje saliente muestra enviando / enviado / **no salió: motivo**, con "Reintentar" |
| Ventana de 24 h | "Escribió hace 3 h: podés responder" / "Pasaron más de 24 h: se envía igual, con un tope bajo por día" |
| Conexión | Banner "WhatsApp desconectado: los mensajes no salen" (usa `GET /mensajeria/whatsapp/estado`, solo Configurador) |
| Nombre de contacto | Si la conversación no tiene paciente, la lista y el panel muestran el nombre del perfil de WhatsApp como "Contacto: …" (D5) |
| Lista | Paginada; "archivar" las cerradas de más de N días |
| Campanita | "Chat nuevo" (una por conversación; vuelve a avisar tras 30 min de silencio), solo para el Configurador |

### 5.7 Permisos por rol

Con la **D2**: solo el Configurador ve y contesta chats. Los profesionales atienden, no contestan mensajes: **no ven ningún chat ni reciben sus notificaciones**
(403 en todo `/mensajeria`). Se descartan la visibilidad por paciente y Tomar/Devolver/Asignar a profesionales.

| Acción | Configurador | Profesional | Paciente | Sin sesión |
|---|---|---|---|---|
| Ver lista, hilo y panel | todas | **no (403)** | no (403) | no (401) |
| Responder, cerrar, reabrir, notas, etiquetas, respuestas rápidas | sí | **no (403)** | no (403) | no (401) |
| Vincular o desvincular un paciente (rama 3) | sí | **no (403)** | no (403) | no (401) |
| Campanita de chats ("chat nuevo", urgencias) | sí | **no** | no | no |
| Estado de WhatsApp (conexión, QR) y desvincular el número | sí | no (403) | no (403) | no (401) |
| Catálogo de etiquetas (alta, baja, cambio) | sí | no (403) | no (403) | no (401) |
| Tomar, Devolver, Asignar | se descartan (la propiedad del hilo ya no se usa) | no (403) | | |
| Webhook entrante y de estado de envío | | | | solo `whatsapp-service` (pedido local + secreto) |

### 5.8 Qué datos NO se loguean ni se guardan

| Dato | Log del servicio | Log de la API | Base | Nota |
|---|---|---|---|---|
| Texto de los mensajes | Nunca | Nunca | **Se guarda** en `MensajeChat` (como hoy) | Los textos de las notas y de las respuestas tampoco van a la auditoría |
| Teléfono completo | Nunca (`***1234`) | Nunca | **Se guarda** en `Conversacion.Telefono` | |
| Nombre del perfil de WhatsApp | Nunca | Nunca | Solo en `Conversacion` si se aprueba D5 | |
| Fotos, audios, documentos | No se descargan | — | **No se guardan** | Solo el aviso del tipo |
| Grupos, difusión, estados, canales, historial anterior a la vinculación | No se procesan | — | No | `syncFullHistory` está apagado |
| QR, credenciales de la sesión, secreto compartido | Nunca | Nunca | Carpeta de la sesión, fuera del repo | |
| Auditoría (`LogAuditoria`) | — | — | Solo ids de usuario, conversación y paciente, y la acción | Se corrige la fila j de la tabla de la sección 2: hoy tres acciones guardan el nombre del paciente |
| Extracto del mensaje en la urgencia | — | — | **Se guarda** (300 caracteres + nombre) en `NotificacionInterna` | Se conserva: es lo que permite reaccionar; se revisa en D7 |

Cada rama nueva suma pruebas que capturan los logs y verifican que no aparezca un texto ni un teléfono (como ya hace `privacidad-logs.test.js` en el servicio).
Los motivos de error que ve el personal son códigos de una lista cerrada, nunca el mensaje de una excepción.

### 5.9 Fuera de alcance por ahora

- **Borradores de IA.** Solo dejo previsto el lugar: un botón "Sugerir respuesta" que **inserta un borrador en el composer**, marcado y editable, sin enviar jamás.
  Hace falta decidir antes qué se le manda a un proveedor externo (mínimo y seudonimizado: sin nombre, DNI ni teléfono): es una **transferencia de datos de salud
  a un tercero** y requiere criterio legal y contrato. La interfaz actual (`IInterpreteIntencion`) clasifica intenciones del menú, no redacta: sería otra
  (`ISugeridorDeRespuestas`). La auditoría guardaría quién pidió el borrador y cuándo, sin contenido.
- Descargar y guardar archivos; importar los mensajes que se escriben desde el teléfono; ticks de entregado y leído; un rol "Recepción" propio; el despliegue
  (en el VPS, con los valores por defecto, `SoloLocal` y Caddy dejan la bandeja en 404 desde internet; hay que decidir cómo se publica).
- **Retirar del código lo que la D2 deja sin uso**: la visibilidad por paciente de `VisibilidadConversacion`, `PUT /tomar`, `PUT /devolver`, `PUT /asignar` y
  la columna `IdProfesionalAsignado`. Hoy quedan inalcanzables para los profesionales (el rol no pasa de la puerta); borrarlos toca mucho código y pruebas de otra
  tanda ("Pertenencia de conversaciones"), así que se hace en una rama aparte, si la querés.

### 5.10 Ramas, en 3 hitos

Tamaños aproximados: **chico** = unas 150 a 300 líneas de código y sus pruebas; **mediano** = 400 a 900.

| # | Rama | Qué incluye | Tamaño | Hito |
|---|---|---|---|---|
| 1 | `feat/chat-personal-modo-personal` | La llave `ModoAtencion` y el modo `Personal` (5.1 y 5.2): sin asistente, conversación para el Configurador, riesgo marcado `Urgente`, emergencia desactivada por defecto con su resguardo (D3), "chat nuevo" en la campanita, `cerrar`/`reabrir` sin texto automático, `/capacidades` con el modo. **D2**: todo `/mensajeria` solo Configurador (403 a un Profesional en cada ruta), sin cajón de chats, "Mías", Tomar ni Asignar en la pantalla. Sin migración | mediano | **1** |
| 2 | `feat/chat-personal-entrantes-completos` | Webhook v2 compatible (tipo, id, hora, nombre), migración 17, avisos de tipo para fotos y audios (D4), nombre de contacto (D5), reintentos en memoria y contadores en `whatsapp-service` (D6), deduplicación por id. **Apilada sobre la 1** (usa `ModoAtencion`): con `Asistente` no usa ninguna columna nueva | mediano (la más grande: dos procesos y SQL) | **1** |
| 3 | `feat/chat-personal-vinculo-paciente` | Vínculo automático por teléfono (un solo candidato) sin asistente, candidatos y búsqueda, vincular y desvincular a mano, `VinculoOrigen` (migración 18), panel "sin identificar" | mediano | 2 |
| 4 | `feat/chat-personal-envio-visible` | `codigoError` y autor (migración 19) en la pantalla, "Reintentar", ventana de 24 h, banner de conexión, estado real del envío (`referencia` y callback) | mediano | 2 |
| 5 | `feat/chat-personal-pantalla` | Rótulos por modo, sin "Chat simulado", lista paginada, archivar y purgar según la retención (D7) | chico a mediano | 3 |
| 6 | `chore/chat-personal-topes` | Presupuestos separados: respuestas a quien escribió hace menos de 24 h **vs.** mensajes que inicia la clínica; variables nuevas; documentación | chico | 3 |
| 7 | `docs/chat-personal-prueba-local` | `Scripts/iniciar-chat-local.ps1` (levanta la API y el servicio con las variables, sin imprimir secretos). Opcional: por ahora los comandos van en el reporte de cada rama | chico | opcional |

- **Hito 1** (ramas 1 y 2, en ese orden): ya se prueba con un teléfono real: el paciente escribe, el Configurador lo ve y contesta, y fotos y audios dejan un aviso en el hilo.
  Sin vínculo con el paciente todavía (la conversación sale "sin identificar").
- **Hito 2** (ramas 3 y 4): vínculo con el paciente y envíos que muestran por qué fallan.
- **Hito 3** (ramas 5 y 6): lo que hace usable el día a día.

**Cómo se prueba cada rama:** pruebas de unidad y de integración contra `yosoy_test` (`Scripts\correr-tests.ps1`), y también contra una base de 44 tablas
(`-SinConsentimientoWhatsApp`), porque la 16 sigue sin aplicarse; `npm test` en el servicio con la fachada falsa de Baileys; mutaciones dirigidas, como en las
tandas anteriores; un banco con Chrome contra la API real y una base descartable. Ninguna toca WhatsApp real: eso solo pasa en tu prueba manual (sección 6).

---

## 6. Correr whatsapp-service en tu PC (Windows, PowerShell) con el número dedicado

### 6.1 Qué hace falta

- **Node 22 o más** (tenés v24.14.1) y las dependencias de `whatsapp-service` (ya instaladas: Baileys 6.7.24). **.NET** para la API. Nada de Chromium, Puppeteer, sudo ni systemd.
- Un teléfono con el **número dedicado** y WhatsApp instalado. **No uses tu número personal**: Baileys es un cliente no oficial y un uso brusco puede hacer que
  bloqueen el número (es el motivo de los topes).
- Que `yosoy_test` exista (existe: 45 tablas) y tenga un usuario Configurador (no tiene: 0 usuarios).

### 6.2 Variables de entorno (solo los nombres)

| Dónde | Variable | Obligatoria | Qué es |
|---|---|---|---|
| whatsapp-service | `API_BASE_URL` | sí | Dónde está la API: `http://127.0.0.1:5000` |
| whatsapp-service | `Mensajeria__WhatsApp__Secreto` | sí | Secreto compartido con la API (mismo valor en las dos) |
| whatsapp-service | `WHATSAPP_BAILEYS_SESSION_PATH` | sí | Carpeta de la sesión: ruta absoluta, fuera del repo y de la app |
| whatsapp-service | `WHATSAPP_TOPE_DIARIO` y demás topes (`..._POR_HORA`, `..._POR_PERSONA`, `..._EN_CONVERSACION`) | no | Para un número nuevo conviene empezar bajo (30 por día) |
| whatsapp-service | `WHATSAPP_SERVICE_PORT` | no | **No la cambies**: `CanalBaileys` tiene el 3002 fijo en el código |
| API | `ConnectionStrings__YoSoy` | sí | La base: `yosoy_test` |
| API | `Mensajeria__WhatsApp__Secreto` | sí | El mismo valor que el servicio. Sin él el webhook contesta 404 a todo |
| API | `Mensajeria__CanalActivo` | sí | `Baileys` |
| API | `Email__Password` | recomendada | Un espacio: así no sale ningún mail de verdad |
| API | `Jwt:Key` (secreto de usuario) | sí | Ya la tenés si corrés la API en tu PC; si no, ver 6.3 |

No hace falta tocar `Mensajeria__ChatSimuladoHabilitado` ni `Mensajeria__SoloLocal` (ya vienen en `true`), y `Mensajeria__AvisosPorWhatsApp` sigue apagada.

### 6.3 Una sola vez

La carpeta de la sesión y el secreto van **fuera del repositorio**, en `%LOCALAPPDATA%\YoSoy\whatsapp`. No los pongas dentro de Google Drive, OneDrive ni
Dropbox: la sesión es una credencial de la cuenta de WhatsApp. En Windows el servicio no puede ajustar permisos (en Linux deja la carpeta en 700): la protege el
perfil de tu usuario.

```powershell
# 1) Comprobaciones (solo lectura)
node --version                                   # 22 o más
dotnet --version
cd D:\Claude\YoSoy\YoSoy\whatsapp-service
npm ls --omit=dev --depth=0                      # tiene que listar @whiskeysockets/baileys 6.7.24
# Solo si faltaran las dependencias:  npm ci --omit=dev

# 2) Secreto compartido: al azar, se guarda en un archivo y NO se imprime
$base = Join-Path $env:LOCALAPPDATA 'YoSoy\whatsapp'
New-Item -ItemType Directory -Force $base | Out-Null
$b = New-Object byte[] 32
[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
(($b | ForEach-Object { $_.ToString('x2') }) -join '') | Set-Content (Join-Path $base 'secreto.txt') -NoNewline -Encoding ascii

# 3) Primer Configurador en yosoy_test (el script del repo apunta a [yosoy]: se usa una copia con el nombre cambiado)
cd D:\Claude\YoSoy\YoSoy
$sql = (Get-Content .\docs\produccion\03-usuario-admin.sql -Raw -Encoding UTF8).Replace('USE [yosoy];', 'USE [yosoy_test];')
if ($sql -notmatch 'USE \[yosoy_test\];') { throw 'No se pudo cambiar el nombre de la base en el script.' }
$tmp = Join-Path $env:TEMP 'usuario-admin-yosoy_test.sql'
[IO.File]::WriteAllText($tmp, $sql, (New-Object Text.UTF8Encoding($true)))
sqlcmd -S .\SQLEXPRESS -d yosoy_test -E -C -f 65001 -v AdminPassword="UnaClaveSoloParaPruebas1" -i $tmp
Remove-Item $tmp
```

- La contraseña del paso 3 tiene que ser **ASCII puro y sin comillas simples** (cambiala por la que quieras: el usuario es `admin`). Es una clave de una base de pruebas local.
- Si la API no arranca por falta de `Jwt:Key`: `dotnet user-secrets set "Jwt:Key" "<32 caracteres o más, al azar>"` (parado en `D:\Claude\YoSoy\YoSoy`).
- Si preferís no mezclar tus pruebas manuales (con números reales) con las de la suite, creá otra base con `.\Scripts\crear-base-de-pruebas.ps1 -Base yosoy_wa` y
  usala en la cadena de conexión de abajo. La suite solo borra lo suyo, pero conviene separar.

### 6.4 Cada vez: dos ventanas de PowerShell

Las variables `$env:` viven **solo en la ventana donde las definís**: cada ventana necesita las suyas. Primero la API, después el servicio (si el servicio
recibe un mensaje antes de que la API esté arriba, ese mensaje se pierde).

**Ventana A, la API** (queda en `http://localhost:5000`):

```powershell
cd D:\Claude\YoSoy\YoSoy
$env:ConnectionStrings__YoSoy = 'Server=.\SQLEXPRESS;Database=yosoy_test;Trusted_Connection=True;TrustServerCertificate=True;'
$env:Email__Password = ' '                       # un espacio: no sale ningún mail
$env:Mensajeria__CanalActivo = 'Baileys'
$env:Mensajeria__WhatsApp__Secreto = (Get-Content "$env:LOCALAPPDATA\YoSoy\whatsapp\secreto.txt" -Raw).Trim()
dotnet run
```

**Ventana B, whatsapp-service** (queda en `127.0.0.1:3002`):

```powershell
cd D:\Claude\YoSoy\YoSoy\whatsapp-service
$env:API_BASE_URL = 'http://127.0.0.1:5000'
$env:Mensajeria__WhatsApp__Secreto = (Get-Content "$env:LOCALAPPDATA\YoSoy\whatsapp\secreto.txt" -Raw).Trim()
$env:WHATSAPP_BAILEYS_SESSION_PATH = "$env:LOCALAPPDATA\YoSoy\whatsapp\sesion-baileys"
$env:WHATSAPP_TOPE_DIARIO = '30'                 # número nuevo: empezar bajo
node index.js
```

La carpeta de la sesión la crea el servicio (con su subcarpeta `datos`). En el log de la ventana B tenés que ver, en este orden: `Carpeta de la sesión de
WhatsApp lista`, `whatsapp-service escuchando en 127.0.0.1:3002 (Baileys)`, `estado iniciando`, y después `hay un código QR para vincular el número`.
Con el número ya vinculado, `WhatsApp conectado (número ***1234)`.

### 6.5 Escanear el QR

**Opción A (recomendada): en el navegador.**

1. Abrí `http://localhost:5000/pantallas/index.html` e ingresá con `admin` y la contraseña del paso 3.
2. En el menú, **WhatsApp**. Con el servicio corriendo y sin sesión aparece el QR (se renueva solo; si no sirve, "¿El código no funciona? Generar uno nuevo").
3. En el teléfono del número dedicado: WhatsApp, **Ajustes, Dispositivos vinculados, Vincular un dispositivo**, y escaneá el código.
4. La pantalla pasa a "Conectado (+54…)". Desde ahí, reiniciar el servicio **no pide QR**: la sesión queda en la carpeta.

**Opción B: en la terminal** (con el servicio **parado**; el comando se niega si hay algo escuchando en el 3002 y si la salida no es una terminal):

```powershell
cd D:\Claude\YoSoy\YoSoy\whatsapp-service
$env:WHATSAPP_BAILEYS_SESSION_PATH = "$env:LOCALAPPDATA\YoSoy\whatsapp\sesion-baileys"
chcp 65001                                       # para que se dibujen los bloques del QR
npm run vincular
```

No probé cómo se dibuja el QR en tu consola (usa caracteres de medio bloque): si sale deformado, usá la opción A o Windows Terminal. Si no te deja correr
`npm` ("no se puede cargar el archivo npm.ps1"), usá `npm.cmd run vincular`.

### 6.6 Comprobar

```powershell
curl.exe -s http://127.0.0.1:3002/salud          # 200 conectado; 503 si falta vincular. Sin teléfonos ni textos
curl.exe -s http://localhost:5000/capacidades    # {"mensajeria":true}
```

### 6.7 Parar y empezar de cero

- **Parar:** `Ctrl+C` en cada ventana (el servicio cierra ordenado sin cerrar la sesión de WhatsApp).
- **Empezar de cero** (olvida la sesión **y** el registro de quién escribió, porque `datos` vive en la misma carpeta), con el servicio parado:

```powershell
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\YoSoy\whatsapp\sesion-baileys"
```

  Después, en el teléfono: Dispositivos vinculados, y cerrá esa sesión.

### 6.8 Probar el flujo hoy, sin código (hasta que exista el modo `Personal`)

1. Desde **otro** teléfono, escribile al número dedicado: `quiero hablar con una persona`. (El asistente entiende `persona`, `humano`, `humana`, `operador`,
   `operadora`, `secretaria` y `recepcion`, como palabra entera, como pedido de una persona.)
2. El asistente contesta **una vez** ("le pasamos tu conversación a una persona…"), la conversación queda escalada y sin dueño, aparece un aviso en la campanita y el
   asistente se calla.
3. En el navegador: **CRM, Chats**. Elegí la conversación y respondé desde el composer. Tiene que llegar al otro teléfono.
4. Cosas que vas a notar: el número aparece "sin identificar"; si mandás una foto o un audio, **no aparece**; si respondés desde el teléfono del número,
   **tampoco aparece en el sistema**.
5. Para ver una conversación **con un paciente vinculado**: registrá un paciente con ese número desde la pantalla de registro (con `Email__Password` en blanco no sale
   ningún mail) y mandá primero `hola`: el asistente reconoce el teléfono, vincula y saluda por el nombre. **Recién después** escribí `quiero hablar con una persona`.
   Si pedís una persona de entrada, la conversación se escala antes de identificar y queda sin vincular (sección 3).

### 6.9 Si algo falla

| Síntoma | Causa probable |
|---|---|
| `Falta API_BASE_URL` / `Falta Mensajeria__WhatsApp__Secreto` / `Falta WHATSAPP_BAILEYS_SESSION_PATH` | La variable no está definida **en esa ventana** |
| `La carpeta de la sesión … está dentro de un repositorio git` | La ruta quedó dentro de `D:\Claude\YoSoy\YoSoy` (incluida `whatsapp-service`) |
| `tiene archivos que no son de la sesión` | La carpeta no es dedicada (el servicio no usa ni borra nada ajeno) |
| El Configurador dice "Servicio no disponible" | El servicio no corre, o los secretos de las dos ventanas no coinciden (la API recibe 401), o cambiaste el puerto |
| `/salud` da 503 `requiere_vincular` | Falta escanear el QR (o venció: pedir otro) |
| `/salud` da 503 `requiere_atencion` | WhatsApp cortó por un motivo que reintentar no arregla; el log dice cuál (`sesion_reemplazada`: hay otra copia del servicio o el comando de vincular corriendo) |
| No aparece el botón Chats ni el grupo CRM | `/capacidades` da `false`: `ChatSimuladoHabilitado` apagado, o el pedido no es local (proxy) |
| El mensaje llega al teléfono del número pero no a Chats | Mirá la ventana A y el log de la B: `La API respondió 404` es un secreto distinto |
| `sin_consentimiento` al responder | Esa persona nunca le escribió texto a este número (o `datos` se borró) |

---

## 7. Decisiones tomadas (aprobadas por Agustín el 2026-10-19)

Agustín aprobó el diseño con estas ocho decisiones. Dos cambian lo que yo había recomendado: la **D2** (más estricta) y la **D3** (el mensaje de emergencia, apagado).

| # | Decisión | Qué implica | Dónde se implementa |
|---|---|---|---|
| D1 | **Configurador como recepción por ahora** | El único rol que ve y contesta chats es el Configurador. Un rol "Recepción" propio queda como proyecto aparte: toca el login, los menús y los permisos de todo el sistema | Criterio de todo el diseño |
| D2 | **Los profesionales NO ven ningún chat de WhatsApp ni reciben notificaciones de chats**; solo atienden, no contestan mensajes. **Solo el Configurador ve y responde.** Se descartan la visibilidad por paciente y Tomar/Asignar a profesionales. *(Corregida: yo había propuesto que vieran los chats sin dueño de sus pacientes)* | Todo `/mensajeria` exige el rol Configurador: un Profesional recibe **403** en cada ruta (probado ruta por ruta). De su pantalla se sacan el botón y el cajón Chats, la campanita de chats, "Mías", Tomar y Asignar. La campanita de chats ("chat nuevo", urgencias) es solo del Configurador | Rama 1 |
| D3 | **Asistente automático apagado en modo `Personal`, salvo el mensaje de emergencia, que queda DESACTIVADO por defecto** mientras las líneas de `appsettings.json` digan `[COMPLETAR Y VALIDAR]`. *(Cambia: yo lo había propuesto prendido)* | En `Personal` el asistente no contesta nada. Una frase de riesgo igual marca la conversación `Urgente` y avisa al Configurador en la campanita, pero **no manda nada al paciente**. `Mensajeria:Personal:RespuestaDeEmergencia` viene en `false`; y aunque se ponga en `true`, **no se manda mientras alguna línea de `Mensajeria:LineasEmergencia` esté sin completar** (teléfono vacío o con el texto provisorio): el log lo avisa. El paso manual para activarla está abajo | Rama 1 |
| D4 | **Fotos y audios: solo un aviso en el hilo, sin guardar el archivo** | `[Audio]`, `[Imagen]`, `[Video]`, `[Documento]`, `[Sticker]`, `[Ubicación]`, `[Contacto]`, con la leyenda a continuación si la trae; `MensajeChat.Tipo`; cuenta como "escribió primero". No se descarga ni se guarda el contenido | Rama 2 |
| D5 | **El nombre de perfil de WhatsApp se guarda en la conversación, nunca en logs** | `Conversacion.NombreContacto`, se actualiza con cada mensaje y solo en modo `Personal`. Ni `whatsapp-service` ni la API lo escriben en un log (pruebas que capturan el log) | Rama 2 |
| D6 | **Si la API falla, reintentos en memoria, sin escribir textos a disco** | 3 intentos en unos 3 minutos (solo ante error de red, timeout o 5xx; nunca ante un 4xx), con un tope de mensajes en espera. Si se agotan, se cuenta como perdido en `GET /salud` | Rama 2 |
| D7 | **Retención: 90 días para quienes no son pacientes; para pacientes, configurable y pendiente de validación legal** | `Mensajeria:Retencion:SinPacienteDias` = 90 y `Mensajeria:Retencion:PacientesDias` **sin valor** (= no se borra nada) hasta que legal valide un plazo. **Todavía no hay tarea que borre**: llega con la rama 5 (hito 3) | Rama 5 |
| D8 | **No se responde desde el teléfono del número; siempre desde YoSoy** | Regla de uso: la API no importa los mensajes propios (`fromMe`), así que lo que se conteste desde el teléfono no queda en el sistema ni pasa por los topes (sección 4, brecha 5). Importarlos queda como mejora posterior | Documentada |

### Cómo activar el mensaje de emergencia (paso manual, D3)

Hacerlo **solo cuando haya líneas oficiales verificadas** (las de `appsettings.json` hoy dicen `[COMPLETAR Y VALIDAR]`). Pasos, para Agustín:

1. Conseguir los teléfonos oficiales de emergencias y de la línea de salud mental, y **llamar para comprobar que atienden** (con la psicóloga que validó el texto, si se puede).
2. Cargarlos en la configuración de la API. O se editan las dos entradas de `Mensajeria:LineasEmergencia` en `appsettings.json` (no son secretos), o se definen en el entorno del servidor:
   `Mensajeria__LineasEmergencia__0__Nombre`, `Mensajeria__LineasEmergencia__0__Telefono`, `Mensajeria__LineasEmergencia__1__Nombre` y `Mensajeria__LineasEmergencia__1__Telefono`.
3. Poner `Mensajeria__Personal__RespuestaDeEmergencia=true` y reiniciar la API.
4. Probarlo con **tu propio teléfono de prueba** (el mensaje sale de verdad): escribirle al número dedicado una de las frases de `Mensajeria:PalabrasRiesgo`. Tiene que llegar el mensaje con las líneas reales, y la
   conversación tiene que quedar `Urgente` con un aviso en la campanita del Configurador.
5. Mirar el log de la API: **no** tiene que aparecer el aviso "las líneas de emergencia están sin completar". Si aparece, el mensaje no se mandó.

Para volver a apagarlo basta con `Mensajeria__Personal__RespuestaDeEmergencia=false` (o borrar la variable) y reiniciar.

### Pendientes que no son código

- **Legal:** validar el plazo de retención de las conversaciones con paciente (D7) y, de paso, el texto de consentimiento `v0-provisorio` (dormido).
- **Líneas de emergencia oficiales y verificadas** (D3).
- **Antes de usar esto con pacientes reales:** que los profesionales sepan que ya no ven chats (D2), y decidir cómo se publica la bandeja en el VPS (sección 5.9).

---

## 8. Qué verifiqué, qué no, y hallazgos menores

**Leí:** todo el código de `whatsapp-service` y, de la API, los endpoints de mensajería, los servicios, los DAOs, los modelos y las opciones; el esquema
(`docs/produccion/01-produccion.sql`); las pantallas (`bandeja.js`, `shell.js`, el panel del Configurador); `docs/MATRIZ_ENDPOINTS.md`, `CAMBIOS_AGUS.md`
(sección de pertenencia de conversaciones) y la documentación de despliegue y de desarrollo local.

**Ejecuté (todo de solo lectura o sin red):**

- Conteos en `yosoy_test`: 45 tablas; 0 usuarios, pacientes, profesionales, conversaciones, mensajes y consentimientos.
- Un script descartable, sin red, que llama a `leerConfig` y a `crearSesion().preparar()` de whatsapp-service en esta PC (Windows): acepta las tres variables obligatorias,
  acepta `localhost`, rechaza las faltantes y una ruta relativa, crea la carpeta con su subcarpeta `datos`, es idempotente y **se niega** a usar una carpeta dentro
  del repositorio o de la app. La carpeta de prueba la borré.
- El análisis de sintaxis de todos los bloques de PowerShell de la sección 6 con el parser de Windows PowerShell 5.1, y la prueba del bloque del secreto en una carpeta
  temporal (64 caracteres hexadecimales, sin BOM ni salto de línea).

**No hice:** arrancar el servicio ni la API, conectar nada a WhatsApp, leer ningún `.env`, tocar `yosoy` ni el VPS, correr la suite de la API.

**Sin verificar (hace falta un teléfono real):**

1. Los mensajes recibidos mientras el servicio estaba apagado: ¿WhatsApp los entrega como `notify` al reconectar (se procesan) o como `append` (se descartan)?
2. Los remitentes con identificador anónimo (`@lid`): ¿Baileys 6.7.24 trae el número en `senderPn`? Si no, se ignoran sin aviso.
3. Cómo se dibuja el QR en tu consola (`npm run vincular`). En el navegador no hay problema.
4. Si WhatsApp desvincula el dispositivo cuando el teléfono principal pasa mucho tiempo sin conectarse (según su ayuda, alrededor de 14 días: confirmalo ahí).
5. Qué valen `Mensajeria__SoloLocal` y `Mensajeria__ChatSimuladoHabilitado` en el VPS (no leí su `.env`).

**Hallazgos menores** (para el backlog; no frenan el diseño):

- `CanalBaileys` tiene `http://127.0.0.1:3002/` fijo en el código (no sigue a `WHATSAPP_SERVICE_PORT`); `Mensajeria:WhatsAppServiceUrl` solo la usa la pantalla de administración.
- `/enviar` devuelve `idExterno = cola-N`, que es de la cola del servicio (se reinicia con el proceso), no un id de WhatsApp; la API lo ignora.
- La pantalla de WhatsApp del Configurador, cuando el servicio no responde, manda a "revisar por SSH (systemctl status whatsapp-service)": está pensada para el VPS, no para tu PC.
- Las preguntas frecuentes las puede editar cualquier profesional (la matriz lo documenta como a propósito); con la D2 pasan a ser solo del Configurador.
- `PacientesPorTelefono` lee todos los pacientes en cada mensaje de una conversación sin vincular.
- El texto de consentimiento `v0-provisorio` (dormido) promete un recordatorio por WhatsApp que ya no existe.
