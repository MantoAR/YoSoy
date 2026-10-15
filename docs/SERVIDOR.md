# Servidor de producción

Estado del VPS donde corre YoSoy. No hay secretos en este archivo (ni contraseñas, ni claves, ni el contenido de
ningún `.env`) —solo **nombres** de variables—: los valores reales están en `/opt/yosoy/config/` en el servidor, que no
se versiona. Cómo se publica una versión nueva: [`DEPLOY.md`](DEPLOY.md). El paso a paso de las videollamadas:
[`DESPLIEGUE_VIDEO.md`](DESPLIEGUE_VIDEO.md).

> **De dónde sale lo que dice este archivo.** De los runbooks del repositorio (`DESPLIEGUE_VIDEO.md`,
> `whatsapp-service/README.md`, `deploy/`) y de lo informado por el equipo después de los despliegues. **No se
> verificó contra el servidor al escribirlo**: la sección "Verificación rápida" tiene los comandos para comprobarlo
> (solo lectura, ninguno imprime un secreto).

- **Servidor:** Ubuntu 24.04.5 LTS, IP `179.199.155.50`.
- **Dominio:** `yosoytelemedicina.tech`, apuntando a esa IP (directo, sin CDN ni proxy: TURN usa UDP y un proxy HTTP no
  lo deja pasar).
- **Zona horaria del servidor:** `America/Argentina/Buenos_Aires`.

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
| `whatsapp-service` | Puente HTTP interno hacia WhatsApp (whatsapp-web.js + Chromium). En `/opt/yosoy/whatsapp/app`. `MemoryMax=2G`. | `127.0.0.1:3002` | `yosoy` | `/opt/yosoy/config/whatsapp.env`; unit en `whatsapp-service/deploy/` |
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
    reverse_proxy localhost:5000
}
```

`/etc/caddy/video.caddy` (copia de `deploy/caddy/video.caddy`) agrega solo una cosa: `handle /socket.io/*` →
`reverse_proxy 127.0.0.1:3001` con `stream_close_delay 5m` (que una recarga de Caddy no corte de golpe los WebSocket de
las llamadas en curso). Dentro de un bloque de sitio, Caddy evalúa `handle` antes que `reverse_proxy`: la API nunca
recibe `/socket.io/*`. El WebSocket no necesita ninguna directiva: Caddy 2 reenvía el `Upgrade` solo.

Antes de recargar: `sudo caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile`.

## Carpetas

Todo bajo `/opt/yosoy/`, dueño `yosoy`:

| Carpeta | Qué hay | Permisos |
|---|---|---|
| `/opt/yosoy/app/` | El binario publicado de la API (`YoSoy`) y sus archivos. | — |
| `/opt/yosoy/app-anterior-<fecha>/` | Copias de la app anterior que deja cada despliegue (para el rollback). Se pueden borrar pasados unos días. | — |
| `/opt/yosoy/config/` | Los archivos de variables: `.env`, `video.env`, `signaling.env`, `whatsapp.env`. Valores reales, cargados a mano o por `generar-secretos.sh`, nunca en el repo. | carpeta `750`; archivos `600` |
| `/opt/yosoy/signaling/` | `app/`: el `signaling-server` (con su `node_modules`). | — |
| `/opt/yosoy/whatsapp/` | `app/` (el servicio), `sesion/` (la sesión de WhatsApp: **es una credencial**) y `cache/` (Chromium). | `sesion` y `cache` `750` |
| `/opt/yosoy/backups/` | Backups de la app, la configuración y las reglas de UFW antes de cada despliegue grande (`video-<fecha>/`) y de la base. | `700` |

Fuera de `/opt`: `/etc/caddy/` (`Caddyfile`, `video.caddy`), `/etc/turnserver.conf`,
`/etc/systemd/system/` (`signaling-server.service`, `whatsapp-service.service` y los drop-ins
`yosoy-api.service.d/whatsapp.conf` y `video.conf`) y `/etc/sudoers.d/yosoy-api`.

El drop-in `video.conf` lleva un `-` delante del archivo (`EnvironmentFile=-/opt/yosoy/config/video.env`) a propósito:
si `video.env` faltara, la API arranca igual y `/video/acceso` contesta 503. El de WhatsApp no: si falta su archivo,
systemd no arranca la API.

## Base de datos

SQL Server 2025 Express. Base `yosoy` creada con `docs/produccion/01-produccion.sql`: **44 tablas**, con sus claves
primarias, foráneas e índices. Dos logins de SQL Server:

- **`yosoy_app`** (`docs/produccion/02-usuario-app.sql`): el que usa la API (`ConnectionStrings__YoSoy`). Permisos
  `db_datareader` + `db_datawriter` solo sobre `yosoy`, nada de servidor ni de otras bases. **No puede crear tablas:**
  las migraciones se aplican con el login administrador.
- **Usuario administrador** (`docs/produccion/03-usuario-admin.sql`): el primer Configurador del sistema, para entrar
  por `/pantallas/index.html`.

Ninguna contraseña de ninguno de los dos está en este archivo ni en el repo.

### Migraciones aplicadas

| Archivo | Qué hace | Estado |
|---|---|---|
| `docs/produccion/01-produccion.sql` | El esquema completo (44 tablas). Siembra `TipoConsentimiento` (telemedicina) y, aparte y opcional, preguntas frecuentes y respuestas rápidas del chat. | Aplicado al crear la base |
| `migrations/add_consentimiento_video.sql` | Tabla `ConsentimientoVideo` y tres columnas de `SesionClinica` (consentimiento de la lectura de emociones, PR 6). Solo agrega; idempotente. | **Aplicada.** (En un servidor nuevo, `01-produccion.sql` ya la incluye) |
| `migrations/seed_catalogos.sql` | Catálogos iniciales que la base nacida de `01-produccion.sql` no traía: especialidades, estados de turno con ids fijos (1 Pendiente, 2 Confirmado, 3 Cancelado, 4 Realizado) y el tipo de consentimiento de telemedicina. Idempotente. | **Aplicada.** El archivo **no está versionado** en el repositorio (ver "Pendiente") |

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
`API_BASE_URL` (`http://127.0.0.1:5000`).

## Verificación rápida (solo lectura)

Ninguno de estos comandos imprime un secreto.

```bash
timedatectl | grep -E "Time zone|Local time"          # America/Argentina/Buenos_Aires
sudo ufw status numbered                              # 22, 80, 443, 3478 (udp y tcp) y 49160:49200/udp; NO 1433, 3001, 3002, 5000
systemctl is-active yosoy-api caddy mssql-server signaling-server coturn whatsapp-service fail2ban
sudo ss -ltnp | grep -E ':(3001|3002|5000) '          # todo en 127.0.0.1, nunca 0.0.0.0 ni *
sudo ss -lunp | grep ':3478'                          # coturn (UDP); y el TCP con ss -ltnp
caddy version                                         # v2.7.0 o más nuevo
sudo caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile
sudo stat -c '%a %U:%G %n' /opt/yosoy/config/*        # los .env en 600 yosoy:yosoy
sudo ls -la /opt/yosoy                                # app, config, signaling, whatsapp, backups (y app-anterior-*)
curl -s -o /dev/null -w "%{http_code} %{redirect_url}\n" http://localhost:5000/    # 302 .../pantallas/index.html
sudo journalctl -u yosoy-api --since '30 minutes ago' --no-pager | grep 'Video:'   # "/video/acceso habilitado."
sudo journalctl -u yosoy-api --since '30 minutes ago' --no-pager | grep 'Zona horaria de la clínica'   # America/Argentina/Buenos_Aires, -03:00:00
```

## Pendiente / a verificar

- **`migrations/seed_catalogos.sql` no está en el repositorio.** Se aplicó en producción, pero el archivo sigue sin
  versionar: si hay que reconstruir el servidor, falta. Conviene commitearlo.
- **Contraseña SMTP** (`Email__Password`): verificar que esté cargada. Sin ella la app funciona pero no manda ningún
  mail real (recuperación de contraseña, avisos).
- **Reinicio por el kernel:** la actualización del sistema dejó un kernel nuevo instalado; verificar si ya se aplicó
  (`uname -r` contra `/boot`).
- **Backup de la base y del código fuera del servidor:** antes de dar de baja este VPS en algún momento, hace falta
  un backup de `yosoy` y del código desplegado que no dependa de que el servidor siga existiendo. Además, la
  **clave de cifrado de las emociones** y la **sesión de WhatsApp** (`/opt/yosoy/whatsapp/sesion`) no se pueden
  regenerar: tienen que estar respaldadas fuera.
- **SQL Server escucha en todas las interfaces** (`0.0.0.0:1433`, observado el 2026-10-14) y lo protege solo UFW.
  Conviene atarlo a localhost (`mssql-conf set network.ipaddress 127.0.0.1`), porque la API corre en el mismo servidor.
- **Mejora no urgente:** las claves de Data Protection de ASP.NET Core (cookies y tokens internos del framework)
  están guardadas sin cifrar en disco, el comportamiento por defecto fuera de Windows/Azure. No es explotable sin
  acceso al servidor, pero conviene cifrarlas (`DataProtection:CertificatePath`/`CertificatePassword` o un KMS).
- **Texto legal del consentimiento de emociones y aviso de cámara/micrófono:** provisorios hasta que los valide un
  profesional legal (ver `VIDEOLLAMADAS.md`) antes de usarlos con pacientes reales.
- **DeepSeek:** no existe ninguna integración con DeepSeek (ni con ningún otro proveedor de IA externo) en el
  código. Si se agrega, la clave va por variable de entorno o `dotnet user-secrets`, nunca en `appsettings.json`.
