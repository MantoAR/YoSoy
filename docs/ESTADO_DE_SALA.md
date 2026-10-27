# Estado de la sala, entrada anticipada del paciente y escena de video

## Estado de la sala en un solo lugar (por abrir / abierta / cerrada)

`Services/Video/EstadoDeSala.cs`. La regla (abre `Video:Ventana:MinutosAntes` antes del turno, cierra al terminar su franja más `MinutosDespues`) la usan: el acceso a la sala (`AccesoSalaService`), los primeros pasos del paciente y `GET /salas/estado?ids=1,2,3` (solo los turnos propios) que lee el inicio del profesional. Ninguna pantalla vuelve a decidirlo por su cuenta.

* **Profesional, Inicio** («Sesiones de hoy» y «Pacientes de hoy»): un turno con la sala cerrada muestra «Sala cerrada» (gris) y el botón deshabilitado con el motivo («La sala cerró a las HH:MM hs: el turno ya pasó»), en lugar de «Sala lista» / «Entrar a la sala». *Sin hacer:* el acceso a «Completar cierre» de una sesión sin cierre: hoy el cierre existe solo dentro de la sala (al finalizar), no como pantalla aparte.
* **Paciente, paso 7**: si el turno ya pasó y la sala cerró sin sesión realizada, el paso dice «Tu turno ya pasó» con el botón **Reprogramar** (lleva a Turnos, en el día de ese turno, con la reprogramación abierta). Si hay otro turno vigente, el paso apunta a ese. Con la sesión realizada, el paso queda hecho. El «turno confirmado» de los primeros pasos ya no es el más viejo: ignora los que cerraron sin sesión.
* **«Próximo turno»** del paciente no muestra un turno que empezó hace más de una hora (`EstadoDeSala.MinutosParaOcultarProximoTurno`, `// TODO-VALIDAR`) ni uno «Realizado» o «Ausente».

## El paciente entra antes que el profesional

**Cómo se «genera la sala» (evidencia):** `PUT /turnos/{id}/generar-sala` (`Program.cs`) escribe `Turno.CodigoSala = "SALA-{id}-{ticks}"`; el profesional lo hacía a mano. `AccesoSalaService.EvaluarVentana` rechazaba con «Todavía no se generó la sala» si el código faltaba, y el botón del paciente decía «Tu profesional todavía no generó la sala». Pero el código es **solo el nombre de la sala para el signaling-server**: el servidor de señalización arma la sala con lo que dice el token (`signaling-server/server.js`), no necesita que el profesional haya hecho nada antes. La dependencia era un paso manual.

**Ahora:** `Services/Video/SalaAutomatica.cs`. Cuando el paciente o el profesional del turno piden entrar (`POST /video/acceso`) con la sala **abierta** y el turno sin código, el servidor lo genera él (un `UPDATE` condicional: si entran a la vez, escribe uno solo y los dos ven el mismo código). Respeta el consentimiento (sin el de tratamiento y telemedicina vigente no se genera) y el estado del turno (solo Confirmado). **Fuera de la ventana** (más de 10 minutos antes, o con el turno pasado) no hace nada y sigue el rechazo de siempre (`aun-no-abre`, `ya-termino`).

* El paciente entra por `?turno=<id>` (no por el código) y espera viendo «Esperando a tu profesional…», su cámara y los botones Salir/Finalizar. Cerrar y volver a entrar no cambia la sala.
* **Orden de llegada (señalización):** quien ofrece es siempre el paciente (`ofrece = rol === "paciente"`, `video.js`). Si el paciente llega primero, al entrar el profesional el servidor avisa `user-joined` al paciente, que arma una oferta nueva y se conecta solo. Si llega primero el profesional, la oferta sale cuando entra el paciente. Probado en `signaling-server/test/servidor.test.js` (ambos órdenes y reingreso). **`server.js` NO se tocó**: no hay que reemplazarlo en el VPS.
* **Ausencias (prompt 04):** «conectado» ya significaba «pidió entrar a la sala» (`TurnoConexion`); ahora el paciente que entra a esperar queda registrado igual, así que el aviso «Tu paciente todavía no se conectó» no se dispara.
* **Profesional:** `GET /salas/estado` suma `pacienteEsperando` (el paciente ya pidió entrar y el profesional todavía no): chip «Esperando» en «Sesiones de hoy» / «Pacientes de hoy»; en la sala, mientras conecta, «Paciente en espera: conectando…». Sin notificaciones nuevas.
* **Emociones/consentimiento:** no cambian. El análisis no arranca hasta tener el video del paciente y su aceptación; en la espera no hay análisis.

## Escena de video (profesional y paciente)

`YS.video.escena` en `PANTALLAS/shared/video.js` y `PANTALLAS/shared/sala.css`, con el mismo componente en las dos salas:

* Video del otro participante grande; el propio, **miniatura flotante** abajo a la derecha: ~200 px (34 % del recuadro como máximo), 4:3, `object-fit: cover`, espejada, bordes redondeados y etiqueta «Vos». Nunca una tira de ancho completo (se eliminó `.mini-video` de 90 px).
* Mientras el otro no está, el recuadro muestra «Esperando al paciente…» / «Esperando a tu profesional…» sobre fondo neutro y la cámara propia llena el recuadro; al conectarse el otro pasa a miniatura.
* Sin cámara (permiso denegado, cámara apagada o desconectada a mitad de la llamada): ícono y «Sin cámara», nunca una caja negra.
* Pantallas chicas: el recuadro tiene `max-height: 68vh` (56vh bajo 760 px) y la miniatura se achica (140 px); los botones Salir/Finalizar quedan debajo, sin scroll dentro del recuadro. La miniatura va abajo a la derecha y la etiqueta del otro y el análisis en vivo, a la izquierda: no se tapan.
* Pruebas de lógica: `estadoDeCamara`, `clasesDeEscena` y una escena con DOM falso (`signaling-server/test/video-frontend.test.js`). Lo visual necesita dos navegadores (guion del reporte).

## Entrar a la sala desde cualquier lugar («el turno entrable»)

* **Regla única:** el turno entrable de una persona es su turno **Confirmado** cuya sala está **abierta** ahora (`EstadoDeSala.Entrable`: desde 10 minutos antes hasta el cierre de la ventana, la misma regla que `AccesoSalaService`); con más de uno, el que empieza más cerca de este momento. Pendiente, Cancelado, Realizado y los turnos ya cerrados no cuentan. El «próximo» es el primer Confirmado que todavía no abrió.
* **Endpoint:** `GET /salas/entrable` (paciente o profesional; no recibe ids: solo mira los turnos propios). Devuelve `{ ahora, entrable, proximo }`; cada turno trae `idTurno`, `idPaciente`, `hora`, `horaAbre`, `inicio`, `abre`, `cierra`, `esHoy` y `contraparte` (el profesional para el paciente, el paciente para el profesional).
* **Cliente:** `PANTALLAS/shared/entrada-sala.js` (`YS.entradaSala`) solo interpreta la respuesta y dibuja; no calcula horas. Lo usan el Inicio del paciente (tarjeta «Tu sesión por video»: «Entrar a la sesión», gris con «La sala abre a las HH:MM» si todavía no abrió), el menú «Sala de video» del paciente (con turno entrable entra directo; si no, un mensaje con el próximo turno y, colapsado, «Tengo un código de sala») y el menú «Sala de video» y «Sesiones de hoy» del profesional (con la sala abierta se entra sin «generarla» ni código). El paso 7 de los primeros pasos usa la misma función de entrada (`entrarASalaDelTurno`).
* **Pantalla en blanco (corregida):** `shared/sala.css` define `.sala-espera { display: none }` para el cartel «Esperando…» de la escena, y la pantalla previa de `Pantalla_Paciente/sesion_video.html` usaba esa misma clase: quedaba escondida y solo se veía el título. Ahora se llama `sala-previa`; una prueba impide que las páginas de la sala usen una clase que `sala.css` oculta.
* **Link del recordatorio:** el aviso de la campanita lleva a `<base>/pantallas/index.html?return=<sala del turno>`; la base sale de `Mensajeria:UrlIngresoTurno` (en el servidor, `Mensajeria__UrlIngresoTurno`) y, si no hay una dirección absoluta configurada, es `https://yosoytelemedicina.tech`. El login valida el `return` (`YS.rutaRetornoValida` acepta solo la reserva pública y la sala de un turno).

