# Videollamadas — acceso a las salas, token de sala y servidores ICE

Cómo entra una persona a una sala de video: qué le pregunta el frontend a la API, qué decide la API, qué
recibe, qué hace la pantalla con eso, y qué hay que configurar en el servidor. Cubre **la API** (PR 3 del plan de
videollamadas), **las pantallas que la usan** (PR 4), **cómo se despliega todo** (PR 5, en
[`docs/DESPLIEGUE_VIDEO.md`](DESPLIEGUE_VIDEO.md): señalización como servicio, secretos, coturn y Caddy; preparado,
todavía no ejecutado en el servidor) y **el consentimiento y el manejo de los datos de la lectura de emociones** (PR 6,
en "Lectura de emociones: consentimiento y datos sensibles"; **el texto legal es provisorio y lo tiene que validar un
profesional legal**).

El contrato del token con el servidor de señalización está en [`signaling-server/README.md`](../signaling-server/README.md)
("Token de sala"). Esta página es el lado de la API y de las pantallas.

## Cómo encaja

```
navegador ──POST /video/acceso (JWT de la sesión)──▶ API ──decide, firma──▶ { token, sala, rol, iceServers }
navegador ──io(url, { auth: { token } })──▶ signaling-server   (verifica el token, no llama a la API)
navegador ◀──WebRTC (STUN / TURN con las credenciales recibidas)──▶ el otro navegador
```

El cliente solo dice **de qué sesión quiere entrar** (un turno o una admisión). La sala, el rol, el horario y el
vencimiento los decide el servidor con los datos de la base: nada de eso se toma del pedido.

## `POST /video/acceso`

Requiere el JWT normal de la API (`Authorization: Bearer ...`), de cualquiera de los tres roles. Tiene su propia
política de rate limit por IP real (`video`, 20 por minuto por defecto).

```json
{ "origen": "turno", "id": 12 }
```

`origen` es `"turno"` o `"admision"` (se ignoran mayúsculas, espacios y tildes). `id` es el `IdTurno` o el
`IdInforme`; no son el mismo número, cada uno vive en su tabla. Cualquier otro campo del cuerpo se ignora.

### Respuesta `200`

```json
{
  "token": "<JWT HS256 de la sala>",
  "sala": "SALA-12-8DE5A3F3A1B2C3D",
  "rol": "paciente",
  "expiraUtc": "2026-10-14T13:00:00Z",
  "turnDisponible": true,
  "iceServers": [
    { "urls": ["stun:yosoytelemedicina.tech:3478"] },
    { "urls": ["turn:yosoytelemedicina.tech:3478?transport=udp", "turn:yosoytelemedicina.tech:3478?transport=tcp"],
      "username": "1760403600:34", "credential": "<Base64 de HMAC-SHA1>" }
  ]
}
```

- `token`: se lo manda al signaling en el handshake (`io(url, { auth: { token } })`). Vale hasta `expiraUtc`.
- `sala` y `rol` son informativos para la pantalla; el signaling los toma del token, no de lo que mande el cliente.
- `iceServers` va tal cual a `new RTCPeerConnection({ iceServers })`. Las entradas de STUN no llevan `username` ni
  `credential`. `turnDisponible` dice si hay una entrada TURN.
- Siempre sale con `Cache-Control: no-store`. El token y las credenciales **no se loguean nunca**.

Así lo usan las pantallas (en resumen; el detalle está en "El frontend"):

```js
const acceso = await YS.video.pedirAcceso({ origen: 'turno', id });   // POST /video/acceso con el JWT de la sesión
const control = YS.video.entrar({ sesion, acceso, stream, ... });     // io(SIGNAL, { auth: token }) + RTCPeerConnection({ iceServers })
```

### Errores

El cuerpo siempre es `{ "mensaje": "...", "codigo": "..." }` (más `abreUtc` o `cerroUtc` cuando corresponde). El
`mensaje` está pensado para mostrarlo tal cual; el `codigo` es estable para decidir qué hacer.

| Status | `codigo` | Cuándo |
|---|---|---|
| 400 | `solicitud-invalida` | `origen` que no es `turno`/`admision`, `id` ausente o menor que 1, JSON roto. |
| 401 | — | Sin JWT, vencido o inválido. Un token de sala **no** sirve como sesión. |
| 404 | `no-existe` | No hay un turno / admisión con ese id, **o existe y la persona no es parte de él**. Es la misma respuesta (mismo status y mismo cuerpo) en los dos casos, así nadie puede averiguar qué ids existen. |
| 409 | `estado-invalido` | Turno que no está Confirmado, o admisión que no está "En entrevista" (o sin fecha de entrevista). |
| 409 | `aun-no-abre` | Antes de la hora. Trae `abreUtc`. |
| 409 | `ya-termino` | Pasada la hora. Trae `cerroUtc`. |
| 409 | `sala-no-generada` | La sesión no tiene `CodigoSala`. **Este endpoint nunca crea la sala.** |
| 409 | `sala-invalida` | El código guardado no cumple el formato que acepta el signaling (no debería pasar). |
| 429 | — | Demasiados pedidos desde esa IP. |
| 503 | `video-no-disponible` | Falta (o es débil) el secreto de la señalización. Mensaje genérico; el motivo está en el log. |

Orden de los chequeos: sesión → pedido → configuración → existencia y **pertenencia (un solo 404 para las dos)** →
estado → horario → sala. La pertenencia va antes que el estado y el horario a propósito: quien no es parte de una
sesión no se entera de su estado, de su horario, de si ya tiene sala ni de si el id existe.

## Quién entra

| Origen | Entra | Rol en el token |
|---|---|---|
| `turno` | El **paciente** del turno | `paciente` |
| `turno` | El **profesional** del turno | `profesional` |
| `turno` | Un **Configurador** (cualquier turno) | `profesional` |
| `admision` | El **paciente** del informe | `paciente` |
| `admision` | Un **Configurador** (es quien hace la entrevista) | `profesional` |

Cualquier otra persona recibe el mismo `404` que si la sesión no existiera (ver "Errores"). El rol del token sale de
esta tabla, es decir, de quién es el usuario **en esa sesión**, y nunca de lo que mande el cliente. Es la misma regla
de `PUT /turnos/{id}/generar-sala`: el profesional solo en su turno, el Configurador en cualquiera.

El signaling tiene dos asientos (`profesional` y `paciente`). Un Configurador ocupa el del profesional: si entra a
un turno antes que el profesional real, el profesional va a recibir el rechazo `rol-ocupado` al conectarse (el mismo
`sub` reconectando reemplaza a la conexión anterior; otro `sub` con el mismo rol se rechaza).

## Cuándo se puede entrar

Desde **10 minutos antes del inicio** hasta **30 minutos después del fin**. Se incluye el instante de apertura y se
excluye el de cierre: con un turno de 10:00 a 10:30, se entra desde las 09:50:00 y hasta las 10:59:59.

- **Inicio** de un turno: `Turno.Fecha` + `Turno.Hora`.
- **Duración** de un turno: el `IntervaloMinutos` de la franja de la agenda del profesional que contiene la hora
  (el turno no guarda su duración). Si no cae en ninguna franja, se usa la del sistema, **30 minutos** (la misma que
  usa el resto de la aplicación al generar los horarios): `Video:DuracionPorDefectoMinutos`.
- **Admisión**: la entrevista no tiene duración propia en la base. Se asume la de un turno (30 minutos,
  `Video:Admision:DuracionMinutos`) a partir de `InformeAdmision.FechaEntrevista`, con la misma ventana. El informe
  tiene que estar en estado **"En entrevista"** (es decir, con la entrevista programada; "Pendiente", "Derivado" y
  "Rechazado" no tienen sala vigente).

Los 10 y los 30 son configurables (`Video:Ventana:MinutosAntes` y `MinutosDespues`).

### La zona horaria

La base guarda fecha y hora **sin zona**: es la hora de la clínica. Toda la API (las reglas de turnos —"ese horario ya
pasó"—, el bot, los recordatorios, la firma del consentimiento y esta ventana de la sala) toma "qué hora es" de **un solo
reloj**, que tiene la zona de la clínica **explícita**: ya no depende de la zona del sistema operativo del servidor, así
que una VPS en UTC no corre las horas. La zona sale, por orden, de:

1. `Turnos__ZonaHoraria` (`Turnos:ZonaHoraria`): identificador IANA (`America/Argentina/Buenos_Aires`) o de Windows. Si
   está y no existe en el servidor, la API **no arranca** y dice cuál es la clave: es preferible enterarse al desplegar.
2. `Video__ZonaHoraria`: es lo que ya existía y significa lo mismo, así que si `Turnos__ZonaHoraria` no está también
   define la zona de la clínica (quien ya la tenía no cambia nada). Si además está y es **distinta** de la anterior, manda
   para la ventana de la sala y la API lo avisa en el log (una vez); conviene dejarla vacía o igual.
3. `America/Argentina/Buenos_Aires`.

Al arrancar, la API deja en el log `Zona horaria de la clínica: <zona>, diferencia con UTC ahora: <±hh:mm:ss>`.
(Las pantallas, por su lado, filtran los horarios de hoy que ya pasaron con la hora de Argentina fija; el servidor es
quien decide.)

## El token de sala

JWT **HS256** con los claims `sala`, `rol`, `sub` y `exp` (nada más: ni nombre, ni mail, ni ids clínicos). Se firma
con `Video__SignalingSecreto` como UTF-8, que tiene que tener **el mismo valor** que `SIGNALING_SECRETO` del
signaling-server.

- `sala`: el `CodigoSala` del turno o de la admisión.
- `rol`: `profesional` o `paciente`, según la tabla de arriba.
- `sub`: el id del usuario.
- `exp`: **el menor** entre el cierre de la ventana de la sesión y `ahora + Video:Token:MaxMinutos` (2 horas por
  defecto). Entrando sobre el borde, el token vive lo que queda de ventana y nada más. El signaling corta el
  socket al llegar a `exp`; para seguir hay que volver a pedir un token (que se vuelve a evaluar).

Dos secretos distintos, a propósito: el de la señalización **no puede ser igual a `Jwt__Key`** (si lo fuera, quien
tiene el secreto del signaling podría falsificar un login). Se valida en cada pedido, no al arrancar: sin secreto, o
con uno de menos de 32 caracteres o igual a `Jwt__Key`, la API **arranca igual** y `/video/acceso` contesta 503. En
el log de arranque y en cada pedido queda el motivo, sin el valor.

## Servidores ICE y TURN

`iceServers` siempre trae STUN. Si hay TURN configurado, agrega una entrada con **credenciales efímeras** al estilo
"TURN REST API" de coturn (`use-auth-secret`):

```
username   = "{vencimientoUnix}:{sub}"
credential = Base64( HMAC-SHA1( Video__TurnSecreto, username ) )
```

coturn las valida con el mismo secreto (`static-auth-secret`), sin base de datos. Cada usuario recibe las suyas.

- **Vencimiento**: `max(ahora + Video:Turn:TtlMinutos, exp del token)`, y el TTL nunca es menor que la ventana de la
  sesión (2 horas por defecto). Así el relay no se corta antes que la señalización ni antes de que termine la sesión.
  Se eligió un TTL largo porque no está confirmado si coturn corta una renovación de ICE hecha después de vencidas
  las credenciales: conviene probar una llamada relayada larga antes de acortarlo.
- **Sin TURN configurado** (no hay `Video:Ice:TurnUrls`): la respuesta trae solo STUN y `turnDisponible: false`.
- **TURN configurado pero sin `Video__TurnSecreto` válido** (falta, de menos de 32 caracteres o igual a `Jwt__Key`):
  el acceso **sigue funcionando** con solo STUN (el video anda por STUN en la mayoría de las redes), con
  `turnDisponible: false` y un aviso en el log. No es un 503: el acceso no depende de TURN.
- **Sin `Video:Ice:StunUrls`**: queda `stun:stun.l.google.com:19302`, el que usan hoy las pantallas. Con coturn
  propio conviene apuntarlo ahí.

Del lado de coturn (`deploy/coturn/turnserver.conf`), el secreto es el mismo: `use-auth-secret` y
`static-auth-secret=<el valor de Video__TurnSecreto>`, que escribe `deploy/video/generar-secretos.sh` sin mostrarlo.

## El frontend

Las dos pantallas de la sala (`Pantalla_Paciente/sesion_video.html` y `Pantalla_Profesional/sesion_video.html`) usan el
contrato de arriba a través de `PANTALLAS/shared/video.js` (`window.YS.video`). Ninguna decide la sala, el rol ni los
servidores ICE: **solo dicen de qué sesión quieren entrar** y usan lo que devuelve la API. Socket.IO sigue
vendoreado en `PANTALLAS/shared/vendor/`: no hay scripts de terceros.

### Cómo se abre la sala

| Quién | URL | Quién la arma |
|---|---|---|
| Paciente | `sesion_video.html?turno=<IdTurno>` o `?admision=<IdInforme>` | (la sesión viene en la URL: no hace falta el código) |
| Paciente | `sesion_video.html?sala=<código>` | las notificaciones y el campo "Código de sala" del panel del paciente |
| Profesional | `sesion_video.html?turno=<IdTurno>&idPaciente=<id>` | el panel: "Entrar a la sala →" de las sesiones de hoy, "Ir a sala" y la cita urgente |
| Profesional | `sesion_video.html` sin parámetros | el menú "Sala de video": un selector con los turnos confirmados que ya tienen sala (de hoy en adelante); con `?idPaciente=` queda elegida la sesión de ese paciente |
| Configurador | `Pantalla_Profesional/sesion_video.html?sala=ADM-…` (o `?admision=<IdInforme>`) | el panel de admisión, hoy con `?sala=` |

Con `?sala=` solo hay un código, y los códigos que genera la API llevan el id de su origen (`SALA-<IdTurno>-…` y
`ADM-<IdInforme>-…`): de ahí la pantalla saca **qué sesión pedir**. El código no abre nada por sí mismo: la API verifica
que la persona sea parte de esa sesión y devuelve la sala vigente (si el código es viejo porque la sala se volvió a
generar, igual se entra a la actual). Un código con otro formato se rechaza en la pantalla, sin llamar a la API.

La sala **ya no se crea en la pantalla del profesional** (antes inventaba un código con su id y la hora, y ese código
no tenía que ver con el del turno): la genera el turno (`PUT /turnos/{id}/generar-sala`, "Iniciar sesión" en el panel)
y la pantalla solo la usa. Tampoco hay ningún id de relleno: si el JWT de la sesión no trae el profesional, la pantalla
lo dice y no pide su lista de pacientes ni guarda nada en la historia clínica.

### Qué hace `video.js`

1. **Acceso.** `YS.video.pedirAcceso(sesion)` hace `POST /video/acceso` con el JWT de la sesión (`YS.api`, sin
   redirigir al login ante un 401: sacaría a la persona de la página y perdería lo que ya cargó). Si la API no deja
   entrar, la pantalla muestra el motivo y **no** pide la cámara ni abre la señalización.
2. **Cámara y micrófono.** Recién con el acceso en la mano se pide `getUserMedia`.
3. **Sala.** `YS.video.entrar(...)` abre Socket.IO mandando el token en el handshake (como función: cada reconexión manda
   el token vigente), emite `join-room` **sin nada** (la sala y el rol salen del token) y arma la `RTCPeerConnection` con
   los `iceServers` de la respuesta.
4. **Quién ofrece.** Manda el rol de la API (`acceso.rol`), no el de la pantalla: quien entra como `paciente` arma las
   ofertas y quien entra como `profesional` (un Configurador incluido) responde. Así no hay ofertas cruzadas.
5. **Un solo aviso por conexión.** `ontrack` llega una vez por pista (audio y video); el video remoto se avisa una sola
   vez por conexión. En la pantalla del profesional el análisis de expresiones y el reloj de la sesión arrancan una sola
   vez (antes arrancaban dos veces, una por pista, y corrían en paralelo).
6. Los eventos de señalización se atienden de a uno y en orden, y los candidatos ICE que llegan antes de la oferta se
   guardan hasta que se pueda usar.

### Lo que dice la pantalla cuando la API no deja entrar

La pantalla vuelve a la espera con el mensaje y la persona puede reintentar.

| API | La pantalla dice |
|---|---|
| 404 | "No encontramos esa sesión. Revisá que el código sea el correcto y que la sesión sea tuya." (es el mismo texto para "no existe" y "no es tuya") |
| 409 `aun-no-abre` | "Todavía no se puede entrar a la sala: se abre a las 09:50." (o "el 14/10 a las 09:50"), en la hora de quien mira |
| 409 `ya-termino` | "Esta sesión ya terminó (la sala cerró a las 11:00)." |
| 409 `estado-invalido` | el mensaje de la API ("El turno fue cancelado.", "El turno todavía no está confirmado."…) |
| 409 `sala-no-generada` | al paciente: "Tu profesional todavía no generó la sala de esta sesión. Probá de nuevo en unos minutos."; al profesional: "Este turno todavía no tiene sala. Generala desde Inicio ("Iniciar sesión") y volvé a entrar." |
| 409 `sala-invalida` | "La sala de esta sesión no está disponible por un problema del sistema. Avisale al equipo de YoSoy." |
| 429 | "Hiciste demasiados intentos seguidos. Esperá un minuto y probá de nuevo." |
| 503 | "La videollamada no está disponible por el momento. Probá de nuevo más tarde; si sigue igual, avisale al equipo de YoSoy." (no dice nada de la configuración) |
| 401, 400, 5xx, sin conexión | cada uno tiene su texto ("Tu sesión de YoSoy venció. Ingresá de nuevo…", etc.) |

### Si algo se corta

- **El token vence.** Si el servidor corta por `sesion-vencida`, o el handshake dice `token-vencido` / `no-autorizado`, la
  pantalla pide `POST /video/acceso` de nuevo, **una vez** por episodio, y vuelve a entrar sola (con el acceso nuevo
  también se renuevan las credenciales TURN, que son efímeras). Si la API ya no deja entrar (la sesión terminó) o el
  segundo token también se rechaza, lo dice y corta; con la llamada andando no la tira: avisa y deja que la persona
  la termine. Mientras se renueva se frena el reintento automático de Socket.IO, que usaría el token vencido.
- **ICE.** Con `failed`, quien ofrece (el paciente) reinicia ICE: una oferta nueva con credenciales nuevas, sobre la misma
  conexión. Con `disconnected` espera 4 segundos (suele ser un bache) y, si sigue, reinicia. Hasta 3 reinicios; si en 30
  segundos no vuelve a `connected`, da la conexión por perdida, lo dice y vuelve a la espera. Quien no ofrece (el
  profesional) solo avisa y espera la oferta nueva. Si la otra persona ya salió de la sala no se reinicia nada ni se da la
  sala por perdida: simplemente se la espera.
- **La otra persona sale y vuelve** (recargó la página, se le cayó la red): se arma una conexión nueva en los dos
  lados. Reutilizar una conexión ya usada con alguien distinto depende de cada navegador; una limpia anda en todos.
- **La señalización se cae.** Con la llamada andando no se muestra nada (el video no depende de ella) y Socket.IO
  reintenta solo; al volver, las dos páginas vuelven a entrar a la sala. Sin llamada, tras 5 fallos seguidos se da por
  perdida.
- **Rechazos de la señalización** (evento `rechazado`): `rol-ocupado` ("Ya hay otra persona conectada en esta sala como
  profesional. Si fuiste vos desde otra pestaña, otro navegador o otro equipo, cerralo; si es otra persona, esperá a que
  salga. Después probá de nuevo."), `sala-llena`, `reemplazado` ("Entraste a esta sala desde otro lugar…": la conexión
  vieja se cierra aunque estuviera andando, para que no haya dos llamadas en paralelo) y `limite-eventos`.

### Aviso al paciente

Antes de conectarse, la pantalla del paciente dice que la sesión usa cámara y micrófono y que el profesional lo va a
ver y escuchar en vivo; mientras dura la sesión queda un recordatorio. **El texto es provisorio** (lleva el cartel "Aviso
provisorio"): no es el consentimiento informado de la atención ni el de la lectura de emociones. Este último es un panel
aparte, con su propio texto (también provisorio): ver "Lectura de emociones: consentimiento y datos sensibles".

### Transcripción (apagada)

`YS.video.FLAGS.transcripcion` es `false` y no se prende desde la URL ni desde el navegador: se cambia en el código, en
una revisión. Con la bandera apagada la pantalla del profesional ni muestra la tarjeta de transcripción (el
reconocimiento de voz del navegador manda el audio a servidores externos en Chrome; la decisión sigue pendiente: no se
trató en el PR 6). La lectura de emociones, en cambio, ahora pide consentimiento: ver la sección siguiente.

### Probar en una máquina

```powershell
# ventana 1: la app
$env:Email__Password = " "                       # no manda mails reales
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:Video__SignalingSecreto = -join ((1..64) | ForEach-Object { '{0:x}' -f (Get-Random -Maximum 16) })
$env:Video__SignalingSecreto                     # copiá el valor para la otra ventana
dotnet run

# ventana 2: la señalización, con el MISMO valor
cd signaling-server
$env:SIGNALING_SECRETO = "<el mismo valor>"
$env:SIGNALING_ORIGENES_CORS = "http://localhost:5000"
npm start
```

El profesional entra por `http://localhost:5000/pantallas/index.html`, genera la sala del turno ("Iniciar sesión") y entra
con "Entrar a la sala →"; el paciente entra desde su notificación o escribiendo el código. Para probar sin cámaras ni
micrófonos, Chrome con `--use-fake-device-for-media-stream --use-fake-ui-for-media-stream`. Los pasos con `docs/DEMO.md`.

## Lectura de emociones: consentimiento y datos sensibles

> **Todo lo legal de esta sección es provisorio.** El texto que lee el paciente, el plazo de conservación, el trato de
> los menores y la relación con la historia clínica son una propuesta técnica que **tiene que validar un profesional
> legal** antes de usarse con pacientes reales. Nada de lo que sigue afirma que se cumpla una norma: al final de la
> sección está la lista de lo que hay que verificar.

### Qué hace la lectura de emociones y qué se guardaba

- **Dónde corre.** En el navegador del profesional (o del Configurador, en una entrevista de admisión), sobre el video
  que recibe del paciente, con `face-api.js` y sus tres modelos de `PANTALLAS/models/` (detector, puntos de la cara y
  expresiones). El video no sale del navegador: **no hay análisis en el servidor ni se envía ninguna imagen**.
- **Qué produce.** Unas dos veces por segundo, siete números de 0 a 1: `neutral`, `happy`, `sad`, `angry`, `fearful`,
  `surprised` y `disgusted`. Es una estimación automática de la expresión del rostro, no un diagnóstico.
- **Qué se guarda.** Al tocar "Guardar en HC": el **promedio** de cada expresión durante la sesión, cuántas muestras
  fueron y de cuándo a cuándo se tomaron. No se guarda cada muestra ni ninguna imagen. Va en
  `SesionClinica.HistorialEmociones`.
- **Cómo era antes del PR 6.** La pantalla mandaba ese resumen como un texto JSON cualquiera, la API lo guardaba **en
  claro** sin preguntar nada a nadie, y `GET /pacientes/{id}/sesiones-clinicas` y
  `GET /profesionales/{id}/sesiones-clinicas` lo devolvían a cualquier rol con acceso clínico, el Configurador incluido
  (su pantalla nunca lo mostró, pero la API se lo mandaba).

### Lo que decide el PR 6

| Tema | Decisión |
|---|---|
| Consentimiento | **Por sesión** (un turno o una entrevista de admisión), explícito y **sin opción premarcada**: sin una aceptación no hay análisis. Decide solo el paciente de esa sesión. Queda registrado con la versión y el hash del texto que leyó y la fecha y hora UTC. |
| Si no acepta | **La videollamada anda igual**, sin análisis. Puede cambiar de idea después. |
| Revocar | En cualquier momento, durante la sesión o después. Corta el análisis (la pantalla del profesional se entera en unos segundos), descarta lo acumulado y **anula las emociones ya guardadas de esa sesión**. **Nunca** toca las notas clínicas, la transcripción ni el tipo de cierre. Queda auditado con la cantidad de sesiones anuladas. |
| Autoridad | **El servidor**: guarda solo con una aceptación vigente y lo vuelve a validar todo (ver "Qué valida el servidor al guardar"). La pantalla pregunta y obedece. |
| Menores | **Decisión provisoria, a validar legalmente**: a una persona menor de 18 años, con un tutor cargado, o de la que no se conoce la fecha de nacimiento, **no se le ofrece** la lectura. La llamada anda igual. |
| Quién lo lee | El profesional con acceso clínico al paciente (la API se lo devuelve descifrado, con el formato de siempre). **El Configurador no**: la API no le devuelve `historialEmociones` ni nada que diga qué contenía. |
| Cifrado | **AES-256-GCM** en la aplicación, antes de escribir en la base, con una clave que va por variable de entorno. Ver "Cifrado". |
| Retención | `Video__Emociones__RetencionDias` = **3650** (provisorio), con una purga que **arranca deshabilitada**: sin el flag solo dice en el log cuántas filas anularía. |
| Lo ya guardado en claro | No se toca solo: hay un recifrado manual, en simulación por defecto, que corrés vos después de tu backup. Ver "Recifrar lo que ya estaba guardado". |

### Contrato de la API

Los cuatro endpoints nuevos piden el JWT de la sesión de YoSoy (no sirve el token de sala), nunca se cachean
(`Cache-Control: no-store`), tienen su propia política de rate limit por IP (`video-consentimiento`,
`Video__Emociones__LimitePorMinuto`, 240 por minuto) y tratan igual a quien no es parte de la sesión que a una sesión
que no existe: el mismo `404`, con el mismo cuerpo (como `POST /video/acceso`). `origen` e `id` dicen **de qué sesión**
se habla; quién es parte lo decide el servidor con la base.

| Endpoint | Quién | Qué hace |
|---|---|---|
| `GET /video/consentimiento?origen=&id=` | El paciente de la sesión | Su estado y, si se le puede ofrecer, el texto que tiene que leer. |
| `PUT /video/consentimiento` | El paciente de la sesión | Decide: `{ "origen", "id", "decision": "acepta" \| "rechaza", "version" }`. |
| `POST /video/consentimiento/revocar` | El paciente de la sesión | Deja de compartir: `{ "origen", "id" }`. Anula lo ya guardado. Anda aunque la sala ya haya cerrado y se puede repetir. |
| `GET /video/emociones/permiso?origen=&id=` | El profesional del turno, o un Configurador | ¿Se puede analizar ahora? Es lo que pregunta la pantalla cada pocos segundos. |

`GET /video/consentimiento` contesta `200`:

```json
{
  "disponible": true,
  "estado": "sin-decision",
  "motivo": null,
  "version": "v0-provisorio",
  "provisorio": true,
  "titulo": "Lectura de expresiones faciales durante la videollamada",
  "texto": ["TEXTO PROVISORIO. Todavía no fue validado por un profesional legal…", "Qué es. …"],
  "decididoUtc": null,
  "revocadoUtc": null
}
```

- `estado`: `sin-decision` · `acepta` · `rechaza` · `revocado` · `no-disponible`. Con `no-disponible` no hay texto y
  `motivo` dice por qué: `menor-de-edad`, `edad-desconocida` o `no-configurado` (falta la clave de cifrado).
- `version` es la del texto **vigente**: es la que hay que repetir al decidir. Si el texto cambió desde que la persona lo
  leyó, `PUT` contesta `409 version-desactualizada` y la pantalla vuelve a pedirlo.
- Se puede leer y decidir desde antes de que abra la sala. Con la sesión en otro estado (turno sin confirmar, sala sin
  generar, sesión ya terminada) contesta los mismos `409` que `POST /video/acceso`. Revocar no depende de la ventana.

`PUT` y `revocar` contestan `200` con el mismo cuerpo (sin `titulo` ni `texto`). Errores de `PUT`: `400
solicitud-invalida` (decisión que no es `acepta` ni `rechaza`), `404 no-existe`, y `409` con `version-desactualizada`,
`usar-revocar` (ya aceptó: dejar de compartir es `revocar`, que además anula lo guardado), `menor-de-edad`,
`edad-desconocida`, `no-configurado`, o los de la sala (`estado-invalido`, `sala-no-generada`, `sala-invalida`,
`ya-termino`, igual que `/video/acceso`).
La misma decisión sobre el mismo texto, repetida, no agrega otra fila.

`GET /video/emociones/permiso` contesta `200`:

```json
{
  "permitido": true,
  "estado": "acepta",
  "motivo": null,
  "desdeUtc": "2026-10-14T13:02:11Z",
  "revocadoUtc": null,
  "ahoraUtc": "2026-10-14T13:05:40Z",
  "consultarCadaSegundos": 3,
  "vigenciaSegundos": 10,
  "version": "v0-provisorio"
}
```

`permitido` es `true` solo con `estado: "acepta"` (una aceptación vigente, de alguien a quien se le puede ofrecer).
`consultarCadaSegundos` y `vigenciaSegundos` son lo que la pantalla tiene que respetar: preguntar cada tanto, y frenar si
pasa la vigencia sin una respuesta. `desdeUtc` es cuándo aceptó: lo que se guarda no puede empezar antes.

**`POST /sesiones-clinicas` cambió.** El campo `historialEmociones` (el texto libre de antes) se ignora. En su lugar:

```json
{
  "idPaciente": 7, "idProfesional": 3, "idTurno": 12, "notasEvolucion": "…", "tipoCierre": "Seguimiento",
  "emociones": {
    "origen": "turno", "id": 12,
    "desdeUtc": "2026-10-14T13:02:11Z", "hastaUtc": "2026-10-14T13:31:40Z",
    "muestras": 3540,
    "promedios": { "neutral": 60, "happy": 10, "sad": 20, "angry": 3, "fearful": 3, "surprised": 2, "disgusted": 2 }
  }
}
```

La respuesta `201` trae `emociones: { estado, motivo, mensaje }`: `guardadas`, `descartadas` (con el `motivo` y un
`mensaje` para mostrar) o `no-enviadas`. **Descartar las emociones nunca impide guardar la sesión**: las notas y el tipo
de cierre se guardan igual. Motivos de descarte: `formato-viejo`, `sesion-invalida`, `sesion-ajena`,
`paciente-no-coincide`, `no-configurado`, `menor-de-edad`, `edad-desconocida`, `sin-consentimiento`, `rechazado`,
`revocado`, `ventana-invalida`, `ventana-anterior-al-consentimiento`, `ventana-futura`, `muestras-invalidas` y
`promedios-invalidos`.

**Lectura.** `GET /pacientes/{id}/sesiones-clinicas` y `GET /profesionales/{id}/sesiones-clinicas` siguen devolviendo
`historialEmociones` como `{ "Neutral": 60, "Felicidad": 10, … }` (o `null`), y suman `emociones`:
`{ muestras, desdeUtc, hastaUtc, textoVersion, anuladasUtc, anuladasMotivo }`. Si se anularon, `historialEmociones` es
`null` y `emociones` dice cuándo y por qué (`revocacion` o `retencion`). Para el Configurador las dos son `null`, y no hay
nada en la respuesta que indique si había emociones.

### Cómo lo cumplen las pantallas

`PANTALLAS/shared/emociones.js` (`YS.emociones`) tiene la lógica; las dos `sesion_video.html` ponen la interfaz.

- **Paciente.** Con la sesión en la URL (`?turno=`, `?admision=` o `?sala=`) pide el estado y muestra el texto que sirve
  la API, con el cartel "Texto provisorio", sin ninguna opción premarcada y con dos botones: "Acepto que se analicen mis
  expresiones" y "No acepto: la videollamada sigue igual, sin análisis". **"Conectarme" no conecta hasta que decida**
  (no pide ni el acceso a la sala). Durante la sesión un recuadro dice si se están analizando sus expresiones y ofrece
  "Dejar de compartir mis expresiones" (o "Permitir…" si no había aceptado). Si no se puede saber el estado, no se
  ofrece nada y la videollamada sigue. A una persona menor, o si el servidor no tiene configurado el cifrado, el panel
  ni aparece.
- **Profesional / Configurador.** Pregunta `GET /video/emociones/permiso` al entrar y cada `consultarCadaSegundos`.
  - **Sin permiso el modelo ni se llama.** No dibuja, no acumula, y la tarjeta queda apagada diciendo por qué.
  - **Falla cerrada.** Si pasan `vigenciaSegundos` sin una respuesta válida (sin red, 5xx, 429) el análisis se **pausa**:
    lo ya medido se conserva y retoma solo cuando la API vuelve a contestar. Un `401` (venció la sesión de YoSoy) también
    pausa, con su propio mensaje y sin sacar a la persona de la sala. Un `404` frena y descarta.
  - **Si el paciente rechaza o revoca, o la aceptación vigente es otra** (revocó y volvió a aceptar), lo acumulado se
    **descarta**: lo medido antes de una revocación no se guarda nunca.
  - **Guardar y exportar el reporte vuelven a preguntar.** Sin un permiso confirmado en ese momento, la sesión se guarda
    sin emociones y la pantalla dice por qué.
  - La ventana se arma con la hora del servidor (calibrada con `ahoraUtc`): el reloj del navegador puede estar corrido.

Con los tiempos de producción (preguntar cada 3 s) y la sala abierta con navegadores reales, el análisis se corta unos
2 o 3 segundos después de que el paciente toca "Dejar de compartir" (peor caso: 3 s de consulta más el pedido).

### Qué valida el servidor al guardar

Nada de lo que manda la pantalla se guarda tal cual: `GuardiaEmocionesService` lo valida contra la base y, si pasa,
**arma de nuevo** lo que se guarda. Si algo no cierra, la sesión se guarda **sin** emociones y la respuesta dice por qué.

1. La sesión de video existe y quien guarda la atiende (el profesional del turno, o un Configurador).
2. El paciente de la sesión es el de la sesión clínica que se está guardando.
3. A esa persona se le puede ofrecer el análisis (hay clave de cifrado, no es menor, se conoce su edad).
4. Hay una **aceptación vigente**: ni rechazada ni revocada. Con una revocación posterior no se guarda **nada**, ni
   siquiera lo medido antes de revocar.
5. La ventana (`desdeUtc`–`hastaUtc`) dura hasta 12 horas, no empieza antes de la aceptación (con la tolerancia de
   `ToleranciaSegundos`) ni termina en el futuro.
6. La cantidad de muestras es posible: entre 1 y `ceil(segundos × MuestrasMaxPorSegundo) + 5`.
7. Los promedios son solo las siete expresiones del modelo, de 0 a 100, y suman entre 90 y 110 (cada uno viene
   redondeado).

Lo que se cifra y se guarda es `{ "v": 1, "promedios": {…}, "muestras": n, "desdeUtc", "hastaUtc", "consentimiento": <id>,
"textoVersion": "…" }`: ninguna cadena libre que haya mandado la pantalla.

### Datos que se guardan

- **`ConsentimientoVideo`** (migración `migrations/add_consentimiento_video.sql`): una fila por **decisión** — `IdUsuario`,
  `IdPaciente`, `Origen` y `IdOrigen` (la sesión), `VersionTexto`, `HashTexto` (SHA-256 del texto exacto que se mostró),
  `Decision`, `DecididoUtc` y `RevocadoUtc`. La vigente es la más nueva de esa sesión. Una fila no se edita salvo
  `RevocadoUtc`; cambiar de idea es otra fila. Las fechas van en UTC (`DATETIME2`).
- **`SesionClinica`**: `IdConsentimientoVideo` (con qué consentimiento se guardaron las emociones),
  `EmocionesAnuladasUtc` y `EmocionesAnuladasMotivo` (`revocacion` o `retencion`). Anular pone `HistorialEmociones` en
  `NULL`; **no se borra la sesión ni se toca ninguna otra columna**.

### Cifrado

- **Mecanismo.** AES-256-GCM (`System.Security.Cryptography.AesGcm`, el que trae .NET: no se agregó ningún paquete),
  en la aplicación, antes de escribir en la base. Se eligió ese nivel porque SQL Server 2025 Express **no tiene TDE** ni
  backups cifrados, y Always Encrypted necesita un almacén de claves que este servidor no tiene.
- **Formato guardado.** `ys1.<idClave>.<base64url(nonce de 12 bytes | etiqueta de 16 | dato cifrado)>`. El nonce es
  distinto en cada cifrado. El `idClave` (`k1` por defecto) dice con qué clave se cifró, para poder rotarla algún día.
- **Atado a la fila.** El dato asociado autenticado es `ys1|<idClave>|sesion:<IdSesion>`: un valor copiado a otra fila
  no se descifra (la etiqueta no cierra), y un valor alterado tampoco.
- **La clave.** `Video__Emociones__ClaveCifrado`: base64 de 32 bytes, **solo por variable de entorno** (nunca en
  `appsettings.json`), distinta de `Jwt__Key`, `Video__SignalingSecreto` y `Video__TurnSecreto`. La crea
  `deploy/video/generar-secretos.sh` **una sola vez** y no la pisa nunca (no hay una opción para rotarla). **Si se
  pierde, lo guardado no se puede volver a leer: hay que guardar una copia fuera del servidor y fuera del repositorio,
  nunca en el chat.**
- **Sin clave** (o con una inválida) la API arranca igual, la lectura de emociones queda "no disponible" y **no se guarda
  nada en claro**; el log de arranque lo dice, sin valores: `Video: lectura de emociones habilitada…` o `deshabilitada…`.
- **Lo que no hace.** No cifra la base entera (solo este dato), y la clave vive en el mismo servidor que la API: protege
  contra una copia de la base o de un backup, no contra quien controle el servidor.
- **Lo guardado antes del PR 6** (JSON en claro) se sigue leyendo tal cual, y se cifra con el recifrado manual.

### Retención y purga

`Video__Emociones__RetencionDias` (**3650**, provisorio: el plazo lo tiene que definir el equipo legal; `0` = no se
purga nunca) y `PurgaEmocionesBackgroundService`:

- **Arranca deshabilitada.** Sin `Video__Emociones__PurgaHabilitada=true` solo escribe en el log cuántas sesiones
  superan el plazo y se anularían: `Video: purga de emociones DESHABILITADA (simulación): N sesión(es)…`.
- Habilitada, corre 2 minutos después de arrancar y cada `PurgaCadaHoras` (24), anula en lotes de `PurgaLote` (500) y
  deja una fila de auditoría con la cantidad. Anular es lo mismo que al revocar: `HistorialEmociones` en `NULL`, con
  fecha y motivo `retencion`. **Las notas clínicas no se tocan jamás.**
- Corta por `SesionClinica.FechaHora` (la fecha de la sesión).

### Recifrar lo que ya estaba guardado

Las emociones guardadas **antes** del PR 6 están en claro. No se tocan solas. El mismo ejecutable de la API trae una
herramienta manual:

```bash
YoSoy --recifrar-emociones             # SIMULACIÓN (por defecto): cuenta y dice qué haría; no modifica nada
YoSoy --recifrar-emociones --aplicar   # cifra de verdad (después de TU backup de la base)
```

Usa la misma configuración que la API (`ConnectionStrings__YoSoy` y `Video__Emociones__ClaveCifrado`). **Nunca borra ni
anula nada**: reemplaza el texto en claro por su versión cifrada, fila por fila, comprobando antes que se vuelve a
descifrar igual y actualizando solo si la fila no cambió mientras tanto. Se puede repetir (lo ya cifrado se saltea) y no
imprime la clave, ningún contenido ni ids: solo cantidades. Cómo correrla en el servidor: `docs/DESPLIEGUE_VIDEO.md`.

### Auditoría

Con `LogAuditoria`, como el resto: solo ids y códigos, nunca contenido ni claves. La hora exacta de cada decisión, en
UTC, está en `ConsentimientoVideo`; `LogAuditoria.FechaHora` es la hora del servidor.

| `Accion` | `Entidad` / `IdEntidad` | `Detalle` |
|---|---|---|
| `Consentimiento de video aceptado` / `rechazado` | `Turno` o `InformeAdmision`, y su id | `version: v0-provisorio; consentimiento: <id>` |
| `Consentimiento de video revocado` | ídem | `version: …; consentimiento: <id>; emociones anuladas: <n>` |
| `Emociones guardadas` | `SesionClinica`, `IdSesion` | `consentimiento: <id>; muestras: <n>` |
| `Emociones descartadas` | `Turno` o `InformeAdmision` (o `SesionClinica` si el pedido no dice la sesión de video) | `motivo: <código>; sesion: <IdSesion>` |
| `Purga de emociones` | `SesionClinica`, sin id (`IdUsuario` vacío: no hay una persona detrás) | `motivo: retencion; cantidad: <n>` |

### Qué hay que verificar con el equipo legal

Esta es la lista de lo que **no está resuelto** y que no se puede decidir desde el código. No es una lista cerrada de
normas ni una afirmación de que alguna se cumpla; las referencias son las que se identificaron para revisar.

1. **Naturaleza del dato.** Si la expresión facial estimada es un dato de salud o sensible (Ley 25.326, protección de
   los datos personales) en este contexto, y qué requisitos agrega.
2. **Forma del consentimiento.** Si un botón en pantalla, con registro de la versión del texto y la fecha, alcanza como
   consentimiento libre, expreso e informado (Ley 25.326; consentimiento informado de la Ley 26.529), o si hace falta
   algo más (firma, constancia por escrito). La plataforma ya tiene un consentimiento de telemedicina firmado desde el
   panel (tabla `Consentimiento`): ver si este lo integra, lo complementa o es independiente.
3. **Revocación.** La Ley 26.529 prevé la revocación del consentimiento y, para ciertos casos, que conste por escrito
   (art. 7): ver si el botón "Dejar de compartir", con su fila y su auditoría, alcanza como constancia.
4. **Historia clínica vs. dato auxiliar.** Si lo guardado forma parte de la historia clínica (que se conserva un mínimo de
   diez años, Ley 26.529) o es un dato auxiliar de la sesión. De eso depende que revocar pueda **anular** el dato y que
   se pueda purgar. El diseño anula solo las emociones y nunca las notas: validar que sea lo correcto.
5. **Plazo de conservación.** 3650 días es un valor provisorio. Definir el plazo y qué pasa al vencer.
6. **Menores y personas con tutor** (Ley 26.657 y las normas de capacidad y autonomía progresiva de niñas, niños y
   adolescentes). Hoy no se ofrece a menores de 18, ni a quien tiene un tutor cargado, ni a quien no tiene fecha de
   nacimiento. Si corresponde ofrecerlo con el consentimiento del representante, o con otro criterio de edad.
7. **Quién lo ve.** Hoy: los profesionales con acceso clínico al paciente; el Configurador, no. Revisar si el resto del
   equipo (derivaciones, supervisión) corresponde.
8. **Derechos de la persona.** Hoy el paciente puede revocar por sesión, pero no tiene una pantalla para ver lo guardado
   ni para pedir su supresión o rectificación (derechos de acceso, rectificación y supresión de la Ley 25.326).
9. **Registro y responsable de la base** (inscripción ante la autoridad de datos personales, aviso de privacidad) y las
   **medidas de seguridad** que se exigen: el cifrado de este dato es una medida técnica, no un cumplimiento.
10. **El texto.** Hoy es `v0-provisorio`: lleva tres marcas a completar (plazo, datos de contacto de la institución y
    referencias). Se publica una versión nueva como un archivo nuevo en `Services/Video/Consentimiento/`; **un texto
    publicado no se edita**.
11. **El texto del registro de cuenta.** El consentimiento de `crear_cuenta.html` menciona **"grabaciones"**, que no
    existen (la videollamada no se graba). **No se editó**: queda como pendiente para el abogado.
12. **La transcripción.** Sigue apagada (ver arriba). Si algún día se prende, necesita su propio consentimiento.

### Límites que conviene tener presentes

- **Ningún servidor puede impedir que un navegador alterado analice el video que recibe**: el análisis corre en la
  máquina del profesional. Lo que el sistema controla es lo que **se guarda**, y queda auditado.
- La clave de cifrado y la base viven en el mismo servidor.
- El consentimiento es **por sesión**: no hay un "recordar mi decisión".
- La edad se calcula con la fecha de nacimiento de la ficha del paciente; si está mal cargada, la regla se equivoca.
- Con muchas personas detrás de una misma IP, el tope de `video-consentimiento` (240 por minuto) puede dejar a alguien sin
  respuesta: la pantalla del profesional lo trata como "no se pudo confirmar" y **pausa** el análisis (falla del lado
  seguro). Se sube con `Video__Emociones__LimitePorMinuto`.

## Configuración

Todas viven en la sección `Video` y, como cualquier configuración de .NET, se pisan por variable de entorno con
doble guion bajo. Los números sin secreto están en `appsettings.json` con sus valores por defecto; **los dos
secretos nunca van en el archivo**.

| Variable de entorno | Por defecto | Qué hace |
|---|---|---|
| `Video__SignalingSecreto` | — (obligatorio para emitir) | Secreto HS256 del token de sala. Mínimo 32 caracteres, distinto de `Jwt__Key`. **El mismo valor que `SIGNALING_SECRETO`.** |
| `Video__TurnSecreto` | — | Secreto de las credenciales TURN. Mínimo 32 caracteres, distinto de `Jwt__Key`. El mismo que `static-auth-secret` de coturn. Recomendado: distinto del de la señalización. |
| `Video__Ice__StunUrls__0`, `__1`... | vacío (→ STUN de Google) | URLs STUN, una variable por URL. |
| `Video__Ice__TurnUrls__0`, `__1`... | vacío (→ sin TURN) | URLs TURN, una variable por URL (por ejemplo UDP y TCP). |
| `Video__Ventana__MinutosAntes` | `10` | Cuánto antes del inicio abre la sala (0–240). |
| `Video__Ventana__MinutosDespues` | `30` | Cuánto después del **fin** cierra la sala (0–240). |
| `Video__DuracionPorDefectoMinutos` | `30` | Duración de un turno que no cae en ninguna franja de la agenda (1–480). |
| `Video__Admision__DuracionMinutos` | `30` | Duración que se asume para la entrevista de admisión (1–480). |
| `Video__Token__MaxMinutos` | `120` | Techo de vida del token de sala (1–720). |
| `Video__Turn__TtlMinutos` | `120` | Vida de las credenciales TURN (1–1440); nunca menos que la ventana de la sesión. |
| `Video__ZonaHoraria` | vacío (la de la clínica: `Turnos__ZonaHoraria` o `America/Argentina/Buenos_Aires`) | Zona en la que están escritas las fechas de los turnos. Ver "La zona horaria". |
| `Video__LimitePorMinuto` | `20` | Pedidos por minuto y por IP a `/video/acceso` (1–10000). |
| `Video__Emociones__ClaveCifrado` | — (sin ella no hay lectura de emociones) | Clave AES-256 en base64 (32 bytes) con la que se cifran las emociones guardadas. **Solo por variable de entorno**, distinta de los otros secretos. La crea `generar-secretos.sh` una vez; si se pierde, lo guardado no se puede leer. |
| `Video__Emociones__IdClave` | `k1` | Identificador de la clave: va en cada dato cifrado. Solo cambia el día que se rote la clave. |
| `Video__Emociones__VersionTexto` | `v0-provisorio` | Versión del texto del consentimiento que se muestra (`Services/Video/Consentimiento/<versión>.txt`, incrustado en el ejecutable). Un texto publicado no se edita: uno nuevo es una versión nueva. |
| `Video__Emociones__RetencionDias` | `3650` | **Provisorio.** Días que se conservan las emociones guardadas; `0` = no se purga nunca. |
| `Video__Emociones__PurgaHabilitada` | `false` | La purga arranca **deshabilitada**: sin esto solo escribe en el log cuántas sesiones anularía. |
| `Video__Emociones__PurgaCadaHoras` | `24` | Cada cuánto corre la purga (1–720). |
| `Video__Emociones__PurgaLote` | `500` | Cuántas sesiones anula por vuelta (1–5000). |
| `Video__Emociones__ConsultaCadaSegundos` | `3` | Cada cuánto pregunta la pantalla del profesional si sigue el permiso (1–60). |
| `Video__Emociones__VigenciaSegundos` | `10` | Cuánto vale una respuesta del permiso; pasado ese tiempo sin una nueva, la pantalla frena el análisis (2–300). |
| `Video__Emociones__ToleranciaSegundos` | `5` | Holgura entre el reloj del navegador y el del servidor al validar la ventana de lo que se guarda (0–120). |
| `Video__Emociones__MuestrasMaxPorSegundo` | `4` | Tope de muestras por segundo que se acepta al guardar; la pantalla toma unas 2 (1–30). |
| `Video__Emociones__PermitirEdadDesconocida` | `false` | Con `true`, a quien no tiene fecha de nacimiento (ni tutor) se le ofrece la lectura, tratándolo como adulto. Es para una base de desarrollo; en producción, `false`. |
| `Video__Emociones__LimitePorMinuto` | `240` | Pedidos por minuto y por IP a los endpoints del consentimiento y del permiso (1–10000). |

Un valor numérico fuera del rango se acota al rango en vez de romper nada.

### Cargarlas en el servidor

Mismo esquema que `whatsapp.env` (`whatsapp-service/deploy/yosoy-api-whatsapp.conf`): un archivo de variables aparte y un
*drop-in* de systemd, sin tocar la unit original ni el `.env` principal. El paso a paso completo, con la verificación y el
rollback de cada paso, está en [`docs/DESPLIEGUE_VIDEO.md`](DESPLIEGUE_VIDEO.md) (**preparado en el PR 5; todavía no se
ejecutó en el servidor**). Lo que sigue es qué hay en cada pieza:

| En el repositorio | En el servidor | Qué es |
|---|---|---|
| `deploy/video/generar-secretos.sh` | se corre con `sudo` | Genera con `openssl rand -hex 32` el secreto de la señalización y el de TURN y los escribe, **sin mostrarlos**, en los dos `.env` y en `turnserver.conf`. Con `openssl rand -base64 32` genera además la **clave de cifrado de las emociones**, una sola vez: **no se rota sola** (no hay opción para eso) y, al crearla, avisa que hay que guardar una copia fuera del servidor y del repositorio. No pisa lo que ya existe; `--rotar-signaling` y `--rotar-turn` generan uno nuevo, con copia previa del archivo. |
| *(lo genera el script)* | `/opt/yosoy/config/video.env` (600, `yosoy`) | `Video__SignalingSecreto`, `Video__TurnSecreto`, `Video__Emociones__ClaveCifrado`, `Video__Ice__StunUrls__0`, `Video__Ice__TurnUrls__0` y `__1` (UDP y TCP) y `Video__ZonaHoraria=America/Argentina/Buenos_Aires`. Sin comillas ni espacios alrededor del `=`. |
| `deploy/yosoy-api.service.d/video.conf` | `/etc/systemd/system/yosoy-api.service.d/video.conf` | Drop-in: `EnvironmentFile=-/opt/yosoy/config/video.env`. El `-` es a propósito: si el archivo faltara, la API arranca igual y `/video/acceso` contesta 503 en vez de quedar caída. |
| *(lo genera el script)* | `/opt/yosoy/config/signaling.env` (600, `yosoy`) | `SIGNALING_SECRETO` (**el mismo valor** que `Video__SignalingSecreto`), `HOST=127.0.0.1` y `PORT=3001`. |
| `deploy/signaling-server.service` | `/etc/systemd/system/signaling-server.service` | La unit del signaling-server (ver `signaling-server/README.md`, "En el servidor"). |
| `deploy/coturn/turnserver.conf` | `/etc/turnserver.conf` | La configuración de coturn, sin ningún secreto: `static-auth-secret` lo escribe el script. |
| `deploy/caddy/video.caddy` | `/etc/caddy/video.caddy` | `/socket.io/*` hacia `127.0.0.1:3001`; se suma al `Caddyfile` con una sola línea `import`. |

**Verificar que la API quedó bien**, sin imprimir nada secreto: en `journalctl -u yosoy-api` tiene que aparecer
`Video: /video/acceso habilitado.` (si aparece `deshabilitado`, la misma línea dice por qué: secreto ausente, corto o igual
a `Jwt__Key`). El script no lee el `.env` de la API, así que no compara con `Jwt__Key`; esa comprobación la hace la propia API
al arrancar.

Si `SIGNALING_SECRETO` o `Video__SignalingSecreto` se rotan, hay que rotar los dos y reiniciar los dos servicios: los tokens
ya emitidos (hasta 2 horas) dejan de valer y la gente vuelve a pedir uno. `generar-secretos.sh --rotar-signaling` lo hace en un
solo paso (ver "Rotar un secreto más adelante" en el runbook).

## Qué queda auditado

Cada acceso concedido y cada rechazo de una sesión que existe (pertenencia, estado, horario o sala) deja una fila en
`LogAuditoria`, con los mismos criterios que las demás auditorías del sistema: solo ids y códigos, nunca contenido.

| Campo | Valor |
|---|---|
| `IdUsuario` | Quién pidió el acceso. |
| `Accion` | `Acceso a sala de video` o `Acceso a sala de video rechazado`. |
| `Entidad` / `IdEntidad` | `Turno` o `InformeAdmision`, y su id. |
| `Detalle` | `rol: <rol>; resultado: concedido` o `rol: <rol>; resultado: rechazado; motivo: <codigo>`. |

No se guarda ni la sala, ni el token, ni las credenciales. Un rechazo por pertenencia queda con `motivo: sin-acceso`
aunque por HTTP se conteste el 404 uniforme: así se puede ver quién anda probando ids que existen. Un id que no
existe, un pedido mal armado (400) y un 503 no se auditan: no hay a qué atarlos y sería una fila por cada sondeo. Si la
auditoría falla, el acceso sigue y queda un aviso en el log con el tipo de error.

Las decisiones del consentimiento de la lectura de emociones (acepta, rechaza, revoca), lo que se guarda, lo que se
descarta y la purga tienen su propia tabla en "Lectura de emociones: consentimiento y datos sensibles" → "Auditoría".

## Cabeceras de seguridad

Todas las respuestas que pasan por el middleware global de `Program.cs` (páginas incluidas) salen ahora con
`Permissions-Policy: camera=(self), microphone=(self)`: la cámara y el micrófono quedan permitidos para el propio
origen —la sala de video, también dentro del `<iframe>` del dashboard— y bloqueados para cualquier contenido embebido
de otro origen. El resto de las funciones del navegador conserva su comportamiento por defecto. **No se agrega
Content-Security-Policy**: hay mucho JS y CSS inline en `PANTALLAS/` y una política restrictiva rompería pantallas.

## Probar

Con las variables de entorno de siempre (`ConnectionStrings__YoSoy`, `Email__Password=" "`):

```bash
dotnet test --filter "FullyQualifiedName~Video|FullyQualifiedName~AccesoSala|FullyQualifiedName~TurnCredentials|FullyQualifiedName~TokenSala|FullyQualifiedName~Emociones|FullyQualifiedName~ConsentimientoVideo|FullyQualifiedName~Purga|FullyQualifiedName~TextoConsentimiento"
```

- `AccesoSalaServiceTests`, `VideoAccesoEndpointsTests`: reglas, matriz 401/404/409, bordes de la ventana, rol y
  `exp` del token, 503, ICE, auditoría, cabeceras y rate limit. Sin base (dobles en memoria).
- `TurnCredentialsServiceTests`: credenciales TURN contra vectores calculados aparte (RFC 2202, OpenSSL y Python).
- `TokenSalaServiceTests`: el token contra las reglas del README del signaling, verificado a mano (HMAC UTF-8), sin
  la librería que lo emite.
- `VideoInteropNodeTests`: el token emitido por la API, verificado con `signaling-server/token.js` (el mismo código
  del handshake) vía `node`. Necesita `node` y `npm ci` en `signaling-server/`; si falta, **se omiten** (aparecen como
  omitidos, no como aprobados) y el contrato queda cubierto solo por `TokenSalaServiceTests`.
- `VideoBaseRealTests`: contra la base local real (`OrigenSalaDAO`, la duración desde la agenda y la auditoría en
  `LogAuditoria`). Inserta filas del año 2031 y las borra al terminar.
- `signaling-server/test/video-frontend.test.js` (parte de `npm test` en `signaling-server/`): carga el `video.js` real
  en un contexto de Node, con un doble de `RTCPeerConnection`, y lo prueba contra el **servidor de señalización real**:
  qué sesión se pide, los mensajes, qué manda la página (sin sala ni rol), quién ofrece, un solo aviso por conexión,
  reinicio de ICE, conexión nueva cuando la otra persona vuelve, renovación del token (una vez), `rol-ocupado`,
  `reemplazado` y `sala-llena`. No prueba WebRTC de verdad: eso necesita navegadores.

Del consentimiento de la lectura de emociones (PR 6):

- `EmocionesCifradoServiceTests`: AES-256-GCM contra el dato atado a la fila (otra fila, otra clave o un byte cambiado no
  descifran), la validación de la clave (base64, 32 bytes, distinta de los otros secretos) y que ningún mensaje la incluya.
- `TextoConsentimientoVideoTests`: el texto embebido, su versión y su hash (fijado: si alguien edita un texto publicado, falla).
- `ConsentimientoVideoServiceTests`: la máquina de estados (sin decisión, acepta, rechaza, revoca, cambia de idea), menores,
  tutor y edad desconocida, versión del texto, pertenencia (el mismo 404) y la ventana de la sala.
- `GuardiaEmocionesServiceTests`: cada motivo de descarte, la ventana, las muestras y los promedios, y que lo que se guarda
  sale armado por el servidor.
- `PurgaEmocionesTests`: deshabilitada no toca nada, el plazo, los lotes y la auditoría.
- `ConsentimientoVideoEndpointsTests`: los cuatro endpoints y `POST /sesiones-clinicas` por HTTP, el Configurador sin emociones
  y `no-store`. Sin base (dobles en memoria).
- `EmocionesConfiguracionTests`: los valores de `appsettings.json` y que ningún archivo versionado traiga la clave.
- `RecifrarEmocionesTests`: el recifrado manual, que es lo único que modifica datos ya guardados: la simulación no toca
  nada, solo cifra lo que está en claro y vuelve igual, no pisa una fila que cambió mientras tanto, no escribe si la ida y
  vuelta no cierra, repetirlo no hace nada y sin clave no se aplica.
- `EmocionesBaseRealTests`: contra una base real que **ya tenga la migración** `add_consentimiento_video.sql` (si falta la
  tabla, o no hay cadena de conexión, **se omiten**): consentimiento, guardado cifrado, revocación, purga y el acceso a la
  base del recifrado. Marca sus filas `[TEST-PR6]` y las borra; no hace ninguna pasada global sobre filas ajenas.
- `signaling-server/test/emociones-frontend.test.js` (parte de `npm test`): el `emociones.js` real en un contexto de Node,
  con relojes y API de mentira: falla cerrada, pausa contra descarte, ventana nueva al volver a aceptar y qué se manda al
  guardar.

Con navegadores reales (Chromium, cámara falsa, la API y la señalización reales contra una base descartable) el circuito
completo se probó con un banco local que no se versiona; el modelo de expresiones se reemplazó por un doble de expresiones
fijas, porque la cámara falsa no tiene rostro.

## Pendiente / fuera de alcance

- **Despliegue** (PR 5): está preparado (unit del signaling, drop-in de la API, script de secretos, coturn, el snippet de
  Caddy y el runbook [`DESPLIEGUE_VIDEO.md`](DESPLIEGUE_VIDEO.md)) pero **todavía no se ejecutó en el servidor**. Hasta que
  estén los PR 2, 3 y 4 en producción, `/socket.io/*` no se expone: en el runbook, Caddy es el último paso.
- **Consentimiento, cifrado y retención** (PR 6): implementado. **Falta la validación legal** del texto, del plazo de
  conservación y del trato de los menores (lista en "Qué hay que verificar con el equipo legal"), y completar las tres
  marcas del texto. Antes de desplegar la API hay que aplicar la migración y generar la clave de cifrado (ver
  [`DESPLIEGUE_VIDEO.md`](DESPLIEGUE_VIDEO.md), "Paso f0"). La transcripción sigue apagada y sin decisión.
- **Sin verificar con un despliegue real.** La sala se probó en `http://localhost` con Chromium y Firefox, una máquina y
  cámara y micrófono falsos, con la API y la señalización reales. Falta: cámaras y micrófonos físicos, TURN y el relay
  (la configuración de coturn está escrita pero no se corrió con un coturn real), redes y NAT distintos, Safari y celulares,
  y HTTPS detrás de Caddy (el snippet sí se probó con un Caddy real, por HTTP; ver el final de `DESPLIEGUE_VIDEO.md`).
  Del PR 6, además: el análisis con un rostro real (en las pruebas el modelo se reemplazó por un doble), la migración
  sobre la base de producción, la custodia y la restauración de la clave de cifrado, y la comprensión del texto por parte
  de pacientes reales.
- **Panel del Configurador.** Sigue abriendo la entrevista con `?sala=ADM-…` (anda porque el código lleva el id) y guarda
  en `localStorage` el id del profesional admisionista (`ADM-001`) para poder usar la pantalla del profesional. El video
  ya no necesita ese truco (el acceso sale del JWT del Configurador); solo lo usa "Guardar en HC". Conviene pasar
  `?admision=<IdInforme>` y decidir cómo se guarda la entrevista en la historia clínica. No se tocó en este PR.
- **coturn real sin probar.** Las credenciales TURN están verificadas contra vectores calculados aparte (RFC 2202,
  OpenSSL y Python) y, en el PR 5, recalculadas a mano con el secreto que genera el script, pero no contra un coturn de
  verdad. Tampoco se sabe si coturn corta una renovación de ICE hecha después de vencidas las credenciales (por eso el TTL
  de 2 horas): conviene probar una llamada relayada larga al desplegar (paso h5 del runbook).
- **El signaling real con un socket.** El token se verifica con `signaling-server/token.js`, que es el código del
  handshake, pero no hay un test automático que levante `server.js` y conecte un cliente de socket.io con un token de la
  API. En el PR 5 se comprobó a mano, fuera del repositorio: la API real y el signaling real, arrancados con los archivos
  que escribe `generar-secretos.sh`, aceptan ese token y rechazan uno sin token o firmado con otro secreto.
- **Los 401 y 403 de la autorización** (los que corta la autenticación antes de llegar al middleware de headers) salen
  sin headers de seguridad, `Permissions-Policy` incluido; ya era así con los otros tres. Son JSON sin contenido, no
  documentos donde se use la cámara. Un 404 de este endpoint, en cambio, sí los lleva.
- **Un `PUT /turnos/{id}/generar-sala` repetido genera un código nuevo** (así funciona hoy, sin tocarlo): quien ya
  estaba en la sala con el código anterior queda en otra. No es de este PR, pero conviene saberlo.
- **Tiempos de respuesta.** El status, los headers y el cuerpo del 404 son idénticos para "no existe" y "no sos
  parte", pero el tiempo no: el segundo caso lee la agenda y escribe la auditoría. Es una diferencia de milisegundos
  que, con el rate limit (20 pedidos por minuto por IP), no alcanza para sondear en la práctica; igualarla requeriría
  auditar también los ids que no existen o auditar en segundo plano.
