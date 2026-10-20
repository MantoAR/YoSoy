# Despliegue a producción: chat de WhatsApp para el personal

Runbook para llevar a producción el chat de WhatsApp para el personal (CRM → Chats, solo el Configurador). Se ejecuta **a mano, en dos máquinas** (la PC con PowerShell y el VPS por SSH), con el flujo de
siempre de [`DEPLOY.md`](DEPLOY.md). Escrito el 2026-10-19 contra `main` en `4cff56e` (con los PR #46 a #50) más la rama `fix/chat-simulado-cerrado` (el simulador anónimo cerrado en el código), que tiene que estar en `main` antes de armar el paquete (paso 1.0). Qué se decidió y qué se construyó: [`PROGRESO_CHAT_PERSONAL_WHATSAPP.md`](PROGRESO_CHAT_PERSONAL_WHATSAPP.md).

- **Se escribió sin acceso al VPS y se ejecutó a mano el 19/10/2026** ("Registro de la ejecución del 19/10/2026", al final). Lo que se probó antes de ejecutarlo, y cómo, está en "Qué se verificó y qué no"; lo que no, también. Las frases en presente sobre el VPS (lo que "corre hoy", que "casi seguro tiene una API más vieja") son de antes de esa ejecución: el estado actual está en [`SERVIDOR.md`](SERVIDOR.md), "Estado de producción al 19/10/2026".
- **Sin secretos en este archivo.** Ningún comando imprime un secreto ni el contenido de un `.env`; si alguno lo hiciera, pará.
- Cada paso dice **dónde** se corre, el **comando exacto**, qué **esperar** y cómo **volver atrás**.

## Qué sale a producción (y qué no)

| Qué | Cómo queda |
|---|---|
| Mensajes de **texto** de pacientes | Llegan a CRM → Chats. Contesta el **Configurador** (hace de recepción). Los profesionales **no ven chats** (403 en todo `/mensajeria`) |
| Asistente automático | **Apagado** (`Mensajeria__ModoAtencion=Personal`): nada contesta solo |
| Avisos y recordatorios por WhatsApp | **No salen**. `Mensajeria__AvisosPorWhatsApp` no se define (apagada por defecto). Los recordatorios siguen por mail y campanita |
| Mensaje de emergencia | **Desactivado** (`Mensajeria__Personal__RespuestaDeEmergencia` no se define: falso). Una frase de riesgo marca la conversación *Urgente* y avisa en la campanita, pero al paciente no le llega nada |
| Consentimiento de WhatsApp (migración 16) | **Dormido**. La migración 16 **no hace falta** (paso 2.1) |
| Fotos, audios, documentos | Llegan como un aviso (`[Imagen]`, `[Audio]`...); el archivo **no se descarga ni se guarda**: se mira en el teléfono del número |
| Topes de envío | 30 mensajes por día y 10 por hora (`WHATSAPP_TOPE_DIARIO`, `WHATSAPP_TOPE_POR_HORA`) |
| Número de WhatsApp | **Sin vincular** al terminar el despliegue. El QR se escanea más adelante (paso 7) |
| Apagado de emergencia | **`sudo systemctl stop whatsapp-service`** (y `sudo systemctl disable --now whatsapp-service` para que no vuelva con un reinicio del VPS). **No** `Mensajeria__CanalActivo=Simulado`: no apaga nada y marca como "Enviado" respuestas que no salen (paso 8) |

## Antes de empezar: cinco cosas que cambian el plan

**1. Para ver Chats en el sitio real hace falta `Mensajeria__SoloLocal=false`, y el simulador anónimo queda cerrado en el código, no en la configuración.** Con el valor por defecto (`true`) la API trata como "remoto" todo lo que llega por
Caddy y contesta `/capacidades` con `"mensajeria":false`: el Configurador no ve el grupo CRM → Chats. Con `false` Chats aparece, y eso dejaba abiertas a internet dos rutas del mismo grupo que son **anónimas por diseño**
(`POST /mensajeria/simulado/entrante` y `GET /mensajeria/simulado/conversacion`, en `Endpoints/MensajeriaEndpoints.cs`): cualquiera, sin sesión, podía meter una conversación falsa en la bandeja con el teléfono que quisiera y averiguar si un
teléfono ya habló con la clínica. **Ya no:** la rama `fix/chat-simulado-cerrado` les pone el mismo candado que ya tiene el webhook de WhatsApp: solo contestan a pedidos de la propia máquina, **siempre**, aunque `SoloLocal` sea `false`; a un pedido
que llegó por Caddy le contestan 404 (y el chat simulado de desarrollo, en tu PC, sigue andando igual). Comprobado con la API de `main` y con la del arreglo detrás de un Caddy real (v2.7.0, la versión mínima que pide el VPS), con `curl` y con Chrome,
pidiendo desde una dirección que no es la propia máquina:

| Build | `SoloLocal` | Bloque de Caddy (3.2) | `GET /capacidades` | `POST /mensajeria/simulado/entrante` desde internet |
|---|---|---|---|---|
| Sin el arreglo (`main` hasta `4cff56e`) | por defecto (`true`) | no | `"mensajeria":false` | 404 |
| Sin el arreglo | `false` | no | `"mensajeria":true` | **200: crea la conversación** |
| Sin el arreglo | `false` | sí | `"mensajeria":true` | 404 (lo corta Caddy) |
| **Con el arreglo** | `false` | no | `"mensajeria":true` | **404 (lo corta la API)** |
| **Con el arreglo** | `false` | sí | `"mensajeria":true` | 404 |

El **build que publiques tiene que traer el arreglo** (el paso 1.0 lo comprueba). El bloque de Caddy del **paso 3.2 queda como segunda capa**: si algún día se publica un build sin el arreglo, cambia cómo llega `X-Forwarded-For` o alguien toca el código
sin querer, el simulador sigue cerrado. Las sondas del paso 4.4 comprueban cada capa por separado. (Las del sitio público salen del VPS hacia su propia dirección pública, que para la API es remota; si las forzaras por `127.0.0.1` —por ejemplo con
`curl --resolve`—, Caddy le pasa `X-Forwarded-For: 127.0.0.1` y la API lo toma como la propia máquina.) *Alternativa más conservadora:* dejar `SoloLocal=true` y entrar a Chats por un túnel (`ssh -L 5000:127.0.0.1:5000 agustin@179.199.155.50`, y abrir
`http://localhost:5000/pantallas/index.html`); la pantalla WhatsApp, la del QR, anda por el sitio real igual, porque no depende de `SoloLocal`.

**2. El VPS casi seguro tiene una API más vieja que `main`.** Este despliegue lleva de una vez varios PR (Baileys como único motor, recordatorios sin WhatsApp, mails mínimos, consentimiento dormido, el chat del
personal). Tres cosas que rompen o sorprenden si no se miran antes (el paso 1.2 las chequea):
- Si el `.env` tiene `Mensajeria__CanalActivo=WhatsAppWebJs`, la API nueva **no arranca**. El paso 3.4 lo reemplaza por `Baileys`.
- `Email__From` ya no viene escrito en `appsettings.json` (PR #34): si el `.env` solo tenía `Email__Password`, los mails dejan de salir. Cargala **antes** de publicar (`DEPLOY.md`, "Credenciales de correo").
- **Los profesionales dejan de ver los chats** (decisión D2): avisales antes.

**3. "Desplegado sin vincular" es un estado quieto y ordenado, pero la pantalla no ayuda.** Sin número vinculado el servicio abre 2 ventanas de QR de ~2,7 minutos cada una (unos 5 minutos en total desde que arranca) y
después **se queda quieto**. Pasado ese rato, Configurador → WhatsApp muestra "Desconectado / Generando el código QR..." **sin ningún botón**: para tener un QR nuevo hay que reiniciar el servicio por SSH
(`sudo systemctl restart whatsapp-service`). Es una mejora pendiente (un botón en esa pantalla; decidido: no se hace ahora, ver "Mejoras pendientes"). Todo el detalle está en el paso 6.

**4. Lo que corre hoy en el VPS como `whatsapp-service` es, según `SERVIDOR.md`, el servicio viejo** (whatsapp-web.js + Chromium, `MemoryMax=2G`). Este runbook lo reemplaza (con backup y rollback) y sirve igual si
nunca se instaló (el paso 1.2 dice cuál es el caso). **Nunca los dos servicios a la vez**: usan el mismo puerto.

**5. Apagar de verdad es parar el servicio, no cambiar `CanalActivo`.** `sudo systemctl stop whatsapp-service` corta todo (y `sudo systemctl disable --now whatsapp-service`, para que no vuelva con un reinicio del VPS). `Mensajeria__CanalActivo=Simulado` **no apaga nada**: la API deja de hablarle al servicio, pero guarda las respuestas del equipo como **"Enviado"** aunque **no salen**, y el equipo cree que se mandaron y los pacientes no reciben nada. No lo uses como interruptor (paso 8, "Apagar todo rápido").

## Convenciones

- **[PC]** es PowerShell en tu PC. **[VPS]** es una sesión SSH que ya abriste (`ssh agustin@179.199.155.50`): pegá las líneas ahí. No uses `ssh servidor 'comando con sudo'`: `sudo` necesita una terminal para
  pedirte la contraseña. Los comandos del VPS que **cambian algo** son **una sola línea** con `&&`: si un paso falla, los siguientes no corren. Los de chequeo (paso 1.2) son líneas sueltas de solo lectura. Los tests de existencia y los `ls` sobre `/opt/yosoy` llevan `sudo`: tu usuario no puede entrar a esas carpetas; y si el comando usa un `*`, va dentro de `sudo sh -c '...'`, porque tu shell lo expande antes de `sudo` y no encuentra nada.
  **Pegá un comando por vez**, y corré `sudo -v` antes de empezar (pide la contraseña una vez y `sudo` la recuerda unos minutos): si pegás varias líneas juntas y alguna pide la contraseña de `sudo`, las que siguen se mezclan con lo que escribas.
- `FECHA` es **la de la PC** (`Get-Date -Format yyyyMMdd`), no la del `date` del servidor; si el despliegue cruza la medianoche, seguí usando la del día en que armaste los paquetes. `TAG` es `chat`. Lo primero en la sesión SSH (y de nuevo si la cerrás):
  ```bash
  FECHA=YYYYMMDD; TAG=chat; export PATH="$PATH:/opt/mssql-tools18/bin"      # FECHA: la de la PC
  ```
- `<usuario-administrador>` es el login administrador de SQL Server (el que creó la base), **no** `yosoy_app`. `sqlcmd` y `sudo` te piden la contraseña: no la escribas en el comando (`-P`).
- Los `grep` de este runbook imprimen solo la línea que buscan o un conteo, nunca el resto de un `.env`.

## El orden, de un vistazo

| Paso | Qué | Dónde | Corta el servicio | Se deshace |
|---|---|---|---|---|
| 1 | Backup de la base y chequeo previo del VPS | PC y VPS | No | — (solo lee y copia) |
| 2 | Migración 17 | VPS | No | Sí (solo agrega) |
| 3 | Respaldo de la configuración, bloque de Caddy, secreto y variables | VPS | Recarga de Caddy (sin cortar) | Sí (3.1 guarda lo anterior) |
| 4 | Publicar la API | PC y VPS | La API, unos segundos | Sí (rollback 4.5) |
| 5 | Instalar `whatsapp-service` (corriendo y sin vincular) | PC y VPS | El servicio viejo, si había | Sí (rollback 5.10) |
| 6 | Qué pasa mientras está sin vincular | — | — | — |
| 7 | **Para después:** escanear el QR y probar con tu teléfono | VPS y navegador | No | Sí (desvincular) |
| 8 | Operación del día a día | VPS | — | — |
| 9 | Riesgos conocidos | — | — | — |

Antes de empezar: elegí un horario de poco uso (la API se reinicia) y avisá a los profesionales de que ya no van a ver los chats.

---

## Paso 1. Backup de la base y chequeo previo del VPS

### 1.0 [PC] El código que vas a desplegar

```powershell
cd D:\Claude\YoSoy\YoSoy
git switch main; git pull --ff-only; git status --short; git log -1 --oneline
(Select-String -Path Endpoints\MensajeriaEndpoints.cs -Pattern 'SoloDesdeLaPropiaMaquina' -SimpleMatch | Measure-Object).Count      # 3: el simulador anónimo está cerrado en el código
$fecha = Get-Date -Format yyyyMMdd; $fecha                  # anotá el valor: es la FECHA que se usa en el VPS
New-Item -ItemType Directory -Force 'D:\Claude\Backups\yosoy-produccion' | Out-Null
```

**Esperado:** `git status` sin líneas; `git log -1` es `4cff56e` (*Merge pull request #50*) o uno posterior; el conteo da `3` (el simulador cerrado en el código; si da `0`, tu `main` todavía no trae `fix/chat-simulado-cerrado`: mergealo antes de seguir, o seguí solo si aceptás que **únicamente el bloque de Caddy del 3.2** lo cierra); anotá el commit: es lo que desplegás. Si `main` avanzó, mirá qué cambió desde `4cff56e` en lo que usa este
runbook (`git diff 4cff56e..HEAD --stat -- migrations docs/produccion appsettings.json Endpoints Services/Mensajeria whatsapp-service`) antes de seguir. La carpeta de backups está **fuera de cualquier repositorio**.

### 1.1 Backup de la base `yosoy`, que termina solo en tu PC

La base de producción vive en el VPS, así que el `.bak` se genera ahí y se baja a la PC. **[VPS]** El backup y su verificación (te pide la contraseña una vez):

```bash
sudo mkdir -p /var/opt/mssql/backup && sudo chown mssql:mssql /var/opt/mssql/backup && sqlcmd -S localhost -U <usuario-administrador> -C -Q "BACKUP DATABASE [yosoy] TO DISK = N'/var/opt/mssql/backup/yosoy-pre-$TAG-$FECHA.bak' WITH CHECKSUM, INIT; RESTORE VERIFYONLY FROM DISK = N'/var/opt/mssql/backup/yosoy-pre-$TAG-$FECHA.bak' WITH CHECKSUM"
```

**Esperado:** `BACKUP DATABASE successfully processed ... pages` y `The backup set on file 1 is valid.` **Sin `WITH COMPRESSION`, a propósito:** el ejemplo de `DEPLOY.md` lo trae, pero SQL Server Express no crea backups comprimidos
(probado en la Express 2019 de tu PC: `Msg 1844, BACKUP DATABASE WITH COMPRESSION is not supported on Express Edition (64-bit)`). Si tu versión del VPS lo admite, el backup es más chico con él; sin él anda seguro.

**[VPS]** El archivo lo escribe `mssql`, así que tu usuario no lo puede leer: se copia a tu carpeta y se anota su huella.

```bash
sudo cp /var/opt/mssql/backup/yosoy-pre-$TAG-$FECHA.bak ~/ && sudo chown "$USER": ~/yosoy-pre-$TAG-$FECHA.bak && ls -la ~/yosoy-pre-$TAG-$FECHA.bak && sha256sum ~/yosoy-pre-$TAG-$FECHA.bak
```

**[PC]** Bajarlo y comparar la huella (`$fecha` es la de 1.0; si abriste otra ventana de PowerShell, definila de nuevo: `$fecha = 'YYYYMMDD'`):

```powershell
scp "agustin@179.199.155.50:/home/agustin/yosoy-pre-chat-${fecha}.bak" 'D:\Claude\Backups\yosoy-produccion\'
Get-FileHash "D:\Claude\Backups\yosoy-produccion\yosoy-pre-chat-${fecha}.bak" -Algorithm SHA256; (Get-Item "D:\Claude\Backups\yosoy-produccion\yosoy-pre-chat-${fecha}.bak").Length
```

**Esperado:** la misma huella SHA-256 que `sha256sum` (las mayúsculas no importan) y el mismo tamaño en bytes que `ls -la`. **Recién entonces, [VPS]**, borrar las copias del servidor (los respaldos de la base quedan solo en tu PC):

```bash
sudo rm -f /var/opt/mssql/backup/yosoy-pre-$TAG-$FECHA.bak ~/yosoy-pre-$TAG-$FECHA.bak && sudo ls -la /var/opt/mssql/backup/
```

**Ojo:** según `SERVIDOR.md` el VPS tiene SQL Server 2025 y tu PC tiene 2019 (15.0.2000.5): una base de una versión más nueva **no se restaura en una más vieja**, así que este `.bak` sirve para restaurar en el VPS
(u otro SQL Server 2025), no en tu instancia local. `RESTORE VERIFYONLY` ya comprobó que se puede leer. **Volver atrás:** no aplica (solo lee y copia). Para este despliegue no hace falta restaurar nunca: la
migración 17 solo agrega.

### 1.2 [VPS] Chequeo previo (todo de solo lectura)

Pegá estas líneas **de a una**. Si algo no da lo esperado, **no sigas** hasta entender por qué.

```bash
# Sistema
timedatectl | grep -E "Time zone|System clock synchronized"          # America/Argentina/Buenos_Aires y "synchronized: yes" (con la hora mal, WhatsApp no conecta)
df -h / | tail -n 1; free -m | sed -n 1,2p                           # espacio libre de varios GB; memoria: el servicio puede llegar a 512 MB
node --version; npm --version                                        # v22.x o más nuevo
caddy version                                                        # v2.7.0 o más nuevo (con la 2.6 el snippet de video no valida)
getent hosts web.whatsapp.com | head -n 1; timeout 5 bash -c '</dev/tcp/web.whatsapp.com/443' && echo "443 alcanzable"     # salida hacia WhatsApp: solo abre un socket TCP, no habla con WhatsApp
```

```bash
# Servicios y puertos
systemctl is-active yosoy-api caddy mssql-server signaling-server coturn fail2ban      # active, seis veces
systemctl is-active whatsapp-service; systemctl is-enabled whatsapp-service           # active/enabled: hay un servicio (el viejo). inactive/not-found: nunca se instaló
sudo ss -ltnp | grep -E ':(3001|3002|5000) '                                          # todo en 127.0.0.1, nunca 0.0.0.0 ni *
```

```bash
# Configuración de la API (imprimen solo esas líneas o un conteo)
sudo grep -n -E '^Mensajeria__(CanalActivo|ModoAtencion|SoloLocal|ChatSimuladoHabilitado|AvisosPorWhatsApp|Personal__RespuestaDeEmergencia)=' /opt/yosoy/config/.env     # ideal: nada, o CanalActivo=Simulado|Baileys. Si dice WhatsAppWebJs: el paso 3.4 lo cambia. AvisosPorWhatsApp o RespuestaDeEmergencia en true: sacalas. ChatSimuladoHabilitado=false: sacala (apaga Chats)
sudo grep -c -E '^Email__From=.+' /opt/yosoy/config/.env                                # 1. Si da 0, cargá Email__From antes de publicar (DEPLOY.md, "Credenciales de correo")
sudo sh -c "stat -c '%a %U:%G %n' /opt/yosoy/config/.env /opt/yosoy/config/*.env"       # 600 yosoy:yosoy en cada .env
sudo ls /opt/yosoy/app | grep -i appsettings                                            # appsettings.json y nada más: el corte del paso 4.3 vacía la carpeta, y un appsettings.Production.json propio del servidor se perdería (quedaría solo en app-anterior-*)
systemctl cat yosoy-api --no-pager | grep -E '^EnvironmentFile='                        # .env, whatsapp.env (drop-in whatsapp.conf) y -video.env. Si falta whatsapp.env, el paso 3.3 instala el drop-in
sudo grep -c -E '^Mensajeria__WhatsApp__Secreto=.{32,}$' /opt/yosoy/config/whatsapp.env # 1: ya hay un secreto de 32+ caracteres. 0 o "No such file": el paso 3.3 lo crea
```

```bash
# Lo que hay instalado de WhatsApp y de despliegues anteriores
sudo ls -la /opt/yosoy /opt/yosoy/whatsapp                                              # ¿existe whatsapp/app? ¿sesion/ (la vieja)? sesion-baileys/ no tiene que existir todavía
sudo sh -c 'ls -d /opt/yosoy/app-anterior-* /opt/yosoy/backups/* 2>/dev/null'           # lo que dejaron despliegues anteriores (no se borra)
systemctl cat whatsapp-service --no-pager 2>/dev/null | grep -E '^(ExecStart|WorkingDirectory|MemoryMax)=|PUPPETEER|WHATSAPP_BACKEND'     # el viejo: MemoryMax=2G y variables PUPPETEER. Sin salida: no había servicio
sudo cat /etc/caddy/Caddyfile                                                           # sin secretos: tiene que parecerse al de SERVIDOR.md (el sitio, el import de video.caddy y el reverse_proxy)
```

```bash
# Lo que ya anda
curl -s -o /dev/null -w "%{http_code} %{redirect_url}\n" http://localhost:5000/         # 302 http://localhost:5000/pantallas/index.html
curl -s http://127.0.0.1:5000/capacidades; echo                                         # {"mensajeria":...}: la API vieja puede no traer modoAtencion
curl -s -o /dev/null -w "%{http_code}\n" https://yosoytelemedicina.tech/pantallas/index.html     # 200
sqlcmd -S localhost -d yosoy -U <usuario-administrador> -C -I -Q "SELECT COUNT(*) AS tablas FROM sys.tables"      # 44 (o 45 si alguna vez se aplicó la migración 16)
```

**Si el servicio viejo está activo**, abrí Configurador → WhatsApp: si dice "Conectado (+54...)", ese número está vinculado al servicio viejo. Este runbook lo reemplaza y **la sesión vieja no se migra a Baileys**
(el número se vuelve a vincular en el paso 7); el dispositivo viejo queda en "Dispositivos vinculados" del teléfono hasta que lo cierres (`DEPLOY.md`, paso 10). Si ese es el número que hoy atiende pacientes y no querés
cortarlo todavía, no sigas.

---

## Paso 2. Migración 17

### 2.1 ¿Hace falta la migración 16? No

- **Con `Mensajeria__AvisosPorWhatsApp` apagada (el valor por defecto) la API no toca la tabla `ConsentimientoWhatsApp`**: ni al registrar pacientes, ni en el perfil, ni en los envíos, ni cuando llega una BAJA
  (`DEPLOY.md`, "Consentimiento de WhatsApp"). Este runbook no define esa llave.
- Producción tiene 44 tablas, sin la 16 (`SERVIDOR.md`). La 17 es independiente de la 16 (lo dice el encabezado de la migración).
- Comprobado para esta versión: la suite completa de `main` más el arreglo contra una base de **44 tablas con la 17 aplicada** (la forma de producción después de este paso), sin las 45 pruebas que son de la tabla de la 16,
  da **1772 de 1772** con el arreglo del simulador (ver "Qué se verificó").
- Solo habría que aplicar la 16 el día que se prenda `Mensajeria__AvisosPorWhatsApp` (y antes del texto legal validado).

### 2.2 [PC] Subir el archivo

```powershell
scp D:\Claude\YoSoy\YoSoy\migrations\add_chat_personal_entrantes.sql agustin@179.199.155.50:/home/agustin/
```

### 2.3 [VPS] Estado antes

```bash
sqlcmd -S localhost -d yosoy -U <usuario-administrador> -C -I -Q "SELECT (SELECT COUNT(*) FROM sys.columns WHERE (object_id = OBJECT_ID('dbo.MensajeChat') AND name IN ('IdExterno', 'Tipo')) OR (object_id = OBJECT_ID('dbo.Conversacion') AND name = 'NombreContacto')) AS columnas, (SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_MensajeChat_Tipo' AND parent_object_id = OBJECT_ID('dbo.MensajeChat')) AS restriccion, (SELECT COUNT(*) FROM sys.default_constraints WHERE name = 'DF_MensajeChat_Tipo' AND parent_object_id = OBJECT_ID('dbo.MensajeChat')) AS valor_por_defecto, (SELECT COUNT(*) FROM sys.indexes WHERE name = 'UQ_MensajeChat_IdExterno' AND object_id = OBJECT_ID('dbo.MensajeChat')) AS indice, (SELECT COUNT(*) FROM sys.tables) AS tablas"
```

**Esperado:** `0  0  0  0  44` (columnas, restricción, valor por defecto, índice, tablas). Si ya da `3 1 1 1`, la migración está aplicada: seguí al paso 3.

### 2.4 [VPS] Aplicar (idempotente)

```bash
sqlcmd -S localhost -d yosoy -U <usuario-administrador> -C -I -b -f 65001 -i ~/add_chat_personal_entrantes.sql
```

Con el login administrador, **no** con `yosoy_app`; `-I` es obligatorio (índice filtrado) y `-f 65001` es por los acentos. **Esperado:** sin salida y sin errores (`-b` corta si algo falla).

### 2.5 [VPS] Estado después

Repetí la consulta del 2.3. **Esperado:** `3  1  1  1  44` (las tres columnas, la restricción del tipo, su valor por defecto, el índice único filtrado; las tablas siguen siendo 44). Volver a correr el 2.4 es seguro:
no cambia nada.

### 2.6 Volver atrás

No hace falta: solo agrega, la API anterior ignora las columnas (el valor por defecto de `Tipo` hace que sus `INSERT` sigan andando) y el rollback de la API (4.5) no la toca. Si de verdad hubiera que sacarla (se pierden el
tipo, el id y el nombre de lo ya guardado, no el texto), con un backup hecho: `DROP INDEX UQ_MensajeChat_IdExterno ON dbo.MensajeChat; ALTER TABLE dbo.MensajeChat DROP CONSTRAINT CK_MensajeChat_Tipo; ALTER TABLE dbo.MensajeChat DROP CONSTRAINT DF_MensajeChat_Tipo; ALTER TABLE dbo.MensajeChat DROP COLUMN IdExterno, Tipo; ALTER TABLE dbo.Conversacion DROP COLUMN NombreContacto;`
(el encabezado del script dice lo mismo).

---

## Paso 3. Configuración: respaldo, Caddy, secreto y variables

Todo **antes** de publicar la API. **No reinicies la API vieja entre el 3.4 y el 4.3**: con `Mensajeria__CanalActivo=Baileys` en el `.env`, una API muy vieja podría no arrancar.

### Las variables, solo los nombres

| Variable | Archivo | Valor | Para qué |
|---|---|---|---|
| `Mensajeria__CanalActivo` | `/opt/yosoy/config/.env` | `Baileys` | El canal real (habla con `whatsapp-service`). Valores válidos: `Simulado` y `Baileys` |
| `Mensajeria__ModoAtencion` | `.env` | `Personal` | El asistente no contesta; el chat es del personal. Un valor inválido o vacío frena el arranque |
| `Mensajeria__SoloLocal` | `.env` | `false` | Para que Chats sea visible por el sitio. El simulador anónimo sigue cerrado: en el código (siempre solo local) y por el bloque de Caddy del 3.2 |
| `Mensajeria__ChatSimuladoHabilitado` | — | **no definir** (`true` en `appsettings.json`; el 3.4 saca la que hubiera) | Con `false` se apaga todo `/mensajeria`, también la bandeja |
| `Mensajeria__AvisosPorWhatsApp` | — | **no definir** (apagada) | Sin avisos de turno por WhatsApp |
| `Mensajeria__Personal__RespuestaDeEmergencia` | — | **no definir** (desactivada) | Ver "Qué sale a producción" |
| `Mensajeria__WhatsApp__Secreto` | `/opt/yosoy/config/whatsapp.env` | generado en el VPS, nunca visible | Secreto compartido API ↔ servicio (`X-Shared-Secret`) |
| `API_BASE_URL` | `whatsapp.env` | `http://127.0.0.1:5000` | Adónde reenvía los mensajes el servicio |
| `WHATSAPP_TOPE_DIARIO`, `WHATSAPP_TOPE_POR_HORA` | `whatsapp.env` | `30` y `10` | Topes iniciales bajos |
| `WHATSAPP_BAILEYS_SESSION_PATH` | la unit | `/opt/yosoy/whatsapp/sesion-baileys` | Carpeta de la sesión (es una credencial) |
| `WHATSAPP_BAILEYS_VENTANAS_QR` | (opcional) | `2` por defecto, de 1 a 5 | Ventanas de QR que se abren solas sin número vinculado |

**El secreto compartido vive en un solo archivo, `whatsapp.env`,** y lo cargan los dos procesos: la API por el drop-in `yosoy-api.service.d/whatsapp.conf` (`EnvironmentFile=`) y el servicio por su unit. No hace falta
copiarlo al `.env` de la API, y dos copias podrían desfasarse. Ninguna otra variable es obligatoria para la API con `Baileys` (sin el secreto, cada pedido al servicio recibe 401).

### 3.1 [VPS] Respaldo de la configuración (el rollback lo usa)

```bash
sudo mkdir -p /opt/yosoy/backups/chat-$FECHA && for a in /opt/yosoy/config/.env /opt/yosoy/config/whatsapp.env /etc/caddy/Caddyfile /etc/systemd/system/whatsapp-service.service /etc/systemd/system/yosoy-api.service.d/whatsapp.conf; do sudo test -e "$a" && sudo cp -a "$a" /opt/yosoy/backups/chat-$FECHA/ || echo "(no existe: $a)"; done; sudo ls -A /opt/yosoy/backups/chat-$FECHA/
```

**Esperado:** la lista de lo que se copió (`.env`, `Caddyfile`, y los demás si existen) y un `(no existe: ...)` por cada archivo que no esté. La carpeta guarda secretos: queda en `/opt/yosoy/backups/` (700), no la copies a ningún lado.

### 3.2 [VPS] Caddy: la segunda capa del simulador anónimo

Con el arreglo en el build, la API ya contesta 404 por su cuenta a un pedido que llegó por Caddy (primera capa, en el código). Este bloque es la **segunda capa**: no cuesta nada y mantiene cerrado el simulador si por error se publicara un build sin el arreglo, si cambiara cómo llega `X-Forwarded-For` o si alguien tocara el código sin querer. Mostrá el `Caddyfile` (no tiene secretos) y comprobá que se parece al de `SERVIDOR.md`; después agregá el bloque, que va dentro del sitio, justo después del `import` de `video.caddy`. El comando es idempotente (si el
bloque ya está, no lo repite) y **se corta sin recargar nada** si el archivo no tiene esa línea de `import` tal cual:

```bash
{ sudo grep -q 'mensajeria/simulado' /etc/caddy/Caddyfile || sudo sed -i '/^[[:space:]]*import \/etc\/caddy\/video\.caddy[[:space:]]*$/a\    handle /mensajeria/simulado/* {\n        respond 404\n    }' /etc/caddy/Caddyfile; } && sudo grep -c 'mensajeria/simulado' /etc/caddy/Caddyfile && sudo caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile && sudo systemctl reload caddy && echo "caddy recargado"
```

**Esperado:** `1`, después `Valid configuration` (más unas líneas de log en JSON, y quizás un aviso de formato) y `caddy recargado`. Si imprime `0` y se corta, el `Caddyfile` es distinto de lo esperado: editalo a mano
(`sudo nano /etc/caddy/Caddyfile`) y poné dentro del sitio, antes del `reverse_proxy`:

```
    handle /mensajeria/simulado/* {
        respond 404
    }
```

y repetí `sudo caddy validate ...` y `sudo systemctl reload caddy`. Recargar no corta las videollamadas en curso (`stream_close_delay`, ver `SERVIDOR.md`). **Volver atrás:** `sudo cp -a /opt/yosoy/backups/chat-$FECHA/Caddyfile /etc/caddy/Caddyfile && sudo systemctl reload caddy`.
Hasta que la API pase a `SoloLocal=false` (paso 4) este bloque no cambia nada visible, y con el arreglo en el build tampoco después: las sondas del 4.4 comprueban cada capa por separado.

### 3.3 [VPS] El secreto compartido, sin imprimirlo

Mirá lo que dio el chequeo del paso 1.2 (`Mensajeria__WhatsApp__Secreto` con 32+ caracteres):

- **Si ya hay un secreto** (dio `1`): no lo regeneres. Asegurá solo que `whatsapp.env` tenga `API_BASE_URL`:
  ```bash
  sudo sed -i '/^API_BASE_URL=/d' /opt/yosoy/config/whatsapp.env && printf 'API_BASE_URL=http://127.0.0.1:5000\n' | sudo tee -a /opt/yosoy/config/whatsapp.env >/dev/null && sudo stat -c '%a %U:%G %n' /opt/yosoy/config/whatsapp.env
  ```
- **Si no hay** (dio `0` o el archivo no existe): se crea con permisos restrictivos y el secreto se genera ahí mismo; el valor pasa por una tubería y no se imprime ni queda en el historial:
  ```bash
  sudo test -f /opt/yosoy/config/whatsapp.env || sudo install -m 600 -o yosoy -g yosoy /dev/null /opt/yosoy/config/whatsapp.env; sudo sed -i '/^Mensajeria__WhatsApp__Secreto=/d;/^API_BASE_URL=/d' /opt/yosoy/config/whatsapp.env && printf 'Mensajeria__WhatsApp__Secreto=%s\nAPI_BASE_URL=http://127.0.0.1:5000\n' "$(openssl rand -hex 32)" | sudo tee -a /opt/yosoy/config/whatsapp.env >/dev/null && sudo chown yosoy:yosoy /opt/yosoy/config/whatsapp.env && sudo chmod 600 /opt/yosoy/config/whatsapp.env && sudo stat -c '%a %U:%G %n' /opt/yosoy/config/whatsapp.env && sudo grep -c -E '^Mensajeria__WhatsApp__Secreto=[0-9a-f]{64}$' /opt/yosoy/config/whatsapp.env
  ```
- **Si el drop-in `whatsapp.conf` no figuraba** en el chequeo de `EnvironmentFile=` del paso 1.2, la API no lee `whatsapp.env`: instalalo (si existe, este comando lo deja igual):
  ```bash
  sudo mkdir -p /etc/systemd/system/yosoy-api.service.d && printf '[Service]\nEnvironmentFile=/opt/yosoy/config/whatsapp.env\n' | sudo tee /etc/systemd/system/yosoy-api.service.d/whatsapp.conf >/dev/null && sudo systemctl daemon-reload && systemctl cat yosoy-api --no-pager | grep -E '^EnvironmentFile='
  ```

**Esperado:** `600 yosoy:yosoy /opt/yosoy/config/whatsapp.env` y, si lo generaste, un `1` (hay una línea con un secreto de 64 caracteres hexadecimales). El valor del secreto no aparece en ninguna salida.
**Volver atrás:** `sudo cp -a /opt/yosoy/backups/chat-$FECHA/whatsapp.env /opt/yosoy/config/whatsapp.env` (o borrarlo si no existía).

### 3.4 [VPS] Las variables de la API

Quita las que ya estuvieran (incluido un `WhatsAppWebJs` viejo), saca también un `Mensajeria__ChatSimuladoHabilitado` si lo hubiera (con `false` se apagaría todo `/mensajeria`, también Chats) y agrega las tres, sin imprimir el resto del archivo:

```bash
sudo sed -i '/^Mensajeria__ModoAtencion=/d;/^Mensajeria__CanalActivo=/d;/^Mensajeria__SoloLocal=/d;/^Mensajeria__ChatSimuladoHabilitado=/d' /opt/yosoy/config/.env && printf '\nMensajeria__ModoAtencion=Personal\nMensajeria__CanalActivo=Baileys\nMensajeria__SoloLocal=false\n' | sudo tee -a /opt/yosoy/config/.env >/dev/null && sudo grep -n -E '^Mensajeria__(CanalActivo|ModoAtencion|SoloLocal)=' /opt/yosoy/config/.env
```

### 3.5 [VPS] Comprobar sin imprimir

```bash
sudo grep -n -E '^Mensajeria__(CanalActivo|ModoAtencion|SoloLocal|ChatSimuladoHabilitado|AvisosPorWhatsApp|Personal__RespuestaDeEmergencia)=' /opt/yosoy/config/.env; sudo stat -c '%a %U:%G %n' /opt/yosoy/config/.env /opt/yosoy/config/whatsapp.env; systemctl cat yosoy-api --no-pager | grep -c '^EnvironmentFile=.*whatsapp.env'
```

**Esperado:** exactamente tres líneas de `Mensajeria__...` (`ModoAtencion=Personal`, `CanalActivo=Baileys`, `SoloLocal=false`) y ninguna de `ChatSimuladoHabilitado`, `AvisosPorWhatsApp` ni `RespuestaDeEmergencia` (si
aparece alguna con otro valor, corregila); `600 yosoy:yosoy` dos veces; y un `1` al final. **Volver atrás:** `sudo cp -a /opt/yosoy/backups/chat-$FECHA/.env /opt/yosoy/config/.env`.

---

## Paso 4. Publicar la API

### 4.1 [PC] Publicar y subir (el flujo de siempre: autocontenido, fuera del repositorio)

```powershell
cd D:\Claude\YoSoy\YoSoy
$fecha = Get-Date -Format yyyyMMdd; $tag = 'chat'
dotnet publish YoSoy.csproj -c Release -r linux-x64 --self-contained true -o "D:\Claude\Publish-YoSoy-${fecha}-${tag}"
tar -czf "$env:TEMP\Publish-YoSoy-${fecha}-${tag}.tar.gz" -C "D:\Claude\Publish-YoSoy-${fecha}-${tag}" .
scp "$env:TEMP\Publish-YoSoy-${fecha}-${tag}.tar.gz" agustin@179.199.155.50:/home/agustin/
```

**Esperado:** el `publish` termina sin errores (imprime muchos avisos de nulabilidad `CS86xx`: son normales). En el ensayo con este mismo comando: 434 archivos, unos 133 MB, con el binario `YoSoy`, `appsettings.json` y la
carpeta `PANTALLAS\`, y sin ningún `.env`. Usa la `FECHA` de esta PC; la misma va en el VPS.

### 4.2 [VPS] Desempaquetar y comprobar que está completo

```bash
test -f ~/Publish-YoSoy-$FECHA-$TAG.tar.gz && mkdir ~/Publish-YoSoy-$FECHA-$TAG && tar -xzf ~/Publish-YoSoy-$FECHA-$TAG.tar.gz -C ~/Publish-YoSoy-$FECHA-$TAG && ls -la ~/Publish-YoSoy-$FECHA-$TAG/YoSoy ~/Publish-YoSoy-$FECHA-$TAG/PANTALLAS/Pantalla_ChatSimulado/js/bandeja.js
```

**Esperado:** dos archivos listados. Si el `.tar.gz` no está o el `mkdir` dice que ya existe, la `FECHA` está mal o quedó algo de antes: **no sigas**.

### 4.3 [VPS] Backup `app-anterior-<tag>-<fecha>`, corte y arranque

```bash
test -f ~/Publish-YoSoy-$FECHA-$TAG/YoSoy && sudo test ! -e /opt/yosoy/app-anterior-$TAG-$FECHA && sudo cp -a /opt/yosoy/app /opt/yosoy/app-anterior-$TAG-$FECHA && sudo diff -rq /opt/yosoy/app /opt/yosoy/app-anterior-$TAG-$FECHA && echo "backup idéntico" && sudo systemctl stop yosoy-api && sudo find /opt/yosoy/app -mindepth 1 -delete && sudo cp -a ~/Publish-YoSoy-$FECHA-$TAG/. /opt/yosoy/app/ && sudo chown -R yosoy:yosoy /opt/yosoy/app && sudo chmod +x /opt/yosoy/app/YoSoy && sudo systemctl start yosoy-api && sleep 6 && systemctl is-active yosoy-api && curl -s -o /dev/null -w "%{http_code} %{redirect_url}\n" http://localhost:5000/
```

**Esperado:** `backup idéntico`, `active` y `302 http://localhost:5000/pantallas/index.html`. La cadena se frena **antes** de parar la API si el publish no está desempaquetado, si el backup ya existe o si `diff -rq` encuentra diferencias.
Si se corta **después** de `systemctl stop`, la API quedó parada: corré el rollback (4.5).

### 4.4 [VPS] Verificar

```bash
sudo journalctl -u yosoy-api --since '5 minutes ago' --no-pager | grep -iE 'error|exception|fail|Zona horaria|Video:|migración 17'
curl -s http://127.0.0.1:5000/capacidades; echo; curl -s https://yosoytelemedicina.tech/capacidades; echo
curl -s -o /dev/null -w 'simulador, desde la propia máquina: %{http_code}\n' -X POST http://127.0.0.1:5000/mensajeria/simulado/entrante -H 'Content-Type: application/json' -d '{}'
curl -s -o /dev/null -w 'simulador, como si llegara por un proxy (la API sola): %{http_code}\n' -X POST http://127.0.0.1:5000/mensajeria/simulado/entrante -H 'X-Forwarded-For: 203.0.113.7' -H 'Content-Type: application/json' -d '{}'
curl -s -o /dev/null -w 'simulador, por el sitio público (Caddy y la API): %{http_code}\n' -X POST https://yosoytelemedicina.tech/mensajeria/simulado/entrante -H 'Content-Type: application/json' -d '{}'
```

**Esperado:**
- Del journal: la línea `Zona horaria de la clínica: America/Argentina/Buenos_Aires, ...` (y `Video: /video/acceso habilitado.` si el video está configurado); **ninguna excepción** y ninguna mención de `migración 17`.
- `/capacidades`, las dos veces: `{"mensajeria":true,"modoAtencion":"personal","canalActivo":"WhatsApp"}`. El `canalActivo` lo trae `main` desde el PR #50: con un build anterior ese campo no viene (y el selector "Enviar por" de Chats sigue diciendo "Chat simulado": es solo un rótulo, las respuestas salen igual por el canal activo). Si la pública dice `false`, `SoloLocal` no quedó en `false`: revisá el 3.4 y reiniciá la API.
- Las tres sondas del simulador mandan un cuerpo **sin teléfono** (`{}`): si llegan al código, la API contesta `400` ("El teléfono no es válido") y **no escribe nada**; si no llegan, `404`. Tienen que dar, en orden:
  1. **`400`, desde la propia máquina:** el simulador sigue disponible, pero solo ahí.
  2. **`404`, "como si llegara por un proxy"** (directo a la API, sin Caddy de por medio): es la **capa de código**. Si da `400`, el build **no trae `fix/chat-simulado-cerrado`** y queda solo la capa de Caddy: volvé al 1.0.
  3. **`404`, por el sitio público:** lo que ve internet, con las dos capas. Si da `400`, **el simulador está expuesto**: volvé al 3.2 antes de seguir.
  Y la prueba más fiel, **[PC]**, internet de verdad (tiene que dar `404`):

  ```powershell
  curl.exe -s -o NUL -w "simulador, desde tu PC: %{http_code}`n" -X POST https://yosoytelemedicina.tech/mensajeria/simulado/entrante -H "Content-Type: application/json" -d "{}"
  ```
- En el navegador, ingresá como Configurador: aparece el grupo **CRM** con **Chats** ("Todavía no hay conversaciones") y la sección **WhatsApp** ("Servicio no disponible" hasta el paso 5). Con un profesional, el botón Chats no existe.

### 4.5 Rollback de la API

Vuelve a la app del backup **y al `.env` anterior** (la versión vieja no tiene por qué conocer las variables nuevas):

```bash
sudo test -d /opt/yosoy/app-anterior-$TAG-$FECHA && sudo systemctl stop yosoy-api && sudo find /opt/yosoy/app -mindepth 1 -delete && sudo cp -a /opt/yosoy/app-anterior-$TAG-$FECHA/. /opt/yosoy/app/ && sudo chown -R yosoy:yosoy /opt/yosoy/app && sudo cp -a /opt/yosoy/backups/chat-$FECHA/.env /opt/yosoy/config/.env && sudo systemctl start yosoy-api && sleep 6 && systemctl is-active yosoy-api && curl -s -o /dev/null -w "%{http_code} %{redirect_url}\n" http://localhost:5000/
```

**Esperado:** `active` y `302 ...`. Las migraciones no se deshacen (2.6). El bloque de Caddy puede quedar (no molesta). Los `app-anterior-*` y el `Publish-YoSoy-*` del home no se borran hasta pasados unos días.

---

## Paso 5. Instalar `whatsapp-service` (corriendo y sin vincular)

Corre como **`yosoy`** (el usuario de sistema, sin login, que ya corre la API y la señalización; el servicio solo escucha en `127.0.0.1:3002`, no se abre ningún puerto en UFW), con `Node 22`, **Baileys 6.7.24** (versión exacta, con
`libsignal` 6.0.0 desde el registro de npm y la integridad de cada paquete en el `package-lock.json`) y **sin devDependencies** (no hay ninguna) ni Chromium. Sigue el flujo de [`DEPLOY.md`](DEPLOY.md), "Despliegue de whatsapp-service (Baileys)":
se instala **al lado** del viejo y recién después se corta.

### 5.1 [PC] Empaquetar y subir (sin `node_modules`)

```powershell
cd D:\Claude\YoSoy\YoSoy\whatsapp-service
$fecha = Get-Date -Format yyyyMMdd
tar --exclude=node_modules -czf "$env:TEMP\whatsapp-service-${fecha}.tar.gz" .
scp "$env:TEMP\whatsapp-service-${fecha}.tar.gz" agustin@179.199.155.50:/home/agustin/
```

**Esperado:** un `.tar.gz` de unos 160 KB (en el ensayo: 163.574 bytes) con el código, `test/`, `deploy/` y los dos `package*.json`; sin `node_modules`, sin `.env` y sin ninguna sesión.

### 5.2 [VPS] Instalar al lado y probar en Linux

```bash
sudo test ! -e /opt/yosoy/whatsapp/app-nueva && sudo install -d -o yosoy -g yosoy /opt/yosoy/whatsapp /opt/yosoy/whatsapp/app-nueva /opt/yosoy/whatsapp/cache && sudo tar -xzf ~/whatsapp-service-$FECHA.tar.gz -C /opt/yosoy/whatsapp/app-nueva && sudo chown -R yosoy:yosoy /opt/yosoy/whatsapp/app-nueva && sudo -u yosoy bash -c 'cd /opt/yosoy/whatsapp/app-nueva && HOME=/opt/yosoy/whatsapp/cache npm ci --omit=dev --cache /opt/yosoy/whatsapp/cache/npm' && sudo ls /opt/yosoy/whatsapp/app-nueva/index.js /opt/yosoy/whatsapp/app-nueva/deploy/whatsapp-service.service
```

```bash
sudo -u yosoy bash -c 'cd /opt/yosoy/whatsapp/app-nueva && HOME=/opt/yosoy/whatsapp/cache npm ls --omit=dev --depth=0; npm test 2>&1 | tail -n 12'
```

**Esperado:** `npm ci` sin errores (en la PC, con Windows, dice `added 183 packages`; en Linux el número puede diferir por los paquetes opcionales de cada plataforma). `npm ls` muestra `@whiskeysockets/baileys@6.7.24`,
`express`, `qrcode` y `qrcode-terminal`. `npm test` termina con `tests 454`, `fail 0` y `skipped 6` (las que necesitan el repositorio o `docs/`, que acá no están; las 6 de permisos POSIX **sí corren acá**: en Windows se saltan).
Si falla alguna, **no sigas**, salvo el caso conocido de abajo. Si `app-nueva/` ya existía, el primer comando se frena: algo quedó de antes, mirá qué es.

**Caso conocido (paquete anterior a `fix/test-sesion-posix`, PR #53):** si el `sesion.test.js` del paquete es el viejo, falla **1** prueba, en `sesion.test.js` línea 205 ("preparar crea la carpeta en 700 y lo que escriba la librería después queda en 600"), y el resumen dice `pass 447` y `fail 1`. **No es un defecto del servicio:** esa prueba esperaba la carpeta de la sesión sin `datos/`, que `preparar()` crea a propósito. Con un `main` que ya trae el arreglo da `fail 0`. La carpeta de la sesión y `datos/` en 700 se comprueban otra vez, en el servidor, en el 5.5.

### 5.3 [VPS] Corte y reemplazo

La carpeta de la sesión nueva (**700**, dueño `yosoy`, fuera del código), se para el servicio viejo (si lo hay), se intercambian las carpetas y se instala la unit:

```bash
sudo install -d -m 700 -o yosoy -g yosoy /opt/yosoy/whatsapp/sesion-baileys && { sudo systemctl stop whatsapp-service || true; } && { sudo test ! -d /opt/yosoy/whatsapp/app || sudo mv /opt/yosoy/whatsapp/app /opt/yosoy/whatsapp/app-anterior-$FECHA; } && sudo mv /opt/yosoy/whatsapp/app-nueva /opt/yosoy/whatsapp/app && sudo cp /opt/yosoy/whatsapp/app/deploy/whatsapp-service.service /etc/systemd/system/whatsapp-service.service && sudo sed -i 's/\r$//' /etc/systemd/system/whatsapp-service.service && sudo systemctl daemon-reload && sudo ls /opt/yosoy/whatsapp
```

```bash
sudo systemd-analyze verify /etc/systemd/system/whatsapp-service.service; sudo systemd-analyze security whatsapp-service --no-pager | tail -n 3
```

**Esperado:** el `ls` muestra `app`, `app-anterior-<fecha>` (solo si había servicio viejo), `cache` y `sesion-baileys` (y `sesion` si quedaba la vieja: no se toca). `verify` sin errores (puede avisar de cosas ajenas a esta unit);
`security` es informativo. **No arranques todavía:** primero los topes.

### 5.4 [VPS] Topes iniciales y variables viejas

```bash
sudo sed -i '/^WHATSAPP_TOPE_DIARIO=/d;/^WHATSAPP_TOPE_POR_HORA=/d;/^WHATSAPP_BACKEND=/d;/^WHATSAPP_SESSION_PATH=/d;/^WHATSAPP_WEB_VERSION=/d;/^WHATSAPP_DEBUG=/d;/^PUPPETEER_/d' /opt/yosoy/config/whatsapp.env && printf 'WHATSAPP_TOPE_DIARIO=30\nWHATSAPP_TOPE_POR_HORA=10\n' | sudo tee -a /opt/yosoy/config/whatsapp.env >/dev/null && sudo stat -c '%a %U:%G %n' /opt/yosoy/config/whatsapp.env && sudo grep -c -E '^WHATSAPP_TOPE_(DIARIO=30|POR_HORA=10)$' /opt/yosoy/config/whatsapp.env
```

**Esperado:** `600 yosoy:yosoy ...` y `2`. Se sacan de paso las variables del motor viejo (un `WHATSAPP_BACKEND=webjs` haría que el servicio **no arranque**); si hubiera que volver al servicio viejo, su `whatsapp.env` está en el
respaldo del 3.1. Los topes por persona quedan en los valores por defecto (3 por día para un aviso suelto, 30 si la persona escribió hace menos de 24 horas). **Ojo:** un solo paciente conversador puede gastar
buena parte de los 30 del día; los 10 por hora hacen esperar en la cola (y lo que lleva más de 3 horas esperando se descarta). Se suben de a poco, durante semanas.

### 5.5 [VPS] Arrancar y verificar (todavía sin vincular)

```bash
sudo systemctl enable --now whatsapp-service && sleep 8 && systemctl is-active whatsapp-service && curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:3002/salud && curl -s http://127.0.0.1:3002/salud; echo; sudo ss -ltnp | grep ':3002 '; sudo stat -c '%a %U:%G %n' /opt/yosoy/whatsapp/sesion-baileys /opt/yosoy/whatsapp/sesion-baileys/datos
```

**Esperado:** `active`, `503` (sin vincular, y es lo correcto), y un JSON con `"estado":"requiere_vincular"`, `"motivo":"esperando_escaneo"`, `"qrPendiente":true`, `"conectado":false` (en los primeros segundos puede decir
`"estado":"iniciando"`); `127.0.0.1:3002` (nunca `0.0.0.0` ni `*`); y `700 yosoy:yosoy` para la carpeta y para `datos` (`datos/` la crea el servicio al arrancar, a propósito: aunque no haya nada vinculado, la carpeta de la sesión ya la trae adentro; lo que escriba Baileys al vincular queda en `600`). En el navegador, Configurador → WhatsApp muestra el **QR** ("Esperando escaneo del QR"): **no lo escanees todavía**.

### 5.6 [VPS] El log, sin texto de pacientes ni teléfonos completos

```bash
sudo journalctl -u whatsapp-service --since '10 minutes ago' --no-pager -o cat; sudo journalctl -u whatsapp-service -o cat --since '10 minutes ago' | grep -cE '[0-9]{7,}'
```

**Esperado**, en este orden: `Carpeta de la sesión de WhatsApp lista: ...`, `whatsapp-service escuchando en 127.0.0.1:3002 (Baileys)`, `Sin número vinculado: se abre una ventana de vinculación por QR (1 de 2).` y `WhatsApp: hay un código QR para
vincular el número ...`; sin excepciones; y el conteo final en `0` (ningún número de 7 o más dígitos; la única excepción es la línea de la versión de WhatsApp Web, que sale solo si fijaste `WHATSAPP_BAILEYS_VERSION_WA` o `WHATSAPP_BAILEYS_BUSCAR_VERSION`, y no es de nadie). Si dice `Aviso: estas variables de entorno ya no se usan ...` con nombres, son restos del servicio viejo (el 5.4 ya las saca). El servicio **nunca** escribe el texto de
un mensaje, el nombre del perfil, un teléfono completo (queda `***4455`), el QR, las credenciales ni el secreto (tres capas de protección y pruebas en cada `npm test`; `whatsapp-service/README.md`, "qué se escribe en el log"). La API no escribe el texto ni el teléfono de los chats
(lo vigila una prueba); sí escribe la ruta de cada pedido. Para revisar la API: `sudo journalctl -u yosoy-api --since '30 minutes ago' --no-pager | grep -E '[0-9]{10}'` no tiene que mostrar el número de ningún paciente.

### 5.7 La unit, revisada

`whatsapp-service/deploy/whatsapp-service.service` (se instala con el mismo nombre que la vieja). Revisada línea por línea contra el código y contra las pruebas que la vigilan (`unidad-y-documentacion.test.js`):

| Qué | Cómo está | Para qué |
|---|---|---|
| Quién | `User=yosoy`, `Group=yosoy`; `WorkingDirectory=/opt/yosoy/whatsapp/app`; `ExecStart=/usr/bin/node index.js` | El mismo usuario de sistema que la API; sin shell de login |
| Configuración | `EnvironmentFile=/opt/yosoy/config/whatsapp.env` (sin `-`: si falta, no arranca) y `Environment=WHATSAPP_BAILEYS_SESSION_PATH=/opt/yosoy/whatsapp/sesion-baileys` | El secreto y los topes viven en `whatsapp.env`; la sesión no está en el código ni en el repositorio |
| Reinicios | `Restart=on-failure`, `RestartSec=10`, y `StartLimitBurst=5` en `StartLimitIntervalSec=300` | Si cae 5 veces en 5 minutos, systemd se rinde: un proceso en bucle volvería a golpear la puerta de WhatsApp |
| Apagado | `TimeoutStopSec=20`; el servicio atiende `SIGTERM`, termina en 1 a 3 segundos y sale con 0 | Cierra sin cortar la sesión; los reintentos que esperaban se pierden |
| Memoria | `MemoryMax=512M` | Corta una fuga antes de que moleste a la API o a SQL Server. **Es una estimación**: confirmar con `Memory:` en `systemctl status` |
| Endurecimiento | `NoNewPrivileges`, `ProtectSystem=strict` con `ReadWritePaths=` solo la carpeta de la sesión, `ProtectHome`, `PrivateTmp`, `PrivateDevices`, `ProtectKernelTunables/Modules`, `ProtectControlGroups`, `RestrictSUIDSGID`, `LockPersonality`, `RestrictRealtime`, `SystemCallArchitectures=native`, `RestrictAddressFamilies=AF_INET AF_INET6 AF_UNIX AF_NETLINK`, `CapabilityBoundingSet=` vacío, `UMask=0077` | Solo escribe en la sesión; sin privilegios nuevos ni capacidades |
| Red | Escucha **solo** en `127.0.0.1` (en el código: `escuchar(app, puerto, cb, host = "127.0.0.1")`, el host no es configurable); no hay regla en UFW | Nunca expuesto a internet |

**Es razonable para este servicio.** Lo que **no** tiene, a propósito, y no probé: `MemoryDenyWriteExecute` (Node no arranca con él) ni filtros de llamadas al sistema. Mejoras opcionales para más adelante, sin aplicar: que el servicio no
pueda leer `/opt/yosoy/config` (corre como `yosoy`, el mismo dueño del `.env`: un compromiso del servicio podría leerlo; se resolvería con un usuario propio o con `InaccessiblePaths=` en un drop-in), y `ProtectKernelLogs`, `ProtectClock`, `ProtectHostname`, `RestrictNamespaces`.
**La unit nunca corrió en un systemd real:** si alguna opción de endurecimiento impidiera arrancar, la unit lo dice: se comenta esa línea y se sigue.

### 5.8 Si no arranca

Mirá la primera línea de error: `sudo journalctl -u whatsapp-service --since '5 minutes ago' --no-pager -o cat`.

| Dice | Es |
|---|---|
| `Falta API_BASE_URL` o `Falta Mensajeria__WhatsApp__Secreto` | `whatsapp.env` incompleto (3.3) |
| `Failed to load environment files` | `whatsapp.env` no existe (la unit lo exige) |
| `status=226/NAMESPACE` | No existe la carpeta de `ReadWritePaths`: crear `sesion-baileys` (5.3) |
| `WHATSAPP_BAILEYS_SESSION_PATH ...` (dueño, permisos, "otros archivos") | La carpeta de la sesión no es del usuario `yosoy`, no es 700 o tiene archivos ajenos |
| `No se pudo abrir el puerto 3002` | Algo más escucha en el 3002 (¿el servicio viejo sigue vivo?): `sudo ss -ltnp \| grep :3002` |
| `WHATSAPP_BACKEND="webjs" ya no existe` | Quedó esa variable en `whatsapp.env` (5.4 la saca) |
| `Start request repeated too quickly` | Llegó al límite de reinicios; después de arreglar la causa: `sudo systemctl reset-failed whatsapp-service && sudo systemctl start whatsapp-service` |

### 5.9 Probar el apagado (una vez, ahora que no hay nada vinculado)

```bash
time sudo systemctl stop whatsapp-service; sudo journalctl -u whatsapp-service --since '1 minute ago' --no-pager -o cat | grep -E 'SIGTERM|Apagado'; sudo systemctl start whatsapp-service && sleep 5 && curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:3002/salud
```

**Esperado:** el `stop` tarda de 1 a 3 segundos (no los 20 de `TimeoutStopSec`), el log dice `SIGTERM recibido, apagando...` y `Apagado completo.`, y después de arrancar, `503` otra vez. Ojo: cada arranque sin número abre ventanas de QR (paso 6).

### 5.10 Rollback del servicio

- **Parar el servicio nuevo y dejar todo quieto** (alcanza si no tenías servicio viejo, o si no lo querés de vuelta): `sudo systemctl disable --now whatsapp-service`. La API sigue andando (los envíos fallan visiblemente, ver el paso 8).
- **Volver al servicio viejo** (solo mientras no hayas hecho la limpieza del paso 10 de `DEPLOY.md`): `DEPLOY.md`, "11. Rollback". Resumen con los nombres de este runbook:
  ```bash
  sudo systemctl stop whatsapp-service && sudo mv /opt/yosoy/whatsapp/app /opt/yosoy/whatsapp/app-baileys-$FECHA && sudo mv /opt/yosoy/whatsapp/app-anterior-$FECHA /opt/yosoy/whatsapp/app && sudo cp -a /opt/yosoy/backups/chat-$FECHA/whatsapp-service.service /etc/systemd/system/whatsapp-service.service && sudo cp -a /opt/yosoy/backups/chat-$FECHA/whatsapp.env /opt/yosoy/config/whatsapp.env && sudo systemctl daemon-reload && sudo systemctl start whatsapp-service && curl -s http://127.0.0.1:3002/salud
  ```
  (el viejo contesta `{"ok":true}`). La API no se toca: le habla al servicio por HTTP y no sabe qué motor hay detrás. **Si en el 3.3 generaste un secreto nuevo en lugar de uno viejo**, el `whatsapp.env` restaurado tiene el secreto anterior y la
  API (que ya leyó el nuevo) tiene que reiniciarse para leerlo: `sudo systemctl restart yosoy-api`. La sesión de Baileys no se borra.

### Al terminar los pasos 1 a 5

- Anotá el commit desplegado (`4cff56e` o el que hayas publicado), la fecha, el nombre del `app-anterior-chat-<fecha>` y que se aplicó la migración 17: es lo que pide `DEPLOY.md` ("Al terminar") y lo que va en la tabla "Migraciones aplicadas" de [`SERVIDOR.md`](SERVIDOR.md).
- Borrá del home del VPS lo que ya no hace falta (no tiene secretos, pero ocupa lugar): `rm -rf ~/Publish-YoSoy-$FECHA-$TAG ~/Publish-YoSoy-$FECHA-$TAG.tar.gz ~/whatsapp-service-$FECHA.tar.gz ~/add_chat_personal_entrantes.sql`. Los `app-anterior-chat-*` y los respaldos de `/opt/yosoy/backups/chat-*` se quedan unos días.
- Actualizá [`SERVIDOR.md`](SERVIDOR.md) con el estado real (servicios, carpetas, variables): antes del 19/10/2026 decía que lo que corría era el servicio viejo de WhatsApp (ya se actualizó: "Estado de producción al 19/10/2026").
- Si cerraste la sesión SSH, las variables `FECHA` y `TAG` se pierden: no las necesitás para operar.

---

## Paso 6. El estado "desplegado sin vincular"

Es lo que queda al terminar el paso 5. Todo lo que sigue sale del código de `main` (se cita el archivo y la función) y de las pruebas, que usan un socket simulado y relojes falsos: **nada se probó contra WhatsApp real**.

**¿Qué hace el servicio mientras nadie escanea?** Al arrancar, `iniciar()` (`baileys/servicio.js`) abre una **ventana de vinculación por QR**: se conecta a WhatsApp sin cuenta y recibe un QR que WhatsApp **renueva cada ~20 segundos**. La ventana dura **~2,7 minutos**;
si nadie escanea, WhatsApp la cierra (`_alCerrar`) y el servicio abre **otra** (son `WHATSAPP_BAILEYS_VENTANAS_QR`, **2 por defecto**, de 1 a 5). Cuando se acaban, **se queda quieto** en `requiere_vincular` / `qr_vencido`, no vuelve a conectarse
a WhatsApp y el log dice `La ventana de vinculación por QR terminó sin escanearse y no se abren más solas: hay que pedir otra`. En total: **unos 5 minutos de conexiones desde cada arranque**, y después silencio. El proceso sigue vivo y contesta `/salud` con 503.
Un reinicio del servicio o del VPS vuelve a abrir 2 ventanas.

**¿Cómo consigo un QR fresco?**
1. **Mientras hay un QR a la vista**: botón **"¿El código no funciona? Generar uno nuevo"** en Configurador → WhatsApp (corre `desvincular()`: descarta el QR, abre una ventana nueva y reinicia el cupo; deja una fila en la auditoría; las acciones de este tipo
   tienen un límite de 10 cada 5 minutos).
2. **Con la ventana vencida (`qr_vencido`)**: el Configurador **no ofrece ningún botón** (ver abajo). Hay que reiniciar el servicio: `sudo systemctl restart whatsapp-service`; el QR aparece en la pantalla a los 5 o 10 segundos.
3. Por HTTP desde el VPS, con el secreto (diagnóstico): `POST http://127.0.0.1:3002/vincular` con el encabezado `X-Shared-Secret`; el secreto no se imprime si lo leés dentro del mismo comando:
   `sudo bash -c 'set -a; . /opt/yosoy/config/whatsapp.env; curl -s -X POST http://127.0.0.1:3002/vincular -H "X-Shared-Secret: $Mensajeria__WhatsApp__Secreto"'` (imprime solo el JSON de respuesta).

**¿Qué muestra Configurador → WhatsApp?** (`dashboard.js`, `waHtml`; consulta cada 3 segundos mientras no esté conectado)
- Con una ventana abierta: chip amarillo **"Esperando escaneo del QR"**, el QR, las instrucciones ("WhatsApp → Dispositivos vinculados → Vincular un dispositivo"), el aviso de que quien escanee controla ese WhatsApp, y el botón "Generar uno nuevo".
- Los primeros segundos después de arrancar, y **toda vez que la ventana venció, se está reconectando o el servicio necesita atención**: chip rojo **"Desconectado"** y el texto **"Generando el código QR..."**, sin botones. La pantalla **no distingue** esos casos (la API no le pasa el estado ni el motivo): para saber cuál es, `curl -s http://127.0.0.1:3002/salud`.
- Con el servicio apagado o inalcanzable: chip **"Servicio no disponible"**.

**¿Y Chats si alguien intenta responder?** Sin número vinculado no puede entrar ningún mensaje, así que no habrá conversaciones (solo "Todavía no hay conversaciones"). Si igual hubiera una y alguien contesta: el servicio rechaza el envío con `503 no_conectado`
(`cola.js`), la API guarda el mensaje con estado *Error* y **en el hilo aparece "· no entregado" con un botón "Reintentar"**, sin el motivo (el código del rechazo queda en el servidor, no se muestra; llega con el hito 2 del plan). No sale nada, no se pierde nada, y la API
deja **una línea de advertencia por intento**: `whatsapp-service no aceptó el envío: WhatsApp no está disponible en este momento (requiere_vincular).`

**¿La API con `CanalActivo=Baileys` y el servicio sin vincular llena el log de errores?** No: falla de forma ordenada. Al arrancar la API no contacta al servicio. Nada en segundo plano le habla (el recordatorio solo manda mail y campanita; los avisos por WhatsApp están apagados). Solo hay líneas
cuando alguien manda una respuesta (1 advertencia por intento). **La única fuente de ruido** es otra: si el servicio está **apagado o caído** (no sin vincular) y alguien tiene abierta la pantalla WhatsApp, la API registra un **error con su pila cada 3 segundos**
(`No se pudo contactar a whatsapp-service.`). Cerrá esa pantalla cuando pares el servicio.

**¿Riesgo de que muchos intentos de QR sin escanear traigan problemas con WhatsApp?** No lo puedo afirmar con datos de WhatsApp (no se probó contra WhatsApp real y WhatsApp no publica sus umbrales). Por diseño es bajo y está acotado: sin número no hay una cuenta que
bloquear (una ventana de QR es como abrir WhatsApp Web sin entrar); el servicio no pide QR sin parar (2 ventanas por arranque, un tope duro de 5); un corte que no sea la ventana vencida se reintenta con espera creciente de 2 s hasta 5 minutos; y si el proceso cae
en bucle, systemd se rinde a los 5 reinicios en 5 minutos. Lo que no hay que hacer es reiniciar el servicio o apretar "Generar uno nuevo" en bucle durante horas.

**Lo que queda pendiente de esa pantalla** (decidido el 2026-10-19: **no se hace ahora**; el detalle está en "Mejoras pendientes", al final): un botón para pedir un código QR nuevo cuando la ventana venció, y que la pantalla diga "El código venció" o "Necesita atención"
en lugar de "Generando el código QR...". Hasta entonces, el reinicio del servicio por SSH (arriba, "¿Cómo consigo un QR fresco?"). El otro cambio que se había propuesto, cerrar el simulador en el código, **ya está hecho** (ver "Antes de empezar", 1).

---

## Paso 7. (Para después) Vincular el número y probar con tu teléfono

### 7.1 Escanear el QR desde el sitio real

Con el teléfono del número dedicado a mano, con conexión y con WhatsApp abierto en **Ajustes → Dispositivos vinculados → Vincular un dispositivo**. Ese número no tiene que estar vinculado a ninguna otra instancia (tu PC local, el servicio viejo): cerralas desde **Dispositivos vinculados**.

1. **[VPS]** Para tener las 2 ventanas frescas justo antes de escanear: `sudo systemctl restart whatsapp-service && sleep 8 && curl -s http://127.0.0.1:3002/salud` (tiene que decir `"estado":"requiere_vincular"` y `"qrPendiente":true`).
2. **[Navegador]** `https://yosoytelemedicina.tech/pantallas/index.html` → ingresá como Configurador → **WhatsApp**: aparece el QR. Escanealo con el teléfono. Mostrale el QR solo a quien lo va a escanear.
3. **Esperado:** en unos segundos la pantalla pasa a **"Conectado (+54...)"**. Al terminar de vincular, WhatsApp pide reiniciar la conexión y el servicio la reabre solo (es el código 515: es normal). Queda una fila en la auditoría ("Vinculación de WhatsApp").

### 7.2 [VPS] Cuándo mirar `/salud`

```bash
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:3002/salud; curl -s http://127.0.0.1:3002/salud; echo
sudo systemctl restart whatsapp-service && sleep 10 && curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:3002/salud
```

**Esperado:** `200` y `"estado":"conectado"` (con `"conectado":true` y `entrantes` en cero). Después del reinicio, otra vez `200`: **la sesión se guardó y no pide QR**. Si dice `qr_vencido` o sigue en `requiere_vincular`, volvé al 7.1 con un reinicio. Si dice
`requiere_atencion`, mirá el paso 8. Revisá también `sudo journalctl -u whatsapp-service --since '10 minutes ago' --no-pager -o cat`: `WhatsApp conectado (número ***1234).`

### 7.3 Prueba de humo con tu teléfono personal

Usá **tu** teléfono, no el de un paciente. El teléfono de la clínica tiene que estar conectado.

| # | Hacé | Esperado |
|---|---|---|
| 1 | Desde tu teléfono, escribile al número de la clínica: `prueba de humo 1` | En CRM → Chats (se actualiza solo en unos 3 a 12 segundos): una conversación titulada con tu nombre de perfil de WhatsApp (la cabecera del hilo dice `Contacto: ...`) o, si no lo trae, con tu número; chip **"Para atender"**, 1 sin leer; y un aviso **"Escribió una persona por WhatsApp"** en la campanita |
| 2 | Esperá un minuto mirando tu teléfono | **No llega ninguna respuesta automática** (ni menú ni "no te entendí") |
| 3 | En Chats, respondé `respuesta de prueba` | En el hilo: "· enviando" y enseguida sin marca de error. Llega a tu teléfono en pocos segundos (el servicio simula "escribiendo..." hasta 4 s antes de mandar; la pausa de 4 a 9 s es entre mensajes seguidos) |
| 4 | Mandá una foto desde tu teléfono | En Chats: `[Imagen]` con el aviso "El archivo no se guarda en YoSoy: mirá el teléfono del número" |
| 5 | Mandá `BAJA`; después respondé desde Chats | El mensaje `BAJA` se ve en Chats (no se esconde); tu respuesta queda "· no entregado" (el servicio ya no te escribe). Mandá `ALTA` para revertirlo y probá de nuevo |
| 6 | **[VPS]** `curl -s http://127.0.0.1:3002/salud` | `entrantes.recibidos` y `entrantes.reenviados` subieron; `entrantes.perdidos` y `fallidos` en 0; `cola.enviadosHoy` cuenta tus respuestas |
| 7 | **[VPS]** `sudo journalctl -u whatsapp-service -o cat --since '15 minutes ago' \| grep -cE '[0-9]{7,}'` | `0`: sin teléfonos completos. Y ningún texto de tus mensajes |
| 8 | **[VPS]** `sudo journalctl -u yosoy-api --since '15 minutes ago' --no-pager \| grep -iE 'warn\|fail\|exception\|migración 17'` | Nada raro. Ninguna mención de `migración 17` |
| 9 | **[VPS]** `sqlcmd -S localhost -d yosoy -U <usuario-administrador> -C -I -Q "SELECT TOP 3 IdMensaje, Direccion, Origen, Tipo, EstadoEnvio, CASE WHEN IdExterno IS NULL THEN 0 ELSE 1 END AS con_id FROM MensajeChat ORDER BY IdMensaje DESC"` | Los entrantes tienen `Tipo` (`texto`, `imagen`) y `con_id = 1`: la API está usando las columnas de la migración 17 (no imprime texto) |

El cuadro de respuesta de Chats dice **"Enviar por: WhatsApp"** al abrir la conversación (desde el PR #50, ya en `main`). Si dice "Chat simulado", `CanalActivo` está en `Simulado`: no sale nada de la máquina (revisá el 3.4). Un build anterior al PR #50 siempre dice "Chat simulado", y no significa nada.

Cerrá la conversación de prueba desde Chats ("Cerrar conversación") cuando termines; sus mensajes quedan en la base como cualquier otro (no se borran a mano).

---

## Paso 8. Operación

| Qué | Cómo |
|---|---|
| **Ver el estado** | `systemctl status whatsapp-service --no-pager` (mirá `Memory:`) y `curl -s http://127.0.0.1:3002/salud`. En la pantalla: Configurador → WhatsApp. Para la API: `systemctl status yosoy-api --no-pager` |
| **Ver el log** | `sudo journalctl -u whatsapp-service -n 100 --no-pager -o cat` (sin textos ni teléfonos completos por diseño). La API: `sudo journalctl -u yosoy-api --since '1 hour ago' --no-pager \| grep -iE 'warn\|fail\|exception'` |
| **Reiniciar el servicio** | `sudo systemctl restart whatsapp-service`. Corta WhatsApp unos segundos; los mensajes entrantes que esperaban un reintento hacia la API **se pierden** (`entrantes.perdidos`); si no hay número vinculado, abre 2 ventanas de QR |
| **Reiniciar la API** | `sudo systemctl restart yosoy-api` (la regla `sudoers` lo permite sin contraseña) |
| **Desvincular / cambiar de número** | Configurador → WhatsApp → **"Desvincular y cambiar número"** (solo aparece conectado): cierra la sesión, borra las credenciales y deja un QR nuevo. Desde el teléfono: Dispositivos vinculados → cerrar la sesión (el servicio queda en `requiere_vincular` / `sesion_cerrada`) |
| **WhatsApp desconectó la sesión** | `/salud` dice `requiere_vincular` con `sesion_cerrada` (se desvinculó desde el teléfono, el teléfono estuvo ~14 días sin conectarse, o se reinstaló WhatsApp con ese número). El servicio **no reintenta** y borra las credenciales muertas: volver a escanear (7.1). Cuidá el teléfono de la clínica: conectado, con batería y sin reinstalar WhatsApp |
| **WhatsApp restringió o bloqueó el número** | `/salud` dice `requiere_atencion` con `cuenta_restringida` (código 403). El servicio **no reintenta ni borra nada**. Mirá el teléfono (WhatsApp avisa y a veces permite pedir una revisión), **no** sigas mandando, pará el servicio si querés (`sudo systemctl stop whatsapp-service`) y decidí: esperar, o cambiar de número ("Desvincular y cambiar número" y vincular el nuevo). Otros motivos de `requiere_atencion`: `sesion_reemplazada` (440: otra copia del servicio o el comando de vinculación corriendo), `version_rechazada` (405: actualizar Baileys o fijar `WHATSAPP_BAILEYS_VERSION_WA`), `sesion_invalida` (500 repetido). Salir: resolver lo que dice el log y `sudo systemctl restart whatsapp-service` |
| **Apagar todo rápido** | **Apagar de verdad es parar el servicio: `sudo systemctl stop whatsapp-service`.** No sale ni entra nada, y lo que el equipo conteste desde Chats queda marcado "· no entregado" (visible). Lo que escriban los pacientes queda en el teléfono de la clínica. Para que no vuelva a arrancar solo con un reinicio del VPS: `sudo systemctl disable --now whatsapp-service`. **`Mensajeria__CanalActivo=Simulado` NO es un apagado:** la API deja de hablarle al servicio, pero guarda las respuestas del equipo como **"Enviado"** aunque **no salen**: el equipo cree que se mandaron y los pacientes no reciben nada. No lo uses como interruptor. Si por algún motivo lo usás, avisale al equipo que no conteste (con `Simulado`, el cuadro de respuesta de Chats pasa a decir "Enviar por: Chat simulado": una pista, que no reemplaza el aviso) y volvelo a `Baileys` apenas puedas |
| **Subir los topes** | Editar `WHATSAPP_TOPE_DIARIO` y `WHATSAPP_TOPE_POR_HORA` en `whatsapp.env` (`sed` como en 5.4) y `sudo systemctl restart whatsapp-service`; de a poco, durante semanas |
| **Límites de la pantalla WhatsApp** | `GET /mensajeria/whatsapp/estado` admite 60 pedidos por minuto por IP (una pantalla abierta hace 20): no dejes más de 2 pestañas con esa pantalla desde la misma red |

---

## Paso 9. Riesgos conocidos (checklist)

Marcá cada uno como "visto" antes de dar el servicio por entregado.

| | Riesgo | Detalle y qué hacer |
|---|---|---|
| ☐ | **Se pierde lo que espera un reintento** | Si la API falla, `whatsapp-service` reintenta el reenvío 3 veces (a los 5 s, 30 s y 2 min) **solo en memoria**. Si el servicio cae o se reinicia en ese rato, esos mensajes se pierden (el log y `entrantes.perdidos` dicen cuántos, nunca cuáles). Quedan en el teléfono de la clínica. Hasta 200 esperando a la vez; el resto se pierde |
| ☐ | **Límite de 4000 caracteres** | Un mensaje de más de 4000 caracteres se **recorta** al guardarlo (columna `nvarchar(4000)`); lo que escribe el equipo tiene el mismo tope en la pantalla, y el servicio rechaza más de 4096 |
| ☐ | **Pendiente de validación legal** | (1) **Retención**: no hay tarea que borre conversaciones (la purga y los plazos, 90 días para quienes no son pacientes y a definir con paciente, están decididos pero **no implementados**): hoy todo se conserva, incluidos teléfonos y nombres de perfil (Ley 25.326). (2) El texto de consentimiento `v0-provisorio` (dormido) no está validado y promete un recordatorio por WhatsApp que ya no existe: no usarlo. (3) Las **líneas de emergencia** dicen `[COMPLETAR Y VALIDAR]`: el mensaje de emergencia está desactivado y el paciente en riesgo **no recibe nada**; el equipo se entera por la campanita y el estado *Urgente* |
| ☐ | **Hay que mirar Chats** | Los avisos son la campanita y el contador: no hay sonido ni notificación fuera de la pestaña. Si nadie mira, nadie responde. La conversación de una frase de riesgo queda *Urgente*, no se descarta sola |
| ☐ | **Las respuestas no se pueden mandar a cualquiera** | El servicio solo escribe a quien **le escribió primero** (vale 365 días) y nunca a quien mandó `BAJA`. Un envío rechazado figura como "· no entregado" **sin motivo** (el motivo está en el log de la API: `whatsapp-service no aceptó el envío: ...`) |
| ☐ | **"Enviado" no es "salió"** | "Enviado" significa que el servicio lo aceptó en su cola; después puede esperar (topes, pausas de 4 a 9 s) o descartarse (más de 3 horas esperando). Los errores del envío real solo van al log del servicio |
| ☐ | **No hay aviso de "WhatsApp desconectado" en Chats** | Si la sesión se cae, Chats sigue pareciendo normal; el equipo se entera por un "no entregado" o porque nadie escribe. Mirá Configurador → WhatsApp o `/salud` todos los días (no hay monitoreo externo) |
| ☐ | **Bloqueo del número** | Baileys no es la API oficial: WhatsApp puede restringir o bloquear el número (riesgo asumido; por eso es un número dedicado). Topes bajos, pausas al azar y "escribiendo..." mitigan, **no eliminan**. No usar el número para otra cosa ni desde otro teléfono en simultáneo; no reinstalar WhatsApp en él |
| ☐ | **Responder desde el teléfono del número** | Lo que se conteste desde el teléfono no queda en YoSoy ni pasa por los topes (decisión D8: responder siempre desde Chats) |
| ☐ | **Fotos y audios no se guardan** | Hay que mirar el teléfono de la clínica para verlos |
| ☐ | **Sin vínculo con el paciente** | Las conversaciones salen "Paciente sin identificar" o "Contacto: <nombre del perfil>" (el vínculo por teléfono es del hito 2) |
| ☐ | **El simulador anónimo** | Cerrado **en el código** (`fix/chat-simulado-cerrado`: solo responde a la propia máquina, aunque `SoloLocal=false`) y, como segunda capa, por el bloque de Caddy (3.2). Si publicás un build sin el arreglo, queda solo Caddy; si alguien edita el `Caddyfile` y lo saca, queda solo el código. Comprobarlo con las sondas del 4.4 después de publicar y después de cualquier cambio de Caddy |
| ☐ | **`CanalActivo=Simulado` no apaga nada** | Guarda las respuestas del equipo como "Enviado" aunque no salgan. Para apagar: `sudo systemctl stop whatsapp-service` (paso 8) |
| ☐ | **Los profesionales no ven chats** | Es la decisión D2; ya no tienen el botón Chats y el servidor les contesta 403 |
| ☐ | **La sesión de Baileys es una credencial** | `sesion-baileys/` (700) da acceso al WhatsApp de la clínica: no copiarla ni pegarla en un chat o ticket. Si se pierde, se vuelve a escanear un QR (no se pierde el número). Respaldarla o no es una decisión pendiente (`PROGRESO_BAILEYS.md`); si se respalda, cifrada |
| ☐ | **La unit nunca corrió en un systemd real** | `MemoryMax=512M` es una estimación: mirá `Memory:` y busca `OOMKilled` en `systemctl status` en la primera semana |
| ☐ | **El servicio corre con el mismo usuario que la API** | Un compromiso del servicio podría leer `/opt/yosoy/config/.env` (ver 5.7) |
| ☐ | **Backups de la base** | Contienen todos los chats. Solo en tu PC, fuera de cualquier repositorio; el `.bak` del VPS 2025 no se restaura en tu SQL Server 2019 |

---

## Hallazgos de esta revisión (y qué se decidió)

1. **El simulador anónimo quedaba abierto con `SoloLocal=false`** (comprobado, ver "Antes de empezar"). **Resuelto en el código** (rama `fix/chat-simulado-cerrado`): las dos rutas solo responden a la propia máquina, siempre. El bloque de Caddy queda como segunda capa. El build que se publique tiene que traerlo (paso 1.0).
2. **La pantalla WhatsApp no tiene ningún botón cuando el QR venció** (ni cuando el servicio necesita atención) y dice "Generando el código QR..." para siempre. **Mejora pendiente** (decidido: no se hace ahora; ver "Mejoras pendientes"). Mientras tanto: `sudo systemctl restart whatsapp-service`.
3. **El selector "Enviar por: Chat simulado"** de Chats aparecía aunque el canal activo fuera `Baileys`. **Arreglado** (PR #50, ya en `main`): `/capacidades` informa `canalActivo`, el selector dice "WhatsApp" y la insignia de cada conversación de la lista dice el canal de esa conversación.
4. **`CanalActivo=Simulado` hace que las respuestas figuren "Enviado"** aunque no salgan. **Documentado** con la misma fuerza en "Antes de empezar" (5), en el paso 8 y en el 9: apagar de verdad es `sudo systemctl stop whatsapp-service`.
5. **El ejemplo de backup de `DEPLOY.md` falla en SQL Server Express:** usa `WITH COMPRESSION`, que Express no admite (`Msg 1844`, comprobado en la Express 2019). En este runbook va sin compresión y se corrigió también el ejemplo de `DEPLOY.md`.
6. **El servicio nuevo de WhatsApp nunca habló con WhatsApp real:** este despliegue y el paso 7 son la primera vez. Hacelos con tiempo y con el teléfono del número a mano.

---

## Mejoras pendientes (decididas: no se hacen ahora)

1. **Un botón "Pedir un código QR nuevo" en Configurador → WhatsApp cuando el QR venció.** Hoy el único botón ("¿El código no funciona? Generar uno nuevo") está dentro de la rama "hay QR"; con la ventana vencida (`qr_vencido`) la pantalla dice "Desconectado / Generando el código QR..." **sin ningún botón**, y para un QR nuevo hay que reiniciar el servicio por SSH (`sudo systemctl restart whatsapp-service`). Propuesta mínima: en `waHtml` (`PANTALLAS/Pantalla_Configurador/js/dashboard.js`), cuando el servicio responde, no está conectado y no hay QR, mostrar un botón que llame a `waGenerarQrNuevo()` (unas 4 líneas y su prueba). Decisión de Agustín (2026-10-19): no se hace ahora.
2. **(Opcional) Que la pantalla diga por qué está desconectada.** Que `GET /mensajeria/whatsapp/estado` le pase el `estado` y el `motivo` que el servicio ya informa, para mostrar "El código venció" o "Necesita atención" en lugar de "Generando el código QR...".
3. **(Opcional, ver 5.7) Endurecer más el servicio:** que no pueda leer `/opt/yosoy/config` (usuario propio o `InaccessiblePaths=` en un drop-in), y `ProtectKernelLogs`, `ProtectClock`, `ProtectHostname`, `RestrictNamespaces`.

---

## Qué se verificó y qué no

### Verificado (con qué y cómo repetirlo)

| Qué | Resultado | Cómo |
|---|---|---|
| `main` contiene las ramas del chat, la del arreglo de la prueba intermitente, la del documento y la de la etiqueta del canal | `a5161b8`, `803f441`, `ef3d055`, `1e00365` y `2562304` son ancestros de `4cff56e`. Desde `d18702e` (donde se escribió la primera versión de este runbook) solo cambió el PR #50 (la API, la bandeja y dos documentos): `whatsapp-service/`, `migrations/`, `docs/produccion/` y `appsettings.json` no cambiaron | `git merge-base --is-ancestor <commit> origin/main` y `git diff --stat d18702e origin/main` |
| La migración 17 está en `migrations/` y en el esquema consolidado | `migrations/add_chat_personal_entrantes.sql`; `docs/produccion/01-produccion.sql` trae `IdExterno`, `Tipo`, `NombreContacto`, `CK_MensajeChat_Tipo`, `DF_MensajeChat_Tipo` y `UQ_MensajeChat_IdExterno` | Lectura de los dos archivos |
| Suite de la API contra `yosoy_test` (45 tablas, con la 17) | **1807 de 1807** en `main` (`4cff56e`); **1817 de 1817** con `fix/chat-simulado-cerrado` (+10 pruebas, `SimuladorSoloLocalTests`), 0 omitidas | `cd D:\Claude\YoSoy\YoSoy; .\Scripts\correr-tests.ps1` |
| La misma suite contra una base con la **forma de producción** (44 tablas, sin la 16, con la 17 aplicada con el comando del paso 2.4) | **1772 de 1772** con el arreglo (las 45 pruebas de la tabla de la 16 quedan afuera) | `.\Scripts\crear-base-de-pruebas.ps1 -Base <descartable> -SinConsentimientoWhatsApp -SinMigracion17`, aplicar la migración, y `.\Scripts\correr-tests.ps1 -Base <descartable> -Filtro 'Tabla!=ConsentimientoWhatsApp'` |
| Suite de `whatsapp-service` | 454 pruebas: **448 pasan, 6 saltadas (permisos POSIX en Windows), 0 fallas** (vuelta a correr contra el árbol final) | `cd whatsapp-service; npm test` |
| Ensayo de la migración sobre esa base descartable | Antes `0 0 0 0 44`; después `3 1 1 1 44`; una segunda aplicación no cambia nada | Los comandos de los pasos 2.3 a 2.5 (con `sqlcmd -E` en vez de `-U`) |
| El backup del paso 1.1 | `BACKUP ... WITH CHECKSUM, INIT; RESTORE VERIFYONLY ... WITH CHECKSUM` anda (`The backup set on file 1 is valid.`); con `COMPRESSION` falla con `Msg 1844` | SQL Server 2019 Express de tu PC, sobre la base descartable (los archivos de prueba se borraron) |
| Empaquetado del servicio | `.tar.gz` de 163.574 bytes, sin `node_modules`, sin `.env`, sin sesión; la unit y el drop-in sin CR. `npm ci --omit=dev` desde el lock: sin errores, Baileys 6.7.24, 207 paquetes en el lock, ninguno por git, todos con integridad, ninguno de desarrollo. `npm test` en el paquete extraído, sin el repositorio: 442 pasan, 12 saltadas, 0 fallas | Los comandos del paso 5.1 y de 5.2 en una carpeta temporal de la PC (Windows) |
| `dotnet publish` del flujo habitual | Sin errores con el código final (`main` más el arreglo); 434 archivos, ~133 MB; trae `YoSoy`, `appsettings.json` y `PANTALLAS\Pantalla_ChatSimulado\js\bandeja.js`; ningún `.env` | El comando del paso 4.1 con una carpeta de salida temporal |
| Chats por el sitio y el simulador, con Caddy real | Las cinco filas de la tabla de "Antes de empezar" | API de `main` y del arreglo (Production, Personal, Baileys, sin servicio de WhatsApp) detrás de Caddy 2.7.0, pidiendo desde la dirección de red de la PC, contra bases de prueba |
| El bloque de Caddy y su inserción con `sed` | La inserción es idempotente y se corta si falta el `import`; el `Caddyfile` resultante valida en Caddy 2.7.0, 2.8.4 y 2.11.7. (Con la 2.6.4 no valida, pero tampoco valida el original por `video.caddy`) | `caddy validate --adapter caddyfile` y `sed` GNU 4.9 |
| La lógica de los comandos de una línea | 31 comprobaciones en bash contra un árbol de archivos de prueba (con `sudo`, `systemctl`, `chown`, `install`, `curl` y `npm` simulados), **con los comandos extraídos de este mismo documento** (vuelta a correr contra su versión final): edición idempotente de los `.env`, el secreto no se imprime, el drop-in, el desempaquetado, el corte de la API (y que se frena antes de parar si el backup existe o falta el publish), el rollback de la API y del servicio, el respaldo con archivos faltantes, la instalación al lado y el corte del servicio con y sin servicio viejo, y el bloque de Caddy (idempotente, y se corta sin recargar si falta el `import`) | Un script descartable (no versionado) |
| Comportamiento sin vincular | Lo descripto en el paso 6 sale de leer `baileys/servicio.js`, `cola.js`, `servidor.js`, `config.js`, `CanalBaileys.cs`, `WhatsAppAdminService.cs` y `dashboard.js`, y de las pruebas del servicio (socket simulado, reloj falso) | Lectura del código |
| El cierre del simulador: las pruebas | 10 pruebas nuevas (`SimuladorSoloLocalTests`, sin base de datos). **Con el código de `main` fallan 5** (las del cierre remoto: POST y GET con `X-Forwarded-For` y con `CF-Connecting-IP`, y el recorrido por la tabla de rutas) y **pasan las 10 con el arreglo**; las otras 5 vigilan que el simulador local y lo que ya cerraba el grupo no cambien | Volver `Endpoints/MensajeriaEndpoints.cs` al de `main` y correr `.\Scripts\correr-tests.ps1 -Filtro FullyQualifiedName~SimuladorSoloLocalTests` |
| El simulador, en Chrome real, en los dos modos | 47 comprobaciones en 5 escenarios, con la API real y Caddy 2.7.0 real, contra `yosoy_test` (filas marcadas que se borran). **Local** (directo a la API, `SoloLocal` en `true` y en `false`): el simulador de la pantalla anda y la conversación aparece en la bandeja. **Producción** (Chrome → Caddy → API, cliente con la IP de red de la PC, `SoloLocal=false`, modo Personal, canal Baileys), con el arreglo y con y sin el bloque de Caddy: el Configurador ve Chats y lo que entra por el webhook, el simulador de la pantalla da "No se encontró lo que buscabas.", un visitante anónimo recibe 404 en las dos rutas y **no se crea ninguna conversación**. Con el código de `main` y el mismo montaje, el visitante anónimo **crea la conversación** (200) y la bandeja la muestra | Un script de Playwright con el Chrome de la PC (no versionado) |
| Las sondas del 4.4 | Extraídas tal cual de este documento (solo cambian las direcciones) y corridas contra la API y Caddy reales: con el arreglo dan `400 / 404 / 404` (propia máquina / proxy / sitio público) y `404` la de la PC (PowerShell), con y sin el bloque de Caddy; con el código de `main`, `400 / 400 / 400` y `400` desde la PC sin el bloque, y `400 / 400 / 404` y `404` con el bloque | Un script descartable (no versionado) |
| El texto contra el código | 160 comprobaciones de lectura (archivos, variables de entorno, rutas, contenido de la unit y del drop-in, textos de pantalla y de log, valores por defecto, secciones de otros documentos) contra el árbol de `main` más el arreglo: no falta ninguna | Un script descartable (no versionado) |

### No se pudo verificar (sin acceso al VPS ni a WhatsApp)

- **El estado real del VPS**: qué versión de la API corre hoy, si el servicio viejo existe y está vinculado, el `Caddyfile` y la versión de Caddy, el drop-in `whatsapp.conf`, los permisos de `/opt/yosoy`, la versión de Node, el espacio y la memoria libres, el valor de `Mensajeria__CanalActivo` y de `Email__From` en el `.env`. Para eso es el paso 1.2.
- **Los comandos con `sudo`, `systemctl`, `sqlcmd` contra SQL Server 2025 y el login administrador** (el nombre del login es un dato que no tengo). Se probó su lógica, no su ejecución.
- **`npm ci` en Linux** (en Windows instala 183 paquetes; los opcionales por plataforma pueden diferir) y **las 6 pruebas de permisos POSIX** del servicio (en Windows se saltan). Corrieron por primera vez en el paso 5.2 del 19/10/2026: una estaba desactualizada y se corrigió (ver "Registro de la ejecución del 19/10/2026").
- **La unit en un systemd real** (`systemd-analyze verify` y `security`), el apagado en 1 a 3 segundos, el uso real de memoria (`MemoryMax=512M`) y que `ProtectSystem=strict` deje escribir la carpeta de la sesión.
- **Cualquier conexión a WhatsApp**: los tiempos reales del QR (~20 s por código, ~2,7 min por ventana), los códigos de desconexión que manda WhatsApp, los umbrales de bloqueo, el envío y la recepción de verdad. Todo está probado con un socket simulado y la red bloqueada.
- **El simulador cerrado contra el Caddy y la API reales del VPS.** Se probó con Caddy 2.7.0 y la API en una PC. Lo que hace que el código lo cierre es que el pedido llegue con `X-Forwarded-For` (Caddy lo agrega por defecto, ver `NOTAS_MERGE.md`: si se cambia el proxy o su configuración hay que volver a verificarlo); las sondas del 4.4 son la comprobación real en el servidor.
- **La salida a internet del VPS hacia WhatsApp** (el paso 1.2 lo comprueba con una conexión TCP) y la hora sincronizada.
- **El mensaje de emergencia con líneas oficiales** (desactivado) y los textos legales.

---

## Registro de la ejecución del 19/10/2026

Agustín ejecutó este runbook a mano en el VPS el 19/10/2026, con la API del commit `7889226`. Lo que quedó en el servidor está en [`SERVIDOR.md`](SERVIDOR.md), "Estado de producción al 19/10/2026".

- **Pasos hechos: 1 a 5.** Backup de la base (solo en su PC, fuera del repositorio), respaldo de la configuración en `/opt/yosoy/backups/chat-20261019/`, migración 17, bloque de Caddy del simulador, secreto y variables, API publicada (rollback en `app-anterior-chat-20261019`) y `whatsapp-service` instalado (el anterior quedó en `/opt/yosoy/whatsapp/app-anterior-20261019`).
- **El servicio quedó sin vincular, a propósito:** activo, con `/salud` en 503. El paso 7 (escanear el QR con el teléfono dedicado de la clínica y la prueba de humo) sigue pendiente, igual que la limpieza de lo viejo (`DEPLOY.md`, paso 10): la sesión de whatsapp-web.js (`/opt/yosoy/whatsapp/sesion`) no se tocó.
- **Cuatro ajustes que salieron de la ejecución**, ya incorporados arriba: (1) el `sed` del 3.4 también saca `Mensajeria__ChatSimuladoHabilitado`; (2) los `ls` sobre `/opt/yosoy` llevan `sudo` y los `*` van dentro de `sudo sh -c '...'` (5.2, 5.3 y el chequeo 1.2); (3) la prueba de permisos de la sesión estaba desactualizada y `datos/` también queda en 700 (5.2 y 5.5); (4) los comandos se pegan de a uno, después de `sudo -v`.
- **Comprobado el 19 y el 20/10:** el login, el pedido y la reprogramación de turnos y el registro de un paciente con mail (sin la casilla ni la tarjeta de WhatsApp). Todo bien.
