# DEPLOY — Videollamadas seguras (YoSoy)

Runbook para levantar las videollamadas en dos modos: **LAN/demo** (misma red, sin
TURN ni HTTPS) y **online** (Caddy + coturn + DDNS sobre `comuna31.com.ar`).

---

## 1. Pre-requisitos (ambos modos)

1. **Base de datos.** Aplicar el script de sesiones de video sobre la base `yosoy`
   en SQL Server Express:

   ```powershell
   sqlcmd -S .\SQLEXPRESS -d yosoy -i sql-local\01_sesiones_video.sql
   ```

   (El esquema base de la app vive en la rama `DataBase` del repo.)

2. **Variables de entorno del backend.** Copiar `.env.example` y setear los valores
   reales. Las claves críticas:
   - `Jwt__Secret` — el MISMO valor que `JWT_SECRET` del signaling.
   - `Turn__StaticAuthSecret` — el MISMO valor que `static-auth-secret` de coturn.
   - `Sesion__ClaveCifrado` — base64 de 32 bytes (cifrado en reposo de emociones).
   - `Signaling__SecretoInterno` — el MISMO valor que `INTERNAL_SECRET` del signaling.
   - `Cors__Origenes__0`, `Dominio`, `Signaling__UrlPublica`.

   En Windows estos se exportan con `$env:Jwt__Secret = "..."` antes de `dotnet run`,
   o se dejan en `appsettings.json` para la demo local.

3. **Dependencias del signaling.**

   ```powershell
   cd signaling-server
   npm install
   ```

---

## 2. Modo LAN / demo (sin TURN ni HTTPS)

Sirve para probar dos máquinas en la misma red local. No requiere router ni dominio.

1. Backend (`appsettings.json` de demo ya viene así):
   - `Sesion:ModoLan = true`
   - `Turn:Habilitado = false`
   - `Cors:Origenes = []` (permite cualquier origen en demo)
   - `Signaling:UrlPublica = http://<IP-LOCAL-DE-LA-PC>:3001`

2. Signaling (`signaling-server/.env`, ver `.env.example`):
   - `MODO_LAN=true`
   - `CORS_ORIGINS=http://<IP-LOCAL>:5000,http://localhost:5500`
   - `JWT_SECRET=` el mismo que el backend.

3. Levantar:

   ```powershell
   # Terminal 1
   dotnet run
   # Terminal 2
   cd signaling-server; node server.js
   ```

4. Probar: entrar desde el sistema (login → dashboard → sección Sesión) a la hora del
   turno, o abrir el link de email. En LAN no hay TURN: la conexión usa candidatos de
   host (misma red) y, si está configurado, STUN.

> En modo LAN, si el backend no responde, el signaling **permite** la unión (solo demo).
> En online el signaling **rechaza** si no puede verificar el estado en la DB.

---

## 3. Modo online (Caddy + coturn + DDNS)

### 3.1 Servicios en la PC

```powershell
# Backend
$env:Sesion__ModoLan = "false"; $env:Turn__Habilitado = "true"; dotnet run
# Signaling
cd signaling-server; node server.js
```

`coturn` corre en Linux/WSL/contenedor con `turnserver -c turnserver.conf` (ver
`signaling-server/turnserver.conf`, reemplazando los placeholders de IP y certificados).

### 3.2 Caddy (TLS + reverse proxy)

```bash
caddy run --config ./Caddyfile
```

Caddy obtiene el certificado de Let's Encrypt automáticamente, sirve el frontend +
backend en `https://comuna31.com.ar` y hace proxy del WSS del signaling (`/socket.io/*`).

### 3.3 DDNS (IP dinámica → Cloudflare)

La IP del hogar cambia: hay que actualizar los registros A en Cloudflare. Ejemplo de
script (cron cada 5 min), usando un API token con permiso de editar DNS de la zona:

```bash
#!/usr/bin/env bash
# actualizar-ddns.sh  — requiere CF_API_TOKEN, CF_ZONE_ID y los record id.
IP=$(curl -s https://api.ipify.org)
for REC in "$CF_RECORD_APP" "$CF_RECORD_TURN"; do
  curl -s -X PATCH \
    "https://api.cloudflare.com/client/v4/zones/$CF_ZONE_ID/dns_records/$REC" \
    -H "Authorization: Bearer $CF_API_TOKEN" \
    -H "Content-Type: application/json" \
    --data "{\"content\":\"$IP\"}" > /dev/null
done
```

```cron
*/5 * * * * /ruta/actualizar-ddns.sh
```

---

## 4. Tareas MANUALES (no las puede hacer Claude)

Estas dependen de tu router / cuenta de Cloudflare / ISP:

### Router
- Abrir/reenviar a la IP local de la PC: **80/TCP**, **443/TCP**, **3478/TCP+UDP**,
  **5349/TCP** y el **rango UDP de relay** (`49160-49200`, debe coincidir con
  `min-port`/`max-port` de `turnserver.conf`).
- Reservar IP local fija para la PC (reserva DHCP) para que el port-forward no se rompa.

### Cloudflare (zona `comuna31.com.ar`)
- Registro **A** de la app (`comuna31.com.ar`) → IP pública.
- Registro **A** del TURN (`turn.comuna31.com.ar`) → IP pública.
- Ambos en **DNS-only / nube gris**: el proxy naranja de Cloudflare NO sirve para
  TURN/UDP ni para WebRTC; tiene que pegarle directo a tu IP.

### DDNS
- Crear un **API token** de Cloudflare con permiso *DNS:Edit* sobre esa zona.
- Programar el script de actualización (cron/Task Scheduler) — ver 3.3.

### Caveat ISP
- Si el ISP **bloquea el puerto 80 entrante**, Let's Encrypt no puede validar por
  HTTP-01. Usar el challenge **DNS-01** de Caddy (bloque comentado en el `Caddyfile`),
  con un build de Caddy que incluya el plugin de Cloudflare y el `CLOUDFLARE_API_TOKEN`.

---

## 5. Verificación rápida

- `GET https://comuna31.com.ar/config` devuelve `signalingUrl`, `stun`, `modoLan`.
- `GET https://comuna31.com.ar/health` (proxy al signaling) devuelve `ok`.
- Test de TURN: https://webrtc.github.io/samples/src/content/peerconnection/trickle-ice/
  cargando `turn:turn.comuna31.com.ar:3478` con credenciales REST debe listar candidatos `relay`.
- Sesión real: login de profesional y paciente, entrar a la hora del turno, ver video
  bidireccional y la cuenta regresiva; el profesional prueba **Prolongar** y **Finalizar**.
