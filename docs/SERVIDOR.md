# Servidor de producción

Estado del VPS donde corre YoSoy. No hay secretos en este archivo (ni contraseñas, ni claves, ni el contenido de
ningún `.env`) —solo **nombres** de variables—: los valores reales están en `/opt/yosoy/config/` en el servidor, que no
se versiona. Cómo se publica una versión nueva: [`DEPLOY.md`](DEPLOY.md). El paso a paso de las videollamadas:
[`DESPLIEGUE_VIDEO.md`](DESPLIEGUE_VIDEO.md).

> **De dónde sale lo que dice este archivo.** De los runbooks del repositorio (`DESPLIEGUE_VIDEO.md`,
> `whatsapp-service/README.md`, `deploy/`) y de lo informado por el equipo después de los despliegues. **No se
> verificó contra el servidor al escribirlo**: la sección "Verificación rápida" tiene los comandos para comprobarlo
> (solo lectura, ninguno imprime un secreto). La sección "Estado de producción al 19/10/2026" la informó quien ejecutó
> ese despliegue, con lo que comprobó el 19 y el 20/10.

- **Servidor:** Ubuntu 24.04.5 LTS, IP `179.199.155.50`.
- **Dominio:** `yosoytelemedicina.tech`, apuntando a esa IP (directo, sin CDN ni proxy: TURN usa UDP y un proxy HTTP no
  lo deja pasar).
- **Zona horaria del servidor:** `America/Argentina/Buenos_Aires`.

## Estado de producción al 19/10/2026

Lo que quedó en el VPS después de desplegar el chat de WhatsApp para el personal, el 19/10/2026, a mano y con el paso a
paso de [`DEPLOY_CHAT_PERSONAL_PRODUCCION.md`](DEPLOY_CHAT_PERSONAL_PRODUCCION.md) (su "Registro de la ejecución", al
final, cuenta cómo salió). Solo hay nombres y valores que no son secretos.

- **API:** .NET 9, servicio `yosoy-api` en `127.0.0.1:5000`. Publicada el 19/10/2026 desde el commit `7889226` en
  `/opt/yosoy/app`. Para volver atrás: `/opt/yosoy/app-anterior-chat-20261019`.
- **Backups del despliegue:** `/opt/yosoy/backups/chat-20261019/` (copia del `.env`, del `Caddyfile`, de `whatsapp.env`,
  de la unit de `whatsapp-service` y del drop-in). El backup de la base está **solo en la PC del dueño del proyecto**,
  fuera del repositorio y del servidor.
- **Base `yosoy`** (SQL Server Express, **44 tablas**): **migración 17 aplicada** (`migrations/add_chat_personal_entrantes.sql`):
  `MensajeChat.IdExterno` con su índice único filtrado `UQ_MensajeChat_IdExterno`, `MensajeChat.Tipo` con su restricción
  `CHECK` y su valor por defecto, y `Conversacion.NombreContacto`. La **migración 16 no se aplicó y no hace falta**:
  `Mensajeria__AvisosPorWhatsApp` está apagada y la API no toca esa tabla.
- **Mensajería, en `/opt/yosoy/config/.env`:** `Mensajeria__ModoAtencion=Personal`, `Mensajeria__CanalActivo=Baileys` y
  `Mensajeria__SoloLocal=false`. **No están definidas** `Mensajeria__ChatSimuladoHabilitado` (con `false` se apagaría todo
  `/mensajeria`, también Chats), `Mensajeria__AvisosPorWhatsApp` ni `Mensajeria__Personal__RespuestaDeEmergencia`: valen sus
  valores por defecto (Chats habilitados, avisos apagados y mensaje de emergencia desactivado).
- **Modo Personal:** el asistente automático está apagado. Los chats son solo del Configurador (CRM → Chats): los ve y los
  contesta; los profesionales reciben 403 en `/mensajeria`. El mensaje de emergencia está desactivado y sus líneas siguen
  pendientes de validar.
- **El simulador del chat está cerrado en dos capas:** en el código (`POST /mensajeria/simulado/entrante` y
  `GET /mensajeria/simulado/conversacion` solo responden a la propia máquina: filtro `SoloDesdeLaPropiaMaquina`) y en Caddy
  (`handle /mensajeria/simulado/* { respond 404 }` en `/etc/caddy/Caddyfile`, ver "Caddy").
- **`whatsapp-service` nuevo** (Baileys `6.7.24`, versión exacta; sin navegador): usuario `yosoy`, `127.0.0.1:3002`, código en
  `/opt/yosoy/whatsapp/app`. Unit `/etc/systemd/system/whatsapp-service.service`, con endurecimiento de systemd,
  `MemoryMax=512M` y `StartLimitBurst=5` en `StartLimitIntervalSec=300` (si cae 5 veces en 5 minutos, systemd se rinde).
  Sesión en `/opt/yosoy/whatsapp/sesion-baileys` (carpeta `700`, `datos/` `700`, archivos `600`). Configuración en
  `/opt/yosoy/config/whatsapp.env` (`600`, `yosoy:yosoy`) y el drop-in `whatsapp.conf` de `yosoy-api`. Topes de envío: 30 por
  día y 10 por hora. El servicio anterior quedó en `/opt/yosoy/whatsapp/app-anterior-20261019`.
- **Lo viejo sigue en el disco:** la sesión de whatsapp-web.js, `/opt/yosoy/whatsapp/sesion`, no se tocó. Su baja está
  pendiente: [`DEPLOY.md`](DEPLOY.md), paso 10, "Limpieza de lo viejo".
- **WhatsApp: vinculado el 20/10/2026.** El 19/10 el servicio quedó instalado y sin ningún número, a propósito; el 20/10 se
  escaneó el QR desde Configurador → WhatsApp con el teléfono dedicado de la clínica y la pantalla quedó en "Conectado". Es la
  primera conexión real del servicio a WhatsApp, y la prueba de humo con un celular personal salió bien (ver "Actualización del
  20/10/2026", abajo). El número de la clínica no está en este archivo. **Si se desvincula o se cambia de número, hay que
  actualizar esta línea**, con la fecha.
- **Pruebas del servicio:** la que falló al correr `npm test` en el VPS (`sesion.test.js`, línea 205) estaba desactualizada
  —esperaba la carpeta de la sesión sin `datos/`, que el servicio crea a propósito—, no era un defecto del servicio. Se
  corrigió en `fix/test-sesion-posix` (PR #53, ya en `main`). La suite queda en 454 pruebas: 448 pasan, 6 se saltan y 0
  fallan (en Windows se saltan las 6 de permisos POSIX).
- **Chequeos del 19 y el 20/10/2026:** el login, el pedido y la reprogramación de turnos y el registro de un paciente con
  mail (sin la casilla ni la tarjeta de WhatsApp) anduvieron bien.

### Actualización del 20/10/2026

Lo verificó quien opera el VPS, en el servidor. Es la primera conexión real del servicio a WhatsApp.

- **Vinculación:** se escaneó el QR desde Configurador → WhatsApp con el teléfono dedicado de la clínica; la pantalla quedó en
  "Conectado".
- **Prueba de humo con un celular personal:** el mensaje entró a CRM → Chats (conversación `Contacto: <nombre de perfil>`,
  "Para atender", 1 sin leer y aviso en la campanita); no hubo respuesta automática; la respuesta del Configurador llegó al
  teléfono, con "Enviar por: WhatsApp".
- **Prueba de apagado** (5.9 de [`DEPLOY_CHAT_PERSONAL_PRODUCCION.md`](DEPLOY_CHAT_PERSONAL_PRODUCCION.md)):
  `systemctl stop whatsapp-service` tardó **0,43 s** (el tope es `TimeoutStopSec=20`). Con el servicio recién arrancado,
  `Memory: 56.3M (peak 63.5M)` contra `MemoryMax=512M`.
- **Uso:** por ahora quien atiende los Chats es solo el Configurador (recepción); todavía no hay uso real de la app.
- **Sigue pendiente:** la limpieza de lo viejo ([`DEPLOY.md`](DEPLOY.md), paso 10). Siguen sin verificarse los umbrales de
  bloqueo de WhatsApp, un QR vencido real y la reconexión tras una caída.

## Cómo entra el tráfico

```
Internet ──443──► Caddy ──/socket.io/*──► signaling-server  127.0.0.1:3001   (señalización de las videollamadas)
                    └─────todo lo demás──► yosoy-api         127.0.0.1:5000   (API + pantallas)
                                              ├──► SQL Server  localhost:1433
                                              └──► whatsapp-service 127.0.0.1:3002
Internet ──3478 (UDP/TCP) y 49160–49200 (UDP)──► coturn (STUN/TURN: el relay del video cuando no hay conexión directa)
```

El video no pasa por la API ni por la señalización: va directo entre los navegadores o, cuando la red no lo permite,
por coturn.

## Usuarios

- **`agustin`**: usuario administrador. Entra por SSH solo con clave (no hay login por contraseña). Tiene sudo
  completo —pide contraseña para cualquier comando— y además una regla sin contraseña, limitada a un solo comando:
  reiniciar el servicio `yosoy-api` (`/etc/sudoers.d/yosoy-api`).
- **`yosoy`**: usuario de sistema, sin shell de login (`nologin`). Es el dueño de la app, sus carpetas y de los
  procesos de `yosoy-api`, `signaling-server` y `whatsapp-service`; no se usa para entrar al servidor.
- **`turnserver`**: usuario del paquete de coturn; es el dueño de `/etc/turnserver.conf` (grupo) y del proceso.
- **`root`**: login por SSH deshabilitado (`PermitRootLogin no` en `/etc/ssh/sshd_config`).

## Zona horaria y hora del servidor

El servidor está en **`America/Argentina/Buenos_Aires`**. Importa porque los turnos se guardan como fecha y hora
**locales, sin zona** (`Turno.Fecha` y `Turno.Hora`) y hay que saber qué hora es *en la clínica* para compararlas.

**Desde la versión que incluye `Services/RelojDeLaClinica.cs`** (rama `chore/revision-turnos`) las decisiones de
turnos **no dependen de la zona del sistema**: la API usa un solo reloj con la zona de la clínica explícita. La zona
sale, en orden, de:

1. `Turnos__ZonaHoraria` (opcional). Si está y la zona no existe en el servidor, la API **no arranca** y dice cuál es
   la clave.
2. `Video__ZonaHoraria` (la que ya está en `video.env`): significa lo mismo; si `Turnos__ZonaHoraria` no está,
   también define la zona de la clínica.
3. `America/Argentina/Buenos_Aires`.

Con ese reloj deciden que el horario no sea pasado, la cancelación con 24 h de anticipación, el recordatorio de 24 h
antes, el consentimiento y la ventana de las salas de video. Al arrancar, la API deja en el log la zona que usa
(`Zona horaria de la clínica: ...`).

Lo que **sigue tomando la hora del sistema o de SQL Server** (son marcas de tiempo, no decisiones de turnos): el
bloqueo del login (15 minutos tras 3 intentos fallidos), el vencimiento del link de recuperación de contraseña
(30 minutos) y las fechas de auditoría de altas, derivaciones y asignaciones. Por eso conviene que la zona del sistema
siga siendo la de Argentina.

Si se **cambia** la zona del sistema, conviene reiniciar `mssql-server` y `yosoy-api` para asegurarse de que la
tomen. Si el servidor se reconstruye, lo primero es `sudo timedatectl set-timezone America/Argentina/Buenos_Aires`; con otra zona,
las marcas de tiempo de auditoría y el bloqueo del login se corren (los horarios de los turnos no).

## Firewall y protección

- **UFW** activo: deniega todo lo entrante por defecto. Reglas abiertas:

| Puerto | Protocolo | Para qué |
|---|---|---|
| 22 | TCP | SSH (solo con clave; vigilado por fail2ban) |
| 80 | TCP | Caddy (redirige a HTTPS; validación de Let's Encrypt) |
| 443 | TCP | Caddy (la aplicación) |
| 3478 | UDP y TCP | coturn: STUN y TURN |
| 49160–49200 | UDP | coturn: puertos de relay |

- **No expuestos a internet** (no hay regla en UFW y escuchan en loopback o detrás del firewall): SQL Server
  (1433), la API (5000), el signaling (3001) y whatsapp-service (3002).
- Si el panel del proveedor (Hostinger) tiene firewall propio, tiene que tener las mismas reglas de arriba: si no, UFW
  abierto no alcanza.
- **fail2ban**: habilitado y activo, vigilando `sshd`.
- **coturn** corre con cuotas (`user-quota=8`, `total-quota=40`, `max-bps=500000` y `bps-capacity=10000000`, en
  bytes por segundo) y con `denied-peer-ip` para todos los rangos privados, de loopback, link-local y reservados:
  no se puede usar como puente hacia la red interna.

## Servicios

| Servicio | Qué es | Escucha | Corre como | Configuración |
|---|---|---|---|---|
| `yosoy-api` | La API y las pantallas. Binario en `/opt/yosoy/app/YoSoy` (publicado *self-contained*: el servidor no necesita el runtime de .NET). | `127.0.0.1:5000` | `yosoy` | `/opt/yosoy/config/.env` y los drop-ins `whatsapp.conf` y `video.conf` |
| `caddy` | Reverse proxy. HTTPS real, certificado de Let's Encrypt con renovación automática. **Caddy 2.7 o más nuevo** (la 2.6 no valida `video.caddy`). | 80, 443 | `caddy` | `/etc/caddy/Caddyfile` y `/etc/caddy/video.caddy` |
| `signaling-server` | Socket.IO de las videollamadas (Node 22), en `/opt/yosoy/signaling/app`. `MemoryMax=256M`. | `127.0.0.1:3001` | `yosoy` | `/opt/yosoy/config/signaling.env`; unit en `deploy/signaling-server.service` |
| `coturn` | STUN/TURN del video. Paquete de Ubuntu. | 3478 (UDP/TCP), 49160–49200 (UDP) | `turnserver` | `/etc/turnserver.conf` (640 `root:turnserver`); modelo en `deploy/coturn/turnserver.conf` |
| `whatsapp-service` | Puente HTTP interno hacia WhatsApp con **Baileys** (WhatsApp Web sin navegador): es el único motor del código. **Desde el 19/10/2026 corre el nuevo**, que reemplazó al viejo (whatsapp-web.js + Chromium, `MemoryMax=2G`): sin navegador, con `MemoryMax=512M`, instalado ese día y **vinculado el 20/10/2026** (ver "Estado de producción al 19/10/2026", arriba, y "WhatsApp (whatsapp-service con Baileys)", abajo). En `/opt/yosoy/whatsapp/app`. | `127.0.0.1:3002` | `yosoy` | `/opt/yosoy/config/whatsapp.env`; unit en `whatsapp-service/deploy/whatsapp-service.service` |
| `mssql-server` | SQL Server 2025, edición Express. | 1433 | `mssql` | — |
| `fail2ban` | Ver arriba. | — | `root` | — |

Los secretos de signaling y TURN **son los mismos** en los dos lados: `SIGNALING_SECRETO` (`signaling.env`) =
`Video__SignalingSecreto` (`video.env`), y `static-auth-secret` (`/etc/turnserver.conf`) = `Video__TurnSecreto`
(`video.env`). Los genera `deploy/video/generar-secretos.sh` sin imprimirlos.

### Caddy

`/etc/caddy/Caddyfile`:

```
yosoytelemedicina.tech {
    import /etc/caddy/video.caddy
    handle /mensajeria/simulado/* {
        respond 404
    }
    reverse_proxy localhost:5000
}
```

El bloque `handle /mensajeria/simulado/*` se agregó el 19/10/2026: es la segunda capa que cierra el simulador anónimo del chat
(la primera está en el código; ver "Estado de producción al 19/10/2026"). La copia anterior del `Caddyfile` está en
`/opt/yosoy/backups/chat-20261019/`.

`/etc/caddy/video.caddy` (copia de `deploy/caddy/video.caddy`) agrega solo una cosa: `handle /socket.io/*` →
`reverse_proxy 127.0.0.1:3001` con `stream_close_delay 5m` (que una recarga de Caddy no corte de golpe los WebSocket de
las llamadas en curso). Dentro de un bloque de sitio, Caddy evalúa `handle` antes que `reverse_proxy`: la API nunca
recibe `/socket.io/*`. El WebSocket no necesita ninguna directiva: Caddy 2 reenvía el `Upgrade` solo.

Antes de recargar: `sudo caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile`.

### WhatsApp (whatsapp-service con Baileys)

`whatsapp-service` usa **Baileys** (WhatsApp Web sin navegador, no oficial) como único motor: `whatsapp-web.js`, Puppeteer y Chromium se sacaron del código
([`PROGRESO_WHATSAPP_SOLO_BAILEYS.md`](PROGRESO_WHATSAPP_SOLO_BAILEYS.md)). **Desde el 19/10/2026 lo que corre en el VPS es el servicio nuevo**, instalado ese día y **vinculado el 20/10/2026** (ver "Estado de
producción al 19/10/2026", arriba); la limpieza de lo viejo (la sesión de whatsapp-web.js y, si quedó, Chromium) está
pendiente. El paso a paso para reemplazar el servicio viejo, con backup, limpieza de lo viejo y rollback, es [`DEPLOY.md`](DEPLOY.md), "Despliegue de whatsapp-service (Baileys)".

| | Antes (servicio viejo) | Hoy en el VPS (Baileys) |
|---|---|---|
| Librería | whatsapp-web.js + Chromium | `@whiskeysockets/baileys` 6.7.24 (versión exacta), sin navegador |
| Unit | la que estaba instalada (hay copia en `/opt/yosoy/backups/chat-20261019/`) | `whatsapp-service/deploy/whatsapp-service.service` (se instala con el mismo nombre) |
| Memoria | `MemoryMax=2G` | `MemoryMax=512M` (el 20/10/2026, con el servicio recién arrancado: `Memory: 56.3M`, pico 63.5M) |
| Sesión | `/opt/yosoy/whatsapp/sesion` | `/opt/yosoy/whatsapp/sesion-baileys` (700; adentro, `datos/` con los consentimientos y los contadores de envío) |
| Escribe en disco | `sesion/` y `cache/` | solo `sesion-baileys/` |
| Canal en la API | `Mensajeria__CanalActivo=WhatsAppWebJs` (valor que **ya no existe** en la API) | `Mensajeria__CanalActivo=Baileys` |
| Protecciones contra el bloqueo | solo 3 a 5 segundos entre mensajes: **sin consentimiento, sin topes** | consentimiento, topes por día / hora / persona, pausas al azar y "escribiendo..." |
| Salud | `GET /salud` → `{ "ok": true }` | `GET /salud` → 200 si está conectado y 503 si no, con el estado, el motivo y lo de la cola |

El servicio nuevo solo le escribe a quien aceptó (le escribió primero al número o la API marcó `optIn`), con pausas, topes por día, por hora y por persona, y sin
teléfonos ni textos en el log (`whatsapp-service/README.md`). Es un **número dedicado de la clínica**: Baileys no es la API oficial y WhatsApp puede bloquearlo.
**La API no manda recordatorios por WhatsApp** (siguen por mail y campanita): por WhatsApp salen las respuestas del asistente y del equipo y los avisos por evento. La API
(versión con el consentimiento de WhatsApp, migración 16: [`PROGRESO_CONSENTIMIENTO_WHATSAPP.md`](PROGRESO_CONSENTIMIENTO_WHATSAPP.md); **dormido por defecto**: solo rige con
`Mensajeria__AvisosPorWhatsApp=true`, y con ella apagada los avisos de turno no salen por WhatsApp) manda `optIn:true` **solo a quien
aceptó** (en el registro o en su perfil; revocable); a quien nunca decidió le manda sin `optIn` y el servicio decide (solo sale si esa persona le escribió primero al número, por
ejemplo desde el link `wa.me` que muestra el panel si está cargado `Mensajeria__WhatsAppNumeroClinica`); a quien revocó, o escribió BAJA, no le manda nada. Los avisos que salen
solos dicen el día y la hora y nada más. **El texto del consentimiento es provisorio** (`v0-provisorio`) hasta que lo valide el equipo legal.

## Carpetas

Todo bajo `/opt/yosoy/`, dueño `yosoy`:

| Carpeta | Qué hay | Permisos |
|---|---|---|
| `/opt/yosoy/app/` | El binario publicado de la API (`YoSoy`) y sus archivos. | — |
| `/opt/yosoy/app-anterior-<fecha>/` | Copias de la app anterior que deja cada despliegue (para el rollback). Se pueden borrar pasados unos días. | — |
| `/opt/yosoy/config/` | Los archivos de variables: `.env`, `video.env`, `signaling.env`, `whatsapp.env`. Valores reales, cargados a mano o por `generar-secretos.sh`, nunca en el repo. | carpeta `750`; archivos `600` |
| `/opt/yosoy/signaling/` | `app/`: el `signaling-server` (con su `node_modules`). | — |
| `/opt/yosoy/whatsapp/` | `app/` (el servicio), `sesion-baileys/` (la sesión de Baileys: **es una credencial**; adentro `datos/`: consentimientos y contadores), `cache/` (solo la caché de npm para instalar) y, en un despliegue, `app-nueva/` y `app-anterior-<fecha>/`. **Del servicio viejo** (whatsapp-web.js) todavía pueden quedar `sesion/` (**credencial**) y, dentro de `cache/`, Chromium: se borran en "Limpieza de lo viejo" de [`DEPLOY.md`](DEPLOY.md). | `sesion-baileys` `700`; `cache` `750` |
| `/opt/yosoy/backups/` | Backups de la app, la configuración y las reglas de UFW antes de cada despliegue grande (`video-<fecha>/`) y de la base. | `700` |

Fuera de `/opt`: `/etc/caddy/` (`Caddyfile`, `video.caddy`), `/etc/turnserver.conf`,
`/etc/systemd/system/` (`signaling-server.service`, `whatsapp-service.service` y los drop-ins
`yosoy-api.service.d/whatsapp.conf` y `video.conf`) y `/etc/sudoers.d/yosoy-api`.

El drop-in `video.conf` lleva un `-` delante del archivo (`EnvironmentFile=-/opt/yosoy/config/video.env`) a propósito:
si `video.env` faltara, la API arranca igual y `/video/acceso` contesta 503. El de WhatsApp no: si falta su archivo,
systemd no arranca la API.

## Base de datos

SQL Server 2025 Express. Base `yosoy` creada con `docs/produccion/01-produccion.sql`: **44 tablas** al crearla (el archivo trae hoy 45: la que
se agregó con el consentimiento de WhatsApp llega a esta base con su migración, ver abajo), con sus claves primarias, foráneas e índices.
Dos logins de SQL Server:

- **`yosoy_app`** (`docs/produccion/02-usuario-app.sql`): el que usa la API (`ConnectionStrings__YoSoy`). Permisos
  `db_datareader` + `db_datawriter` solo sobre `yosoy`, nada de servidor ni de otras bases. **No puede crear tablas:**
  las migraciones se aplican con el login administrador.
- **Usuario administrador** (`docs/produccion/03-usuario-admin.sql`): el primer Configurador del sistema, para entrar
  por `/pantallas/index.html`.

Ninguna contraseña de ninguno de los dos está en este archivo ni en el repo.

### Migraciones aplicadas

| Archivo | Qué hace | Estado |
|---|---|---|
| `docs/produccion/01-produccion.sql` | El esquema completo (45 tablas; la base del servidor se creó con 44, antes de la migración 16). Siembra `TipoConsentimiento` (telemedicina) y, aparte y opcional, preguntas frecuentes y respuestas rápidas del chat. | Aplicado al crear la base |
| `migrations/add_consentimiento_video.sql` | Tabla `ConsentimientoVideo` y tres columnas de `SesionClinica` (consentimiento de la lectura de emociones, PR 6). Solo agrega; idempotente. | **Aplicada.** (En un servidor nuevo, `01-produccion.sql` ya la incluye) |
| `migrations/add_consentimiento_whatsapp.sql` | Tabla `ConsentimientoWhatsApp` (migración 16): el paciente acepta o revoca recibir avisos de sus turnos por WhatsApp; una fila por aceptación, con la versión y el hash del texto que vio, el número y las fechas en UTC. Solo agrega una tabla, dos claves foráneas y dos índices; idempotente. | **No hace falta por ahora.** Solo se aplica si se prende `Mensajeria__AvisosPorWhatsApp` (apagada por defecto: con ella apagada la API no toca la tabla y se publica sin esta migración); en ese caso va **antes** de prender la llave: ver [`DEPLOY.md`](DEPLOY.md), "Consentimiento de WhatsApp". |
| `migrations/add_chat_personal_entrantes.sql` | Migración 17, del chat de WhatsApp para el personal: `MensajeChat.IdExterno` (con el índice único filtrado `UQ_MensajeChat_IdExterno`), `MensajeChat.Tipo` (con su `CHECK` y su valor por defecto) y `Conversacion.NombreContacto`. No crea tablas; idempotente; es independiente de la 16. | **Aplicada el 19/10/2026** (la base sigue con 44 tablas). Hace falta con `Mensajeria__ModoAtencion=Personal`. (`01-produccion.sql` ya la incluye, pero la base del servidor es anterior) |
| `migrations/seed_catalogos.sql` | Catálogos iniciales que la base nacida de `01-produccion.sql` no traía: especialidades, estados de turno con ids fijos (1 Pendiente, 2 Confirmado, 3 Cancelado, 4 Realizado) y el tipo de consentimiento de telemedicina. Idempotente. | **Aplicada.** Versionada en el repo; para un servidor nuevo, ver [`DEPLOY.md`](DEPLOY.md), "Servidor nuevo: base y catálogos" |

Cómo se aplican (`-I` es obligatorio, `-f 65001` por los acentos): [`DEPLOY.md`](DEPLOY.md), sección "Migraciones".

## Reglas de negocio que dependen de lo desplegado

- **Pedir un turno:** ya **no hay anticipación mínima**. Solo se rechaza un horario que ya pasó (posterior a "ahora",
  en la hora local del servidor). Siguen valiendo la disponibilidad de la agenda, los horarios alineados al
  intervalo, que no esté ocupado y un solo turno Pendiente o Confirmado por paciente, profesional y día.
- **Cancelar un turno** (paciente web y bot): **sigue** exigiendo 24 horas de anticipación (RN-05). Reprogramar
  cancela primero, así que un turno a menos de 24 h todavía no se puede reprogramar.
- **Recordatorio:** el servicio en segundo plano avisa de los turnos Confirmados de las próximas 24 h.
- **Videollamada:** la sala abre 10 minutos antes del inicio del turno y cierra 30 minutos después de su fin
  (`Video__Ventana__MinutosAntes` y `__MinutosDespues`), con la duración que da la agenda.

## Frontend público

- **`https://yosoytelemedicina.tech/pantallas/index.html`** — login real, los 3 roles. Responde `200` con HTTPS
  válido (no autofirmado).
- **`GET /capacidades`** — sin sesión, no toca la base; sirve como chequeo liviano de que la API está arriba.

## Variables de entorno

Solo nombres y para qué sirve cada una; los valores están en `/opt/yosoy/config/`.

**`/opt/yosoy/config/.env`** (la API):

| Variable | Para qué sirve |
|---|---|
| `ConnectionStrings__YoSoy` | Cadena de conexión a SQL Server (login `yosoy_app`). Obligatoria: si falta, la app tira un error claro apenas intenta tocar la base. |
| `Jwt__Key` | Clave con la que se firman y validan los tokens de sesión. Obligatoria, 32 caracteres o más: si falta o es corta, la app no arranca. |
| `Email__Password` | Contraseña de la cuenta de Gmail que manda los mails reales (recuperación de contraseña, avisos). Si se deja vacía, la app arranca igual pero no manda mails. |
| `ASPNETCORE_ENVIRONMENT` | `Production` en este servidor (evita páginas de error con detalles internos, entre otras cosas). |
| `ASPNETCORE_URLS` | `http://localhost:5000` — nunca `0.0.0.0` ni `+`. Caddy es el único camino de entrada desde internet, por el 443. |
| `Turnos__ZonaHoraria` | **Opcional.** Zona horaria de la clínica para las reglas de turnos y la sala de video (por defecto `America/Argentina/Buenos_Aires`; si falta, usa `Video__ZonaHoraria`). Una zona que no existe en el servidor impide que la API arranque. |
| `Mensajeria__AvisosPorWhatsApp` | **Opcional. Apagada por defecto (`false`).** Los cuatro avisos de turno por evento (solicitud, propuesta, confirmación, cancelación) por WhatsApp. Apagada: no sale ninguno por WhatsApp (el mail y la campanita siguen igual), el consentimiento de WhatsApp no se ofrece en ninguna pantalla y **la API no toca la tabla `ConsentimientoWhatsApp`** (se publica sin la migración 16). `true` solo con la migración 16 aplicada antes: ver [`DEPLOY.md`](DEPLOY.md), "Consentimiento de WhatsApp". |
| `Mensajeria__WhatsAppNumeroClinica` | **Opcional.** El número de WhatsApp de la clínica (el que vincula `whatsapp-service`), para el link con el que un paciente le escribe primero. Formato libre (`+54 9 11 5555-0000`). **Vacío o ausente = no se muestra ningún link**, y **con `Mensajeria__AvisosPorWhatsApp` apagada tampoco hay link**. No es un secreto. |
| `Mensajeria__WhatsAppTextoInicio` | **Opcional.** Lo que viene escrito cuando se abre ese link (por defecto `Hola`). |
| `Mensajeria__ConsentimientoWhatsApp__VersionTexto` | **Opcional.** Versión del texto del consentimiento de los avisos por WhatsApp (por defecto `v0-provisorio`, provisorio). **Cambiarla detiene los avisos por WhatsApp hasta que cada paciente vuelva a aceptar.** |

**`/opt/yosoy/config/video.env`** (la API lee las de video; las genera `generar-secretos.sh`; detalle y valores por
defecto en [`VIDEOLLAMADAS.md`](VIDEOLLAMADAS.md)):

| Variable | Para qué sirve |
|---|---|
| `Video__SignalingSecreto` | Firma el token de sala. Igual a `SIGNALING_SECRETO` del signaling; distinto de `Jwt__Key`. |
| `Video__TurnSecreto` | Credenciales efímeras de TURN. Igual a `static-auth-secret` de coturn. |
| `Video__Emociones__ClaveCifrado` | Clave AES-256 con la que se cifran las emociones guardadas. **No se rota sola; si se pierde, lo guardado no se puede volver a leer.** Hay que tener una copia fuera del servidor. |
| `Video__Ice__StunUrls__0`, `Video__Ice__TurnUrls__0` a `__2` | Servidores STUN/TURN que la API entrega a los navegadores. |
| `Video__ZonaHoraria` | Zona en la que están escritas las fechas de los turnos (`America/Argentina/Buenos_Aires`). Si `Turnos__ZonaHoraria` no está, también es la zona de la clínica; si está y es distinta, la API avisa en el log. |
| `Video__Ventana__MinutosAntes`, `Video__Ventana__MinutosDespues`, `Video__DuracionPorDefectoMinutos`, `Video__Admision__DuracionMinutos`, `Video__Token__MaxMinutos`, `Video__Turn__TtlMinutos`, `Video__LimitePorMinuto` | Ventana de la sala, duraciones y límites (opcionales: tienen valor por defecto). |
| `Video__Emociones__RetencionDias`, `__PurgaHabilitada`, `__PurgaCadaHoras`, `__PurgaLote`, `__IdClave`, `__VersionTexto`, `__LimitePorMinuto`... | Retención y purga de las emociones, y el resto de la lectura de emociones (opcionales). |

**`/opt/yosoy/config/signaling.env`** (el signaling): `SIGNALING_SECRETO` (obligatoria), `HOST` (`127.0.0.1`) y
`PORT` (`3001`); opcionales: `SIGNALING_ORIGENES_CORS`, `SIGNALING_MAX_PAYLOAD_BYTES`,
`SIGNALING_MAX_HANDSHAKES_POR_MIN`, `SIGNALING_MAX_EVENTOS_POR_*` (ver `signaling-server/README.md`).

**`/opt/yosoy/config/whatsapp.env`** (WhatsApp): `Mensajeria__WhatsApp__Secreto` (secreto compartido con la API) y
`API_BASE_URL` (`http://127.0.0.1:5000`). La unit (`whatsapp-service/deploy/whatsapp-service.service`) agrega
`WHATSAPP_BAILEYS_SESSION_PATH=/opt/yosoy/whatsapp/sesion-baileys`; y en `whatsapp.env` se pueden poner, opcionales, los topes y las pausas
(`WHATSAPP_TOPE_DIARIO`, `WHATSAPP_TOPE_POR_HORA`, `WHATSAPP_TOPE_POR_PERSONA`, `WHATSAPP_TOPE_EN_CONVERSACION`, `WHATSAPP_CONSENTIMIENTO_DIAS`, `WHATSAPP_ZONA_HORARIA`,
`WHATSAPP_PAUSA_MIN_S`, `WHATSAPP_PAUSA_MAX_S`, `WHATSAPP_SIMULAR_ESCRITURA`, `WHATSAPP_BAILEYS_VENTANAS_QR`...): lista completa y valores
por defecto en `whatsapp-service/README.md`. Las variables del servicio viejo (`WHATSAPP_BACKEND`, `WHATSAPP_SESSION_PATH`, `WHATSAPP_WEB_VERSION`, `WHATSAPP_DEBUG` y `PUPPETEER_*`)
ya no se leen: si quedaron, el servicio las avisa en el log (solo los nombres) y se pueden borrar. En la API: `Mensajeria__CanalActivo` (`Simulado` o `Baileys`; `WhatsAppWebJs` ya
no existe y la API no arranca con él), en `.env`.

**Chat de WhatsApp para el personal** (en `.env`, de la API; opcionales, sin ellas todo sigue como siempre; el paso a paso y el paso manual del mensaje de emergencia están en
[`DEPLOY.md`](DEPLOY.md), "Chat de WhatsApp para el personal"): `Mensajeria__ModoAtencion` (`Asistente`, por defecto, o `Personal`; otro valor frena el arranque),
`Mensajeria__Personal__RespuestaDeEmergencia` (`false` por defecto: en modo `Personal`, el mensaje de emergencia que se le manda al paciente ante una frase de riesgo; aunque sea `true`
no sale mientras alguna línea esté sin completar) y `Mensajeria__LineasEmergencia__0__Nombre`, `__0__Telefono`, `__1__Nombre` y `__1__Telefono` (las líneas de ese mensaje; hoy dicen
`[COMPLETAR Y VALIDAR]` en `appsettings.json`). Los chats son **solo del Configurador**: los profesionales reciben 403 en `/mensajeria`. Con `Personal` hace falta la migración 17 (`migrations/add_chat_personal_entrantes.sql`; el orden, en
`DEPLOY.md`), y `GET /salud` de `whatsapp-service` trae `entrantes` (cuántos mensajes se procesaron, ignoraron, reenviaron, reintentaron y perdieron: solo números).

## Verificación rápida (solo lectura)

Ninguno de estos comandos imprime un secreto. (`/opt/yosoy/config` es `750` de `yosoy`: un `*` sobre esa carpeta va dentro de
`sudo sh -c`, porque el shell de `agustin` lo expande antes de `sudo` y no encuentra nada.)

```bash
timedatectl | grep -E "Time zone|Local time"          # America/Argentina/Buenos_Aires
sudo ufw status numbered                              # 22, 80, 443, 3478 (udp y tcp) y 49160:49200/udp; NO 1433, 3001, 3002, 5000
systemctl is-active yosoy-api caddy mssql-server signaling-server coturn whatsapp-service fail2ban
sudo ss -ltnp | grep -E ':(3001|3002|5000) '          # todo en 127.0.0.1, nunca 0.0.0.0 ni *
sudo ss -lunp | grep ':3478'                          # coturn (UDP); y el TCP con ss -ltnp
caddy version                                         # v2.7.0 o más nuevo
sudo caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile
sudo sh -c "stat -c '%a %U:%G %n' /opt/yosoy/config/.env /opt/yosoy/config/*.env"    # los .env en 600 yosoy:yosoy
sudo ls -la /opt/yosoy                                # app, config, signaling, whatsapp, backups (y app-anterior-*)
curl -s -o /dev/null -w "%{http_code} %{redirect_url}\n" http://localhost:5000/    # 302 .../pantallas/index.html
sudo journalctl -u yosoy-api --since '30 minutes ago' --no-pager | grep 'Video:'   # "/video/acceso habilitado."
sudo journalctl -u yosoy-api --since '30 minutes ago' --no-pager | grep 'Zona horaria de la clínica'   # America/Argentina/Buenos_Aires, -03:00:00
# Con el servicio de Baileys:
curl -s http://127.0.0.1:3002/salud                   # 200 y "estado":"conectado"; 503 con el estado y el motivo si no
sudo stat -c '%a %U:%G %n' /opt/yosoy/whatsapp/sesion-baileys /opt/yosoy/whatsapp/sesion-baileys/datos    # 700 yosoy:yosoy
sudo journalctl -u whatsapp-service -o cat --since '1 hour ago' | grep -E '[0-9]{7,}'    # no tiene que devolver nada: sin teléfonos completos
```

## Pendiente / a verificar

- **Contraseña SMTP** (`Email__Password`): verificar que esté cargada. Sin ella la app funciona pero no manda ningún
  mail real (recuperación de contraseña, avisos).
- **Reinicio por el kernel:** la actualización del sistema dejó un kernel nuevo instalado; verificar si ya se aplicó
  (`uname -r` contra `/boot`).
- **Backup de la base y del código fuera del servidor:** antes de dar de baja este VPS en algún momento, hace falta
  un backup de `yosoy` y del código desplegado que no dependa de que el servidor siga existiendo. Además, la
  **clave de cifrado de las emociones** no se puede regenerar: tiene que estar respaldada fuera. (La sesión de WhatsApp sí se puede rehacer escaneando un QR: la de Baileys,
  `sesion-baileys/`, respaldarla o no es una decisión pendiente, ver `PROGRESO_BAILEYS.md`; si se respalda, cifrada. La vieja, `sesion/`, se borra con la limpieza de lo viejo.)
- **WhatsApp con Baileys (único motor):** desplegado el 19/10/2026 con topes iniciales bajos (30 por día y 10 por hora) y **vinculado el 20/10/2026**, con la prueba de humo, el apagado
  y la memoria ya hechos (ver "Actualización del 20/10/2026"). Cuando esté estable, **limpiar lo viejo** del VPS (Chromium, la sesión de whatsapp-web.js y la caché: [`DEPLOY.md`](DEPLOY.md), paso 10).
  Siguen sin verificarse los umbrales de bloqueo de WhatsApp, un QR vencido real y la reconexión tras una caída.
  Todo en [`DEPLOY.md`](DEPLOY.md) y [`PROGRESO_WHATSAPP_SOLO_BAILEYS.md`](PROGRESO_WHATSAPP_SOLO_BAILEYS.md). El consentimiento ya está resuelto en el
  código (las opciones A y B de esa decisión: ver el punto siguiente); falta aplicarlo.
- **Consentimiento de WhatsApp (dormido por defecto):** con `Mensajeria__AvisosPorWhatsApp` apagada (lo de siempre) **no hace falta aplicar la migración 16** para publicar la API. Si se decide prenderla:
  aplicar la migración 16 **antes** de prender la llave, cargar `Mensajeria__WhatsAppNumeroClinica` si se quiere el link `wa.me`, y **validar el texto con el equipo legal** antes de usarlo con pacientes reales (`v0-provisorio` es un borrador).
  Pasos y orden: [`DEPLOY.md`](DEPLOY.md), "Consentimiento de WhatsApp"; qué se decidió y qué falta: [`PROGRESO_CONSENTIMIENTO_WHATSAPP.md`](PROGRESO_CONSENTIMIENTO_WHATSAPP.md).
- **SQL Server escucha en todas las interfaces** (`0.0.0.0:1433`, observado el 2026-10-14) y lo protege solo UFW.
  Conviene atarlo a localhost (`mssql-conf set network.ipaddress 127.0.0.1`), porque la API corre en el mismo servidor.
- **Mejora no urgente:** las claves de Data Protection de ASP.NET Core (cookies y tokens internos del framework)
  están guardadas sin cifrar en disco, el comportamiento por defecto fuera de Windows/Azure. No es explotable sin
  acceso al servidor, pero conviene cifrarlas (`DataProtection:CertificatePath`/`CertificatePassword` o un KMS).
- **Texto legal del consentimiento de emociones y aviso de cámara/micrófono:** provisorios hasta que los valide un
  profesional legal (ver `VIDEOLLAMADAS.md`) antes de usarlos con pacientes reales.
- **DeepSeek:** no existe ninguna integración con DeepSeek (ni con ningún otro proveedor de IA externo) en el
  código. Si se agrega, la clave va por variable de entorno o `dotnet user-secrets`, nunca en `appsettings.json`.
