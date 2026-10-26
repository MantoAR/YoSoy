# Alta clínica y derivación interna asistidas desde el cierre de sesión

Continúa [`CIERRE_SESION.md`](CIERRE_SESION.md). Antes, elegir **Alta** o **Derivación** en el panel de cierre solo dejaba el tipo anotado. Ahora las dos salidas son **asistidas**: el sistema precarga, guía, avisa a quien corresponde y deja constancia; **una persona confirma**. Nada cambia de estado, de caso ni de vínculo sin una acción explícita del rol correcto. Migración **27**. Para desplegar: [`DEPLOY_ALTA_DERIVACION_PRODUCCION.md`](DEPLOY_ALTA_DERIVACION_PRODUCCION.md).

## Alta clínica (≠ alta administrativa)

| | Alta **administrativa** (de siempre) | Alta **clínica** (nueva) |
|---|---|---|
| Endpoint | `PUT /pacientes/{id}/alta` (Configurador) | `POST /casos/{id}/alta-clinica` (el profesional a cargo) |
| Qué hace | Reactiva la ficha de un paciente dado de baja (y su usuario) | Guarda el informe de alta y el plan de control y pasa el **caso** a «En alta» |
| Toca al paciente / su usuario | Sí (lo reactiva) | **No**: no lo inactiva, no toca su usuario ni sus consentimientos ni sus vínculos |

**El asistente** (en la sala, con tipo de cierre Alta): paciente, profesional y caso vienen precargados; se pide el **informe de alta**, la **fecha de alta** (hoy, editable, no futura) y el **plan de control** (cuántos contactos de control —0 a 12—, cada cuántos días —7 a 365— y la fecha del primero; se sugiere 3 contactos cada 30 días). «Revisar el alta» muestra el resumen y recién el botón **«Confirmar el alta de &lt;paciente&gt;»** aplica. El botón espera a que el cierre esté **guardado** (el alta se enlaza con esa sesión); el aviso «Falta completar el alta» desaparece al terminar.

**Contrato** — `POST /casos/{id}/alta-clinica`:

```json
{ "confirma": true, "informe": "…", "fechaAlta": "2026-10-26", "controlesPrevistos": 3, "controlCadaDias": 30, "primerControl": "2026-11-25", "idSesion": 88, "claveCierre": "guid" }
```

`201` (o `200` con `repetido: true`) → `{ idAltaClinica, idCaso, idPaciente, idSesion, repetido, fechaAlta, caso: { cambio, estadoAnterior, estado: "En alta" }, plan: { controles, cadaDias, primerControl } }`.

| Código | HTTP | Cuándo |
|---|---|---|
| `falta-confirmacion` | 400 | falta `confirma: true` (no se cambia nada) |
| `datos-invalidos` | 400 | informe vacío o de más de 8000 caracteres, fecha inválida o futura, plan incoherente (con el mensaje de qué corregir) |
| `caso-ajeno` | 403 | el caso no está a cargo de este profesional |
| `caso-inexistente` | 404 | no existe el caso |
| `estado-no-permitido` | 409 | el caso no está «Asignado» ni «En atención» (cerrado, nuevo, derivado, ya en alta) |
| `sesion-no-coincide` / `sesion-no-es-del-tipo` | 409 | la sesión del cierre no es de este profesional y paciente, o se guardó con otro tipo de cierre |
| `falta-migracion-27` | 409 | la base no tiene la migración 27 |

**Transacción única, auditada e idempotente:** se bloquea el caso; si ya hay alta, se devuelve la que quedó (sin escribir ni auditar de nuevo; un índice único por caso lo garantiza aunque lleguen dos a la vez); se inserta `AltaClinica`; `UPDATE Caso … WHERE Estado = <el de partida>`; dos filas de auditoría (`Alta clínica confirmada`, `Caso pasa a En alta`) con solo ids. Si algo falla, no queda nada.

**Estado nuevo del caso: «En alta».** Es el mínimo necesario. Queda fuera de «Asignado / En atención», así que el caso **deja de contar en la carga** del profesional para nuevas asignaciones; el caso sigue abierto (no es «Cerrado») y a cargo del profesional. `GET /casos/{id}/alta-clinica` devuelve el alta (con el informe) al profesional a cargo y a los auditores, con auditoría de lectura.

**El plan de control se GUARDA, no se programa.** Está en columnas de `AltaClinica` (`ControlesPrevistos`, `ControlCadaDias`, `PrimerControl`) para que la etapa siguiente (contactos de control en agenda, recordatorios, alertas de contactos perdidos) lo lea. Esa etapa **no** está.

## Derivación interna asistida

### Qué mecanismo se eligió y por qué

El proyecto tiene dos mecanismos, y **ninguno** tenía la «aceptación del profesional receptor»:

| | `Derivacion` («solicitudes de cambio») | `CasoDerivacion` (Fase 2) |
|---|---|---|
| Modelo | paciente + profesional **de origen** + profesional **de destino** + motivo + estado | caso → **grupo** (+ profesional sugerido/asignado) + origen Regla/Manual/Bot |
| Quién resuelve | Admisión (`aceptar` relinka **al instante**) | CRM (confirmar/corregir/rechazar) |
| Para qué sirve | cambiar de profesional a un paciente | enrutar casos **nuevos** que entran por admisión |
| Origen→destino entre profesionales | **Sí** | No (no hay profesional de origen) |

**Se eligió `Derivacion`**: es la que modela «el profesional A deriva a B con un motivo». `CasoDerivacion` no tiene profesional de origen ni sirve para una derivación entre colegas. No se unificaron ni se migraron datos. Para no tocar la «solicitud de cambio» de siempre se agregaron columnas (`Origen` 'Solicitud'/'Cierre', `IdCaso`, `Observaciones`, `IdSesion`, respuesta y resolución) y **estados propios** de la derivación del cierre; las filas viejas son `Origen = 'Solicitud'` y no cambian. Las acciones viejas de Admisión (`aceptar`, `rechazar`, `PUT`) **se niegan** sobre una derivación del cierre (409 con el camino correcto): relinkarían sin pasar por el traspaso confirmado.

### El circuito

```
Profesional A (a cargo del caso)           Profesional B (destino)                Admisión (CRM)
 1. Deriva con confirmación ───────────►  campanita: «Derivación pendiente»       campanita (bandeja): «Derivación interna iniciada»
    (Pendiente del destino)                  ve el informe: motivo + observaciones
    campanita de A: «Derivación enviada»  2. Acepta / Rechaza
    mail a A y B (sin datos del paciente)     │
                                              ├─ Acepta ─► «Aceptada por el destino» ─► tarea «Confirmar traspaso»  ──► 3. Confirma: reasignación de casos de siempre
                                              │            aviso a A                                                     (motivo + historial) → «Traspasada»
                                              └─ Rechaza ► «Rechazada por el destino» ─► aviso «decidí cómo seguir» ──► 3'. «Dar por resuelta»
                                                           aviso a A                                                     (el caso sigue con A)
```

* **Quién puede qué:** deriva solo el profesional a cargo del caso (no a sí mismo; el caso tiene que estar «Asignado» o «En atención», no cerrado ni en alta; el destino, activo). Responde **solo** el profesional de destino. El traspaso y «dar por resuelta» los ejecuta el rol que ya puede reasignar casos (**CRM / Administración**).
* **Los vínculos NO cambian** al derivar ni al aceptar. Recién «Confirmar traspaso» llama a `AsignacionService.ReasignarManual` (categoría de motivo «Otro», nota «Derivación interna #id»), que deja `CasoDerivacion` (Manual, Confirmada), `HistorialAsignacion` y sincroniza `PacienteProfesional`. Se rechaza si el caso se cerró, cambió de manos o el destino ya no está activo. Es idempotente (reintentar tras un fallo parcial solo cierra la derivación).
* **Qué ve el destino:** `GET /derivaciones/{id}/informe` → motivo, observaciones, fecha y los nombres (paciente, origen, destino). **Nada** de la historia clínica ni de las sesiones (siguen dando 404 al destino).
* **Avisos** (`NotificacionInterna`, en la misma transacción): destino, origen y «Administrador» (Admisión). Mail (`EmailService.EnviarAviso`, después de confirmar) a destino y origen **sin datos del paciente**; si el correo no está configurado queda en el log y no se deshace nada.
* **Idempotencia:** derivar lo mismo dos veces devuelve la derivación en curso (`repetido`); a otro destino mientras hay una en curso, 409 `derivacion-en-curso` (índice único filtrado por caso). Responder igual dos veces es `repetido`; la respuesta contraria, 409 `ya-respondida`.

| Endpoint | Rol | Qué |
|---|---|---|
| `GET /casos/{id}/destinos-derivacion` | profesional a cargo | profesionales activos con especialidad y carga de casos |
| `POST /casos/{id}/derivar` | profesional a cargo | crea la derivación (`confirma: true`, `idProfesionalDestino`, `motivo`, `observaciones`, `idSesion`/`claveCierre`) |
| `PUT /derivaciones/{id}/responder` | profesional de destino | `{ acepta, motivo? }` |
| `GET /derivaciones/{id}/informe` | origen o destino | el informe |
| `GET /profesionales/{id}/avisos-clinicos`, `PUT /avisos-clinicos/{id}/leida` | cada profesional (los propios) | alimenta la campanita |
| `GET /derivaciones/traspasos-pendientes` | CRM | la cola de Admisión |
| `POST /derivaciones/{id}/traspasar` | CRM | confirma el traspaso |
| `POST /derivaciones/{id}/resolver` | CRM | da por resuelta una rechazada |

**Derivación externa (a un psiquiatra): NO está.** El mecanismo elegido no tiene un tipo «externa» ni un destinatario en texto libre; agregarlo implica columnas, un camino distinto sin caso ni vínculo y otra pantalla: no es un cambio chico. Queda pendiente (la psiquiatría está fuera del alcance de la tesis).

## Pantallas

* **Sala del profesional** (`sesion_video.html`): los dos asistentes, descritos arriba.
* **Campanita del profesional** (`shared/shell.js`): la derivación pendiente con **Aceptar** / **Rechazar** (el rechazo permite contar el motivo) y los avisos de derivación y traspaso con **Entendido**. No se tocó `nuevo_dashboard.js`.
* **CRM › Derivaciones** (`Pantalla_Configurador/js/traspasos.js`): «Traspasos por confirmar» con **Confirmar traspaso** (pide confirmación) y **Dar por resuelta**.

## Cómo se probó

`AltaClinicaBaseRealTests`, `DerivacionAsistidaBaseRealTests`, `Migracion27Tests`, `AltaDerivacionPantallasTests` y `CierreSesionPantallaTests` (xUnit contra `yosoy_test`); el banco de Chromium con la API simulada (escenarios 16 a 18) y una corrida de punta a punta con la API real y datos `[E2E-AD]` (alta; derivación; aceptación desde la campanita de B; traspaso desde CRM).

## Fuera de esta tanda

Contactos de control programados, alertas por contactos perdidos, episodio de atención, unificar los dos mecanismos de derivación, seguimiento hasta la primera sesión del destino, avisos por WhatsApp, «Ausente», derivación externa, que el origen retire una derivación pendiente, y cualquier cambio en `nuevo_dashboard.js`.
