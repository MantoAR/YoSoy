# Caminos que crean turnos, y «Agendar turno» del profesional

## «Agendar turno» (nuevo)

`POST /turnos/agendar` (`Endpoints/AgendarTurnoEndpoints.cs`, `TurnoReglasService.Agendar`), solo un profesional, para un paciente **de su cartera**, en un horario libre de **su propia agenda**. El turno nace **Confirmado** (`CreadoPor = 'profesional'`, `ConfirmadoPaciente = 1`), no requiere aceptación de nadie y el paciente recibe un aviso en la campanita («Tu profesional te agendó un turno…»). Auditado («Turno agendado por el profesional»). Reglas: horario dentro de la agenda y libre (RN-01), un turno por día con ese profesional (RN-07), paciente derivado a psiquiatría (409) y consentimiento firmado y vigente (RN-02: el turno queda confirmado). Un paciente ajeno recibe 403; un doble clic recibe 409 («Ya tenés un turno con ese profesional ese día») y no duplica el turno ni el aviso. En el panel: botón **Agendar turno** del Inicio del profesional (paciente → día → horario → motivo).

Antes, el profesional tenía **Cita urgente** (ahora mismo) y **proponer un turno** (queda Pendiente hasta que el paciente lo confirma), y ninguno agendaba un turno normal ya confirmado; en hoy mismo solo la cita urgente. Ahora también se puede para hoy, en un horario que todavía no pasó.

## Estado inicial de cada camino (probado contra los endpoints reales)

| Camino | Quién lo crea | Estado inicial | `CreadoPor` |
|---|---|---|---|
| Autoservicio del paciente (`POST /turnos/solicitar`) | Paciente | **Pendiente** (lo acepta el profesional) | paciente |
| Link público del profesional (`POST /public/profesionales/{slug}/reservar`) | Paciente | **Pendiente** | paciente |
| Bot de chat (`TurnoReglasService.Solicitar`) | Paciente (por chat) | **Pendiente** | paciente |
| Reprogramación pedida por el paciente (cancela y pide por `POST /turnos/solicitar`) | Paciente | **Pendiente** | paciente |
| Propuesta automática de admisión («Me sirve») | Paciente, sobre algo ya revisado por recepción | **Confirmado** | (propuesta) |
| Agendado por recepción (`POST /turnos/profesional-solicitar`, CRM con Gestión de agendas) | Recepción | Pendiente del paciente (propuesto) | profesional |
| Proponer un turno (`POST /turnos/profesional-solicitar`) | Profesional | Pendiente del paciente (propuesto) | profesional |
| Cita urgente (`profesional-solicitar` con `esUrgente`) | Profesional | Pendiente; el panel lo confirma en el momento y genera la sala | profesional |
| **Agendar turno** (`POST /turnos/agendar`) | Profesional | **Confirmado** | profesional |
| Primer turno de derivación de admisión | Recepción/Admisión | Pendiente del paciente (propuesto) | profesional |
| `POST /turnos` (camino viejo) | — | 410, no crea nada | — |

**Único camino que se saltaba la aceptación, corregido:** `PUT /turnos/{id}/confirmar-paciente` dejaba que el paciente confirmara su **propia** solicitud (la pantalla solo ofrecía el botón para turnos propuestos por el profesional, pero la API lo aceptaba). Ahora, si el turno lo creó el paciente, responde 409 («Tu solicitud todavía espera la aceptación de tu profesional»). El profesional sigue pudiendo confirmar por ahí (cita urgente) y por `POST /turnos/{id}/aceptar-solicitud`.
