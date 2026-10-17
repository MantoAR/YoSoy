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
- **Última actualización:** 2026-10-17, al arrancar (lectura del código y diseño; todavía no hay código).

## Estado de las etapas

| Etapa | Qué | Estado | Commit |
|---|---|---|---|
| — | Este archivo (plan, hallazgos y decisiones) | en curso | |
| 1 | Modelo de datos: tabla, migración idempotente, `01-produccion.sql` | pendiente | |
| 2 | API: aceptar/revocar, `optIn` solo con consentimiento vigente, teléfono argentino, BAJA | pendiente | |
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

## Pasos manuales para Agustín

(Se completa etapa por etapa. Lo que ya se sabe:)

1. **Validación legal del texto** `v0-provisorio` antes de usarlo con pacientes reales. Un texto nuevo es una versión nueva (archivo
   nuevo y `Mensajeria__ConsentimientoWhatsApp__VersionTexto`): **al cambiar la versión, los avisos por WhatsApp se detienen hasta que
   cada paciente vuelva a aceptar**. Es a propósito.
2. **Aplicar `migrations/add_consentimiento_whatsapp.sql`** a la base `yosoy` **antes** de publicar la API de esta rama (ver `DEPLOY.md`).
3. En la PC: la `yosoy_test` de siempre necesita esa migración (o recrearla con el esquema nuevo) para que la suite pase.

## Qué falta / pendiente de decisión

(Se completa etapa por etapa.)
