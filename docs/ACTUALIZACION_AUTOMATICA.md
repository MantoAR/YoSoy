# Actualización automática del Inicio del paciente (sin F5)

**Qué resuelve.** Tras cada cambio de estado hecho por otra persona (la recepción acepta el formulario, el sistema propone un profesional, el profesional acepta o rechaza, se confirma un turno) el paciente tenía que recargar con F5. Las acciones del propio paciente (firmar, enviar «Sobre mí», «Me sirve», «Prefiero otro horario») ya redibujaban la pantalla; ahora además toman la referencia nueva para no redibujar dos veces.

**Cómo funciona.**
* `GET /pacientes/{id}/novedades` (`Endpoints/NovedadesPacienteEndpoints.cs`, solo el propio paciente) devuelve `{ huella, noLeidas }`. La huella es un resumen corto de lo que hace cambiar el Inicio: estado del último formulario y de su asignación, consentimientos, propuesta pendiente, vínculo con el profesional, turnos desde ayer y notificaciones sin leer. Son pocas consultas con `COUNT` y `CHECKSUM_AGG` sobre las filas de **una** persona; no devuelve datos clínicos. Antes de calcularla vence las propuestas cuyo plazo terminó.
* `PANTALLAS/shared/refresco.js` (`YS.refresco`) consulta cada ~15 s **solo con la pestaña visible**; con la pestaña oculta no hay timer, y al volver o enfocar la ventana consulta en el acto. Si hay errores, el intervalo se duplica (30, 60, 120 s de tope) y vuelve a 15 s con la primera respuesta buena. `unico(clave, …)` garantiza un solo refresco: al volver a armar la pantalla no se acumulan timers.
* Si la huella cambió, el Inicio se redibuja **en silencio** (sin esqueleto de carga, y solo si el HTML resultante es distinto) y la campanita se refresca. Si el paciente está en «Sobre mí» viendo el estado, también se actualiza; si está escribiendo el formulario, no se le pisa.

**Lo que no se actualiza solo** (a propósito): Turnos (calendario y reprogramación) y Perfil, porque son pantallas con formularios y selecciones en curso; el próximo turno y las solicitudes pendientes del Inicio sí.

**Pruebas.** `YoSoy.Tests/Pantallas/refresco-paciente.prueba.js` (DOM falso y reloj simulado) y `NovedadesPacienteBaseRealTests`.
