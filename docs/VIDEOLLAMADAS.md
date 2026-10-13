# Videollamadas — acceso a las salas, token de sala y servidores ICE

Cómo entra una persona a una sala de video: qué le pregunta el frontend a la API, qué decide la API, qué
recibe, y qué hay que configurar en el servidor. Cubre **solo la API** (PR 3 del plan de videollamadas). Lo que
falta: el frontend que usa esto (PR 4), el despliegue de la señalización, Caddy y coturn (PR 5) y el
consentimiento y la privacidad (PR 6).

El contrato del token con el servidor de señalización está en [`signaling-server/README.md`](../signaling-server/README.md)
("Token de sala"). Esta página es el lado de la API.

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

```js
// Ejemplo para el frontend (PR 4)
const r = await fetch(API + '/video/acceso', {
  method: 'POST',
  headers: { Authorization: 'Bearer ' + jwt, 'Content-Type': 'application/json' },
  body: JSON.stringify({ origen: 'turno', id })
});
if (!r.ok) { const e = await r.json(); /* e.codigo y e.mensaje */ return; }
const { token, iceServers } = await r.json();
const socket = io(SIGNAL, { auth: { token } });
const pc = new RTCPeerConnection({ iceServers });
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

Del lado de coturn (se arma en el PR 5), el secreto es el mismo: `use-auth-secret` y
`static-auth-secret=<el valor de Video__TurnSecreto>`.

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

### Cargarlas en el servidor (solo documentado; todavía no se despliega)

Mismo esquema que `whatsapp.env` (`whatsapp-service/deploy/yosoy-api-whatsapp.conf`): un archivo de variables
aparte y un *drop-in* de systemd, sin tocar la unit original ni el `.env` principal.

**1. Generar los secretos** (uno para cada cosa, distintos entre sí y distintos de `Jwt__Key`):

```bash
openssl rand -hex 32     # → Video__SignalingSecreto  (y SIGNALING_SECRETO, el mismo valor)
openssl rand -hex 32     # → Video__TurnSecreto       (y static-auth-secret de coturn, el mismo valor)
```

**2. Crear `/opt/yosoy/config/video.env`**, dueño `yosoy`, permisos `600`:

```bash
sudo install -m 600 -o yosoy -g yosoy /dev/null /opt/yosoy/config/video.env
sudoedit /opt/yosoy/config/video.env
```

```
Video__SignalingSecreto=<el primer valor>
Video__TurnSecreto=<el segundo valor>
Video__Ice__StunUrls__0=stun:yosoytelemedicina.tech:3478
Video__Ice__TurnUrls__0=turn:yosoytelemedicina.tech:3478?transport=udp
Video__Ice__TurnUrls__1=turn:yosoytelemedicina.tech:3478?transport=tcp
Video__ZonaHoraria=America/Argentina/Buenos_Aires
```

(Los hosts son un ejemplo; el real se define con coturn en el PR 5. Sin comillas ni espacios alrededor del `=`.)

**3. Crear el drop-in `/etc/systemd/system/yosoy-api.service.d/video.conf`:**

```ini
[Service]
EnvironmentFile=/opt/yosoy/config/video.env
```

**4. Recargar y reiniciar:** `sudo systemctl daemon-reload && sudo systemctl restart yosoy-api`.

**5. Verificar** sin imprimir nada secreto: en `journalctl -u yosoy-api` tiene que aparecer
`Video: /video/acceso habilitado.` (si aparece `deshabilitado`, la misma línea dice por qué).

El signaling-server lee `SIGNALING_SECRETO` de **su propio** archivo de entorno (su unit se define en el PR 5), con
**el mismo valor** que `Video__SignalingSecreto`. Si uno de los dos se rota, hay que rotar los dos y reiniciar los dos
servicios: los tokens ya emitidos (hasta 2 horas) dejan de valer y la gente vuelve a pedir uno.

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

## Pendiente / fuera de alcance

- **Frontend** (PR 4): pedir el acceso, mandar el token al signaling, usar `iceServers`, mostrar los mensajes de los
  409. Hasta entonces las pantallas actuales no conectan con el signaling.
- **Despliegue** (PR 5): unit del signaling, `/socket.io/*` en Caddy, coturn y los puertos de UFW.
- **Consentimiento, cifrado y retención** (PR 6).
- **coturn real sin probar.** Las credenciales TURN están verificadas contra vectores calculados aparte (RFC 2202,
  OpenSSL y Python), pero no contra un coturn de verdad. Tampoco se sabe si coturn corta una renovación de ICE hecha
  después de vencidas las credenciales (por eso el TTL de 2 horas): conviene probar una llamada relayada larga en el
  PR 5.
- **El signaling real con un socket.** El token se verifica con `signaling-server/token.js`, que es el código del
  handshake, pero no hay un test que levante `server.js` y conecte un cliente de socket.io con un token de la API.
- **Los 401 y 403 de la autorización** (los que corta la autenticación antes de llegar al middleware de headers) salen
  sin headers de seguridad, `Permissions-Policy` incluido; ya era así con los otros tres. Son JSON sin contenido, no
  documentos donde se use la cámara. Un 404 de este endpoint, en cambio, sí los lleva.
- **Un `PUT /turnos/{id}/generar-sala` repetido genera un código nuevo** (así funciona hoy, sin tocarlo): quien ya
  estaba en la sala con el código anterior queda en otra. No es de este PR, pero conviene saberlo.
- **Tiempos de respuesta.** El status, los headers y el cuerpo del 404 son idénticos para "no existe" y "no sos
  parte", pero el tiempo no: el segundo caso lee la agenda y escribe la auditoría. Es una diferencia de milisegundos
  que, con el rate limit (20 pedidos por minuto por IP), no alcanza para sondear en la práctica; igualarla requeriría
  auditar también los ids que no existen o auditar en segundo plano.
