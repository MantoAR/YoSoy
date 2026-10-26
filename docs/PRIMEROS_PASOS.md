# Primeros pasos del paciente

El paciente nuevo ve en su Inicio siete pasos que **calcula el servidor con datos reales** (nada se marca a mano) y un único botón de «qué tengo que hacer ahora».

| # | Paso | Está hecho cuando | Botón principal si le toca |
|---|------|-------------------|----------------------------|
| 1 | Registro | tiene fecha de nacimiento y teléfono | Ir a Mis datos |
| 2 | Consentimiento | hay una firma vigente (misma función de «vigente» para todo el sistema) | Leer y firmar (ventana del panel) |
| 3 | «Sobre mí» | mandó el formulario (y no está devuelto) | Completar «Sobre mí» (ventana del panel) |
| 4 | Revisión | la recepcionista lo aceptó (`Aprobado`) | — (le toca al equipo) |
| 5 | Profesional | tiene un vínculo no rechazado ni cancelado | — (la asignación es manual) |
| 6 | Turno | tiene un turno confirmado o realizado | Confirmar turno / Ir a Turnos |
| 7 | Sesión | tiene un turno realizado | Entrar a la sesión (gris hasta 10 minutos antes, hora del servidor) |

`GET /pacientes/{id}/primeros-pasos` (`Endpoints/PrimerosPasosEndpoints.cs`, lógica en `Services/PrimerosPasos.cs`). Con el consentimiento vencido o revocado después de asignar un profesional, el único botón vuelve a ser el del consentimiento y los demás pasos conservan su estado real.

## Revisión del formulario (la recepcionista)

La entrevista de admisión ya no es parte del recorrido. El paciente completa «Sobre mí» (constante `YS.NOMBRE_FORMULARIO_ADMISION` en `shared/ui.js`) y lo revisa la recepcionista (Admisión o el módulo CRM, visible como «Recepcionista»). Tres acciones, cada una con confirmación explícita (`{ "confirmo": true }`), una transacción, auditoría (quién y cuándo en `InformeAdmision`) e idempotente:

- `PUT /informes-admision/{id}/aceptar` → `Aprobado`. Si el consentimiento no está vigente, dispara el aviso para firmar (sin duplicar). **Punto de enganche** (comentario en `RevisionAdmision.Aceptar`): acá va la propuesta automática de profesional y horario; hoy la asignación es manual (`PUT /informes-admision/{id}/derivar`, que ahora acepta un formulario aprobado sin entrevista).
- `PUT /informes-admision/{id}/devolver` `{ aclaracion }` → `Devuelto`. El paciente lo ve en sus pasos y lo completa con `PUT /pacientes/{id}/informe-admision` (mismo informe, vuelve a `Pendiente`).
- `PUT /informes-admision/{id}/otro-recurso` `{ motivo }` → `Otro recurso`: cierra el ingreso, el caso y descarta la sugerencia pendiente. Es el único «no ingresa».

Sin uso en el recorrido (siguen en el código): `PUT /informes-admision/{id}/entrevista`, la agenda de admisión (`Agenda.EsAdmision`), `PlazoEntrevistaAdmision`, los estados `En entrevista` y `Rechazado` (se leen como «en revisión») y los endpoints de rechazo del profesional.

## Consentimiento de tratamiento

- **Revocar**: `POST /pacientes/{id}/consentimiento-telemedicina/revocar` `{ confirmo: true }`. No borra la fila: queda `Inactivo` con `FechaRevocacion` e `IdUsuarioRevocacion`. Deja de ser vigente; no se pueden asignar profesionales ni confirmar turnos hasta volver a firmar. Avisa a la recepcionista por la campanita y, por cada turno futuro confirmado, al profesional (los turnos **no** se cancelan solos). Nada por WhatsApp.
- **Vigencia anual**: `Consentimiento:VigenciaMeses` (por defecto 12; 0 = no vence). Una firma más vieja deja de valer y se pide firmar de nuevo. El cambio de versión del texto sigue exigiendo firma nueva. No hay aviso previo al vencimiento: el sistema no tiene avisos programados simples para el paciente.
- Texto provisorio: la marca sale de `Consentimiento:MarcaProvisoria` (vaciarla la quita).

## «Sobre mí»: horario y profesionales preferidos

Se guardan en `InformeAdmision.HorarioPreferido` (`{"dias":["martes"],"franja":"tarde"}`; franjas: mañana 8–12, tarde 12–18, noche 18–22, cualquiera) y `ProfesionalesPreferidos` (`{"cualquiera":false,"ids":[3]}`). Solo se guardan y se muestran a la recepcionista; el cruce con las agendas es un trabajo siguiente. El formulario no abre ni crea la historia clínica.

## Avisos

`Notificacion.Evento` + índice único `(IdPaciente, Evento)`: un mismo evento (`informe:12:aprobado`) no puede crear dos avisos (`AvisosPaciente.CrearUnaVez`). La campanita del paciente lista solo las no leídas y las tarjetas flotantes (que repetían lo mismo) se sacaron. Los mensajes emergentes salen arriba (`shared/tema.css`).

## Migración 31

`migrations/add_primeros_pasos.sql` (deshacer: `docs/produccion/deshacer-migracion-31.sql`). La API la necesita: aplicarla antes de publicar. Las de Mariana pasaron a ser la 29 (agenda de admisión) y la 30 (vigencia de la agenda).
