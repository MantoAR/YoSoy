# Asignación automática de profesional y horario

Cuando la recepción **acepta** un formulario «Sobre mí», el sistema propone solo **un profesional y un horario** y el **paciente decide**. Es la pieza que faltaba entre «formulario aceptado» y «primer turno confirmado» en los primeros pasos (`docs/PRIMEROS_PASOS.md`).

Principio (no se negocia): **el sistema detecta, propone y precarga; una persona confirma.** Acá confirman dos personas, cada una con su acción explícita y auditada: la recepción (acepta el formulario) y el paciente (acepta la propuesta). El sistema nunca crea un vínculo paciente–profesional ni un turno por su cuenta.

- Código: `Services/AsignacionAutomatica.cs` (reglas de elección puras + servicio), `Endpoints/AsignacionAutomaticaEndpoints.cs`.
- Enganche: `RevisionAdmision.Aceptar` (el «PUNTO DE ENGANCHE»), dentro de la misma transacción de la aceptación.
- Migración **32**: `migrations/add_propuesta_asignacion.sql` (deshacer: `docs/produccion/deshacer-migracion-32.sql`). **Opcional**: sin ella todo sigue como antes (la recepción asigna a mano) y las pantallas no muestran nada nuevo.

## El recorrido

```
paciente envía «Sobre mí» ──► aviso a la recepción (campanita, uno por formulario)
recepción acepta ──► el sistema calcula la propuesta nº 1 ──► propuesta Pendiente (vence a las 48 hs)
      │                                                              │
      │ no hay propuesta posible                                     ├─ «Me sirve, confirmar» ─► vínculo + turno confirmado (una sola vez)
      ▼                                                              ├─ «Prefiero otro horario» ─► elige uno de los próximos 10 libres del MISMO profesional ─► propuesta nº+1
«Asignaciones para resolver» (recepción)                             ├─ «Probar con otro profesional» ─► propuesta nº+1, sin repetir los que ya rechazó
      ▲                                                              └─ tercer rechazo (o sin más candidatos) ───────────┐
      └──────────────────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

Casos que dejan el formulario en **«Asignaciones para resolver»** (con el motivo en lenguaje claro, aviso por campanita a la recepción y, al paciente, «Estamos buscando la mejor opción para vos, te avisamos apenas esté lista.»):

| Motivo | Cuándo |
|---|---|
| Sin horarios libres para esa especialidad | No hay profesional activo de la especialidad con un horario libre dentro del horizonte (o el paciente pidió «otro profesional» y no quedan). |
| El consentimiento del paciente no está vigente | Está sin firmar, revocado, vencido, de una versión anterior o sin la firma del tutor. **No se propone.** También si se revoca entre la propuesta y la confirmación (la propuesta se cancela). |
| El paciente no aceptó las tres propuestas | Tercer rechazo. |
| El paciente no respondió la propuesta a tiempo | Pasaron 48 horas. |

Desde ahí la recepción puede **volver a calcular la propuesta** (`POST /informes-admision/{id}/proponer-asignacion`, sirve cuando ya hay horarios o el paciente firmó; hasta 3 propuestas por formulario) o **asignar a mano** con el flujo de siempre (`PUT /informes-admision/{id}/derivar`), que **cancela cualquier propuesta pendiente del paciente** y libera su horario. Si la recepción devuelve el formulario o lo cierra con «otro recurso», la propuesta pendiente también se cancela.

## La regla de elección (`AsignacionAutomaticaReglas.Elegir`, pura, probada sin base)

1. **Candidatos**: profesionales **activos** cuya especialidad es la «Especialidad solicitada» del formulario. Nunca los de especialidad «Admisión» (ni la matrícula `ADM-001`) ni los inactivos.
2. **Profesionales preferidos**: se prueba primero solo con ellos; si ninguno tiene horario, se amplía a todos los de la especialidad y el motivo anota «Los profesionales elegidos no tenían disponibilidad.»
3. **Días y franja preferidos**: se filtran los horarios; si no queda ninguno se **relaja primero la franja y después los días**, y el motivo anota qué se relajó. «Me da igual» y los campos vacíos no filtran. (Franjas: mañana 8–12, tarde 12–18, noche 18–22.)
4. **Horarios**: salen de las franjas de agenda **vigentes** (`Agenda.FechaInicio/FechaFin`, migración 30), **sin las franjas de admisión** (`Agenda.EsAdmision`, migración 29), descontando los turnos activos (no cancelados ni rechazados) y las **propuestas pendientes** de otros pacientes. Anticipación mínima de 24 hs y horizonte de 30 días.
5. **Elección**: el horario libre más próximo; empate → el profesional con **menos pacientes activos**; empate → **menor Id**.

Las cifras sin validar están en un solo lugar (`AsignacionAutomaticaReglas`, con `// TODO-VALIDAR`): `HorasAnticipacionMinima = 24`, `DiasHorizonte = 30`, `HorasParaResponder = 48` y `HorasAntesDelTurnoParaCerrar = 2`.

**Vencimiento (`ExpiraEn`)** = el MENOR entre «creación + 48 hs» y «horario del turno propuesto − 2 hs» (`AsignacionAutomaticaReglas.Vencimiento`). Antes era solo lo primero y podía quedar después del turno. Es lo que se guarda y lo que ve el paciente en «Podés responder hasta…». Al confirmar («Me sirve») o elegir otro horario, el servidor vuelve a mirar que la propuesta no haya vencido y que el turno conserve ese margen de 2 hs; si no, la rechaza (409, `propuesta-vencida`: «Esta propuesta venció, te armamos otra»), la pasa a «Vencida» y arma la siguiente con la misma numeración (no cuenta como rechazo). No requiere migración.

## «Me sirve» (`POST /propuestas-asignacion/{id}/aceptar`)

Una sola transacción, de a una propuesta y de a un profesional a la vez (`sp_getapplock`, el mismo recurso `YoSoy.PrimerTurno.<id>` que usa el primer turno del profesional):

1. Revalida que la propuesta siga **Pendiente** y vigente, y que el formulario siga «Aprobado».
2. Revalida el **consentimiento** (la misma decisión de `ConsentimientoTratamiento.Estado`). Si ya no está vigente: propuesta Cancelada y formulario a «Asignaciones para resolver».
3. Revalida el horario con las reglas de turnos de siempre (`TurnoReglasService.ValidarSolicitud`: RN-01 agenda y horario libre, RN-07, y RN-02 si está en modo estricto). Si el horario ya no está libre: la propuesta se **reemplaza** por otra (misma numeración, no cuenta como rechazo del paciente), responde 409 con «Ese horario se acaba de ocupar. Te propusimos otro…».
4. Crea el **turno confirmado** (`TurnoRepositorioDAO.InsertarSiLibreEn`, el mismo chequeo que `InsertarSiLibre` pero dentro de la transacción), el **vínculo** `PacienteProfesional` en estado `Confirmada`, marca el formulario «Derivado» (asignado), resuelve el caso de seguimiento, deja la propuesta «Aceptada» con su `IdTurno` y avisa al profesional por la campanita (`NotificacionInterna`, tipo `asignacion-nueva`).
5. Repetirlo (doble clic, dos pestañas) devuelve el mismo turno: **no crea otro**.

## Cómo se bloquea el horario

Mientras una propuesta está **Pendiente** su horario cuenta como ocupado: para otras propuestas, para `TurnoReglasService` (pedir turno, horarios libres, página pública, bot) y para la inserción del turno. Dos índices únicos filtrados en la base (`UX_PropuestaAsignacion_HorarioPendiente`, `UX_PropuestaAsignacion_InformePendiente`) garantizan que no haya dos propuestas pendientes para el mismo horario ni dos para el mismo formulario aunque lleguen a la vez. Las propuestas del propio paciente no lo bloquean a él.

## Vencimiento sin procesos en segundo plano

No hay servicio nuevo: el vencimiento se **evalúa al consultar**. `AsignacionAutomaticaService.VencerVencidas` pasa a «Vencida» (con constancia en la auditoría, sin usuario) toda propuesta pendiente con `ExpiraEn` vencido y deja el formulario en «Asignaciones para resolver». Se llama al leer los primeros pasos, al responder una propuesta, al listar los formularios de la recepción y antes de calcular horarios libres o pedir un turno. Es una consulta barata e idempotente.

## Auditoría

Cada creación, aceptación, rechazo, cancelación y vencimiento deja una fila en `LogAuditoria` (entidad `InformeAdmision`, espacio `asignacion-automatica`, solo ids en el detalle). La creación lleva como usuario a la **persona de recepción que aceptó el formulario** (el origen de la acción automática); la aceptación y los rechazos, al paciente; el vencimiento, ninguno (lo hace el sistema).

## Lo que ve cada uno

- **Paciente**: en el paso «Te asignamos un profesional» una tarjeta «Te propusimos a *Nombre (especialidad)* el *lunes 12/11 a las 10:30 hs*» con **Me sirve, confirmar** y **Prefiero otro horario** (en la tercera propuesta, **No me sirve**). Viaja en `GET /pacientes/{id}/primeros-pasos` (`propuesta`, `asignacionPorResolver`).
- **Recepción** (módulo «Recepcionista» y profesional con Admisión): en el Inicio, las tarjetas **Formularios por revisar** y **Asignaciones para resolver** (llevan a la pestaña Formularios con ese filtro); en la lista, el estado «Propuesta enviada» o «Para resolver», el motivo y el botón «Volver a calcular la propuesta». Los datos salen de `GET /informes-admision-detalle` (`asignacionEstado`, `asignacionMotivo`, `propuestaPendiente`).
- **Profesional**: aviso en la campanita («Tenés un paciente nuevo con primer turno») y el turno confirmado en su agenda; falta generar la sala como siempre.

## Endpoints

| Método y ruta | Quién | Qué hace |
|---|---|---|
| `PUT /informes-admision/{id}/aceptar` | Admisión / CRM | Existente. Ahora también propone (respuesta con `asignacion`: `propuesta` o `por-resolver`). |
| `GET /propuestas-asignacion/{id}/horarios` | Paciente (el dueño) | Hasta 10 horarios libres del mismo profesional. |
| `POST /propuestas-asignacion/{id}/aceptar` | Paciente (el dueño) | «Me sirve». |
| `POST /propuestas-asignacion/{id}/rechazar` `{ tipo, fechaHora? }` | Paciente (el dueño) | `otro-horario` (con `fechaHora` de la lista), `otro-profesional` o `ninguno`. |
| `POST /informes-admision/{id}/proponer-asignacion` `{ confirmo: true }` | Admisión / CRM | Vuelve a calcular la propuesta de un formulario «Por resolver». |
| `PUT /informes-admision/{id}/derivar` | Admisión / CRM | Existente: la asignación a mano; cancela la propuesta pendiente. |

Una propuesta ajena responde 404 (el paciente no se entera de que existe).

## Sin la migración 32

`AsignacionAutomaticaService.Disponible()` es `false`: aceptar un formulario no propone nada (asignación manual, como antes), `informes-admision-detalle` devuelve `asignacionEstado` en `null`, los primeros pasos no muestran propuesta y los horarios y las reservas no miran propuestas. El verificador de esquema la marca como **opcional**.

## A validar (`// TODO-VALIDAR`)

Anticipación mínima (24 hs), horizonte (30 días) y plazo para responder (48 hs); qué hacer cuando el paciente no responde a tiempo (hoy pasa a la recepción); y si «Me sirve» debería exigir que el profesional acepte al paciente (hoy el vínculo queda `Confirmada` y el profesional se entera por la campanita). Las decide el equipo clínico, no el código.
