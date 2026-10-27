# Solicitudes de turno del paciente: las acepta el profesional

Un turno que el paciente pide por su cuenta (autoservicio, link público del profesional, bot de chat) **no es un turno confirmado**: es una **solicitud** que el profesional acepta o rechaza. Migración **36** (opcional): `migrations/add_solicitud_turno_vencimiento.sql` (deshacer: `docs/produccion/deshacer-migracion-36.sql`).

- Código: `Services/SolicitudesTurno.cs` (reglas + DAO), `Endpoints/SolicitudesTurnoEndpoints.cs`. La lista para la campanita sale de `GET /profesionales/{id}/seguimiento` (`Endpoints/AusenciasAltaEndpoints.cs`).

## Qué se encontró y qué cambió

* **Ya existía** el estado `Pendiente` en `EstadoTurno` y `TurnoReglasService.Solicitar` ya creaba el turno del paciente en ese estado (con `CreadoPor = 'paciente'`); el panel del profesional ya tenía aceptar / rechazar en el calendario. No se agregó ningún estado nuevo: «Pendiente de confirmación» **es** `Pendiente` + `CreadoPor = 'paciente'`. (`CreadoPor = 'profesional'` + `Pendiente` es otra cosa: el turno que el profesional le propone al paciente.)
* Lo que faltaba, y ahora está: el **aviso** al profesional (campanita con Aceptar / Rechazar), el **motivo** del rechazo y el aviso al paciente, el **vencimiento**, que el paciente lo vea como **«Esperando confirmación del profesional»** en vez de «Pendiente», y endpoints explícitos, auditados e idempotentes.
* No se pudo reproducir «queda agendado automáticamente» en el código: ningún camino de autoservicio pasa a `Confirmado` sin que alguien lo haga (se buscó `IdEstadoTurno = 2` y toda escritura de estado). Lo más probable es que en el calendario del paciente se viera como un turno más; por eso ahora dice «Esperando confirmación del profesional». Si en producción quedó realmente `Confirmado`, hay que revisar cómo se creó (¿recepción?, ¿una cita propuesta por el profesional?).

## Mientras está pendiente

* **Reserva el horario**: `InsertarSiLibre` cuenta los `Pendiente`; otro paciente recibe 409 y el horario no figura libre.
* **No habilita la sala** (`AccesoSalaService` exige `Confirmado`), **no cuenta** para ausencias (solo se marca ausente un `Confirmado`) **ni** para los recordatorios (que miran solo `Confirmado`).

## Endpoints

| Método y ruta | Quién | Qué hace |
|---|---|---|
| `GET /profesionales/{id}/solicitudes-turno` | el propio profesional | Las solicitudes que esperan su respuesta (futuras), con cuándo vencen. |
| `POST /turnos/{id}/aceptar-solicitud` | el profesional **dueño** del turno | `Pendiente → Confirmado` (y `ConfirmadoPaciente = 1`: lo pidió el paciente). Mismas reglas que siempre al confirmar: consentimiento (RN-02) y paciente derivado (409). Avisa al paciente (`TurnoConfirmado`: campanita y mail, una vez). Repetirlo → 200 `repetido: true`. |
| `POST /turnos/{id}/rechazar-solicitud` `{ motivo? }` | el profesional dueño | `Pendiente → Cancelado` (el horario se libera). Aviso al paciente con el motivo (opcional, hasta 300 caracteres) y la sugerencia de elegir otro horario. Repetirlo → 200 `repetido: true`. |

Un profesional ajeno recibe 404 (igual que un turno inexistente); un paciente o CRM, 403. Aceptar o rechazar algo que ya no es una solicitud pendiente: 409 (`solicitud-cerrada` si se rechazó, canceló o venció; `no-es-solicitud` si lo creó recepción o el profesional).

Todo en una transacción: se **lee antes de escribir** y la auditoría va al final (`LogAuditoria`: «Solicitud de turno aceptada / rechazada / vencida», solo ids; el motivo no se copia a la auditoría).

## Vencimiento

`ExpiraEn = min(creación + 24 hs, inicio del turno − 2 hs)`. Para que una solicitud hecha sobre la hora no nazca vencida: **nunca antes de 30 minutos** y **nunca después del inicio** del turno. Constantes en `SolicitudesTurnoReglas` (`VigenciaHoras`, `MargenAntesDelTurnoHoras`, `VigenciaMinimaMinutos`), con `// TODO-VALIDAR`. Se guarda en `Turno.SolicitudExpiraEn` al crear la solicitud.

`SolicitudesTurnoDAO.VencerSolicitudes(ahora)`: pasa a `Cancelado` las `Pendiente` + `paciente` con plazo cumplido, avisa al paciente (campanita) y al profesional (`solicitud-vencida`), y audita. **Idempotente**: cambia el estado solo si sigue `Pendiente` y el aviso sale en la misma transacción, así que correrlo varias veces no duplica nada. Se ejecuta (1) en la pasada del servicio de fondo que ya existe (`RecordatorioBackgroundService`, vía `ISeguimientoClinico`), (2) antes de calcular horarios libres y de crear una solicitud (así un horario vencido se libera), y (3) al consultar la campanita / lista y al aceptar o rechazar. Mismo patrón que el vencimiento de las propuestas de asignación automática (migración 32), sin reutilizar su tabla.

Sin la migración 36 las solicitudes siguen pendientes hasta que el profesional responda (no vencen).

## No requieren aceptación

Turnos creados por recepción o por el profesional (`CreadoPor = 'profesional'`), el turno que sale de aceptar la propuesta automática de admisión (nace `Confirmado`) y las reprogramaciones que inicia el profesional. `// TODO-VALIDAR`: hoy el paciente no tiene un camino propio para pedir una reprogramación; cuando lo tenga, tendría que dejar el turno pendiente (hoy no hay nada que cambiar).

## A validar

Cuánto tiempo tiene el profesional para responder (24 hs / 2 hs antes / piso de 30 minutos), y si el rechazo debería ofrecer horarios alternativos. El mail de rechazo / vencimiento no existe (solo campanita); el de aceptación es el «Turno confirmado» de siempre.
