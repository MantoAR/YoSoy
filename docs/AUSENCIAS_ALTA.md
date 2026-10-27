# Ausencias con umbrales y alta estructurada con plan de control

El sistema **detecta, avisa, propone y precarga; una persona con el rol correcto confirma**. Nunca marca solo a nadie como ausente ni en abandono, ni da el alta, ni cancela turnos. Todo cambio de estado: transaccional, auditado (quién, cuándo, qué) e idempotente; las notificaciones no se duplican. Migración **35** (opcional): `migrations/add_ausencias_alta.sql` (deshacer: `docs/produccion/deshacer-migracion-35.sql`).

- Código: `Services/Ausencias.cs`, `Services/AltaEstructurada.cs`, `Endpoints/AusenciasAltaEndpoints.cs`.

## Qué se encontró

* **No existía el estado «Ausente»** en `EstadoTurno` (solo Pendiente, Confirmado, Cancelado, Realizado): la migración 35 agrega la fila. Se resuelve siempre por descripción.
* **No existía un estado de abandono** para el paciente. `Caso.Estado` admitía Nuevo, Derivado, Asignado, En atención, En alta, Cerrado: la migración 35 agrega **`Abandono`** (el mínimo). El caso deja de contar en la carga del profesional igual que «En alta».
* El alta clínica (migración 27: informe de texto libre + plan numérico) **se extendió**, no se reemplazó: el camino viejo sigue andando.

## Ausencias

| Regla | Constante (`AusenciasReglas`, con `// TODO-VALIDAR`) |
|---|---|
| El profesional puede **marcar como ausente** pasados **15 minutos** del inicio sin que el paciente haya entrado a la sala | `ToleranciaMarcarMinutos = 15` |
| **Aviso temprano** (campanita del profesional) a los **10 minutos**, una sola vez por turno | `AvisoTempranoMinutos = 10` (vigente hasta 3 hs: `AvisoTempranoVigenciaHoras`) |
| **Alerta a la 2.ª ausencia consecutiva**: campanita al profesional y a recepción, con el nombre | `AusenciasParaAlerta = 2` |
| **Propuesta de abandono a la 3.ª** (NO es un abandono automático) | `AusenciasParaProponerAbandono = 3` |

* **«El paciente se conectó»** = el paciente **pidió entrar a la sala** de ese turno (`POST /video/acceso` con éxito). Se anota la primera vez en `TurnoConexion` (el acceso nunca falla por esto). No se tocó el `signaling-server`. Limitación: un paciente que obtiene el token pero nunca llega a conectar su video cuenta como conectado.
* **Consecutivas**: ausencias del paciente **con ese profesional**, de la más reciente hacia atrás, hasta la primera sesión **Realizada**; los turnos cancelados, pendientes o futuros no cortan ni suman (`// TODO-VALIDAR`). Una realizada entre medio reinicia el conteo.
* **Derivado, en alta y en abandono no generan alertas**: se puede marcar la ausencia igual (es un hecho), pero no hay aviso temprano, ni alerta de la 2.ª, ni propuesta.
* **Cómo se detecta sin servicios nuevos**: la API ya tiene un proceso en segundo plano (`RecordatorioBackgroundService`); su pasada llama a `ISeguimientoClinico.Pasada` (aviso temprano, controles cercanos, solicitudes vencidas). Además se evalúa de forma **perezosa** al consultar la campanita del profesional (`GET /profesionales/{id}/seguimiento`) y la sala consulta `GET /turnos/{id}/ausencia` cada 30 s. Cada aviso tiene una **clave única** (`AvisoSeguimiento`): se reserva la clave y se crea el aviso en la misma transacción, así que ninguna combinación de pasadas duplica una campanita.

### Endpoints

| Método y ruta | Quién | Qué hace |
|---|---|---|
| `GET /turnos/{id}/ausencia` | el profesional del turno | Estado para la sala: `puedeMarcar`, `avisoTemprano`, `pacienteConectado`, desde cuándo se habilita. |
| `POST /turnos/{id}/marcar-ausente` `{ confirma: true }` | el profesional del turno | `Confirmado → Ausente`. 409 antes de los 15 minutos (`demasiado-pronto`), si el paciente ya entró (`paciente-conectado`), si hay sesión guardada o si el turno no está Confirmado. Doble clic → `repetido`. |
| `POST /propuestas-abandono/{id}/confirmar` `{ confirma: true }` | el profesional dueño | El caso pasa a **Abandono**. **No cancela turnos**: se muestran en la confirmación y se avisa a recepción (campanita) con la cantidad para que decida. Si hubo una sesión realizada desde la propuesta, 409 `ya-no-corresponde`. |
| `POST /propuestas-abandono/{id}/descartar` `{ motivo }` | el profesional dueño | Descarta (motivo obligatorio, hasta 500). No cambia nada del paciente. |

Auditoría (`LogAuditoria`, solo ids): «Turno marcado como ausente», «Propuesta de abandono confirmada / descartada», «Caso pasa a Abandono». Un profesional ajeno recibe 404; paciente y CRM, 403.

## Alta estructurada con plan de control

* **Informe de alta con secciones**: motivo de consulta, diagnóstico o hipótesis, objetivos terapéuticos y grado de consecución, técnicas, estado al alta, recursos del paciente, recomendaciones y prevención de recaídas. Obligatorias (`// TODO-VALIDAR`): diagnóstico, objetivos, grado, estado al alta y recomendaciones.
* **Precarga**: `GET /casos/{id}/precarga-alta` trae el motivo de consulta y los objetivos del formulario «Sobre mí» y los controles sugeridos. Los datos personales del paciente **no** se copian a la historia clínica.
* **Se guarda ligado a la historia clínica y a la sesión de cierre**: una evolución en la historia clínica del paciente (diagnóstico, informe completo y recomendaciones), `AltaClinica.IdEvolucion`, y `SesionClinica.IdEvolucion` si la sesión no tenía otra. El JSON de las secciones queda en `AltaClinica.InformeEstructurado`. Nada se borra.
* **Plan de control**: por defecto **dos controles, a los 3 y a los 6 meses** (`ControlesPorDefectoMeses`). El profesional puede mover las fechas, quitar uno o los dos, o agregar otros (hasta 12, a lo sumo 2 años). Se crean como **recordatorios** (`ControlSeguimiento`), **no como turnos**. Cuando faltan **14 días o menos** (`AvisoControlDias`), aviso por la campanita al profesional y a recepción, **una sola vez** (el control pasa `Pendiente → Avisado` en la misma transacción); el profesional lo da por resuelto con «Ya lo coordiné» (`POST /controles/{id}/resolver`).
* Pedido: `POST /casos/{id}/alta-clinica` acepta `informeEstructurado` y `controles` (lista de `aaaa-mm-dd`). Sin ellos, anda como siempre.
* **Una persona confirma** (`confirma: true`), la transacción es única (alta + evolución + controles + caso a «En alta»), la auditoría va al final y repetirla no duplica nada.
* Un paciente «En alta» sigue visible en los listados (ya existía) y no genera alertas de ausencia. `// TODO-VALIDAR`: si un paciente en alta pide un turno nuevo, se deja el comportamiento actual (puede pedirlo).

## A validar

Umbrales (15/10/2, 3), qué cuenta como «conectado», qué turnos cortan las consecutivas, quién decide qué hacer con los turnos futuros de un paciente en abandono, secciones obligatorias del informe de alta, los controles de 3 y 6 meses y el aviso a 14 días, y el comportamiento de un paciente en alta que pide turno.
