# Progreso: consentimiento de WhatsApp para pacientes

Punto de retoma de la tarea "consentimiento de WhatsApp (opciones A + B)". Si se corta una sesión, **este archivo manda**: dice qué
está hecho, qué decidí y por qué, qué falta y qué pasos manuales le tocan a Agustín.

- **Rama:** `feat/consentimiento-whatsapp`, creada desde `origin/main` (316f530, PR #42: el canal Baileys, apagado por defecto).
  Sin merge a `main`.
- **Decisión de producto (Agustín):** (A) consentimiento explícito en el registro y el perfil del paciente, y (B) un link `wa.me` para
  que el paciente inicie el contacto. YoSoy es una app de salud mental: el consentimiento tiene que ser **explícito, documentado y
  revocable**.
- **Límites duros:** nada de `.env`/secretos; sin deploy, sin `sudo`, sin tocar el VPS ni la base `yosoy`; **ninguna migración se aplica a
  una base real** (solo a bases descartables de prueba); sin `--force` ni merge a `main`; los datos de consentimiento no se loguean con
  teléfonos completos ni contenido.
- **Última actualización:** 2026-10-17, tras la etapa 2 (la API).

## Estado de las etapas

| Etapa | Qué | Estado | Commit |
|---|---|---|---|
| — | Este archivo (plan, hallazgos y decisiones) | hecho | `2cb6615` |
| 1 | Modelo de datos: tabla, migración idempotente, `01-produccion.sql` | **hecho** | `2c79f5a` |
| 2 | API: aceptar/revocar, `optIn` solo con consentimiento vigente, teléfono argentino, BAJA | **hecho** | (este commit) |
| 3 | Pantallas: casilla sin tildar en el registro, activar/revocar en el perfil | pendiente | |
| 4 | Link `wa.me` (opción B) configurable | pendiente | |
| 5 | Plantillas de mensajes sin nada clínico | pendiente | |
| 6 | Documentación: `DEPLOY.md`, `SERVIDOR.md`, avisos que quedaron viejos | pendiente | |

## Cómo retomar

1. `git switch feat/consentimiento-whatsapp` y mirar `git log --oneline origin/main..HEAD`: un commit por etapa.
2. Tests del servicio de WhatsApp: `cd whatsapp-service && npm test` (Node 22 o más nuevo; no necesita red ni WhatsApp).
3. Tests de la API: hace falta una base de pruebas **con el esquema nuevo** (`docs/produccion/01-produccion.sql` de esta rama). La
   `yosoy_test` de siempre no tiene la tabla `ConsentimientoWhatsApp` hasta que se le aplique la migración. Crear una nueva con
   `Scripts\crear-base-de-pruebas.ps1 -Base <otro_nombre>` y correr `Scripts\correr-tests.ps1 -Base <otro_nombre>` (**nunca** contra `yosoy`).
   Yo trabajé con `yosoy_test_wa` (descartable; se borra al final con el comando de `docs/DESARROLLO_LOCAL.md`, "Borrar la base de pruebas").
4. Seguir por la primera etapa en `pendiente` de la tabla de arriba.

## Hallazgos de la lectura inicial (antes de tocar nada)

- **El servicio de WhatsApp ya sabe qué hacer con `optIn`.** `POST /enviar` acepta `{ telefono, texto, optIn? }` y solo cuenta el valor
  `true` exacto, en cada pedido (`whatsapp-service/servidor.js`, `consentimiento.js`). Con Baileys, una persona recibe mensajes si (1) le
  escribió primero al número de la clínica hace menos de 365 días o (2) la API manda `optIn:true`. Y **BAJA/STOP gana sobre todo**, hasta
  que escriba ALTA. Sin ninguna de las dos constancias el servicio contesta `403 sin_consentimiento`. Lo que falta es del lado de la API.
- **La API no manda `optIn`.** `CanalWhatsAppWebJs` manda `{ telefono, texto }`; `IMessageChannel.EnviarAsync(telefono, texto)` no tiene
  por dónde pasarlo. Y `NotificadorTurnos.EnviarChat` le manda WhatsApp a **cualquier paciente con teléfono cargado**.
- **Ya hay dos mecanismos de consentimiento, y uno sirve de modelo:**
  - `Consentimiento` + `TipoConsentimiento` (RN-02, telemedicina): una fila por firma, `Estado` Activo/Inactivo, `FechaFirma` en hora
    del servidor, `Version` de 20 caracteres. **Revocar solo cambia el estado: no guarda cuándo.**
  - `ConsentimientoVideo` (PR 6, emociones): una fila por **decisión**, con versión del texto, **SHA-256 del texto exacto que se mostró**,
    quién decidió, fechas en UTC y `RevocadoUtc`. El texto lo sirve la API (archivo embebido por versión, `v0-provisorio`), y el
    servicio de reglas es testeable en memoria. Es el mecanismo más nuevo y el más auditable del proyecto.
- **El teléfono del paciente es texto libre y no se valida en ningún lado** (`POST /pacientes` y `PUT /pacientes/{id}` lo guardan tal
  cual). `Telefono.Normalizar` se queda con los últimos 10 dígitos: sirve para emparejar, pero un celular escrito "011 15 5555-0001"
  queda como `1555550001` (no es el número). Con eso se le podría escribir a un tercero.
- **El registro** es público (`POST /pacientes`, sin sesión): `RegistroPacienteRequest` no tiene nada de consentimiento. La pantalla
  (`Pantalla_Usuario/Usuario/registro.html`) viene después del "Acuerdo de atención al paciente" (que no habla de WhatsApp). El
  **perfil** es la sección `perfil` de `Pantalla_Paciente/js/dashboard.js` (`renderPerfil`), con teléfono editable.
- **Los textos que salen por WhatsApp son de la API**, no del servicio (el servicio no tiene plantillas): `NotificadorTurnos` (avisos de
  turno y recordatorio, que **nombran al profesional** y la modalidad) y `BotReservasService` (la conversación que inicia el paciente).
  Las "respuestas rápidas" del chat (las manda una persona del equipo) no contienen nada clínico.
- **Tamaño de la base:** el esquema consolidado tiene 44 tablas y "las 15 migraciones": agregar una tabla cambia esas cuentas en
  `crear-base-de-pruebas.ps1` y en varios documentos.

## Decisiones tomadas (para revisar)

### 1. Modelo: una tabla nueva `ConsentimientoWhatsApp`, a imagen de `ConsentimientoVideo`

| | Columnas en `Paciente` | Reusar `Consentimiento` + `TipoConsentimiento` | **Tabla nueva (elegida)** |
|---|---|---|---|
| Si aceptó, cuándo, versión del texto | sí | sí (`Version` de 20 caracteres, hora del servidor) | sí |
| **Cuándo revocó** | sí | **no**: `Baja` solo pone `Estado='Inactivo'`; hay que agregar una columna a una tabla vieja | sí (`RevocadoUtc`) |
| Historial (aceptó, revocó, volvió a aceptar) | **no**: cada cambio pisa el anterior | sí | sí |
| Qué texto exacto vio | no | no | sí (versión + SHA-256) |
| Quién lo hizo / para qué número | no | no | sí (`IdUsuario`, `Telefono`) |
| Qué toca de lo que ya existe | `ALTER` de `Paciente` (el `UPDATE` de `PacienteDAO.Modificar` y los 4 caminos que la escriben) | `ALTER` de una tabla con historia (`FechaFirma` pasó de `DATE` a `DATETIME`), y los `/consentimientos` genéricos del Configurador mostrarían filas con otro significado | **nada**: solo `CREATE TABLE` |

Elegí la tabla nueva por las dos cosas que se pidieron: **lo menos invasivo** (no cambia ninguna tabla, consulta ni endpoint existente;
la migración solo agrega) y **lo más auditable** (cada aceptación y cada revocación quedan, con el texto exacto y el número). Además
sigue la convención que ya revisó el equipo para `ConsentimientoVideo`. Una fila es una aceptación; se edita **solo** para revocarla;
volver a aceptar es otra fila. El estado actual es el de la fila más nueva del paciente. Un índice único filtrado garantiza que no pueda
haber dos aceptaciones abiertas del mismo paciente (un doble clic no duplica nada).

### 2. Qué manda la API al servicio de WhatsApp

| Estado del paciente | Qué hace la API con un aviso | Por qué |
|---|---|---|
| **Aceptación vigente** (misma versión del texto y mismo teléfono) | manda `optIn: true` | es el consentimiento explícito |
| **Revocó** (la decisión más nueva es una revocación) | **no manda nada por WhatsApp** (el aviso queda "Omitido") | pedido explícito: si revocó, no se envía nada |
| **Sin decisión** (todos los pacientes de hoy) | manda sin `optIn`; el servicio decide (solo si le escribió primero) y su rechazo (`sin_consentimiento`, `baja`) queda "Omitido" y no se reintenta | no cambia lo de hoy: la opción B depende de esto |
| Aceptó, pero **cambió el teléfono** o **cambió la versión del texto** | igual que "sin decisión" hasta que vuelva a aceptar | la aceptación fue para ese número y ese texto |

La conversación que **inicia el paciente** (el bot, la bandeja del equipo) no pasa por esta regla: es una respuesta a un mensaje suyo.

### 3. BAJA llega a la base

El texto de consentimiento le promete al paciente que puede responder BAJA. El servicio de WhatsApp ya lo respeta (BAJA gana sobre todo),
pero **no se lo cuenta a la API**, así que el perfil seguiría diciendo "activado". Ahora, cuando llega el mensaje BAJA/STOP... por el
webhook, la API **revoca las aceptaciones vigentes de ese teléfono** (motivo `baja-whatsapp`) y no contesta (el servicio bloquearía la
respuesta de todos modos). Las palabras son las del servicio, y una prueba compara las dos listas.

### 4. Teléfono de Argentina

Un validador nuevo (`TelefonoArgentino`) acepta las formas habituales (`+54 9 11 5555-0001`, `011 15 5555-0001`, `0351 15 412-3456`,
`11 5555-0001`...) y devuelve el **número nacional de 10 dígitos** (código de área + abonado), o nada si no es un teléfono argentino
posible. Sin ese número válido no se puede aceptar. `Telefono.Normalizar` (el de siempre, para emparejar conversaciones) no se toca.

### 5. Menores

Mismo criterio provisorio que el consentimiento de video: si se sabe que es menor de 18 años (fecha de nacimiento, o tiene tutor
cargado), **no puede aceptar por sí mismo**; tendría que hacerlo su madre, padre o tutor. Sin fecha de nacimiento se deja aceptar (la
fecha es obligatoria en el registro de la pantalla). **Lo tiene que validar el equipo legal.**

## Etapa 1: el modelo de datos

### Qué quedó hecho

- **`migrations/add_consentimiento_whatsapp.sql`** (la migración 16): crea `ConsentimientoWhatsApp` con `IdPaciente`, `IdUsuario` (quién lo
  dio), `Telefono` (el número para el que se aceptó, 10 dígitos), `Origen` (`registro` | `perfil`), `VersionTexto` y `HashTexto` (el SHA-256
  del texto exacto que se mostró), `AceptadoUtc`, `RevocadoUtc` y `MotivoRevocacion` (`paciente`, `baja-whatsapp`, `telefono-cambiado`,
  `texto-nuevo`). Cuatro restricciones `CHECK`, dos claves foráneas (a `Paciente` y a `Usuario`), un **índice único filtrado** (a lo sumo
  una aceptación abierta por paciente) y uno de consulta. **Solo agrega**: no toca ninguna tabla existente. Idempotente (cada cambio va
  detrás de un `IF`) y con `SET QUOTED_IDENTIFIER ON` propio, igual que `add_link_publico_profesional.sql`.
- **`docs/produccion/01-produccion.sql`**: la misma tabla, índices y claves en su sección de cada cosa (ahora "las 16 migraciones"). Una
  base nueva ya la trae.
- **Cuentas que cambiaron:** `Scripts/crear-base-de-pruebas.ps1` espera **45 tablas** (eran 44) y los documentos que lo decían
  (`README.md` con la fila 16 de la tabla de migraciones, `CATALOGO_SCRIPTS_SQL.md`, `DESARROLLO_LOCAL.md`).
- **Un error del diseño que encontraron las pruebas:** la primera versión del `CHECK` de la revocación dejaba entrar una revocación **con
  fecha y sin motivo**. En SQL Server un `CHECK` se da por cumplido cuando su resultado es "desconocido", y `NULL IN (...)` lo es. Se
  corrigió exigiendo `MotivoRevocacion IS NOT NULL` en la rama de la revocación (en la migración y en el esquema consolidado) y quedó un
  comentario que explica por qué.

### Decisiones y límites

- **El estado no es una columna.** "Aceptó / revocó / sin decidir" se deduce de la fila más nueva del paciente (`RevocadoUtc` nulo o no).
  Así no hay dos lugares que puedan contradecirse.
- **`Telefono` guarda el número nacional de 10 dígitos** (no el texto libre del perfil): es lo que permite saber, sin hooks en cada lugar
  que edita el perfil, que **la aceptación fue para ESE número** y dejar de valer si el paciente lo cambia (etapa 2).
- **Fechas en UTC con `DATETIME2(3)`**, como `ConsentimientoVideo` (el resto del esquema usa `GETDATE()`).
- **La migración no se aplicó a ninguna base real.** Se probó solo en bases descartables que creé y borré.

### Pruebas de esta etapa

- `ConsentimientoWhatsAppEsquemaTests.cs`, **23 pruebas**: 5 sin base (la migración solo agrega y cada cambio está protegido por un `IF`;
  `QUOTED_IDENTIFIER ON` antes del índice filtrado; no toca otras tablas; **la migración y `01-produccion.sql` definen exactamente la
  misma tabla**, columnas, restricciones, índices y claves) y 18 con base (las columnas y su tipo; el índice único filtrado, el de consulta
  y las dos claves; cada valor inválido se rechaza; la revocación exige fecha y motivo juntos y no puede ser anterior a la aceptación;
  dos aceptaciones abiertas no entran pero una nueva después de revocar sí; la limpieza de las pruebas se lleva sus filas; **correr la
  migración sobre una base que ya la tiene no cambia nada**).
- **Verificación a mano** (script descartable): una base con el esquema **viejo** (el de `origin/main`, 44 tablas) + la migración: 45 tablas;
  una segunda corrida y una tercera **sin `-I`**, todas sin error y con la estructura idéntica; y esa estructura es **igual** a la de una
  base creada con el `01-produccion.sql` nuevo (10 columnas, 4 `CHECK`, 2 claves, 3 índices).
- **Mutaciones:** 19 cambios a propósito, **todos detectados**: 12 sobre los archivos SQL (sin `IF`, tipo distinto, falta una clave, un
  `UPDATE` o un `DROP` metidos, otra tabla tocada, sin `SET QUOTED_IDENTIFIER`...) y 7 sobre la base (sin el índice único, sin una clave,
  sin un `CHECK`, y el `CHECK` sin el `IS NOT NULL`, que reproduce el error de arriba).
- Corridas con la base `yosoy_test_wa` (descartable, 45 tablas, creada con `Scripts\crear-base-de-pruebas.ps1`).

## Etapa 2: la API

### Qué quedó hecho

- **Las reglas** (`Services/Mensajeria/ConsentimientoWhatsAppService.cs`), testeables en memoria como las del consentimiento de video:
  `Obtener` (el estado y el texto), `Aceptar`, `Revocar`, `Evaluar` (lo que usa el notificador) y `RegistrarBaja` (lo que usa el bot). Estados:
  `sin-decision`, `acepta`, `revocado`, `telefono-cambiado`, `texto-desactualizado`, `no-disponible`. Acceso a datos en
  `DAOs/ConsentimientoWhatsAppDAO.cs` (aceptar es **una sola transacción**: cierra la abierta y abre la nueva; el índice único resuelve las
  carreras) y auditoría en `LogAuditoria` (solo ids y códigos, nunca el teléfono ni el texto).
- **El texto**, servido por la API y versionado como el de video: `Services/Mensajeria/Consentimiento/v0-provisorio.txt` (embebido), con su
  SHA-256 en cada aceptación, `Mensajeria:ConsentimientoWhatsApp:VersionTexto` para elegir la versión y una prueba que traba el hash de la
  versión publicada. Marcado **provisorio**, con los datos de contacto y las referencias legales entre corchetes.
- **Endpoints** (`Endpoints/ConsentimientoWhatsAppEndpoints.cs`, en `docs/MATRIZ_ENDPOINTS.md`): `GET /consentimiento-whatsapp/texto` (público,
  para el registro), `GET /consentimiento-whatsapp`, `POST /consentimiento-whatsapp/aceptar` y `POST /consentimiento-whatsapp/revocar`. Quién es
  sale **solo del JWT** (no hay ids en la URL ni en el cuerpo): un profesional o el Configurador reciben 403.
- **Registro** (`POST /pacientes`): `aceptaWhatsApp` y `versionTextoWhatsApp`, nunca aceptado por defecto (ausente, `null` o `false` es "no"; un
  texto como `"true"` es 400). Se valida **antes de crear la cuenta** (celular válido, mayor de edad, texto vigente): si algo no sirve, no
  se crea ni la cuenta. La aceptación queda a nombre del usuario recién creado, con origen `registro`, y la respuesta dice
  `consentimientoWhatsApp: { pedido, estado }`.
- **Teléfono argentino** (`TelefonoArgentino`): `+54 9 11 5555-0001`, `011 15 5555-0001`, `0351 15 412-3456`, `02966 15 123456`... → número
  nacional de 10 dígitos; sin código de área, de otro país o con letras → no vale. El aviso también va a ese número (antes, con un `15`, los
  últimos 10 dígitos eran **otro** número).
- **Lo que sale hacia el servicio de WhatsApp** (`IMessageChannel` con `OpcionesEnvio`, `CanalWhatsAppWebJs`): `"optIn": true` solo con una
  aceptación vigente (nunca `optIn:false`); si el paciente **revocó**, `NotificadorTurnos` no manda nada ni arma la conversación; el rechazo del
  servicio por falta de consentimiento o por BAJA (`sin_consentimiento`, `baja`) queda **"Omitido"**, no "Error", así no se reintenta.
- **BAJA** (`ComandoWhatsApp` + `BotReservasService`): el mensaje entero `BAJA`, `STOP`, `no molestar`... revoca las aceptaciones abiertas de ese
  número (motivo `baja-whatsapp`, auditado sin usuario) y el bot no contesta. Las palabras son las del servicio.

### Decisiones y límites

- **Los pacientes que nunca decidieron siguen como hoy** (son todos los existentes): el aviso sale sin `optIn` y el servicio decide, o sea que
  reciben solo si le escribieron primero al número (la opción B del link `wa.me` sigue funcionando así). Si el servicio lo rechaza queda
  "Omitido". **Límite conocido:** para esos pacientes se arma igual la conversación y queda un mensaje saliente en "Error" en la bandeja del
  equipo, aunque el servicio lo rechace. Se evitaría mirando en la API si escribieron primero, pero eso duplicaría la regla del servicio (con
  su propio plazo) y arriesga dejar de avisar a quien sí debería recibir; no lo hice. Queda como mejora si molesta en la bandeja.
- **Aceptación vigente = este número + este texto.** Si cambia el teléfono (en el perfil, o lo cambia el Configurador), o se publica un texto
  nuevo (otra `VersionTexto`), el paciente vuelve a "sin decisión" para los avisos hasta que acepte de nuevo; la fila vieja se cierra en ese
  paso (`telefono-cambiado` / `texto-nuevo`). Cambiar la versión del texto **detiene los avisos por WhatsApp de todos hasta que cada uno vuelva
  a aceptar**: es lo que corresponde cuando el texto provisorio se reemplace por el validado, pero hay que saberlo antes de hacerlo.
- **Después de una BAJA hace falta aceptar de nuevo Y escribir ALTA.** El servicio de WhatsApp mantiene la baja hasta que la persona escriba
  ALTA (decisión de la tarea anterior: BAJA gana sobre todo, aunque la API mande `optIn`). El texto del consentimiento lo dice. No dejé que una
  aceptación nueva levante la baja del servicio: si la API se perdió la BAJA por una caída, esa baja es la última red para no escribirle a
  quien pidió que no.
- **Revocar desde el perfil solo existe si hay una aceptación.** Quien nunca aceptó pero recibe avisos porque le escribió al número primero se
  da de baja respondiendo BAJA (lo dice la pantalla). No inventé una "revocación sin aceptación": ensuciaría el registro con una aceptación que
  nunca existió.
- **El número no se verifica.** Quien se registra con el celular de otra persona y tilda la casilla puede hacer que le escriban a un tercero (hoy
  pasa igual con cualquier paciente con teléfono cargado, pero ahora hay una constancia que dice lo contrario). Mitigaciones que hay: los
  mensajes son solo día y hora (etapa 5) y BAJA funciona desde ese teléfono. **Lo correcto sería un doble opt-in** (que escriba desde ese
  número, o un código): ver "Qué falta".
- **La validación del teléfono es solo para el consentimiento.** `POST /pacientes` y `PUT /pacientes/{id}` siguen guardando el texto libre (cambiar
  eso es otra decisión: hay pacientes con números del exterior o sin código de área).
- **Un número, una baja para todos:** si dos pacientes comparten celular (una familia), la BAJA revoca a los dos.
- **Menores:** mismo criterio provisorio que el video (menor de 18 o con tutor cargado = no acepta solo). Lo tiene que validar el equipo legal.
- **Sin base de datos o sin la migración**, los avisos de WhatsApp quedan en "Error" (no salen) y los demás canales siguen: se prefiere no
  mandar a mandar sin saber si hay consentimiento. Por eso la migración va **antes** que la API en `DEPLOY.md`.

### Pruebas de esta etapa

- **270 pruebas nuevas** (la suite de la API pasó de 1021 a 1291, todas verdes contra una base descartable con el esquema nuevo):
  `TelefonoArgentinoTests` (formas válidas y no válidas, entradas gigantes, y el caso del `15`), `ComandoWhatsAppTests` (la BAJA y, **con el
  código real del servicio de WhatsApp** corrido con node sobre 87 textos raros, que las dos coinciden), `TextoConsentimientoWhatsAppTests`,
  `ConsentimientoWhatsAppServiceTests` (los estados, el teléfono, el texto, los menores, quién puede qué, las carreras, la BAJA y que el
  log y la auditoría no traigan el teléfono ni el texto), `ConsentimientoWhatsAppEnviosTests` (notificador, canal HTTP, servicio de mensajes y
  bot), `ConsentimientoWhatsAppEndpointsTests` (la aplicación real con JWT: 401, 403, el cuerpo que no puede decir de quién es, 409, 429),
  `ConsentimientoWhatsAppBaseRealTests` (el DAO contra la base: reemplazo atómico, 12 pedidos a la vez; el registro con y sin casilla; el
  perfil de punta a punta con cambio de teléfono; la BAJA por el webhook) y `CanalWhatsAppInteropNodeTests` (lo que manda la API lo recibe el
  `crearApp` real del servicio: `optIn` true solo cuando corresponde, y el código de rechazo vuelve).
- `whatsapp-service`: `npm test` sin cambios (388, 382 pasan, 6 saltadas de POSIX): el servicio ya entendía `optIn` y BAJA.

## Pasos manuales para Agustín

(Se completa etapa por etapa. Lo que ya se sabe:)

1. **Validación legal del texto** `v0-provisorio` antes de usarlo con pacientes reales. Un texto nuevo es una versión nueva (archivo
   nuevo y `Mensajeria__ConsentimientoWhatsApp__VersionTexto`): **al cambiar la versión, los avisos por WhatsApp se detienen hasta que
   cada paciente vuelva a aceptar**. Es a propósito.
2. **Aplicar `migrations/add_consentimiento_whatsapp.sql`** a la base `yosoy` **antes** de publicar la API de esta rama (ver `DEPLOY.md`).
3. En la PC: la `yosoy_test` de siempre necesita esa migración (o recrearla con el esquema nuevo) para que la suite pase.

## Qué falta / pendiente de decisión

(Se completa etapa por etapa.)
