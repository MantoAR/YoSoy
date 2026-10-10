# Servidor de producción

Estado del VPS donde va a correr YoSoy. No hay secretos en este archivo (ni
contraseñas, ni claves, ni el contenido de ningún `.env`) — para eso está el
`.env` real en el servidor, que no se versiona.

- **Servidor:** Ubuntu 24.04.5 LTS, IP `179.199.155.50`.
- **Dominio:** `yosoytelemedicina.tech`, ya apunta a esa IP (DNS propagado y
  verificado).

## Usuarios

- **`agustin`**: usuario administrador. Entra por SSH solo con clave (no hay
  login por contraseña). Tiene sudo completo —pide contraseña para cualquier
  comando— y además una regla sin contraseña, limitada a un solo comando:
  reiniciar el servicio `yosoy-api` (`/etc/sudoers.d/yosoy-api`).
- **`yosoy`**: usuario de sistema, sin shell de login (`nologin`). Es el
  dueño de la app y de sus carpetas; no se usa para entrar al servidor.
- **`root`**: login por SSH deshabilitado (`PermitRootLogin no` en
  `/etc/ssh/sshd_config`).

## Firewall y protección

- **UFW** activo: deniega todo lo entrante por defecto, permite solo
  22 (SSH), 80 (HTTP) y 443 (HTTPS). El resto —en particular el puerto de SQL
  Server (1433) y el del servidor de señalización (3001)— no está expuesto
  a internet.
- **fail2ban** activo, vigilando `sshd`.

## Carpetas de la app

Todo bajo `/opt/yosoy/`, dueño `yosoy`:

- `/opt/yosoy/app/` — donde va el código/binario de la API (vacío todavía).
- `/opt/yosoy/config/` — permisos `750`. Adentro, `.env` con permisos `600`,
  vacío por ahora (los valores reales se cargan a mano en el servidor).
- `/opt/yosoy/backups/` — permisos `700`, para los backups de la base.

## Caddy (reverse proxy / HTTPS)

Instalado desde los repositorios de Ubuntu (versión 2.6.2). El
`Caddyfile` (`/etc/caddy/Caddyfile`) ya tiene la configuración para
`yosoytelemedicina.tech` como reverse proxy hacia `localhost:5000`, validada
con `caddy validate`. El servicio está **deshabilitado**: se activa recién
cuando la API esté corriendo en el puerto 5000, para que Caddy pueda sacar el
certificado HTTPS solo.

## Servicio systemd de la API

`/etc/systemd/system/yosoy-api.service` ya existe, como plantilla: corre
como el usuario `yosoy`, con `WorkingDirectory=/opt/yosoy/app` y
`EnvironmentFile=/opt/yosoy/config/.env`. Todavía **no está habilitado**
—no hay ningún binario para correr—.

## Variables de entorno que necesita la API

Van en `/opt/yosoy/config/.env` (solo nombres y para qué sirve cada una —
los valores reales se cargan a mano en el servidor, nunca acá ni en el
repo):

| Variable | Para qué sirve |
|---|---|
| `ConnectionStrings__YoSoy` | Cadena de conexión a SQL Server. Obligatoria: si falta, la app tira un error claro apenas intenta tocar la base (no hay valor por defecto). |
| `Jwt__Key` | Clave con la que se firman y validan los tokens de sesión. Obligatoria, 32 caracteres o más: si falta o es corta, la app no arranca. |
| `Email__Password` | Contraseña de la cuenta de Gmail que manda los mails reales (aprobación de profesional, recuperación de contraseña). Si se deja vacía, la app arranca igual pero no manda mails. |
| `ASPNETCORE_ENVIRONMENT` | Tiene que ser `Production` en el VPS (evita páginas de error con detalles internos, entre otras cosas). |
| `ASPNETCORE_URLS` | Dirección y puerto donde escucha la API. Tiene que ser exactamente `http://localhost:5000` — **nunca** `http://0.0.0.0:5000` ni `http://+:5000`. Con `localhost`, la API solo acepta conexiones desde el propio servidor (UFW ya bloquea el 5000 desde afuera, pero esto es una segunda barrera: ni loopback hacia otras interfaces). Caddy es el único camino de entrada desde internet, por el 443. El puerto 5000 que se usa en desarrollo viene de `launchSettings.json`, que no aplica a un binario publicado corriendo solo — por eso hay que fijarlo acá. |

## Pendiente

- Instalar SQL Server.
- Desplegar la API en `/opt/yosoy/app`.
- Cargar los valores reales en `/opt/yosoy/config/.env` (a mano, en el
  servidor).
- Habilitar `yosoy-api` y `caddy`, y verificar que el certificado HTTPS
  salga bien.
- Reiniciar el servidor: la actualización del sistema dejó un kernel nuevo
  instalado que todavía no se aplicó.
