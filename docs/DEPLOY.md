# Despliegue

Lo que tiene que estar configurado en el servidor y que **no viaja en el repositorio**. El estado del servidor
(usuarios, firewall, servicios, carpetas) está en [`SERVIDOR.md`](SERVIDOR.md); para desarrollo, en
[`DESARROLLO_LOCAL.md`](DESARROLLO_LOCAL.md).

## Secretos

Ningún secreto está en `appsettings.json` ni en ningún archivo versionado: en producción se cargan como **variables de
entorno**, con el nombre de la clave de configuración y doble guion bajo (`__`) en lugar de los dos puntos. Viven en
archivos de entorno del servidor, con permisos `600` y dueño `yosoy`, que **nunca** se copian al repositorio ni se pegan en
un chat.

| Variable | Archivo del servidor | Hace falta | Si falta |
|---|---|---|---|
| `ConnectionStrings__YoSoy` | `/opt/yosoy/config/.env` | Sí | La API arranca; falla al usar la base |
| `Jwt__Key` | `/opt/yosoy/config/.env` | **Sí** (32 caracteres o más) | La API **no arranca** |
| `Email__From` | `/opt/yosoy/config/.env` | Para que salgan mails | Arranca; los mails no salen y el log dice `Correo no configurado` |
| `Email__Password` | `/opt/yosoy/config/.env` | Para que salgan mails | ídem |
| `Email__AppUrl` | `/opt/yosoy/config/.env` | Sí, en producción: URL pública sin barra final (base de los links de los mails) | Los links apuntan a la máquina local, que no le sirve a nadie más |
| `Video__SignalingSecreto` | `/opt/yosoy/config/video.env` | Para videollamadas | `POST /video/acceso` contesta 503 |
| `Video__TurnSecreto` | `/opt/yosoy/config/video.env` | Para videollamadas que necesiten TURN | Solo STUN |
| `Video__Emociones__ClaveCifrado` | `/opt/yosoy/config/video.env` | Para la lectura de emociones | La lectura queda desactivada |
| `Mensajeria__WhatsApp__Secreto` | `/opt/yosoy/config/whatsapp.env` | Para WhatsApp | El webhook entrante contesta 404; el servicio de WhatsApp no arranca |
| `SIGNALING_SECRETO` | `/opt/yosoy/config/signaling.env` | Para videollamadas (es el servidor de señalización, otro proceso) | El signaling no arranca |

Dos pares tienen que valer **lo mismo**: `SIGNALING_SECRETO` = `Video__SignalingSecreto`, y `Mensajeria__WhatsApp__Secreto`
en el servicio de WhatsApp = el de la API. Los secretos de video tienen 32 caracteres o más y son distintos de `Jwt__Key`;
la clave de emociones son 32 bytes en base64. Los de video los genera `deploy/video/generar-secretos.sh` (ver
[`DESPLIEGUE_VIDEO.md`](DESPLIEGUE_VIDEO.md)) y el de WhatsApp está explicado en `whatsapp-service/README.md`
("Secreto compartido"). Las variables `Video__*` no secretas (ventana de la sala, STUN/TURN, retención) y su valor por
defecto están en [`VIDEOLLAMADAS.md`](VIDEOLLAMADAS.md).

Para que la API tome un cambio: `sudo systemctl restart yosoy-api`.

## Credenciales de correo

`appsettings.json` no trae la cuenta ni la contraseña del correo: van vacías (`Email:From` y `Email:Password`).

| Variable | Para qué | Hace falta |
|---|---|---|
| `Email__From` | La cuenta de correo desde la que se manda. **También es el usuario** con el que se autentica en el SMTP. | Para que salgan mails |
| `Email__Password` | La contraseña de esa cuenta (en Gmail, una *contraseña de aplicación*). | Para que salgan mails |
| `Email__AppUrl` | URL pública del sitio, sin barra final: es la base de los links de los mails (recuperación de contraseña). Si no se define, se usa la de `appsettings.json`, que apunta a la máquina local y no le sirve a nadie más. | Sí, en producción |
| `Email__Host`, `Email__Port` | Servidor SMTP y puerto. Vienen en `appsettings.json`. | No |

En `/opt/yosoy/config/.env`, con **valores de relleno** (los reales no se escriben en ningún archivo del repositorio):

```
Email__From=<cuenta-de-correo>
Email__Password=<contraseña-de-aplicación>
Email__AppUrl=https://<dominio-público>
```

### Si faltan

La API **arranca igual**. Cada vez que intente mandar un mail (recuperación de contraseña, recordatorios y avisos de
turno) deja en el log `Correo no configurado: no se envió ... Falta Email:From, Email:Password ...`, no abre ninguna
conexión y los avisos por mail de los recordatorios quedan registrados como `Omitido`. Ojo: **un recordatorio omitido
no se reenvía** cuando después se configura el correo. Para ver el aviso:

```bash
sudo journalctl -u yosoy-api --since '30 minutes ago' --no-pager | grep 'Correo no configurado'
```

### Al pasar a esta versión

Hasta ahora `Email:From` venía escrito en `appsettings.json`; desde esta versión viene vacío. Si el `.env` del servidor
solo tenía `Email__Password`, cargá también `Email__From` **antes** de publicar: sin ella el correo queda sin configurar
(y la API lo dice en el log, nombrando la clave que falta).

### Rotar una credencial expuesta

Si una contraseña estuvo alguna vez en el repositorio, borrarla del archivo no alcanza: queda en el historial de git.
Hay que **revocarla en el proveedor** (en Gmail, revocar la contraseña de aplicación y crear otra) y cargar la nueva
solo en el `.env` del servidor y, para desarrollo, como secreto de usuario.
