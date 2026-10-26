# Cierre de sesión: el panel de después de la videollamada

Qué pasa cuando el profesional termina una videollamada y cómo el sistema encadena el proceso: confirma que la sesión se hizo, pasa el turno a Realizado, mueve el caso a «En atención» si era la primera sesión y le ofrece cómo seguir con el paciente. Es una pieza del «circuito del paciente»: **el sistema detecta, avisa, propone y precarga; UNA PERSONA confirma.** Nada cambia de estado solo.

Escrito el 2026-10-26 sobre la rama `feat/cierre-sesion` (sale de `fix/sala-actividad-inactividad`, que sale de `main` en `a3666a2`). Migración **26**. Para desplegar: [`DEPLOY_CIERRE_SESION_PRODUCCION.md`](DEPLOY_CIERRE_SESION_PRODUCCION.md).

## El flujo, de punta a punta

1. El profesional entra a la sala de un turno **Confirmado**, habla, y mientras escribe la nota se guarda un **borrador cifrado en su equipo** (nada va al servidor).
2. **Finalizar sesión**: se cortan cámara, micrófono y análisis de emociones (como siempre) y aparece el **panel de cierre**:
   - **¿Se realizó esta sesión?** *Sí, se realizó* / *No se realizó* / *Decidir después*. Ninguna viene elegida.
   - **¿Cómo sigue el paciente?** *Seguimiento* / *Alta* / *Derivación*.
   - **Anotar evolución** (lleva a la caja de la nota) y **Agendar próximo turno**.
3. **Guardar cierre** manda UN pedido, `POST /sesiones-clinicas`, con una `claveCierre`. En una sola transacción el servidor guarda la sesión, pasa el turno a Realizado (solo si se eligió *Sí, se realizó*), mueve el caso a En atención (solo si era la primera sesión) y deja la auditoría.
4. El panel muestra un resumen de lo que quedó hecho. El borrador local se descarta.

| Elección | Qué se manda | Qué cambia |
|---|---|---|
| **Sí, se realizó** | `confirmaRealizacion: true` | La sesión se guarda; el turno pasa de Confirmado a **Realizado**; si era la primera sesión con ese profesional, el caso pasa de Asignado a **En atención** |
| **No se realizó** (con una nota que diga qué pasó) | `confirmaRealizacion: false` | Se guarda la nota. **El turno y el caso no cambian.** No existe «Ausente» y esta función no lo agrega |
| **Decidir después** | **Nada** | **Nada.** El borrador sigue en el equipo; la sesión queda sin cerrar |
| Sala sin turno (entrevista de admisión) | `idTurno: null`, `confirmaRealizacion: false` | Solo se guarda la nota; el paso «¿se realizó?» no aparece |

**Alta y Derivación:** con esos tipos de cierre el panel avisa «Falta completar el alta / la derivación del paciente» y ofrece un botón que abre el **asistente** (alta clínica o derivación interna), que precarga, guía y deja que el profesional confirme. Ver [`ALTA_DERIVACION_CIERRE.md`](ALTA_DERIVACION_CIERRE.md). El servidor devuelve `siguientePaso` y la sala despacha el evento DOM `ys:cierre-guardado` (`detail` = ese bloque); los asistentes despachan además `ys:alta-registrada` y `ys:derivacion-enviada`.

**Agendar próximo turno:** se ofrece siempre; con *Seguimiento* viene sugerido (destacado y con el selector abierto). El selector abre con el paciente y el profesional ya cargados, lista los horarios libres (`GET /profesionales/{id}/horarios-libres?dias=14`, los mismos que ve el paciente al reservar) y «Enviar propuesta» usa `POST /turnos/profesional-solicitar` sin `esUrgente`: crea un turno **Pendiente** y le avisa al paciente. **El paciente lo tiene que aceptar** (`PUT /turnos/{id}/confirmar-paciente`, con el consentimiento de telemedicina — RN-02 — y las demás reglas de siempre). Nada se reserva solo. Un rechazo (horario ocupado, RN-07) se muestra con el mensaje de la API y recarga los horarios sin perder el cierre.

## La API: `POST /sesiones-clinicas` (extendido, retrocompatible)

Un pedido **sin** `claveCierre` ni `confirmaRealizacion` sigue el camino de siempre (misma forma de respuesta, sin tocar turno ni caso). Con alguno de los dos entra al camino nuevo (`Endpoints/CierreSesionEndpoints.cs`).

```json
{ "idPaciente": 12, "idTurno": 450, "notasEvolucion": "…", "tipoCierre": "Seguimiento",
  "claveCierre": "3f9c…-guid", "confirmaRealizacion": true, "emociones": { } }
```

Respuesta `201` (`200` si es un reintento con la misma clave): los campos de siempre (`idSesion`, `idTurno`, `idPaciente`, `idProfesional`, `fechaHora`, `tipoCierre`, `emociones`) más

```json
{ "repetido": false,
  "turno": { "cambio": true, "estadoAnterior": "Confirmado", "estado": "Realizado", "motivo": null },
  "caso":  { "cambio": true, "idCaso": 7, "estadoAnterior": "Asignado", "estado": "En atención" },
  "siguientePaso": { "tipo": "seguimiento|alta|derivacion", "idPaciente": 12, "idProfesional": 3, "idSesion": 88, "idTurno": 450 } }
```

`turno` es `null` si no hay turno; `turno.motivo` es `no-confirmado` (el profesional no lo pidió) o `ya-estaba` (ya figuraba Realizado). Las emociones siguen su propia guardia de consentimiento revocable, **sin cambios**.

**Validaciones, en este orden (la primera que falla corta; si falla, no se guarda nada):**

| Código | HTTP | Cuándo |
|---|---|---|
| `tipo-cierre-invalido` | 400 | `tipoCierre` no es Seguimiento / Alta / Derivación (se acepta sin tilde y en minúsculas; se guarda siempre «Derivación») |
| `clave-invalida` / `clave-requerida` | 400 | `claveCierre` falta o no es un GUID (es obligatoria para confirmar) |
| `realizacion-sin-turno` | 400 | `confirmaRealizacion: true` sin `idTurno` |
| `solo-el-profesional-confirma` | 403 | Quien guarda no es el profesional del turno |
| `falta-migracion-26` | 409 | La base no tiene la migración 26 |
| `turno-inexistente` | 404 | El turno no existe |
| `turno-no-coincide` | 409 | El turno es de otro paciente o de otro profesional (el mismo mensaje para los dos: no filtra datos). Deja una fila de auditoría «Cierre de sesión rechazado» |
| (repetido) | 200 | La misma `claveCierre` del mismo profesional ya está guardada: se devuelve lo que quedó, sin escribir ni auditar de nuevo |
| `turno-con-sesion` | 409 | Ese turno ya tiene otra sesión guardada |
| `estado-no-permitido` | 409 | Se pidió Realizado y el turno no está Confirmado (Pendiente, Cancelado, Rechazado) |
| `turno-futuro` | 409 | Se pidió Realizado y el turno es de una fecha futura (el de hoy sí se puede) |
| `catalogo-incompleto` | 409 | Falta el estado «Realizado» en `EstadoTurno` |

Todos los rechazos traen `{ codigo, mensaje }` con un mensaje en castellano claro. **Atomicidad:** una sola conexión y transacción; el turno se bloquea (`UPDLOCK, HOLDLOCK`) hasta el final, así que dos cierres del mismo turno se ponen en fila; el `UPDATE` del turno lleva el estado de partida en el `WHERE`. **Idempotencia:** por clave (índice único filtrado por profesional y clave: la base lo garantiza aunque dos pedidos lleguen a la vez) y por turno (una sola sesión).

### Transiciones

```
Turno:  Pendiente ─(RN-02)→ Confirmado ─(cierre con «Sí, se realizó»)→ Realizado   (terminal para esta función)
Caso:   Asignado ─(primera sesión realizada con el profesional asignado)→ En atención
```

El caso pasa a En atención solo si: el turno **cambió ahora** a Realizado, el caso abierto más reciente del paciente está **Asignado a ese mismo profesional** y **no hay otro turno Realizado** entre ellos (la primera). Cualquier otro caso no se toca: ni si la sesión no se realizó, ni con otro estado, ni de otro profesional. Es la **única** transición automática del caso. Esta función no ofrece «des-realizar» un turno.

### Auditoría (`LogAuditoria`, categoría Clínica, solo ids, dentro de la misma transacción)

| Acción | Entidad | Detalle (ejemplo) |
|---|---|---|
| Cierre de sesión clínica | SesionClinica | `SesionClinica #88, paciente #12, turno #450, cierre Seguimiento, como tratante` |
| Turno marcado como Realizado | Turno | `Turno #450: Confirmado -> Realizado, sesión #88, paciente #12, confirmado por el profesional en el cierre` |
| Caso pasa a En atención | Caso | `Caso #7: Asignado -> En atención, primera sesión realizada #88, paciente #12, turno #450` |
| Cierre de sesión rechazado | Turno | `turno #450, motivo turno-no-coincide, paciente enviado #12, profesional #3` (registro aparte: no hay transacción que lo proteja) |
| Sesión vinculada a una evolución | SesionClinica | `SesionClinica #88 <-> Evolucion #301, paciente #12, como tratante` |

Si no se puede auditar, falla todo (a diferencia de la auditoría de lectura, que no tumba el pedido). Nunca se registra el texto de la nota, el motivo de un turno ni nombres.

## Sesión y evolución: se enlazan, no se unifican

El cierre **no crea ninguna `Evolucion`** (y no abre la historia clínica). `SesionClinica.NotasEvolucion` sigue siendo lo que se escribió en la sala. El vínculo con una evolución de la historia clínica es **manual**, del profesional, y vive en `SesionClinica.IdEvolucion` (migración 26): **la tabla `Evolucion` no se toca**, así que una evolución firmada sigue siendo inmutable.

- `GET /sesiones-clinicas/{id}/evoluciones-vinculables`: las evoluciones **del mismo paciente, del mismo profesional y del mismo día** que la sesión, que todavía no estén vinculadas (id, fecha y los primeros 80 caracteres). Auditada.
- `PUT /sesiones-clinicas/{id}/evolucion` con `{ "idEvolucion": 301 }`: vincula. Solo el profesional de la sesión (una sesión ajena se ve igual que una inexistente: 404). Idempotente (el mismo vínculo da `200` con `yaEstaba: true`). Rechazos 409: `evolucion-no-coincide` (otro paciente, profesional o día), `sesion-ya-vinculada`, `evolucion-ya-vinculada`.
- `GET /pacientes/{id}/sesiones-clinicas` y `GET /profesionales/{id}/sesiones-clinicas` devuelven `idEvolucion` (o `null`).
- **En la sala:** después de guardar el cierre, el panel ofrece «Vincular con una evolución de hoy». Lista las candidatas y vincula solo con el clic en «Vincular»; guardar el cierre no vincula nada solo. La pantalla de Historia clínica todavía no junta ambas en una sola tarjeta (ver «Lo que queda»).

## Base de datos: migración 26

`migrations/add_cierre_sesion.sql` (deshacer: `docs/produccion/deshacer-migracion-26.sql`). Aditiva, idempotente, en una transacción, **opcional** para la API:

- `SesionClinica.ClaveCierre UNIQUEIDENTIFIER NULL` + índice único filtrado `UX_SesionClinica_Clave (IdProfesional, ClaveCierre)`.
- `SesionClinica.TurnoRealizadoUtc DATETIME2(3) NULL`: cuándo este cierre pasó el turno a Realizado. Solo trazabilidad.
- `SesionClinica.IdEvolucion INT NULL` + FK a `Evolucion` + índice único filtrado `UX_SesionClinica_Evolucion`.
- `IX_SesionClinica_Turno (IdTurno)` filtrado, no único (puede haber turnos con más de una sesión de antes).

No toca ninguna fila existente. Sin la migración, el pedido de siempre anda y el pedido nuevo responde `409 falta-migracion-26`.

## El borrador local cifrado (`PANTALLAS/shared/borrador.js`)

- **Qué guarda:** la nota, el tipo de cierre, la respuesta de «¿se realizó?» y la `claveCierre`. **No** la transcripción ni las emociones. Se llama «Borrador»: no es una evolución ni una firma.
- **Cómo:** IndexedDB, AES-GCM de 256 bits; la clave se genera una vez por usuario con `extractable: false`; el dato asociado es «usuario + sesión» (un borrador no se descifra en otro turno ni con otro usuario). Se guarda 2 s después de dejar de escribir y al salir de la caja.
- **Qué NO protege:** código malicioso que corra dentro de esta misma página ni otra persona con la sesión abierta en el equipo. Protege contra leer el disco o el perfil del navegador desde afuera.
- **Cuándo se borra:** al guardar el cierre, con «Descartar», a las 72 horas (se purga al abrir cualquier sala) y al **cerrar sesión de forma explícita**, previa pregunta si hay uno. El **vencimiento de la sesión por inactividad no lo borra** (`YS.sesion.limpiar()` solo borra `localStorage`).
- **Al recargar:** ofrece «Recuperaste un borrador de esta sesión del …» con *Seguir con el borrador* / *Descartar*; **no lo aplica solo** y la caja queda bloqueada hasta decidir. Se recupera también la misma `claveCierre`: un reintento tras la recarga no duplica nada.
- **Sin IndexedDB o WebCrypto** (modo privado, políticas): la pantalla lo dice («No se pudo guardar el borrador en este equipo…») y **no finge**; queda el aviso del navegador al cerrar con notas sin guardar.
- `window.ysCierrePendiente()` (en la sala) dice si hay algo que se perdería al salir; el panel principal todavía **no** lo consulta antes de cambiar de sección (ver «Lo que queda»).

## La sesión del panel principal

La sala corre en un iframe y `js/sesion.js` cierra la sesión por inactividad (10 min) mirando solo su documento. La sala avisa actividad cada minuto con `postMessage('ys-actividad')` mientras está en uso: durante la llamada, con el panel de cierre abierto o con notas sin guardar. **Cuando el cierre queda guardado, la sala suelta la sesión** (`panelDeCierreAbierto = false`, `guardadoEnHC = true`) y deja de avisar.

## Cómo se probó

- **xUnit** (`YoSoy.Tests`): `CierreSesionReglasTests` (reglas puras), `CierreSesionBaseRealTests` (la API completa contra `yosoy_test`: feliz, idempotencia, concurrencia, rechazos, permisos, atomicidad con fallas provocadas, caso, vínculo), `Migracion26Tests` (idempotencia, deshacer, transacción, forma de producción), `BorradorCifradoTests` (cifrado real de Node), `CierreSesionPantallaTests` (lo que la pantalla no puede dejar de cumplir).
- **Navegador real** (Playwright + Chromium, con la API simulada): panel, decisiones, reintento con la misma clave, selector de próximo turno, borrador en IndexedDB real, recarga y recuperación, sin IndexedDB. No se versiona (script local).
- **A mano**: el guion está en el reporte de la entrega; lo que ningún test cubre es la llamada de video real con dos personas.

## Lo que queda (fuera de esta tanda)

«Ausente» y abandono, avisos por WhatsApp, reprogramación atómica, unificar los dos mecanismos de derivación, notas previas dentro de la sala, la lista de «turnos sin cerrar» (el hook queda: *Decidir después*), que el panel principal consulte `ysCierrePendiente()` antes de cambiar de sección (toca `nuevo_dashboard.js`, que tiene conflicto conocido con otra rama), la pantalla de Historia clínica que junta sesión y evolución en una sola tarjeta, y un camino auditado para corregir un «Sí, se realizó» equivocado.
