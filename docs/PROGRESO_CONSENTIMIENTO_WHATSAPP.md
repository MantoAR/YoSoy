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
- **Última actualización:** 2026-10-17, tras la etapa 3 (las pantallas).

## Estado de las etapas

| Etapa | Qué | Estado | Commit |
|---|---|---|---|
| — | Este archivo (plan, hallazgos y decisiones) | hecho | `2cb6615` |
| 1 | Modelo de datos: tabla, migración idempotente, `01-produccion.sql` | **hecho** | `2c79f5a` |
| 2 | API: aceptar/revocar, `optIn` solo con consentimiento vigente, teléfono argentino, BAJA | **hecho** | `8f1335b` |
| 3 | Pantallas: casilla sin tildar en el registro, activar/revocar en el perfil | **hecho** | `766e53a` |
| 4 | Link `wa.me` (opción B) configurable | **hecho** | (este commit) |
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
- **Mutaciones** (65 cambios a propósito en el código de esta etapa, corridos sobre una copia del repo contra las pruebas que los tienen que
  notar): 60 detectadas de entrada y 5 que se escaparon. Cuatro eran huecos de las pruebas y se cerraron (un número de otro país que suma 10
  dígitos, letras junto a un número válido, un profesional o el Configurador con un id de paciente en la identidad, y la BAJA con algo que no
  es un teléfono llegando a la base); se repitieron y quedan **detectadas**. La quinta (`D5`: la rama que atrapa la violación del índice
  único en `ConsentimientoWhatsAppDAO.Aceptar`) **no se puede provocar de forma determinista desde una prueba**: solo ocurre si dos pedidos
  caen en los microsegundos entre el `UPDATE` y el `INSERT` de la misma transacción (cualquier otra secuencia termina bien y sin error). Queda
  cubierta por lo que importa: la prueba de 12 pedidos a la vez falla si esa violación sale sin atrapar, y las reglas se prueban contra un
  repositorio que simula perder la carrera (`null`).
- **Un defecto real que salió de ahí:** `\d` de .NET acepta cualquier dígito Unicode (ancho completo, arábigos, devanagari), así que
  `11` + 8 dígitos de ancho completo pasaba por teléfono argentino y llegaba así al servicio de WhatsApp y a una columna `VARCHAR(10)`.
  `TelefonoArgentino` (y el armado del link `wa.me` de la etapa 4) ahora aceptan solo `0-9`; la prueba nueva falla con el código de la etapa 2
  tal como estaba commiteado.

## Etapa 3: las pantallas

### Qué quedó hecho

- **Módulo compartido** `PANTALLAS/shared/consentimiento-whatsapp.js` (`YS.whatsapp`), que cargan el registro y el panel del paciente: trae el
  texto vigente (`GET /consentimiento-whatsapp/texto`, sin sesión), lo dibuja (etiqueta **"Texto provisorio"**, título, párrafos y "Versión
  del texto: …"), traduce los motivos que manda la API (menor de edad, teléfono que no sirve) y calcula la edad. Todo entra por
  `textContent`: **nada de lo que manda el servidor se interpreta como HTML**.
- **Registro** (`Pantalla_Usuario/Usuario/registro.html` + `js/app.js`): un bloque "Avisos por WhatsApp (opcional)" con el texto de la API y una
  casilla **sin tildar** (sin `checked`, sin `required`, con `autocomplete="off"`, y destildada por código al cargar y al dibujar el texto: ni
  el navegador ni nada la deja tildada). El bloque aparece solo si la API entregó el texto; si no, el registro anda igual que antes.
  Al enviar, `aceptaWhatsApp` es **lo que la persona tildó** y `versionTextoWhatsApp` la versión del texto que vio. Si tildó y no cargó
  teléfono, marca el campo; si es menor de 18, le explica que lo tiene que activar su madre, padre o tutor y que puede registrarse sin
  tildar. El resto (celular válido, edad, texto vigente) lo decide el servidor **antes de crear la cuenta**. La pantalla final confirma
  "Activaste los avisos…" o, si no se pudo guardar, que los active desde su perfil.
- **Perfil del paciente** (`Pantalla_Paciente/dashboard.html`, `js/dashboard.js`, `css/dashboard.css`): una tarjeta "Avisos por WhatsApp" debajo
  de los datos personales, con el estado como chip (**Activados / Pausados / Desactivados**) y una frase que lo explica. El texto está a la
  vista cuando hay que decidir y plegado ("Ver el texto que aceptaste") si ya está activo. Activar = **casilla sin tildar + botón deshabilitado
  hasta tildar**; manda la versión que leyó. Desactivar pide confirmación. Si cambia el teléfono en el mismo perfil, la tarjeta pasa sola a
  "Pausados" y el botón dice "Activar para este número". Si alguien se dio de baja por WhatsApp, la tarjeta le avisa que además de activarlos
  acá tiene que responder ALTA. Si no se puede aceptar (teléfono que no sirve, menor de edad), no se ofrece la casilla y se dice por qué;
  desactivar siempre se puede.
- Estilos en `PANTALLAS/shared/tema.css` y `dashboard.css` (clases `wa-*`), con el mismo lenguaje visual del resto (chips, `.ui-check`, `.ui-alerta`).

### Decisiones y límites

- **El texto no está copiado en ninguna pantalla**: se lee de la API, así que lo que se ve es lo que queda registrado (versión + SHA-256). Una
  prueba busca los párrafos del texto en todo `PANTALLAS/` para que nadie lo copie "para que cargue más rápido".
- **El navegador no guarda ningún consentimiento** (ni `localStorage` ni `sessionStorage`): el estado es siempre el del servidor. La única cosa
  que va a poder recordar es una molestia ("ahora no" a una sugerencia), nunca una aceptación.
- **Sin texto, no se ofrece nada** (el servidor no lo tiene, no contesta, o no está la tabla): no se muestra una casilla de algo que no se
  podría registrar. El registro sigue.
- **La pantalla valida lo mínimo** (teléfono vacío, menor de edad) para no hacer un viaje inútil; **la decisión es del servidor**, que vuelve a
  validar todo. Por eso un pedido armado a mano igual no se salta nada.
- **No se tocó el flujo de registro de siempre** (acuerdo de atención, tutor, validaciones): el bloque va antes de "Acceso" y no cambia nada si no
  se usa. PC primero: no se invirtió en pantallas angostas (la app mobile es aparte).
- **Texto provisorio a la vista.** Mientras el texto sea `v0-provisorio` la etiqueta "Texto provisorio" se ve en ambas pantallas. Se va sola
  cuando se publique una versión sin ese marcador (ver "Pasos manuales").

### Pruebas de esta etapa

- La suite de la API pasó de 1291 a **1329 pruebas, todas verdes** contra la base descartable `yosoy_test_wa`: 14 son las estáticas de las
  pantallas (abajo) y 24 cierran los huecos que mostraron las mutaciones de la etapa 2 (ver ahí).
- **Banco de navegador real** (Chrome por Playwright, la API real contra una base descartable; **no se versiona**, vive fuera del repo): 74
  verificaciones, 0 fallas. Cubre: la casilla nunca viene tildada (ni tras tildar y recargar), el texto de la API se ve entero, sin tildar no se
  crea ninguna fila, con teléfono inválido el servidor rechaza y **no se crea ni la cuenta**, sin teléfono y menor de edad no sale ni el pedido, con
  un celular válido (con el 15) queda **una** fila con el número normalizado, origen `registro` y el hash de 64; en el perfil, los tres estados,
  activar solo con el tilde, desactivar con confirmación ("Volver" no cambia nada), reactivar deja el historial (dos filas), cambiar el teléfono
  pasa a "Pausados" sin recargar y cierra la fila anterior por `telefono-cambiado`, un teléfono inválido explica el motivo, cada paciente ve
  lo suyo; **lo que el servidor mande con HTML (en el registro y en el perfil) se ve como texto y no ejecuta nada**; y el log de la API no trae
  ningún teléfono.
- **`PantallasConsentimientoWhatsAppTests`** (14 pruebas estáticas, leen los archivos de `PANTALLAS/`): casilla sin `checked`/`required` y con
  `autocomplete="off"`, orden de los `<script>`, el registro destilda y manda solo lo tildado, nada tilda la casilla por su cuenta
  (`.checked = true`, `defaultChecked`…), el perfil activa solo con el tilde y la versión leída, nada de lo que manda el servidor entra como
  HTML, el texto no está copiado en ninguna pantalla, y el consentimiento no se guarda en el navegador.
- **Mutaciones de la interfaz** (18 cambios a propósito en `registro.html`, `app.js`, `dashboard.js` y el módulo, con el banco de navegador
  como juez): el banco detectó 14 de entrada; tres se le escaparon (la casilla con `checked` en el HTML, el bloque sin chequear que haya
  texto, el texto insertado como HTML) y una no la pude correr por un error de mi script. Se agregaron al banco los escenarios "el servidor
  no tiene texto" y "el servidor manda HTML" y las pruebas estáticas: **las 18 quedan detectadas**. La del `checked` solo la ve la prueba
  estática: el código destilda la casilla igual, así que en el navegador no se nota, y justamente por eso conviene que no se pueda colar.

## Etapa 4: el link `wa.me` (opción B)

### Qué quedó hecho

- **Configuración** (`Services/Mensajeria/MensajeriaOptions.cs`, `appsettings.json`, fila nueva en el README): `Mensajeria__WhatsAppNumeroClinica`
  (**vacío por defecto = no se muestra ningún link**) y `Mensajeria__WhatsAppTextoInicio` (por defecto `Hola`: lo que viene escrito al abrir
  el link, neutro a propósito, no dice qué es la clínica ni para qué se escribe). El número se escribe como quiera quien lo configure
  (`+54 9 11 5555-0000`, `011 5555-0000`…): un argentino queda `549` + área + abonado, que es lo que espera `wa.me`; uno de otro país solo
  vale con `+`; con letras, símbolos o dígitos que no son ASCII no hay link (y queda **un** aviso en el log, sin el valor).
- **La API arma el link** (`Services/Mensajeria/EnlaceWhatsApp.cs`): `GET /consentimiento-whatsapp` y las respuestas de aceptar y revocar traen
  `contacto: { enlace }`, o `contacto: null` si no hay número. El texto de inicio va codificado, sin caracteres de control y con un tope.
  **Las pantallas no escriben ningún número ni arman ningún link**: lo muestran tal cual lo manda la API, y solo si es `https://wa.me/…` (sin
  credenciales). Una prueba busca `wa.me/` en todo `PANTALLAS/` y en el código.
- **Perfil del paciente:** bajo la casilla de activar, "Si preferís, escribinos vos primero por WhatsApp: el chat lo abrís vos y te
  contestamos por ahí" y el botón **Abrir WhatsApp** (pestaña nueva, `noopener noreferrer`; WhatsApp se abre con el saludo ya escrito y no se
  manda nada hasta que lo envía la persona). Se ofrece donde se pueden activar los avisos (sin decisión, pausados por cambio de teléfono o de
  texto) y **no** a quien los desactivó, ni a quien ya los tiene, ni cuando no se pueden activar (menor de edad, teléfono que no sirve).
- **Después de pedir o confirmar un turno** (`confirmarSolicitudDia` y `confirmarTurnoPaciente` del panel): si la persona **nunca decidió** sobre
  los avisos, un diálogo corto ("el día y la hora, nada más", con el link si hay número) con **Ahora no** y **Ver cómo activarlos** (lleva al
  perfil: ahí se lee el texto y se tilda; desde el diálogo **no se activa nada**). "Ahora no" o cerrarlo lo posterga 30 días. No sale para quien
  ya activó, desactivó o tiene los avisos en pausa.

### Decisiones y límites

- **El link no es una aceptación y no deja nada en `ConsentimientoWhatsApp`.** Cuando la persona le escribe primero al número, `whatsapp-service` aplica
  su regla "escribió primero" (365 días; la BAJA siempre gana) y recién ahí puede contestarle y mandarle avisos. Es el camino B de la
  decisión de producto, y anda sin la tabla ni la migración. **Lo que hay que validar con el equipo legal:** si ese camino necesita su propio texto
  (hoy el texto del consentimiento no lo menciona, porque el link no consiente nada: solo abre el chat).
- **Tiene que ser el número que vincula Baileys** (un celular, el dedicado a la clínica). La API no puede comprobarlo: si se carga otro, el link
  abre un chat con otro número.
- **Sin número, sin link y sin rastros**: no se pide nada ni se rompe nada; las pantallas no muestran el bloque ni el diálogo muestra el link.
- **El registro no muestra el link**: la persona todavía no tiene sesión y el link llega con la respuesta autenticada. Se lo ve en el perfil y
  después de su primer turno.
- **Lo único que se guarda en el navegador es la fecha de "Ahora no"** (30 días, clave `ys.wa.ahoraNo`): una molestia, nunca una aceptación.
  Una prueba estática lo vigila.
- **El límite de la API** (30 pedidos por minuto por IP a `/consentimiento-whatsapp`) alcanza para una persona; una oficina entera detrás de
  la misma IP también. Si una pantalla recibe un 429 muestra "No pudimos consultar tus avisos por WhatsApp" con un botón Reintentar.

### Pruebas de esta etapa

- La suite de la API pasó de 1329 a **1389 pruebas, todas verdes** contra `yosoy_test_wa`: 57 de `EnlaceWhatsAppTests` (los formatos de número, uno
  de otro país solo con `+`, letras y dígitos que no son ASCII, el texto de inicio codificado y con tope, la configuración vacía o inválida, y
  los endpoints con la aplicación real: con número, sin número, con un número que no sirve y que el log no lo traiga) y 3 estáticas más de las
  pantallas (el link lo arma la API y solo se muestra si es de WhatsApp, dónde se ofrece, y que la sugerencia posterior a un turno nunca acepta
  nada). Una prueba busca `wa.me/` con un número escrito en cualquier archivo del producto, y otra en `PANTALLAS/`.
- **Banco de navegador real** (Chrome, la API real; sin versionar), dos corridas: **con número de la clínica** configurado (110 verificaciones, 0
  fallas) y **sin número** (107, 0 fallas). Cubre: el link exacto que arma la API (`https://wa.me/5491155550100?text=Hola`, pestaña nueva y sin
  opener), que aparece en "sin decisión" y en "pausados" y **no** en activados, desactivados por la persona ni en un teléfono que no sirve, que un
  "link" que no es de WhatsApp (otro sitio, `javascript:`, `http`, con usuario y clave, sin host, un número) **no se muestra** aunque la API lo
  mande, y la sugerencia de después de un turno: aparece solo a quien nunca decidió, cuenta que es "el día y la hora, nada más", no registra ni
  activa nada, "Ahora no" y Escape la posponen, vuelve a los 30 días, "Ver cómo activarlos" lleva al perfil con la casilla sin tildar. Sin número,
  ningún link en ninguna pantalla. El banco manda cada pedido a esos endpoints con otra IP en `X-Forwarded-For` (la API la toma de la propia
  máquina, como detrás de Caddy) para no chocar con el límite de 30 por minuto, que prueban las pruebas de la API.
- **Mutaciones de la interfaz** (15: el link a quien desactivó, un link de cualquier sitio o con `http` o con usuario y clave, sin `noopener`,
  en la misma pestaña, la sugerencia para cualquier estado, "Ahora no" que no pospone o que lleva al perfil, la postergación que no vence, la
  sugerencia que lleva a otra sección o que activa directamente, que no se sugiera tras pedir o confirmar un turno, y un link escrito a mano):
  **las 15 detectadas** (12 por el banco de navegador y 3 por las pruebas estáticas).

## Pasos manuales para Agustín

(Se completa etapa por etapa. Lo que ya se sabe:)

1. **Validación legal del texto** `v0-provisorio` antes de usarlo con pacientes reales. Un texto nuevo es una versión nueva (archivo
   nuevo y `Mensajeria__ConsentimientoWhatsApp__VersionTexto`): **al cambiar la versión, los avisos por WhatsApp se detienen hasta que
   cada paciente vuelva a aceptar**. Es a propósito.
2. **Aplicar `migrations/add_consentimiento_whatsapp.sql`** a la base `yosoy` **antes** de publicar la API de esta rama (ver `DEPLOY.md`).
3. En la PC: la `yosoy_test` de siempre necesita esa migración (o recrearla con el esquema nuevo) para que la suite pase.

## Qué falta / pendiente de decisión

(Se completa etapa por etapa.)
