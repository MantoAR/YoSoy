# Despliegue de las videollamadas en el VPS

Paso a paso para poner las videollamadas en `https://yosoytelemedicina.tech`: la señalización como servicio, los
secretos, coturn (el relay TURN), la API y las pantallas nuevas, y Caddy. Es el PR 5 del plan; los PR 1 a 4 (señalización
con token, `POST /video/acceso`, pantallas nuevas) ya están en `main`. **El PR 5 no despliega nada**: solo agrega los
archivos de `deploy/` y este documento. El despliegue lo hacés vos, a mano, con lo que sigue.

**PR 6 (consentimiento de la lectura de emociones).** Es el único PR de video que **toca la base** (una tabla y tres columnas) y
que agrega una **clave de cifrado**. Por eso hay un backup de la base hecho por vos y dos pasos nuevos: **f0** (la migración y la
clave, **antes** de desplegar la API) y **f1** (recifrar lo que ya estaba guardado, a mano, **después**). Los pasos a a e y g no
cambian, salvo lo que se indica en cada uno.

El contrato de la API y las variables están en [`docs/VIDEOLLAMADAS.md`](VIDEOLLAMADAS.md); el del servidor de
señalización, en [`signaling-server/README.md`](../signaling-server/README.md); cómo está armado el servidor hoy, en
[`docs/SERVIDOR.md`](SERVIDOR.md).

- **Quién y dónde.** En la PC, PowerShell, parado en el repositorio (`D:\Claude\YoSoy\YoSoy`). En el VPS, bash, como
  `agustin` (con `sudo`; no podés entrar a las carpetas de `yosoy`, por eso todo lo que toca esos archivos va con `sudo`).
- **Tiempo y cortes.** Una hora, con calma. La API se reinicia dos veces (pasos e y f, unos segundos cada vez); el resto no
  corta nada de lo que anda hoy. Convienen horas de poco uso.
- **Ningún comando de acá imprime un secreto.** Si alguno te lo mostrara, algo está mal: pará y avisá.
- **Cada paso se puede deshacer** (ver "Rollback" en cada uno y el resumen del final) y **cada paso tiene una verificación**.

> **Decidí antes del paso g.** Con el paso g las videollamadas quedan usables por pacientes reales. La sala del paciente trae
> un aviso **provisorio** de cámara y micrófono y, desde el PR 6, un panel de consentimiento de la lectura de emociones
> **con un texto provisorio: lo tiene que validar un profesional legal antes de usarse con pacientes reales** (lista en
> "Qué hay que verificar con el equipo legal", de `docs/VIDEOLLAMADAS.md`). La transcripción sigue apagada y sin decisión.
> Podés hacer los pasos a a f1 y **dejar el g para después**: todo queda instalado y dormido (nadie llega al signaling
> mientras Caddy no lo publique).

## Antes de empezar

En el VPS:

```bash
node --version          # v22 o más nuevo (ya está: lo usa whatsapp-service)
caddy version           # v2.7.0 o más nuevo; con una anterior, el snippet de Caddy no valida (paso g)
sudo ufw status         # activo; hoy permite 22, 80 y 443
df -h /                 # que sobre lugar para el backup del paso a
```

En la PC: `git switch main && git pull`, y `git log -1` para anotar qué commit vas a desplegar. El paso f despliega **todo
lo que hay en `main`**, no solo video; si no sabés qué commit tiene hoy el servidor, mirá `git log` desde la fecha de tu
último despliegue para saber qué más se va.

## Datos a confirmar en el panel de Hostinger

Esto no se puede saber desde el repositorio ni desde mi lado. **Confirmalo antes del paso d**: cambia qué se abre y cuánto
cuesta.

| Dato | Por qué importa | Cómo confirmarlo | Si da distinto |
|---|---|---|---|
| **Transferencia mensual** del plan, y si cuenta entrada y salida o solo salida | El video que pasa por coturn consume transferencia: cada llamada de 30 minutos con las **dos** personas relayadas (video de 2,5 Mbit/s, HD) mueve ≈ 1,1 GB hacia coturn y ≈ 1,1 GB desde coturn; a 1 Mbit/s, menos de la mitad. Muchas llamadas no pasan por coturn (conexión directa o por STUN); las que sí, suelen ser una minoría. | Panel del VPS, uso de ancho de banda del mes. | Si el tope es chico, bajar `bps-capacity` y `max-bps` en `deploy/coturn/turnserver.conf` antes de instalarlo, o no habilitar TURN (ver "Sin TURN" en el paso d). |
| **Firewall del proveedor** (el del panel de Hostinger, aparte de UFW) | Si está activo y bloquea por defecto, aunque abras UFW coturn no se alcanza desde afuera. | Panel del VPS, sección de firewall (el nombre exacto puede variar). | Si está activo, agregar ahí las mismas reglas que en UFW: **3478 UDP y TCP** y **49160–49200 UDP**, además de 22/80/443. |
| **IP pública directa o NAT** | Si la IP pública (`179.199.155.50`, según `docs/SERVIDOR.md`) está en una interfaz del servidor, coturn no necesita nada más. Si el VPS está detrás de NAT, hay que configurar `external-ip`. | En el VPS: `ip -4 -br addr show scope global`. Si ves `179.199.155.50`, es IP directa. Si ves una privada (`10.x`, `172.16–31.x`, `192.168.x`), es NAT. | NAT: descomentar `external-ip` y `allowed-peer-ip` en `/etc/turnserver.conf` (están explicados ahí) **antes** de iniciar coturn. |
| **El dominio apunta directo a la IP** (sin CDN ni proxy) | TURN usa UDP y un proxy HTTP no lo deja pasar. | En el VPS: `getent ahostsv4 yosoytelemedicina.tech \| head -1` tiene que dar la IP del servidor. | Si da otra IP (un proxy tipo CDN), crear un nombre aparte solo para coturn (por ejemplo `turn.yosoytelemedicina.tech`, sin proxy) y usarlo con `--dominio` en el paso c. |
| **IPv6** | Si el servidor y el dominio tienen IPv6, los navegadores pueden usarlo para llegar a coturn; UFW ya cubre IPv6 si `IPV6=yes` en `/etc/default/ufw`. | `ip -6 -br addr show scope global`; `grep ^IPV6 /etc/default/ufw`. | Solo informativo: si no hay IPv6, no hace falta nada. |

## Qué se instala y dónde

| En el repositorio | En el VPS | Dueño y permisos |
|---|---|---|
| `signaling-server/` (sin `node_modules` ni `test`) | `/opt/yosoy/signaling/app/` | `yosoy` |
| `deploy/signaling-server.service` | `/etc/systemd/system/signaling-server.service` | `root`, 644 |
| `deploy/yosoy-api.service.d/video.conf` | `/etc/systemd/system/yosoy-api.service.d/video.conf` | `root`, 644 |
| *(lo genera el script)* | `/opt/yosoy/config/signaling.env` | `yosoy`, **600** |
| *(lo genera el script)* | `/opt/yosoy/config/video.env` | `yosoy`, **600** |
| `deploy/coturn/turnserver.conf` | `/etc/turnserver.conf` | `root:turnserver`, **640** |
| `deploy/caddy/video.caddy` | `/etc/caddy/video.caddy` | `root`, 644 |
| `deploy/video/generar-secretos.sh` | se corre desde `~/video-deploy/deploy/video/` | — |
| `migrations/add_consentimiento_video.sql` | se aplica a la base `yosoy` con `sqlcmd` (paso f0); no se copia a `/opt` | — |

## El orden, y por qué

1. **Backup** antes de tocar nada.
2. **Signaling instalado pero sin arrancar**: su unit necesita `signaling.env`, que todavía no existe.
3. **Secretos**, y recién ahí arranca el signaling (solo en loopback; nadie de afuera llega).
4. **coturn y UFW**: el relay queda listo antes de que la API empiece a ofrecerlo.
5. **Drop-in de la API**: carga `video.env`. La API de hoy todavía no sabe usarlo; es inocuo y se verifica que no rompa nada.
6. **PR 6, la base y la clave de cifrado** (paso f0): tu backup, la migración y la clave, **antes** de que arranque la API nueva.
7. **API y pantallas nuevas** (paso f): ahora `/video/acceso` y el consentimiento existen, con secretos y TURN ya listos.
8. **Recifrar lo guardado en claro** (paso f1): a mano, cuando quieras y con tu backup hecho.
9. **Caddy, lo último**: es lo que publica `/socket.io/*`. Hasta acá nadie de afuera podía llegar a la señalización.

---

## Paso a — Backup

Copia la app, la configuración (con los `.env`), la unit y los drop-ins de la API, el Caddyfile y las reglas de UFW a
`/opt/yosoy/backups/` (700, solo `yosoy`; con `sudo` lo ves).

**En el VPS:**

```bash
SELLO=$(date +%Y%m%d-%H%M)
B=/opt/yosoy/backups/video-$SELLO
sudo mkdir -p $B
sudo cp -a /opt/yosoy/app $B/app
sudo cp -a /opt/yosoy/config $B/config
sudo cp -a /etc/caddy/Caddyfile $B/Caddyfile
sudo cp -a /etc/systemd/system/yosoy-api.service.d $B/yosoy-api.service.d
sudo sh -c "systemctl cat yosoy-api > $B/yosoy-api.systemctl-cat.txt; ufw status numbered > $B/ufw.txt"
sudo chmod -R go-rwx $B
```

**Verificá:**

```bash
sudo ls -la $B            # app, config, Caddyfile, yosoy-api.service.d, yosoy-api.systemctl-cat.txt, ufw.txt
sudo du -sh $B            # tamaño razonable (la carpeta app)
```

**La base.** Los PR 1 a 4 no tocaron ningún `.sql` (verificado con `git diff`) y la columna `CodigoSala` de `Turno` y de
`InformeAdmision` ya está en `docs/produccion/01-produccion.sql`. **El PR 6 sí toca la base** (una tabla y tres columnas): su
backup lo hacés vos, a mano, justo antes de la migración (paso f0.1). Este paso a no hace ningún backup de la base.

**Rollback:** no hay nada que deshacer; este backup es el que usan los otros rollbacks. No lo borres hasta pasados unos
días. (En otra sesión de SSH, para encontrarlo: `B=$(sudo ls -d /opt/yosoy/backups/video-* | tail -1)`.)

---

## Paso b — Desplegar el signaling-server (sin arrancarlo)

**En la PC (PowerShell).** Los paquetes se arman en la carpeta temporal para no dejar archivos sueltos en el repositorio:

```powershell
cd D:\Claude\YoSoy\YoSoy\signaling-server
tar --exclude=node_modules --exclude=test -czf $env:TEMP\signaling-server.tar.gz .
cd D:\Claude\YoSoy\YoSoy
tar -czf $env:TEMP\video-deploy.tar.gz deploy
scp $env:TEMP\signaling-server.tar.gz $env:TEMP\video-deploy.tar.gz agustin@179.199.155.50:/home/agustin/
```

Antes de copiar, mirá qué lleva cada paquete: `tar -tzf $env:TEMP\signaling-server.tar.gz` tiene que listar
`config.js`, `limitador.js`, `package.json`, `package-lock.json`, `server.js`, `token.js` (más `README.md`,
`emitir-token.js` y `.gitignore`) y **ningún** `node_modules`. El otro lista la carpeta `deploy/`.

**En el VPS.** Igual que el despliegue de whatsapp-service: se extrae como `agustin`, se le da la carpeta a `yosoy` y las
dependencias se instalan **como `yosoy`**, con `npm ci` (usa el `package-lock.json` tal cual) y `--omit=dev` (el cliente
de pruebas no hace falta):

```bash
sudo mkdir -p /opt/yosoy/signaling/{app,cache}
sudo chown -R yosoy:yosoy /opt/yosoy/signaling
sudo tar -xzf /home/agustin/signaling-server.tar.gz -C /opt/yosoy/signaling/app
sudo chown -R yosoy:yosoy /opt/yosoy/signaling/app
sudo -u yosoy sh -c 'cd /opt/yosoy/signaling/app && HOME=/opt/yosoy/signaling/cache npm ci --omit=dev --cache /opt/yosoy/signaling/cache/npm'

mkdir -p ~/video-deploy && tar -xzf ~/video-deploy.tar.gz -C ~/video-deploy
sudo cp ~/video-deploy/deploy/signaling-server.service /etc/systemd/system/
sudo systemctl daemon-reload
```

(`HOME` y `--cache` van a una carpeta de `yosoy` con permiso de escritura; es el mismo motivo que en `whatsapp-service/README.md`.)

**Verificá:**

```bash
sudo ls /opt/yosoy/signaling/app
# config.js  emitir-token.js  limitador.js  node_modules  package-lock.json  package.json  README.md  server.js  token.js
sudo -u yosoy sh -c 'cd /opt/yosoy/signaling/app && HOME=/opt/yosoy/signaling/cache npm ls --omit=dev --depth=0'
# yosoy-signaling-server@1.0.0 /opt/yosoy/signaling/app
# ├── jsonwebtoken@9.x
# └── socket.io@4.x           (sin "missing", "invalid" ni "ERR!")
systemd-analyze verify /etc/systemd/system/signaling-server.service
# sin salida (o solo avisos de que falta signaling.env: lo crea el paso c)
```

**Rollback:**

```bash
sudo rm /etc/systemd/system/signaling-server.service && sudo systemctl daemon-reload
sudo mv /opt/yosoy/signaling "/opt/yosoy/backups/signaling-retirado-$(date +%Y%m%d-%H%M)"    # se mueve, no se borra
```

---

## Paso c — Generar los secretos y arrancar el signaling

`generar-secretos.sh` crea con `openssl rand -hex 32` el secreto de la señalización y el de TURN y los escribe **sin
mostrarlos** en `signaling.env` y `video.env` (600, dueño `yosoy`). Después, en el paso d, se corre otra vez para que escriba el de
TURN en la configuración de coturn. Es idempotente: sin opciones **no pisa** ningún secreto que ya exista; si dos archivos
discrepan, se detiene sin tocar nada. Detalle y opciones: `sudo bash ~/video-deploy/deploy/video/generar-secretos.sh --ayuda`.

Desde el PR 6 crea además, con `openssl rand -base64 32`, la **clave de cifrado de las emociones**
(`Video__Emociones__ClaveCifrado`, en `video.env`): una sola vez, y **no se rota sola** (no hay una opción para eso; si el
archivo ya tiene una clave válida, la deja como está). Al crearla imprime un recuadro con la advertencia de guardar una copia
fuera del servidor (ver el paso f0.3). Si este paso c ya lo corriste con una versión anterior del script, **no hace falta
repetirlo ahora**: en el paso f0.3 vas a volver a correrlo y agrega solo la clave que falta.

**En el VPS:**

```bash
sudo bash ~/video-deploy/deploy/video/generar-secretos.sh
```

(Con otro dominio para las URLs STUN/TURN: `--dominio turn.ejemplo.com`.) Tiene que terminar en `Listo.` y mostrar algo así:

```
YoSoy · secretos de las videollamadas (ningún valor se muestra)

Qué se hizo:
  Secreto de la señalización ... generado
  Secreto de TURN .............. generado
  Clave de cifrado (emociones) . generada
  Archivos ..................... signaling.env: creado
  Archivos ..................... video.env: creado
  [i] /etc/turnserver.conf todavía no existe: instalá coturn y copiá turnserver.conf (paso d) y volvé a correr este script.

  ************************************************************************************************
  * SE GENERÓ la clave de cifrado de las emociones (Video__Emociones__ClaveCifrado).
  * Guardá YA una copia FUERA de este servidor y FUERA del repositorio (un gestor de contraseñas o
  * un archivo cifrado que solo tengas vos). NUNCA en el chat, en un mail ni en un ticket.
  * Si se pierde, las emociones guardadas NO se pueden volver a leer. Si se filtra, hay que
  * cambiarla y volver a cifrar lo guardado (todavía no hay una herramienta para eso).
  ************************************************************************************************

Verificación:
  [ok] SIGNALING_SECRETO (signaling.env) es igual a Video__SignalingSecreto (video.env), de 64 caracteres.
  [ok] Video__TurnSecreto es válido y distinto del de la señalización.
  [ok] Video__Emociones__ClaveCifrado: presente (base64 de 32 bytes) y distinta de los otros secretos.
  [ok] HOST=127.0.0.1 (solo loopback: el signaling no se expone sin Caddy).
  [ok] signaling.env: 600 yosoy:yosoy
  [ok] video.env: 600 yosoy:yosoy
  [i] Jwt__Key: no se compara ...

Listo.
```

**Sobre `Jwt__Key`.** El script **no lee el `.env` de la API** (ahí está `Jwt__Key`), así que no puede comprobar que el
secreto de la señalización sea distinto. No hace falta: salen de `openssl` (256 bits al azar), no pueden coincidir salvo
que alguien los edite a mano. Y lo comprueba la propia API sin que nadie lea nada: si `Video__SignalingSecreto` fuera igual
a `Jwt__Key`, en el paso f el journal dice `Video: /video/acceso deshabilitado ...` en vez de `habilitado`.

**Verificá** sin imprimir ningún secreto:

```bash
sudo stat -c '%a %U:%G %n' /opt/yosoy/config/signaling.env /opt/yosoy/config/video.env
# 600 yosoy:yosoy /opt/yosoy/config/signaling.env
# 600 yosoy:yosoy /opt/yosoy/config/video.env
sudo sh -c 'a=$(sed -n "s/^SIGNALING_SECRETO=//p" /opt/yosoy/config/signaling.env); b=$(sed -n "s/^Video__SignalingSecreto=//p" /opt/yosoy/config/video.env); if [ -n "$a" ] && [ "$a" = "$b" ]; then echo IGUALES; else echo DISTINTOS; fi'
# IGUALES
```

**Arrancar el signaling** (solo escucha en `127.0.0.1:3001`):

```bash
sudo systemctl enable --now signaling-server
sudo systemctl is-active signaling-server              # active
curl -s http://127.0.0.1:3001/health                   # ok
sudo ss -ltnp | grep ':3001'                           # 127.0.0.1:3001 ... "node" — NUNCA 0.0.0.0 ni *
sudo journalctl -u signaling-server -n 5 --no-pager    # ... ARRANQUE escuchando=127.0.0.1:3001 cors=ninguno ...
```

Si no arranca, el journal dice por qué sin mostrar valores (`Falta SIGNALING_SECRETO`, `es demasiado corto`, o
`No se pudo abrir 127.0.0.1:3001: EADDRINUSE` si otro proceso usa el puerto: `sudo ss -ltnp | grep 3001`).

**Rollback:**

```bash
B=$(sudo ls -d /opt/yosoy/backups/video-* | tail -1)       # la carpeta del backup del paso a
sudo systemctl disable --now signaling-server
sudo mv /opt/yosoy/config/signaling.env /opt/yosoy/config/video.env "$B/"       # se mueven al backup, no se borran
```

---

## Paso d — coturn y el firewall

**1. Instalar y poner la configuración.** El paquete de Ubuntu arranca coturn solo con su configuración de fábrica: se lo
frena antes de cambiarla. La configuración del repo no tiene ningún secreto: el script lo escribe en el lugar marcado.

```bash
sudo apt update
sudo apt install -y coturn
sudo systemctl stop coturn
if [ -f /etc/turnserver.conf ]; then sudo cp -a /etc/turnserver.conf /etc/turnserver.conf.original; fi
sudo install -m 640 -o root -g turnserver ~/video-deploy/deploy/coturn/turnserver.conf /etc/turnserver.conf
sudo bash ~/video-deploy/deploy/video/generar-secretos.sh          # 2ª corrida: escribe en turnserver.conf el MISMO secreto de TURN
```

En la 2ª corrida tiene que decir `Archivos ..... turnserver.conf: static-auth-secret escrito` y
`[ok] static-auth-secret (/etc/turnserver.conf) es igual a Video__TurnSecreto.`, sin tocar los dos `.env` (los secretos
figuran como `existente`).

**Verificá la configuración (sin imprimir el secreto):**

```bash
sudo stat -c '%a %U:%G' /etc/turnserver.conf               # 640 root:turnserver
sudo grep -c '^static-auth-secret=' /etc/turnserver.conf   # 1  (una sola línea; solo cuenta, no muestra)
sudo grep -c '^denied-peer-ip=' /etc/turnserver.conf       # 21  (los rangos privados, de loopback, link-local, ULA y reservados)
ip -4 -br addr show scope global                           # ¿está 179.199.155.50? Si no, NAT: ver "Datos a confirmar"
```

**2. Arrancarlo y ver que escucha:**

```bash
sudo systemctl enable --now coturn
sudo systemctl is-active coturn                       # active
sudo ss -lunpt | grep ':3478'                         # una línea udp y una tcp, proceso "turnserver"
sudo journalctl -u coturn -n 30 --no-pager            # arranque sin "ERROR" ni "CONFIG ERROR"
turnutils_stunclient 127.0.0.1                        # imprime una "reflexive addr": coturn contesta STUN
```

Si el journal no muestra nada de coturn (la configuración manda sus logs al syslog con la opción `syslog`), probá
`sudo journalctl -t turnserver -n 30 --no-pager`. Si el servicio no arranca y menciona `TURNSERVER_ENABLED`, descomentá
esa línea en `/etc/default/coturn` y reintentá.

**3. Abrir los puertos en UFW.** Recién ahora, con coturn andando:

```bash
sudo ufw allow 3478/udp comment 'coturn STUN/TURN'
sudo ufw allow 3478/tcp comment 'coturn STUN/TURN (TCP)'
sudo ufw allow 49160:49200/udp comment 'coturn relays'
sudo ufw status numbered             # tienen que estar 22, 80, 443 y las tres nuevas (v4 y v6). El 3001 NO.
```

Si el proveedor tiene firewall propio, las mismas reglas van ahí (ver "Datos a confirmar").

**Verificá desde afuera** (PowerShell, en la PC): `Test-NetConnection yosoytelemedicina.tech -Port 3478` →
`TcpTestSucceeded : True`. UDP no se puede probar así: se prueba con la página de ICE del paso h.

**Sin TURN.** Si preferís no instalar coturn todavía, saltá este paso y en `/opt/yosoy/config/video.env` comentá las líneas
`Video__Ice__TurnUrls__*` (con `#` adelante): la API entrega solo STUN y `turnDisponible: false`; el video anda en la
mayoría de las redes, no en las que lo exigen.

**Rollback:**

```bash
sudo ufw delete allow 3478/udp
sudo ufw delete allow 3478/tcp
sudo ufw delete allow 49160:49200/udp
sudo ufw status numbered             # confirmá que no quedó ninguna de coturn
sudo systemctl disable --now coturn
if [ -f /etc/turnserver.conf.original ]; then sudo cp -a /etc/turnserver.conf.original /etc/turnserver.conf; fi
```

Y para que la API deje de ofrecer TURN: comentar `Video__Ice__TurnUrls__*` en `video.env` y reiniciar `yosoy-api`.

---

## Paso e — Drop-in de la API

Hace que `yosoy-api` lea también `/opt/yosoy/config/video.env` (mismo esquema que `whatsapp.conf`, con un `-` a propósito: si
el archivo faltara, la API arranca igual y `/video/acceso` contesta 503, en vez de quedar caída).

```bash
sudo cp ~/video-deploy/deploy/yosoy-api.service.d/video.conf /etc/systemd/system/yosoy-api.service.d/video.conf
sudo systemctl daemon-reload
sudo systemctl restart yosoy-api
```

**Verificá.** La API de hoy todavía no conoce las variables `Video__*`, así que acá solo se comprueba que el drop-in está
cargado y que la API volvió a levantar bien:

```bash
systemctl show yosoy-api -p DropInPaths -p EnvironmentFiles --no-pager
# DropInPaths=.../whatsapp.conf .../video.conf
# EnvironmentFiles=... /opt/yosoy/config/video.env (ignore_errors=yes) ...
sudo systemctl is-active yosoy-api                                        # active
curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:5000/capacidades # 200
```

**Rollback:**

```bash
sudo rm /etc/systemd/system/yosoy-api.service.d/video.conf
sudo systemctl daemon-reload && sudo systemctl restart yosoy-api
```

---

## Paso f0 — PR 6: la base y la clave de cifrado (antes del paso f)

El PR 6 (consentimiento de la lectura de emociones) es el único de los de video que **toca la base**: agrega la tabla
`ConsentimientoVideo` y tres columnas `NULL` a `SesionClinica`. Y necesita una **clave de cifrado** para lo que se guarde. Las dos
cosas tienen que estar **antes** de que arranque la API nueva (paso f): sin la migración la API no puede leer las sesiones
clínicas (las columnas nuevas) y el consentimiento da error; sin la clave la API arranca igual, pero la lectura de emociones queda
"no disponible", no se ofrece a nadie y la videollamada anda igual.

La migración **solo agrega**: no modifica ni borra ninguna fila, y es idempotente (correrla otra vez no hace nada).

### f0.1 — Tu backup de la base

Lo hacés **vos**, a mano, con tu herramienta de siempre. **Comprobá que el archivo existe y tiene un tamaño razonable antes de
seguir.** Este documento no lo hace por vos: la migración solo agrega, pero el recifrado del paso f1 sí modifica filas, y conviene
tener un punto de partida de verdad.

### f0.2 — Aplicar la migración

Con un login que pueda crear tablas: el administrador de SQL Server con el que creaste la base (por ejemplo `sa`), **no**
`yosoy_app` (ese solo lee y escribe). `sqlcmd` te pide la contraseña si no ponés `-P`: no la escribas en la línea de comandos.

```bash
sqlcmd -S localhost -d yosoy -U <usuario-administrador> -C -I -f 65001 -i migrations/add_consentimiento_video.sql
```

(`-I` es obligatorio: `QUOTED_IDENTIFIER ON`, como en los demás scripts; `-f 65001`, por los acentos. En Linux `sqlcmd` suele estar
en `/opt/mssql-tools18/bin/`. El archivo está en el repositorio, en `migrations/`: si lo corrés desde el VPS, copialo antes con
`scp`. Se aplica **antes** de desplegar la API, y no hace falta cortar nada: la API que está andando ignora las columnas y la tabla
nuevas.)

**Verificá** (tiene que dar 3 columnas nuevas y 0 decisiones):

```bash
sqlcmd -S localhost -d yosoy -U <usuario-administrador> -C -I -Q "SELECT (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SesionClinica') AND name IN ('IdConsentimientoVideo','EmocionesAnuladasUtc','EmocionesAnuladasMotivo')) AS columnas_nuevas, (SELECT COUNT(*) FROM ConsentimientoVideo) AS decisiones"
```

**Rollback** (solo mientras todavía no haya decisiones: después se perderían los consentimientos registrados). El rollback de la
API (paso f) **no** necesita esto: la app anterior ignora las columnas y la tabla nuevas.

```sql
ALTER TABLE SesionClinica DROP CONSTRAINT CK_SesionClinica_EmocionesAnuladasMotivo;
ALTER TABLE SesionClinica DROP CONSTRAINT FK_SesionClinica_ConsentimientoVideo;
ALTER TABLE SesionClinica DROP COLUMN IdConsentimientoVideo, EmocionesAnuladasUtc, EmocionesAnuladasMotivo;
DROP TABLE ConsentimientoVideo;
```

### f0.3 — La clave de cifrado

```bash
sudo bash ~/video-deploy/deploy/video/generar-secretos.sh
```

Si el paso c se hizo con el script de antes del PR 6, esta corrida **agrega solo la clave que falta** a `video.env` y no toca
ninguno de los otros secretos (ni siquiera los muestra). Tiene que terminar en `Listo.`, con `Clave de cifrado (emociones) .
generada` y el recuadro de advertencia (ver el paso c). La corrida siguiente dice `existente` y no hace nada.

**Guardá YA una copia de la clave fuera del VPS y fuera del repositorio** (un gestor de contraseñas, o un archivo cifrado que solo
tengas vos). **Nunca en el chat, en un mail ni en un ticket.** Es la única clave de este despliegue que **no se puede
regenerar**: con ella se cifran las emociones guardadas, y si se pierde, esas sesiones ya no se pueden volver a leer (la API
devuelve `null` en ese campo y deja un aviso en el log; las notas y lo demás siguen intactos).

Para sacarla del servidor hay que verla una vez. Es **el único comando de este documento que imprime un secreto**: hacelo en tu
terminal, pasá el valor directo a tu gestor y limpiá la pantalla:

```bash
sudo sed -n 's/^Video__Emociones__ClaveCifrado=//p' /opt/yosoy/config/video.env
clear
```

**Verificá** sin imprimir nada (tiene que decir `44 caracteres` y `600 yosoy:yosoy`):

```bash
sudo sh -c 'v=$(sed -n "s/^Video__Emociones__ClaveCifrado=//p" /opt/yosoy/config/video.env); echo "${#v} caracteres"'
sudo stat -c '%a %U:%G %n' /opt/yosoy/config/video.env
```

**No se rota con el script** (no hay `--rotar-emociones`, a propósito: cambiarla sin más dejaría ilegible lo guardado). Si la clave
se filtrara, hay que cambiarla **y** volver a cifrar lo guardado; para eso todavía no hay una herramienta (`--recifrar-emociones`
solo cifra lo que está en claro, no vuelve a cifrar con otra clave).

**Rollback:** nada que deshacer. Una clave de más en `video.env` no molesta a la API anterior.

---

## Paso f — La API y las pantallas nuevas

Despliega lo que hay en `main` (los PR 3, 4 y 6 todavía no están en producción: `POST /video/acceso`, las pantallas que lo
usan y el consentimiento de la lectura de emociones). **Va después del paso f0**: la migración y la clave primero.
Es tu flujo de siempre: publicar en la PC, subir el publish y reemplazar `/opt/yosoy/app` a mano, dejando antes el backup
`app-anterior-<fecha>`. El publish es `--self-contained true`: lleva su propio runtime de .NET y el servidor no necesita tenerlo.

**En la PC (PowerShell).** La fecha va en los nombres: usá la misma en todo el paso (`$fecha` queda guardada hasta que cierres la
ventana):

```powershell
cd D:\Claude\YoSoy\YoSoy
git status                       # limpio, en main, y git log -1 es el commit que querés desplegar
$fecha = Get-Date -Format yyyyMMdd
dotnet publish YoSoy.csproj -c Release -r linux-x64 --self-contained true -o "D:\Claude\Publish-YoSoy-${fecha}-video"
tar -czf "$env:TEMP\Publish-YoSoy-${fecha}-video.tar.gz" -C "D:\Claude\Publish-YoSoy-${fecha}-video" .
scp "$env:TEMP\Publish-YoSoy-${fecha}-video.tar.gz" agustin@179.199.155.50:/home/agustin/
$fecha                           # anotá el valor: es el FECHA del VPS
```

**En el VPS.** `FECHA` es la que anotaste en la PC (no uses el `date` del servidor: puede estar en otra zona y dar otro día).
Primero se desempaqueta el publish y se comprueba que está completo, **antes** de tocar nada:

```bash
FECHA=20261014                                     # <- la de la PC
mkdir -p ~/Publish-YoSoy-$FECHA-video
tar -xzf ~/Publish-YoSoy-$FECHA-video.tar.gz -C ~/Publish-YoSoy-$FECHA-video
ls ~/Publish-YoSoy-$FECHA-video/YoSoy              # el binario tiene que estar ahí; si da error, FECHA está mal: NO sigas
```

Después el reemplazo: backup de la app que anda, corte, se vacía la carpeta, se copia el publish, dueño y permisos, y se arranca:

```bash
sudo cp -a /opt/yosoy/app /opt/yosoy/app-anterior-$FECHA
sudo diff -rq /opt/yosoy/app /opt/yosoy/app-anterior-$FECHA && echo "backup idéntico"     # si imprime otra cosa, NO sigas
sudo systemctl stop yosoy-api
sudo find /opt/yosoy/app -mindepth 1 -delete                      # vacía la carpeta; la carpeta queda
sudo cp -a ~/Publish-YoSoy-$FECHA-video/. /opt/yosoy/app/
sudo chown -R yosoy:yosoy /opt/yosoy/app
sudo chmod +x /opt/yosoy/app/YoSoy                                # permiso de ejecución del binario
sudo systemctl start yosoy-api
```

**Verificá:**

```bash
curl -s -o /dev/null -w "%{http_code} %{redirect_url}\n" http://localhost:5000/
# 302 http://localhost:5000/pantallas/index.html
sudo systemctl is-active yosoy-api                                  # active
sudo journalctl -u yosoy-api --since '10 minutes ago' --no-pager | grep 'Video:'
# Video: /video/acceso habilitado.          <- tiene que decir "habilitado".
# Si dice "deshabilitado ..." la misma línea da el motivo (sin valores): secreto ausente, corto, o igual a Jwt__Key.
# Video: lectura de emociones habilitada (cifrado configurado, clave k1).     <- con la clave del paso f0.3
# Si dice "deshabilitada ..." falta la clave (o no es válida): la lectura de emociones no se ofrece; la llamada anda igual.
curl -s -o /dev/null -w '%{http_code}\n' -X POST http://127.0.0.1:5000/video/acceso -H 'Content-Type: application/json' -d '{}'
# 401  (existe y pide sesión; antes del PR 3 daba 404)
curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:5000/video/consentimiento
# 401  (existe y pide sesión; sin el PR 6 daba 404)
curl -s https://yosoytelemedicina.tech/pantallas/shared/emociones.js | grep -c 'YS.emociones'     # 1 o más: la pantalla nueva
curl -s https://yosoytelemedicina.tech/pantallas/shared/video.js | grep -c 'video/acceso'                   # 1 o más: las pantallas nuevas
curl -s -o /dev/null -w '%{http_code}\n' https://yosoytelemedicina.tech/pantallas/shared/vendor/socket.io.min.js   # 200: el cliente va vendoreado
ls /usr/share/zoneinfo/America/Argentina/Buenos_Aires               # existe (la zona que usa Video__ZonaHoraria)
```

Y entrá a `https://yosoytelemedicina.tech/pantallas/index.html` con tu usuario: el resto de la aplicación tiene que seguir
andando como siempre.

**Rollback** (vuelve a la app del backup `app-anterior-<fecha>`; la carpeta del publish sigue en tu home, para mirarla):

```bash
FECHA=20261014                       # la del backup; si abriste otra sesión: sudo ls -d /opt/yosoy/app-anterior-*
sudo systemctl stop yosoy-api
sudo find /opt/yosoy/app -mindepth 1 -delete
sudo cp -a /opt/yosoy/app-anterior-$FECHA/. /opt/yosoy/app/
sudo chown -R yosoy:yosoy /opt/yosoy/app
sudo systemctl start yosoy-api
curl -s -o /dev/null -w "%{http_code} %{redirect_url}\n" http://localhost:5000/     # 302 http://localhost:5000/pantallas/index.html
```

(El paso a dejó además una copia de la app en `/opt/yosoy/backups/video-<fecha>/app`, por si hiciera falta.) Este rollback **no**
necesita deshacer la migración del paso f0 ni sacar la clave: la app anterior las ignora. Una sesión que ya guardó emociones
cifradas, vista por la app anterior, devolvería ese texto cifrado (`ys1.…`) tal cual y las pantallas viejas no lo entienden: es
otra razón para no volver atrás sin necesidad una vez que hay datos cifrados.

---

## Paso f1 — Recifrar lo que ya estaba guardado en claro (a mano, cuando quieras)

Las emociones guardadas **antes** del PR 6 están en claro en `SesionClinica.HistorialEmociones`. La API nueva las sigue leyendo
igual, pero **no las cifra sola**: es un paso manual, tuyo, y por defecto **solo simula**. El mismo ejecutable de la API trae la
herramienta (`YoSoy --recifrar-emociones`). Hacelo **con tu backup (f0.1) hecho**.

Tiene que correr con el entorno del servicio (la cadena de conexión y la clave están en sus archivos de entorno), como el usuario
`yosoy`. `systemd-run` carga esos archivos como los carga el servicio, sin mostrar ningún valor:

```bash
# 1) SIMULACIÓN: cuenta cuántas sesiones están en claro y no modifica nada
sudo systemd-run --pty --wait --collect --uid=yosoy --working-directory=/opt/yosoy/app \
  -p EnvironmentFile=/opt/yosoy/config/.env -p EnvironmentFile=/opt/yosoy/config/video.env \
  /opt/yosoy/app/YoSoy --recifrar-emociones
```

Tiene que empezar con `SIMULACIÓN (no se modifica nada)` y decir `Clave de cifrado ... configurada`:

```
Recifrado de emociones guardadas: SIMULACIÓN (no se modifica nada).
  Clave de cifrado ............. configurada
  Sesiones con emociones ....... 3
    ya cifradas ................ 0
    en claro (a recifrar) ...... 2
    en claro pero no son JSON .. 1   (se dejan como están)
Para aplicar, DESPUÉS de tu backup de la base:  YoSoy --recifrar-emociones --aplicar
```

Si `en claro (a recifrar)` da 0, no hay nada que hacer. Si no, y **solo con tu backup hecho**:

```bash
# 2) APLICAR: cifra de verdad
sudo systemd-run --pty --wait --collect --uid=yosoy --working-directory=/opt/yosoy/app \
  -p EnvironmentFile=/opt/yosoy/config/.env -p EnvironmentFile=/opt/yosoy/config/video.env \
  /opt/yosoy/app/YoSoy --recifrar-emociones --aplicar
```

Tiene que decir `APLICANDO` y, al final, `Recifradas ... N` con el mismo N que `en claro (a recifrar)`. Se puede volver a correr: lo
ya cifrado se saltea y la siguiente pasada da 0.

- **Nunca borra ni anula nada.** Reemplaza el texto legible por su versión cifrada, fila por fila. Las notas, la transcripción y
  el resto de la sesión no se tocan.
- **Cada fila se comprueba antes de escribirse**: se cifra, se vuelve a descifrar y se compara con el original. Y se actualiza solo
  si la fila sigue teniendo el mismo texto (si alguien la cambió mientras tanto, la deja y la cuenta: `Cambiaron mientras tanto`;
  se vuelve a correr).
- **No imprime la clave, ni contenido, ni ids**: solo cantidades.
- **Sin la clave no aplica nada** (`NO configurada` y sale con error): la simulación sí funciona.
- Lo que no es un JSON (si hubiera algo así) se deja como está y se cuenta aparte.

**Rollback:** no hay uno directo: la herramienta no guarda el texto en claro aparte. Para volver atrás se restaura el backup de
f0.1 (por eso va primero). Con la clave en tu poder, el texto cifrado sí se puede leer de vuelta (es lo que hace la propia API).

---

## Paso g — Caddy (lo último: es lo que publica `/socket.io/*`)

Antes de empezar: ¿decidiste lo del aviso y del consentimiento provisorios, y que el texto legal lo valide un profesional legal?
(ver el recuadro del principio). Si no, parate acá.

Se agrega a tu `Caddyfile` **una sola línea** (`import`) dentro del bloque del sitio; el bloque nuevo vive en
`deploy/caddy/video.caddy`. No se reescribe el resto.

```bash
caddy version                                                       # v2.7.0 o más nuevo
sudo cp -a /etc/caddy/Caddyfile /etc/caddy/Caddyfile.antes-de-video
sudo cp ~/video-deploy/deploy/caddy/video.caddy /etc/caddy/video.caddy
sudo nano /etc/caddy/Caddyfile
```

Dentro del bloque de `yosoytelemedicina.tech`, agregá la línea del `import` (da igual si va antes o después del
`reverse_proxy` de la API; los demás renglones que tengas, no se tocan):

```
yosoytelemedicina.tech {
    import /etc/caddy/video.caddy
    reverse_proxy localhost:5000
}
```

**Siempre validar antes de recargar:**

```bash
sudo caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile      # tiene que terminar con: Valid configuration
sudo systemctl reload caddy
```

Si `validate` falla con `unrecognized subdirective stream_close_delay`, tu Caddy es anterior a la 2.7 (con la 2.6.4 da
exactamente eso): actualizalo, o sacá la línea `stream_close_delay 5m` de `/etc/caddy/video.caddy` (sin ella, una recarga de
Caddy corta los WebSocket en el acto en lugar de a los 5 minutos). Mientras `validate` falle, **no recargues**.

**Verificá:**

```bash
sudo systemctl is-active caddy                                                              # active
curl -s -o /dev/null -w '%{http_code}\n' https://yosoytelemedicina.tech/pantallas/index.html   # 200: la API sigue sirviendo todo
curl -s 'https://yosoytelemedicina.tech/socket.io/?EIO=4&transport=polling' | cut -c1-20       # 0{"sid":"...  (antes de este paso daba 404)
```

**Rollback** (es una recarga, sin cortar el sitio):

```bash
sudo cp -a /etc/caddy/Caddyfile.antes-de-video /etc/caddy/Caddyfile
sudo caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile
sudo systemctl reload caddy
```

---

## Paso h — Pruebas

Los pasos h1 a h3 son comandos; h4 y h5 son con navegadores. En una segunda sesión de SSH, dejá corriendo
`sudo journalctl -u signaling-server -u coturn -u yosoy-api -f` para ver todo junto mientras probás.

### h1 — El endpoint existe y pide sesión

```bash
curl -s -o /dev/null -w '%{http_code}\n' -X POST https://yosoytelemedicina.tech/video/acceso -H 'Content-Type: application/json' -d '{"origen":"turno","id":1}'
# 401
```

### h2 — El handshake de Socket.IO sin token tiene que rechazar

Son tres pedidos: abrir la conexión, mandar el "CONNECT" sin `auth` y leer la respuesta.

```bash
URL=https://yosoytelemedicina.tech/socket.io/
SID=$(curl -s "$URL?EIO=4&transport=polling" | sed -n 's/.*"sid":"\([^"]*\)".*/\1/p')
curl -s -X POST "$URL?EIO=4&transport=polling&sid=$SID" --data-raw '40'; echo
curl -s "$URL?EIO=4&transport=polling&sid=$SID"; echo
```

Tiene que imprimir `ok` y después:

```
44{"message":"no autorizado","data":{"codigo":"no-autorizado"}}
```

Si en cambio devuelve `40{"sid":...}` (aceptó), **cortá ya**: sacá el `import` del Caddyfile, recargá y avisá; sin token nadie
tiene que poder entrar.

### h3 — Caddy deja pasar el upgrade a WebSocket

```bash
URL=https://yosoytelemedicina.tech/socket.io/
curl --http1.1 -si -m 3 -H 'Connection: Upgrade' -H 'Upgrade: websocket' -H 'Sec-WebSocket-Version: 13' \
     -H 'Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==' "$URL?EIO=4&transport=websocket" | head -n 1
# HTTP/1.1 101 Switching Protocols
```

(`--http1.1` es necesario: los navegadores hacen el upgrade por HTTP/1.1. Después de la primera línea `curl` espera y termina por
el `-m 3`: es normal.)

### h4 — Una llamada real, con dos navegadores

1. Usá **cuentas de prueba** (un profesional y un paciente) y un turno de prueba de hoy, para dentro de unos 5 minutos. La
   sala abre 10 minutos antes del inicio; si probás antes, la pantalla dice a qué hora se abre.
2. Profesional: ingresá, en Inicio generá la sala del turno ("Iniciar sesión") y entrá con "Entrar a la sala →".
3. Paciente (otro navegador, mejor otra máquina o un perfil aparte): entrá desde la notificación o con
   `https://yosoytelemedicina.tech/pantallas/Pantalla_Paciente/sesion_video.html?turno=<IdTurno>`.
4. Tiene que verse y oírse en los dos sentidos, con el 🟢 y sin mensajes de error. En las herramientas de desarrollo (F12):
   en **Network**, el pedido `acceso` (o `video/acceso`) responde `200` con `turnDisponible: true` y trae una entrada `turn:` (no
   compartas esa respuesta: lleva credenciales vigentes); y una conexión `wss://yosoytelemedicina.tech/socket.io/...` con
   estado `101`. En **Console**, sin errores en rojo.
5. En el journal tienen que aparecer `CONEXION ... rol=paciente` y `CONEXION ... rol=profesional`, y al salir `DESCONEXION`.
6. Cuando termines, **cancelá el turno de prueba**.

### h5 — TURN: que coturn entregue un relay

**Credencial temporal.** Las credenciales de TURN se arman con el secreto, así que para probar a mano hace falta una. Este
comando lee el secreto **adentro** de Python y solo imprime una credencial que vence en 10 minutos (el secreto no se ve):

```bash
sudo python3 - <<'PY'
import base64, hashlib, hmac, time
secreto = next(l.split('=', 1)[1].strip() for l in open('/opt/yosoy/config/video.env') if l.startswith('Video__TurnSecreto='))
usuario = f"{int(time.time()) + 600}:prueba"
credencial = base64.b64encode(hmac.new(secreto.encode(), usuario.encode(), hashlib.sha1).digest()).decode()
print("usuario:    ", usuario)
print("credencial: ", credencial)
PY
```

**Con el navegador.** Abrí <https://webrtc.github.io/samples/src/content/peerconnection/trickle-ice/>, borrá el servidor que trae
y agregá `turn:yosoytelemedicina.tech:3478` con ese usuario y esa credencial; **Add Server**, y **Gather candidates**.
(Es una página de terceros, pero la credencial vence en 10 minutos y no da acceso a nada más: no hay problema en pegarla ahí.)
Tiene que aparecer al menos un candidato de tipo **`relay`** (y uno `srflx`, que es el de STUN). Repetí con
`turn:yosoytelemedicina.tech:3478?transport=tcp` para probar el camino por TCP. Si solo aparecen `host` y `srflx`:
UFW o el firewall del proveedor, o credencial equivocada (ver "Si algo falla").

**Por línea de comandos** (viene con el paquete `coturn`; la salida exacta cambia con la versión): lo que importa es que
la allocation se cree y no haya un `401` ni `Unauthorized`:

```bash
turnutils_uclient -v -u '<usuario de arriba>' -w '<credencial de arriba>' yosoytelemedicina.tech
```

**En una llamada real.** Poné a uno de los dos en otra red (datos del celular o una red distinta de la del otro): en
`chrome://webrtc-internals` (Chrome) o `about:webrtc` (Firefox), el par de candidatos elegido tiene que ser de tipo `relay`
cuando no hay camino directo. En la misma red local la llamada va directa (`host`) y no prueba coturn. Antes de dar por
cerrado el TTL de las credenciales, conviene una llamada relayada larga (ver `docs/VIDEOLLAMADAS.md`, "Servidores ICE y
TURN"): no está confirmado cómo se porta coturn con una renovación de ICE después de vencidas las credenciales.

---

## Si algo falla

| Síntoma | Probable causa | Qué hacer |
|---|---|---|
| El signaling no arranca | Falta o es débil `SIGNALING_SECRETO`; puerto en uso. | `sudo journalctl -u signaling-server -n 20 --no-pager` (dice cuál). Volver a correr el script. `sudo ss -ltnp \| grep 3001`. |
| `/video/acceso` contesta 503 | Secreto ausente, corto o igual a `Jwt__Key`. | `sudo journalctl -u yosoy-api --since today --no-pager \| grep 'Video:'`: la línea dice el motivo, sin valores. Corregir `video.env` y reiniciar. |
| `200` pero `turnDisponible: false` | `Video__TurnSecreto` ausente/débil, o sin `Video__Ice__TurnUrls__*`. | Journal de la API: `Video: no se entregan credenciales TURN` con el motivo. |
| El handshake con token válido da `no autorizado` | `SIGNALING_SECRETO` y `Video__SignalingSecreto` distintos (por ejemplo, se rotó uno y no se reinició el servicio). | La comparación `IGUALES` del paso c; volver a correr el script; reiniciar `signaling-server` y `yosoy-api`. |
| trickle-ice no da `relay` | Firewall (UFW o del proveedor), NAT sin `external-ip`, o credencial equivocada. | `sudo ufw status`; panel del proveedor; `sudo journalctl -u coturn -n 50`; credencial nueva (vence a los 10 minutos). |
| coturn rechaza con `401` | El usuario tiene que ser `vencimiento:algo` con el vencimiento en el futuro, y el secreto de `/etc/turnserver.conf` el mismo que `Video__TurnSecreto`. | `sudo bash .../generar-secretos.sh` sin opciones: verifica que coincidan. |
| `caddy validate` falla | Caddy anterior a la 2.7 (`unrecognized subdirective stream_close_delay`) o el `import` mal escrito. | Ver el paso g. **No recargar.** |
| `/socket.io/...` da 502 | El signaling está caído. | `sudo systemctl status signaling-server`; la API y el sitio siguen andando. |
| El paciente no ve el panel de consentimiento (y no es menor ni le falta la fecha de nacimiento) | La API no tiene la clave de cifrado, o la migración no está aplicada. | `sudo journalctl -u yosoy-api --since today --no-pager \| grep 'Video:'`: `lectura de emociones deshabilitada` + motivo. Paso f0.3. |
| `500` al abrir las sesiones clínicas de un paciente, o al consentir | Falta la migración (columnas o tabla nuevas). | Paso f0.2 y su verificación. |
| Una sesión muestra las emociones vacías y el log dice `no se pudieron descifrar las emociones` | La clave de `video.env` no es la que cifró ese dato (se cambió o se perdió), o el dato se alteró. | Volver a poner la clave original (la copia de f0.3). No hay otra forma de leerlo. |

## Qué mirar en `journalctl`

Siempre con `sudo` (tu usuario puede no estar en el grupo del journal); `-n 50` = las últimas 50 líneas, `-f` = en vivo.

| Servicio | Comando | Qué buscar |
|---|---|---|
| signaling | `sudo journalctl -u signaling-server -n 50 --no-pager` | `ARRANQUE escuchando=127.0.0.1:3001`; `CONEXION`/`DESCONEXION` (ip, rol, id de usuario, sala); `RECHAZO` (motivo: `rol-ocupado`, `sala-llena`, token...); `PARADA` al detenerlo; `ERROR`. Nunca hay tokens ni secretos. |
| API | `sudo journalctl -u yosoy-api --since today --no-pager \| grep -i video` | `Video: /video/acceso habilitado.` o `deshabilitado` + motivo; `Video: lectura de emociones habilitada` o `deshabilitada` + motivo; `Video: no se entregan credenciales TURN`; `Video:ZonaHoraria "..." no existe`; `Video: purga de emociones DESHABILITADA (simulación): N sesión(es)…` (la purga no modifica nada mientras no pongas `Video__Emociones__PurgaHabilitada=true`). |
| coturn | `sudo journalctl -u coturn -n 50 --no-pager` (o `-t turnserver`) | Arranque sin `CONFIG ERROR`; altas y bajas de sesiones con el usuario efímero; errores de autenticación. |
| Caddy | `sudo journalctl -u caddy -n 50 --no-pager` | Errores de recarga; `502` por el signaling caído. |
| Firewall | `sudo ufw status numbered` | Las reglas de coturn presentes; el 3001 ausente. |

## Rotar un secreto más adelante

Siempre en horario de poco uso: **reiniciar interrumpe lo que esté en curso** (el signaling reinicia las salas; coturn corta los
relays; las pantallas piden un token nuevo y reconectan solas).

```bash
sudo bash ~/video-deploy/deploy/video/generar-secretos.sh --rotar-signaling
sudo systemctl restart signaling-server yosoy-api
```

```bash
sudo bash ~/video-deploy/deploy/video/generar-secretos.sh --rotar-turn
sudo systemctl restart coturn yosoy-api
```

**La clave de cifrado de las emociones no se rota con este script** (no tiene esa opción): con ella se cifran datos clínicos ya
guardados, y cambiarla sin más los dejaría ilegibles. Ni `--rotar-signaling` ni `--rotar-turn` la tocan.

El script deja una copia `ARCHIVO.previo-AAAAMMDD-HHMMSS` de cada archivo que cambia (mismos permisos). Cuando confirmes que
todo anda, borralas **a mano y por nombre** (contienen el secreto anterior):
`sudo ls -l /opt/yosoy/config/*.previo-* /etc/turnserver.conf.previo-*`.

## Rollback total

En orden inverso, cada uno con su comando en el paso respectivo: **g** (Caddy) → **f1** (el recifrado solo se deshace con el backup
de la base) → **f** (app) → **f0** (migración y clave; normalmente no hace falta) → **e** (drop-in) → **d** (coturn y UFW) →
**c** (signaling y secretos) → **b** (archivos del signaling). Con eso el servidor vuelve a como estaba antes del paso a,
que está en `/opt/yosoy/backups/video-<fecha>/`. Lo mínimo para dejar de exponer la señalización de inmediato es el rollback del
paso g: es una recarga de Caddy, sin cortar el sitio.

## Después del despliegue

Cuando todo esté verificado, actualizá `docs/SERVIDOR.md` (describe lo que está **verificado** en el servidor, por eso no se
tocó en este PR): UFW (3478 UDP/TCP y 49160–49200 UDP), la tabla de servicios (`signaling-server`, `coturn`), las carpetas
(`/opt/yosoy/signaling`, los dos `.env` nuevos), el bloque del `Caddyfile` y las variables de entorno nuevas (para el PR 6:
`Video__Emociones__ClaveCifrado`, solo el nombre, y los `Video__Emociones__*` opcionales), la migración y el recuento de tablas
(44 con `ConsentimientoVideo`). Y decidí, con tu abogado, cuándo se prende la purga (`Video__Emociones__PurgaHabilitada`) y el
plazo de `Video__Emociones__RetencionDias` (3650 es provisorio).

## Mejoras pendientes

- **TLS en coturn** (`turns:` en el 5349, o por el 443): sirve para redes que solo dejan pasar HTTPS. Necesita un certificado que
  coturn pueda leer y su renovación; el detalle está al final de `deploy/coturn/turnserver.conf`. Es una URL más en `video.env`.
- **Medir y ajustar** `user-quota`, `total-quota`, `max-bps` y `bps-capacity` (son puntos de partida) y el rango de 41 puertos UDP
  (alcanza para unas 20 personas relayadas a la vez) con el uso real; y `MemoryMax=256M` del signaling con `systemctl status`.
- **Acortar el TTL** de las credenciales TURN (`Video__Turn__TtlMinutos`) una vez probada una llamada relayada larga.
- **Alertas** sobre la transferencia del mes y sobre los servicios caídos.
- **Rotar la clave de cifrado de las emociones** (volver a cifrar lo guardado con otra clave): no hay una herramienta.
- **Que el paciente vea y pueda pedir la supresión** de lo guardado de él (hoy solo puede revocar por sesión).

## Qué está verificado y qué no

Verificado **sin el VPS** (Windows, con los programas reales cuando existen): `generar-secretos.sh` en 13 escenarios (93
comprobaciones: primera corrida, idempotencia, coturn instalado después, rotaciones con copia previa, conflictos, valores
inválidos, archivos armados a mano, CRLF, `bash -x`, y que **ningún proceso externo reciba un secreto por argv**) y 6 roturas
inventadas a propósito que el banco detecta; la cadena completa con la **API real y el signaling real** arrancados con lo que
escribe el script (token firmado con el secreto generado, credencial TURN recalculada a mano, rechazo sin token y con otra
firma); el empaquetado (`tar` de Windows → `npm ci --omit=dev` → arranque con Node 22); el snippet de Caddy con **Caddy real**
(v2.11.7; versión mínima con 2.6.4, 2.7.0 y 2.8.4): sintaxis en las tres formas de integrar, ruteo, WebSocket, `502` con el
signaling caído, IP del cliente y qué hace `stream_close_delay` en una recarga; los `curl` del paso h y el generador de credencial
TURN (contrastado con `openssl`); el script y los 40 bloques `bash` de este documento (32 del PR 5 y 8 del PR 6) con shellcheck 0.11 (sin avisos); y el apagado
del servidor con clientes conectados (menos de 10 ms; el manejador de `SIGTERM` en sí lo prueba `npm test` en Linux: se omite en Windows).

**No verificado** (necesita el VPS real): coturn arrancando con `deploy/coturn/turnserver.conf` (los nombres de las opciones y
la sintaxis de `denied-peer-ip` salen de la documentación de coturn, no de haberlo corrido; ni qué cuenta exactamente
`user-quota` con usuarios efímeros, ni la ruta de logs por `syslog`, ni si el paquete pide `TURNSERVER_ENABLED`); UFW y el
firewall del proveedor; la unit en un systemd real (`systemd-analyze verify`, el `MemoryMax`, las opciones de aislamiento) y las
instrucciones de `systemctl`/`ss`/`journalctl`; dueños y permisos reales (en Windows no se reflejan; el propio script los
verifica y los imprime al correr en el VPS); las herramientas `turnutils_*` (flags y salida); tu `Caddyfile` real (acá se usó el de
tres líneas de `docs/SERVIDOR.md`); los comandos del paso f (son tu flujo de siempre, transcriptos: no los corrí); y todo lo que
requiere gente y redes reales: cámaras, TURN con relay de verdad, NAT, celulares, Safari.

**PR 6, verificado sin el VPS** (Windows, SQL Server Express local con una base **descartable** que se borró al terminar, Chromium
con cámara falsa, la API y la señalización reales): la migración aplicada dos veces seguidas, con un esquema final idéntico al de
`docs/produccion/01-produccion.sql`, y su rollback; las pruebas de .NET y de Node del PR; 137 comprobaciones con navegadores reales
en 7 escenarios (aceptar y guardar cifrado, rechazar, revocar en plena llamada con los tiempos de producción, menores y sin
fecha de nacimiento, falla cerrada, sin clave, entrevista de admisión y pertenencia); 12 roturas inventadas del código que las
pruebas detectan; `generar-secretos.sh` con la clave (116 comprobaciones, sin que ningún proceso externo reciba un secreto); y el
recifrado real contra esa base (simulación, aplicar, repetir y sin clave).

**PR 6, no verificado** (necesita el despliegue real): la migración sobre la base de **producción** (SQL Server 2025, en Linux, con
el login que la aplique); el `systemd-run` del paso f1 en un systemd real; el análisis con un **rostro real** (en las pruebas el
modelo de expresiones se reemplazó por un doble, porque la cámara falsa no tiene cara); la latencia real a través de Caddy y
HTTPS (el corte por revocación se midió en 2,5 s con la API local); celulares, Safari y pestañas en segundo plano (los
navegadores frenan los temporizadores de una pestaña oculta); la custodia y la restauración de la clave de cifrado; y la
validez legal del texto, del plazo y del tratamiento de menores.
