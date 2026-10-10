# Servidor de producción

Estado del VPS donde corre YoSoy. No hay secretos en este archivo (ni
contraseñas, ni claves, ni el contenido de ningún `.env`) — para eso está el
`.env` real en el servidor, que no se versiona.

- **Servidor:** Ubuntu 24.04.5 LTS, IP `179.199.155.50`.
- **Dominio:** `yosoytelemedicina.tech`, apuntando a esa IP.

## Usuarios

- **`agustin`**: usuario administrador. Entra por SSH solo con clave (no hay
  login por contraseña). Tiene sudo completo —pide contraseña para cualquier
  comando— y además una regla sin contraseña, limitada a un solo comando:
  reiniciar el servicio `yosoy-api` (`/etc/sudoers.d/yosoy-api`).
- **`yosoy`**: usuario de sistema, sin shell de login (`nologin`). Es el
  dueño de la app, sus carpetas y el proceso de `yosoy-api`; no se usa para
  entrar al servidor.
- **`root`**: login por SSH deshabilitado (`PermitRootLogin no` en
  `/etc/ssh/sshd_config`).

## Firewall y protección

- **UFW** activo: deniega todo lo entrante por defecto, permite solo
  22 (SSH), 80 (HTTP) y 443 (HTTPS). El resto —en particular el puerto de SQL
  Server (1433) y el del servidor de señalización (3001)— no está expuesto
  a internet.
- **fail2ban**: habilitado y activo, vigilando `sshd`.

## Servicios (estado verificado)

| Servicio | Habilitado | Activo | Qué es |
|---|---|---|---|
| `yosoy-api` | Sí | Sí | La API, corriendo como el usuario `yosoy`. Binario en `/opt/yosoy/app/YoSoy`. |
| `caddy` | Sí | Sí | Reverse proxy. HTTPS real, certificado de Let's Encrypt, renovación automática. |
| `mssql-server` | Sí | Sí | SQL Server 2025, edición Express. |
| `fail2ban` | Sí | Sí | Ver arriba. |

`Caddyfile` (`/etc/caddy/Caddyfile`):
```
yosoytelemedicina.tech {
    reverse_proxy localhost:5000
}
```

## Carpetas de la app

Todo bajo `/opt/yosoy/`, dueño `yosoy`:

- `/opt/yosoy/app/` — el binario publicado de la API (`YoSoy`).
- `/opt/yosoy/config/` — permisos `750`. Adentro, `.env` con permisos `600`,
  con los valores reales (cargados a mano en el servidor, nunca en el repo).
- `/opt/yosoy/backups/` — permisos `700`, para los backups de la base.

## Base de datos

SQL Server 2025 Express. Base `yosoy` creada con `docs/produccion/
01-produccion.sql`: **43 tablas**, con sus claves primarias, foráneas e
índices. Dos logins de SQL Server:

- **`yosoy_app`** (`docs/produccion/02-usuario-app.sql`): el que usa la API
  (`ConnectionStrings__YoSoy`). Permisos `db_datareader`+`db_datawriter`
  solo sobre `yosoy`, nada de servidor ni de otras bases.
- **Usuario administrador** (`docs/produccion/03-usuario-admin.sql`): el
  primer Configurador del sistema, para entrar por `/pantallas/index.html`.

Ninguna contraseña de ninguno de los dos está en este archivo ni en el repo.

## Frontend público

- **`https://yosoytelemedicina.tech/pantallas/index.html`** — login real,
  los 3 roles. Verificado: responde `200` con HTTPS válido (no autofirmado).
- **`GET /capacidades`** — sin sesión, no toca la base; sirve como chequeo
  liviano de que la API está arriba. Verificado: responde
  `{"mensajeria":false}`.

## Variables de entorno que necesita la API

Van en `/opt/yosoy/config/.env` (solo nombres y para qué sirve cada una):

| Variable | Para qué sirve |
|---|---|
| `ConnectionStrings__YoSoy` | Cadena de conexión a SQL Server (login `yosoy_app`). Obligatoria: si falta, la app tira un error claro apenas intenta tocar la base. |
| `Jwt__Key` | Clave con la que se firman y validan los tokens de sesión. Obligatoria, 32 caracteres o más: si falta o es corta, la app no arranca. |
| `Email__Password` | Contraseña de la cuenta de Gmail que manda los mails reales (aprobación de profesional, recuperación de contraseña). Si se deja vacía, la app arranca igual pero no manda mails — ver "Pendiente". |
| `ASPNETCORE_ENVIRONMENT` | `Production` en este servidor (evita páginas de error con detalles internos, entre otras cosas). |
| `ASPNETCORE_URLS` | `http://localhost:5000` — nunca `0.0.0.0` ni `+`. Caddy es el único camino de entrada desde internet, por el 443. |

## Pendiente

- **Contraseña SMTP** (`Email__Password`): todavía no está cargada. La app
  funciona sin ella, pero no manda ningún mail real (aprobación de
  profesional, recuperación de contraseña).
- **Reiniciar el servidor**: la actualización del sistema (Paso 1 del setup)
  dejó un kernel nuevo instalado que todavía no se aplicó.
- **Backup de la base y del código fuera del servidor**: antes de dar de
  baja este VPS en algún momento, hace falta un backup de `yosoy` y del
  código desplegado que no dependa de que el servidor siga existiendo.
- **Mejora pendiente, no urgente**: las claves de Data Protection de
  ASP.NET Core (las que cifran cookies/tokens internos del framework) están
  guardadas sin cifrar en disco — el comportamiento por defecto fuera de
  Windows/Azure, sin un proveedor de cifrado en reposo configurado. No es
  explotable sin acceso al servidor, pero conviene resolverlo en algún
  momento (por ejemplo, cifrarlas con una clave de `DataProtection:
  CertificatePath`/`CertificatePassword` o un KMS).
- **DeepSeek**: no existe ninguna integración con DeepSeek (ni con ningún
  otro proveedor de IA externo) en el código todavía — confirmado buscando
  en todo el repo. Si se agrega en el futuro, la clave va por variable de
  entorno o `dotnet user-secrets`, nunca en `appsettings.json`.
