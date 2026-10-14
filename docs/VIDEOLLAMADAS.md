# Videollamadas — acceso a las salas, token de sala y servidores ICE

Cómo entra una persona a una sala de video: qué le pregunta el frontend a la API, qué decide la API, qué
recibe, qué hace la pantalla con eso, y qué hay que configurar en el servidor. Cubre **la API** (PR 3 del plan de
videollamadas), **las pantallas que la usan** (PR 4) y **cómo se despliega todo** (PR 5, en
[`docs/DESPLIEGUE_VIDEO.md`](DESPLIEGUE_VIDEO.md): señalización como servicio, secretos, coturn y Caddy; preparado,
todavía no ejecutado en el servidor). Lo que falta: el consentimiento y la privacidad (PR 6).

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

La base guarda fecha y hora **sin zona**: es la hora de la clínica. El sistema asume que es la misma que la del
servidor (lo mismo supone el recordatorio de turnos). Si el VPS corre en UTC y la clínica está en Argentina, hay dos
caminos:

- poner la zona del servidor (`sudo timedatectl set-timezone America/Argentina/Buenos_Aires`), que afecta a todo el
  sistema (recordatorios incluidos), o
- poner `Video__ZonaHoraria=America/Argentina/Buenos_Aires`, que afecta **solo** a las salas de video.

Si el valor no existe en el servidor, la API usa la zona del servidor y deja un aviso en el log.

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
provisorio"): no es el consentimiento informado, que se incorpora en el PR 6 y reemplaza a este.

### Transcripción (apagada)

`YS.video.FLAGS.transcripcion` es `false` y no se prende desde la URL ni desde el navegador: se cambia en el código, en
una revisión. Con la bandera apagada la pantalla del profesional ni muestra la tarjeta de transcripción (el
reconocimiento de voz del navegador manda el audio a servidores externos en Chrome; la decisión es del PR 6). La lectura
de emociones no se tocó.

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
| `Video__ZonaHoraria` | vacío (zona del servidor) | Zona en la que están escritas las fechas de los turnos. |
| `Video__LimitePorMinuto` | `20` | Pedidos por minuto y por IP a `/video/acceso` (1–10000). |

Un valor numérico fuera del rango se acota al rango en vez de romper nada.

### Cargarlas en el servidor

Mismo esquema que `whatsapp.env` (`whatsapp-service/deploy/yosoy-api-whatsapp.conf`): un archivo de variables aparte y un
*drop-in* de systemd, sin tocar la unit original ni el `.env` principal. El paso a paso completo, con la verificación y el
rollback de cada paso, está en [`docs/DESPLIEGUE_VIDEO.md`](DESPLIEGUE_VIDEO.md) (**preparado en el PR 5; todavía no se
ejecutó en el servidor**). Lo que sigue es qué hay en cada pieza:

| En el repositorio | En el servidor | Qué es |
|---|---|---|
| `deploy/video/generar-secretos.sh` | se corre con `sudo` | Genera con `openssl rand -hex 32` el secreto de la señalización y el de TURN y los escribe, **sin mostrarlos**, en los dos `.env` y en `turnserver.conf`. No pisa lo que ya existe; `--rotar-signaling` y `--rotar-turn` generan uno nuevo, con copia previa del archivo. |
| *(lo genera el script)* | `/opt/yosoy/config/video.env` (600, `yosoy`) | `Video__SignalingSecreto`, `Video__TurnSecreto`, `Video__Ice__StunUrls__0`, `Video__Ice__TurnUrls__0` y `__1` (UDP y TCP) y `Video__ZonaHoraria=America/Argentina/Buenos_Aires`. Sin comillas ni espacios alrededor del `=`. |
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

## Cabeceras de seguridad

Todas las respuestas que pasan por el middleware global de `Program.cs` (páginas incluidas) salen ahora con
`Permissions-Policy: camera=(self), microphone=(self)`: la cámara y el micrófono quedan permitidos para el propio
origen —la sala de video, también dentro del `<iframe>` del dashboard— y bloqueados para cualquier contenido embebido
de otro origen. El resto de las funciones del navegador conserva su comportamiento por defecto. **No se agrega
Content-Security-Policy**: hay mucho JS y CSS inline en `PANTALLAS/` y una política restrictiva rompería pantallas.

## Probar

Con las variables de entorno de siempre (`ConnectionStrings__YoSoy`, `Email__Password=" "`):

```bash
dotnet test --filter "FullyQualifiedName~Video|FullyQualifiedName~AccesoSala|FullyQualifiedName~TurnCredentials|FullyQualifiedName~TokenSala"
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

## Pendiente / fuera de alcance

- **Despliegue** (PR 5): está preparado (unit del signaling, drop-in de la API, script de secretos, coturn, el snippet de
  Caddy y el runbook [`DESPLIEGUE_VIDEO.md`](DESPLIEGUE_VIDEO.md)) pero **todavía no se ejecutó en el servidor**. Hasta que
  estén los PR 2, 3 y 4 en producción, `/socket.io/*` no se expone: en el runbook, Caddy es el último paso.
- **Consentimiento, cifrado y retención** (PR 6): reemplaza el aviso provisorio del paciente y decide qué pasa con la
  transcripción (apagada) y con la lectura de emociones.
- **Sin verificar con un despliegue real.** La sala se probó en `http://localhost` con Chromium y Firefox, una máquina y
  cámara y micrófono falsos, con la API y la señalización reales. Falta: cámaras y micrófonos físicos, TURN y el relay
  (la configuración de coturn está escrita pero no se corrió con un coturn real), redes y NAT distintos, Safari y celulares,
  y HTTPS detrás de Caddy (el snippet sí se probó con un Caddy real, por HTTP; ver el final de `DESPLIEGUE_VIDEO.md`).
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
