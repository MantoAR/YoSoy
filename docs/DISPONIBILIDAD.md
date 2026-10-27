# Horarios que ve el paciente (pedir turno y reprogramar)

`GET /pacientes/{id}/disponibilidad?idProfesional=&dias=30&ignorarTurno=` (solo el propio paciente). Código: `AsignacionAutomaticaService.HorariosParaPaciente` y `DisponibilidadReglas` (`Services/DisponibilidadPaciente.cs`), `Endpoints/DisponibilidadEndpoints.cs`.

## Por qué existe

El calendario del paciente armaba los horarios **solo**, en el navegador, a partir de las franjas crudas de `GET /profesionales/{id}/agenda`:

* ignoraba las franjas de **admisión** (migración 29) y la **vigencia** de cada franja (migración 30), y no sabía de las propuestas pendientes de otros pacientes;
* al reprogramar usaba horas redondas fijas (cada 60 minutos) en vez del intervalo de la franja;
* los días con turno propio los calculaba aparte, distinto del servidor (RN-07);
* **si la consulta fallaba, el `catch` dejaba el calendario vacío sin decir por qué** («Seleccioná el nuevo día en el calendario» con todos los días grises).

No se pudo reproducir un calendario vacío con datos válidos contra `yosoy_test` (el endpoint de agenda responde bien para un paciente). La causa más probable en producción es la combinación de lo anterior: una consulta que falla (sesión, red, 4xx) o franjas que el servidor descarta pero el navegador no, y ninguna explicación. Además se encontraron dos reglas que dejaban al paciente sin salida (corregidas):

* **Reprogramar un turno de menos de 24 horas o ya pasado**: reprogramar es «cancelar y pedir uno nuevo»; cancelar exige 24 horas de anticipación (RN-05), así que el paso fallaba y la persona se quedaba sin poder cambiarlo. Ahora, si el turno ya pasó, no se cancela: se pide el nuevo directamente.
* **Pedir otro turno el mismo día** (RN-07): un turno de hoy que ya empezó y al que el paciente no entró (sin sesión) seguía contando como «turno activo». Ahora no cuenta (`ContarTurnosActivos(..., ahora)` e `InsertarSiLibre(..., ahora)`; un turno con sesión sigue contando). Sin el reloj, el conteo es el de siempre.

## Una sola fuente

La disponibilidad sale de `AsignacionAutomaticaService.HorariosLibres` (la función de la propuesta automática de asignación), con la anticipación de un pedido del paciente en vez de las 24 horas de la propuesta, más RN-07 y el bloqueo del paciente derivado. Respuesta: `{ dias: [{ fecha, horas: ["09:00", ...] }], total, codigo, mensaje, mensajeHoy, anticipacionHoras }`.

* Sin franjas de atención vigentes (`sin-agenda`): «Tu profesional todavía no cargó horarios de atención…».
* Con franjas pero sin horarios libres (`sin-horarios`): «Tu profesional no tiene horarios libres en los próximos N días. Probá más adelante o escribinos…».
* Paciente derivado a psiquiatría (`paciente-derivado`): el mensaje de siempre.
* `mensajeHoy`: lo que se muestra al elegir hoy cuando no queda nada: «Para hoy ya no quedan horarios libres. Si necesitás un turno hoy mismo, comunicate con recepción.» (con una anticipación definida: «Los turnos se piden con al menos N horas de anticipación; para hoy mismo comunicate con recepción.»). También lo usa el rechazo del servidor («Ese horario ya pasó. …»).
* La pantalla ya no calcula nada: pinta los días del servidor, y si la consulta falla muestra «No pudimos cargar los horarios…».

`// TODO-VALIDAR`: `DisponibilidadReglas.AnticipacionMinimaPedidoHoras = 0` (hoy se puede pedir para dentro de unos minutos; la de 24 horas quedó solo para cancelar y para la propuesta automática).
