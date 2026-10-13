# signaling-server

Servidor de señalización WebRTC (Socket.IO) de las sesiones de video de YoSoy. Reenvía `offer`, `answer` y
candidatos ICE entre las dos personas de una sala (un profesional y un paciente) y nada más: no guarda estado,
no usa la base y no llama a la API. Quién puede entrar a qué sala lo decide la API al emitir un **token firmado**
(ver "Token de sala").

> **No se expone todavía.** Este servicio **no se publica en Caddy hasta que estén en producción este PR
> (señalización segura), el que emite el token desde la API (PR 3) y el del frontend que lo manda (PR 4)**. El
> puerto 3001 **no se abre nunca en UFW**: Caddy llega por loopback. El despliegue (unit de systemd, Caddy) va en
> el PR 5; nada de eso está en este directorio.
>
> **Cambio incompatible:** desde este PR el servidor exige el token. Las pantallas actuales (`sesion_video.html`)
> todavía no lo mandan, así que **no conectan hasta el PR 4**. Para probar a mano mientras tanto, ver
> "Emitir un token para pruebas".

## Cómo se corre

Requiere Node 22 o más nuevo (es el de la VPS).

```bash
cd signaling-server
npm ci
export SIGNALING_SECRETO="$(openssl rand -hex 32)"          # en PowerShell: $env:SIGNALING_SECRETO = "..."
export SIGNALING_ORIGENES_CORS="http://localhost:5000"      # solo en desarrollo, ver abajo
npm start                                                   # node server.js
npm test                                                    # tests (node:test + socket.io-client)
```

Si falta `SIGNALING_SECRETO`, o tiene menos de 32 caracteres, o cualquier otra variable es inválida, el proceso
**no arranca**: sale con código 1 y un mensaje claro. No hay secreto por defecto, ni modo "LAN", ni modo "dejar
pasar si el backend no responde" (el servidor no depende de ningún backend).

En desarrollo la página la sirve la API en `http://localhost:5000` y la señalización está en otro puerto, o sea
en otro origen: hay que listar ese origen en `SIGNALING_ORIGENES_CORS`. En producción las dos están detrás del
mismo dominio (Caddy) y no hace falta nada.

## Variables de entorno

| Variable | Obligatoria | Por defecto | Qué hace |
|---|---|---|---|
| `SIGNALING_SECRETO` | **sí** | — | Secreto HS256 con el que la API firma los tokens de sala. Mínimo 32 caracteres (`openssl rand -hex 32`). **Distinto de `Jwt__Key`** de la API: así este proceso no puede falsificar un login (si el entorno del proceso trae una `Jwt__Key` con el mismo valor, no arranca). Nunca va en el repo. |
| `HOST` | no | `127.0.0.1` | Dónde escucha. Dejalo en loopback. |
| `PORT` | no | `3001` | Puerto. |
| `SIGNALING_ORIGENES_CORS` | no | vacío | Orígenes permitidos para CORS, separados por coma (`http://localhost:5000,https://...`), uno por uno y sin barra final. Vacío = no se manda ningún header CORS. `*` no se admite: el proceso no arranca. |
| `SIGNALING_MAX_PAYLOAD_BYTES` | no | `65536` | Tamaño máximo de un mensaje. Si se pasa, se corta la conexión del que lo mandó. |
| `SIGNALING_MAX_HANDSHAKES_POR_MIN` | no | `60` | Conexiones nuevas por IP real y por minuto. |
| `SIGNALING_MAX_EVENTOS_POR_10S` | no | `100` | Eventos por socket cada 10 segundos. |

## Token de sala

Es el contrato con la API (PR 3). El cliente lo manda en el handshake: `io(url, { auth: { token } })`. Va en
`auth` y no en la URL para que no quede en logs de acceso.

Desde el PR 3 lo emite la API con `POST /video/acceso` (ver [`docs/VIDEOLLAMADAS.md`](../docs/VIDEOLLAMADAS.md)): el
`SIGNALING_SECRETO` de acá y el `Video__SignalingSecreto` de la API tienen que ser **el mismo valor**.

JWT firmado con **HS256** usando `SIGNALING_SECRETO` interpretado como UTF-8 (en .NET:
`Encoding.UTF8.GetBytes(secreto)`). Cualquier otro algoritmo se rechaza, incluido `alg: none`.

| Claim | Tipo | Obligatorio | Detalle |
|---|---|---|---|
| `sala` | string | sí | Código de la sala. `[A-Za-z0-9_.:-]`, de 1 a 100 caracteres. |
| `rol` | string | sí | `"profesional"` o `"paciente"`, exacto y en minúscula. |
| `sub` | string | sí | Id del usuario. `[A-Za-z0-9_.:-]`, de 1 a 64 caracteres. Un entero también sirve (se lee como texto). |
| `exp` | número | sí | Vencimiento, en segundos desde 1970. **Un token sin `exp` se rechaza.** |

```json
{ "sala": "SALA-12-AB34CD", "rol": "paciente", "sub": "34", "exp": 1760400000 }
```

- `exp` es hasta cuándo vale la sesión (fin del turno más un margen). Además de rechazar tokens vencidos en el
  handshake, **el servidor corta el socket al llegar a `exp`**, sin consultar a la API, y avisa al otro
  participante. Conviene que los tokens sean cortos: este servidor no sabe si la API canceló el turno antes.
- La sala y el rol salen **solo** del token. Lo que el cliente mande como `roomId` / `role` se ignora.
- Ejemplo orientativo para quien lo emita desde .NET (los nombres de claim van cortos, tal cual, para que el
  mapeo de `JwtSecurityTokenHandler` no los cambie):

  ```csharp
  var clave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretoSignaling));
  var token = new JwtSecurityToken(
      claims: new[] { new Claim("sala", codigoSala), new Claim("rol", "paciente"),
                      new Claim(JwtRegisteredClaimNames.Sub, idUsuario.ToString()) },
      expires: finDeLaSesionUtc,
      signingCredentials: new SigningCredentials(clave, SecurityAlgorithms.HmacSha256));
  var jwt = new JwtSecurityTokenHandler().WriteToken(token);
  ```

### Emitir un token para pruebas

Solo para desarrollo (en producción los emite la API). Necesita el mismo `SIGNALING_SECRETO`:

```bash
npm run token -- --sala SALA-12-AB34CD --rol paciente --sub 34 --minutos 30
```

Imprime solo el token. Se niega si los valores no pasarían la validación del servidor.

## Protocolo

**Cliente → servidor.** Todo lo demás se descarta.

| Evento | Payload | Qué hace |
|---|---|---|
| `join-room` | (se ignora) | Ocupa el asiento del rol en la sala del token. |
| `offer` | `{ offer: { type: "offer", sdp } }` | Se reenvía a la otra persona de la sala. |
| `answer` | `{ answer: { type: "answer", sdp } }` | Ídem. |
| `ice-candidate` | `{ candidate: { candidate, sdpMid, sdpMLineIndex, usernameFragment } }` | Ídem. |

Solo se reenvía si el socket ya hizo un `join-room` aceptado, y solo a la otra persona **de la sala del token**
(el `roomId` que traiga el payload no cuenta). Se reenvía una copia saneada: `sdp` de hasta 30 000 caracteres y
solo los campos de arriba; lo mal formado se descarta.

**Servidor → cliente.**

| Evento | Payload | Cuándo |
|---|---|---|
| `joined` | `{ rol }` | Se aceptó el `join-room`. |
| `user-joined` | `{ rol }` | La otra persona entró (o volvió a entrar). |
| `user-left` | `{ rol }` o `{ rol, motivo: "sesion-vencida" }` | La otra persona se fue. |
| `rechazado` | `{ motivo, codigo }` | El servidor lo saca de la sala y corta la conexión. `motivo` es un texto para mostrar. |

Códigos de `rechazado`:

| `codigo` | Cuándo |
|---|---|
| `sala-llena` | Intenta entrar un tercero a una sala que ya tiene profesional y paciente. |
| `rol-ocupado` | Ya hay otra persona (otro `sub`) con ese rol y el otro asiento está libre. |
| `reemplazado` | El mismo usuario entró desde otra conexión: la nueva reemplaza a la vieja. |
| `sesion-vencida` | Llegó el `exp` del token. |
| `limite-eventos` | Mandó más eventos que `SIGNALING_MAX_EVENTOS_POR_10S`. |

**Errores de handshake** (llegan como `connect_error`, con `error.message` y `error.data.codigo`):

| `message` | `data.codigo` | Cuándo |
|---|---|---|
| `no autorizado` | `no-autorizado` | Falta el token, es inválido, otra firma o algoritmo, sin `exp`, o con claims inválidos. No se distingue a propósito. |
| `la sesión ya terminó` | `token-vencido` | Token bien firmado pero vencido. |

Superar `SIGNALING_MAX_HANDSHAKES_POR_MIN` corta la conexión antes del handshake: el cliente ve un error de
transporte y reintenta con espera.

### Asientos

Cada sala tiene dos asientos, uno por rol. Si el asiento está ocupado por **otro** `sub`, el que llega es
rechazado. Si lo ocupa el **mismo** `sub` (se le cayó la red, recargó la página), la conexión nueva reemplaza a la
vieja, que puede seguir "viva" hasta 45 s por el ping timeout; el otro participante ve un `user-joined` y nunca un
`user-left`. La verificación de asientos y su ocupación no tienen ningún `await` en el medio: dos uniones
simultáneas no pueden ganar las dos.

## Detrás de Caddy: la IP del cliente

Caddy y este proceso están en la misma máquina, así que la conexión TCP siempre llega desde `127.0.0.1`. Para
limitar por IP real se usa `X-Forwarded-For`, que Caddy agrega. Eso **es seguro solo porque el servidor escucha
en loopback**: nadie de afuera le habla directo, entonces nadie puede inventar ese header. Dos protecciones más:

- El header solo se cree cuando la conexión viene de loopback. Si alguien cambiara `HOST` y llegara una conexión
  directa, se usa la IP de origen y el header se ignora.
- De la lista se toma la **última** entrada (la que agregó el proxy). Lo que el cliente haya puesto antes queda a
  la izquierda y no cuenta.

Si algún día hay otro proxy delante de Caddy (una CDN, por ejemplo), esto hay que revisarlo.

## Qué más se defiende

- Máximo dos participantes por sala, uno por rol.
- Mensajes de hasta 64 KB (`maxHttpBufferSize`); más que eso corta la conexión.
- Sin CORS salvo la lista explícita. Sin servir el cliente de socket.io (va vendorizado en `PANTALLAS/`).
- Un handler que falla no tira el proceso.
- `npm audit` sin vulnerabilidades conocidas. Se actualizaron `ws`, `engine.io`, `socket.io-parser` y
  `socket.io-adapter` a versiones con arreglo para varios DoS por agotamiento de memoria.

Limitaciones conocidas: los contadores de límites viven en memoria (alcanza para un solo proceso; reiniciar los
pone en cero); las IPv6 se cuentan una por una, no por /64; un token vale hasta su `exp` aunque la API cancele el
turno antes.

## Logs

Salen por stdout (en la VPS, al journal de systemd), una línea por evento: `fecha EVENTO clave=valor ...`.

| Evento | Campos |
|---|---|
| `ARRANQUE` | `escuchando`, `cors`, límites |
| `CONEXION` | `ip`, `rol`, `sub`, `sala` |
| `RECHAZO` | `motivo` (y `ip`, o `rol`, `sub`, `sala` si ya estaba autenticado) |
| `DESCONEXION` | `ip`, `rol`, `sub`, `sala`, `razon` |
| `PARADA`, `ERROR` | `senal` / `detalle` |

Nunca se loguea un token ni el secreto (hay tests que lo verifican). Sí quedan la IP y el id de usuario.

## Tests

`npm test` corre `test/*.test.js` con el runner nativo de Node y `socket.io-client` 4.8.3, la misma versión que
el cliente de `PANTALLAS/shared/vendor/`. Cubren: token válido, vencido, sin `exp`, firma mala, `alg: none` y otros
algoritmos; sala y rol que declara el cliente; sala distinta; tercer participante y dos con el mismo rol (incluido
el caso simultáneo); reemplazo del mismo usuario; evento sin `join` previo; mensajes gigantes y mal formados;
corte por `exp`; límites por IP (con `X-Forwarded-For`) y por socket; CORS; logs sin tokens; y que el proceso no
arranque sin secreto, con uno débil o con configuración inválida.
