# Paciente «Derivado» a psiquiatría

Desde el cierre de sesión, el profesional puede **derivar al paciente a psiquiatría**. Es **solo un cambio de estado que confirma una persona**: el sistema no crea tareas, no manda mails ni avisa a nadie de la derivación en sí. Un paciente derivado **no puede reservar ni recibir turnos** con este equipo hasta que recepción o CRM lo reabra.

- Código: `Services/DerivacionPsiquiatra.cs` (reglas + DAO), `Endpoints/DerivacionPsiquiatraEndpoints.cs`.
- Migración **34**: `migrations/add_derivado_psiquiatra.sql` (deshacer: `docs/produccion/deshacer-migracion-34.sql`). **Opcional**: sin ella nadie figura derivado y el botón responde «falta aplicar la migración 34»; los turnos no cambian.
- Pantalla: `PANTALLAS/Pantalla_Profesional/sesion_video.html` (botón «Derivar a psiquiatra» + ventana de confirmación).

## Cómo se guarda (y por qué no es `Caso.Estado`)

Tabla `DerivacionPsiquiatrica`: una fila por derivación (paciente, profesional, caso, sesión o turno de origen, quién, cuándo) y, si se reabrió, quién y cuándo. El paciente figura **Derivado** mientras tenga una fila **sin reapertura** (índice único filtrado: una vigente por paciente).

`Caso.Estado = 'Derivado'` ya existe con **otro significado** (el caso derivado por admisión a un profesional, esperando asignación): `AsignacionService` y `RevisionAdmision` lo tratan como «todavía sin profesional». Mezclar los dos habría roto ese circuito, así que el estado nuevo vive aparte y **no toca el caso**. Consecuencia buena: reabrir es marcar la fila; el caso nunca cambió, así que «vuelve a su estado anterior» sin tener que recordarlo.

## Endpoints

| Método y ruta | Quién | Qué hace |
|---|---|---|
| `POST /pacientes/{id}/derivar-psiquiatra` `{ confirma: true, idSesion?, idTurno? }` | Profesional **con el paciente a su cargo** | Deriva. Sin `confirma: true` → 400. Repetirlo (doble clic) → 200 `repetido: true`, sin escribir ni auditar de nuevo. |
| `GET /pacientes/{id}/derivacion-psiquiatra` | Profesional (el suyo), Admisión, CRM, auditor | Si figura derivado. |
| `POST /pacientes/{id}/reabrir-derivacion` `{ confirma: true }` | **Admisión o CRM** (recepción) | Reabre. Idempotente. El profesional **no** puede reabrir. |

Todo en una sola transacción; se **lee antes de escribir** y la auditoría va al final (la auditoría serializa las transacciones: ver `docs/ASIGNACION_AUTOMATICA.md`). Auditoría (`LogAuditoria`, solo ids): «Paciente derivado a psiquiatría» (paciente, profesional, sesión o turno, turnos futuros) y «Paciente derivado reabierto».

## Bloqueo de turnos (una sola regla, reutilizada)

`TurnoReglasService.ValidarPacienteNoDerivado` — mensaje: **«Este paciente fue derivado y no puede reservar turnos con este equipo.»** (409, código `paciente-derivado`). La usan:

- `ValidarSolicitud` → `Solicitar`: autoservicio del paciente, link público del profesional, bot, recepción / Configurador con agendas y el profesional proponiendo un turno.
- `SolicitarUrgente`: la cita urgente del profesional.
- `ValidarReprogramacion` (`PUT /turnos/{id}`): cambiar día, hora o profesional. Editar otros datos o cancelar **no** se frena.
- La **asignación automática** (`AsignacionAutomatica.cs`): no propone a un derivado (el formulario queda en «Asignaciones para resolver» con el motivo) y, si lo derivan después de la propuesta, confirmarla se rechaza, la propuesta se cancela y no se crea turno ni vínculo.

## Turnos que ya tenía

**No se cancelan solos.** Si tiene turnos por delante (Pendiente o Confirmado, de cualquier profesional), se avisa por la campanita al profesional que deriva, a cada profesional con turno y a recepción («… tiene N turnos por delante. No se cancelaron solos: decidí si corresponde cancelarlos.»). Una persona decide. Sin turnos por delante no se avisa a nadie.

## Qué se ve

- Cierre de sesión: botón **Derivar a psiquiatra** → ventana de confirmación que explica las consecuencias.
- «Mis pacientes» y «Mis casos» del profesional, y la lista de casos de recepción / CRM: distintivo **Derivado** (y **En alta**). Recepción / CRM ven en el detalle del caso el botón **Reabrir (volver al estado anterior)**.
- El tipo de cierre «Derivación» (derivación interna asistida: `Derivacion` / `CasoDerivacion`) **dejó de ser alcanzable desde el cierre**: el botón está oculto (`DERIVACION_INTERNA_ALCANZABLE = false`) y `seleccionarCierre` lo ignora aunque venga de un borrador. No se borró código ni tablas.

## A validar (`// TODO-VALIDAR`)

Qué significa clínicamente «derivado» (¿el profesional sigue viéndolo en sus listas?, ¿se quita de la carga?), si hace falta un motivo escrito al derivar y al reabrir, y qué hacer con los turnos que ya tenía. Hoy: solo el estado, la constancia y el bloqueo de turnos nuevos.
