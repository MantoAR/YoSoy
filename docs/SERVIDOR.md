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

## Pendiente

- Instalar SQL Server.
- Desplegar la API en `/opt/yosoy/app`.
- Cargar los valores reales en `/opt/yosoy/config/.env` (a mano, en el
  servidor).
- Habilitar `yosoy-api` y `caddy`, y verificar que el certificado HTTPS
  salga bien.
- Reiniciar el servidor: la actualización del sistema dejó un kernel nuevo
  instalado que todavía no se aplicó.
