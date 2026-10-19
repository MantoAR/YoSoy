 fix/sacar-secretos-appsettings
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

# Despliegue de la API y migraciones (flujo manual)

Cómo se publica una versión nueva de YoSoy en el VPS y cómo se le aplican los cambios de base. Es el flujo que se usa
hoy: **a mano, en dos máquinas** (la PC con PowerShell y el VPS con bash), sin pipeline automático. Cómo está armado
el servidor: [`SERVIDOR.md`](SERVIDOR.md). Las videollamadas (signaling, coturn, Caddy) tienen su propio runbook:
[`DESPLIEGUE_VIDEO.md`](DESPLIEGUE_VIDEO.md).

- **Sin secretos en este archivo.** Ningún comando de acá imprime una contraseña o una clave; si alguno lo hiciera,
  algo está mal: pará.
- **En el VPS** se trabaja como `agustin` (con `sudo`: no podés entrar a las carpetas de `yosoy`).
- **Cada paso tiene su verificación y el despliegue tiene rollback** (al final).

## Antes de empezar

1. **`main` actualizado y limpio.** En la PC: `git switch main`, `git pull`, `git status` limpio, y `git log -1` es el
   commit que vas a desplegar (anotalo).
2. **Pruebas.** `dotnet test YoSoy.Tests` con `ConnectionStrings__YoSoy` apuntando a una base **descartable** (nunca la
   real: las pruebas de integración escriben y borran filas propias; se arma una vacía con
   `docs/produccion/01-produccion.sql` cambiándole el nombre). Si las de integración están marcadas con la categoría
   `Integracion`, `--filter "Categoria!=Integracion"` corre solo las que no necesitan base.
3. **¿Cambia la base?** Mirá si desde el último despliegue hay archivos nuevos en `migrations/` o cambios en
   `docs/produccion/`: `git diff <commit-anterior>..HEAD --stat -- migrations docs/produccion`. Si los hay, las
   migraciones van **antes** que la API (sección "Migraciones" abajo).
4. **¿Hay variables nuevas?** Se cargan en `/opt/yosoy/config/*.env` **antes** de arrancar la versión nueva (sección
   "Cambios de configuración"). Y mirá que `Mensajeria__CanalActivo` no sea `WhatsAppWebJs`: ese valor ya no existe y la API no arranca con él
   (sección "WhatsApp en la API").
5. **Horario.** La API se reinicia: unos segundos sin servicio. Mejor en horas de poco uso.

## 1. Publicar en la PC

El publish es **self-contained** (`--self-contained true`): lleva su propio runtime de .NET y el servidor no necesita
tenerlo. La carpeta de salida va **fuera del repositorio**. `publish-vps/` es una carpeta local vieja: no se usa ni se
versiona.

```powershell
cd D:\Claude\YoSoy\YoSoy
$fecha = Get-Date -Format yyyyMMdd
dotnet publish YoSoy.csproj -c Release -r linux-x64 --self-contained true -o "D:\Claude\Publish-YoSoy-${fecha}"
tar -czf "$env:TEMP\Publish-YoSoy-${fecha}.tar.gz" -C "D:\Claude\Publish-YoSoy-${fecha}" .
scp "$env:TEMP\Publish-YoSoy-${fecha}.tar.gz" agustin@179.199.155.50:/home/agustin/
$fecha                    # anotá el valor: es la FECHA que se usa en el VPS
```

## 2. En el VPS: desempaquetar y comprobar

`FECHA` es **la de la PC** (no uses el `date` del servidor: puede dar otro día). Se desempaqueta y se comprueba que está
completo **antes** de tocar nada:

```bash
FECHA=20261015                                    # <- la de la PC
mkdir -p ~/Publish-YoSoy-$FECHA
tar -xzf ~/Publish-YoSoy-$FECHA.tar.gz -C ~/Publish-YoSoy-$FECHA
ls ~/Publish-YoSoy-$FECHA/YoSoy                   # el binario tiene que estar; si da error, FECHA está mal: NO sigas
```

## 3. Backup, corte y reemplazo

```bash
sudo cp -a /opt/yosoy/app /opt/yosoy/app-anterior-$FECHA
sudo diff -rq /opt/yosoy/app /opt/yosoy/app-anterior-$FECHA && echo "backup idéntico"     # si imprime otra cosa, NO sigas
sudo systemctl stop yosoy-api
sudo find /opt/yosoy/app -mindepth 1 -delete                      # vacía la carpeta; la carpeta queda
sudo cp -a ~/Publish-YoSoy-$FECHA/. /opt/yosoy/app/
sudo chown -R yosoy:yosoy /opt/yosoy/app
sudo chmod +x /opt/yosoy/app/YoSoy                                # permiso de ejecución del binario
sudo systemctl start yosoy-api
```

Cuidados que ya costaron tiempo:

- Si `app-anterior-$FECHA` **ya existe**, `cp -a` anida la copia adentro en vez de reemplazarla. Por eso el
  `diff -rq`: si el backup no es idéntico, no sigas.
- Comparar tamaños con `du` da falsos positivos; `diff -rq` es la comprobación buena.
- No borres `app-anterior-*` hasta pasados unos días.

## 4. Verificar

```bash
curl -s -o /dev/null -w "%{http_code} %{redirect_url}\n" http://localhost:5000/
# 302 http://localhost:5000/pantallas/index.html      (Program.cs redirige "/" a "/pantallas/index.html")
sudo systemctl is-active yosoy-api                    # active
curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:5000/capacidades                     # 200
curl -s -o /dev/null -w '%{http_code}\n' https://yosoytelemedicina.tech/pantallas/index.html   # 200
sudo journalctl -u yosoy-api --since '10 minutes ago' --no-pager | grep -iE "error|exception|Video:|Zona horaria"
# sin excepciones al arrancar; "Video: /video/acceso habilitado." si las videollamadas están configuradas; y
# "Zona horaria de la clínica: America/Argentina/Buenos_Aires, ..." (si dice otra, mirar Turnos__ZonaHoraria)
```

Y entrá con tu usuario a `https://yosoytelemedicina.tech/pantallas/index.html`: lo de siempre tiene que seguir
andando. Si una migración o una variable nueva venían con esta versión, probá lo que tocan.

## 5. Rollback

Vuelve a la app del backup. La carpeta del publish sigue en tu home, por si querés mirarla.

```bash
FECHA=20261015                       # la del backup; si abriste otra sesión: sudo ls -d /opt/yosoy/app-anterior-*
sudo systemctl stop yosoy-api
sudo find /opt/yosoy/app -mindepth 1 -delete
sudo cp -a /opt/yosoy/app-anterior-$FECHA/. /opt/yosoy/app/
sudo chown -R yosoy:yosoy /opt/yosoy/app
sudo systemctl start yosoy-api
curl -s -o /dev/null -w "%{http_code} %{redirect_url}\n" http://localhost:5000/     # 302 .../pantallas/index.html
```

El rollback de la app **no** deshace las migraciones: en general solo agregan (tablas, columnas, índices) y la app
anterior ignora lo que no conoce. Si una migración sí hay que deshacerla, el rollback está en el encabezado del propio
script.

## Migraciones

Las migraciones son los `.sql` de `migrations/`. Las de la forma final del esquema (para un servidor nuevo) están
consolidadas en `docs/produccion/01-produccion.sql`; una base que ya existe recibe solo la migración nueva.

**Reglas:**

- **Antes** de desplegar la API que las necesita. Las columnas o tablas nuevas las ignora la app anterior, así que
  aplicarlas antes no corta nada.
- **Con el login administrador** de SQL Server (el que creó la base), **no** con `yosoy_app`: ese solo lee y escribe.
- **Con `-I`**, siempre: las tablas del esquema tienen índices filtrados y sin `QUOTED_IDENTIFIER ON` los `INSERT`,
  `UPDATE` y `ALTER` fallan.
- **Después de un backup de la base** hecho a mano, comprobando que el archivo existe y pesa algo razonable.
- Los scripts están escritos para poder correrse **más de una vez** (`IF NOT EXISTS`); igual, el encabezado de cada
  uno dice qué hace, cómo verificarlo y cómo deshacerlo. Leelo antes.

**Backup** (ejemplo; usá tu herramienta de siempre si es otra). Copiá después el `.bak` **fuera** del servidor:

```bash
FECHA=20261015
sudo mkdir -p /var/opt/mssql/backup && sudo chown mssql:mssql /var/opt/mssql/backup
sqlcmd -S localhost -U <usuario-administrador> -C -Q "BACKUP DATABASE [yosoy] TO DISK = N'/var/opt/mssql/backup/yosoy-$FECHA.bak' WITH COMPRESSION, CHECKSUM, INIT"
sudo ls -la /var/opt/mssql/backup/
```

**Aplicar la migración:**

```powershell
# En la PC: subir el archivo
scp migrations\add_consentimiento_video.sql agustin@179.199.155.50:/home/agustin/
```

```bash
# En el VPS (sqlcmd suele estar en /opt/mssql-tools18/bin/). Te pide la contraseña: no la escribas con -P.
sqlcmd -S localhost -d yosoy -U <usuario-administrador> -C -I -b -f 65001 -i ~/add_consentimiento_video.sql
```

| Opción | Para qué |
|---|---|
| `-I` | `QUOTED_IDENTIFIER ON` (obligatorio, ver arriba) |
| `-C` | Confiar en el certificado del servidor (es local) |
| `-b` | Cortar y devolver error si algo falla |
| `-f 65001` | UTF-8, para que los acentos se guarden bien |

**Verificar:** cada script indica su consulta de verificación. Para ver que el esquema está completo:

```bash
sqlcmd -S localhost -d yosoy -U <usuario-administrador> -C -I -Q "SELECT COUNT(*) AS tablas FROM sys.tables"
# 45  (44 hasta la migración 16, add_consentimiento_whatsapp.sql: si todavía no se la aplicaste, tiene que dar 44, y está bien: la migración 16 solo hace falta si prendés Mensajeria__AvisosPorWhatsApp)
```

**Registrar:** anotá qué migración se aplicó, cuándo y sobre qué commit en la tabla "Migraciones aplicadas" de
[`SERVIDOR.md`](SERVIDOR.md). Si la migración es un archivo nuevo, tiene que estar versionado en `migrations/`.

### Servidor nuevo: base y catálogos

Una base que todavía no existe (servidor nuevo o reconstruido) se arma en este orden, siempre con el login administrador de
SQL Server (no `yosoy_app`) y con `-I -b -f 65001`:

1. **`docs/produccion/01-produccion.sql`**, contra `master`: crea la base `yosoy` (el nombre va fijo en el script) y las 45
   tablas, con el resultado de todas las migraciones (la 16, del consentimiento de WhatsApp, incluida). Se corre **una sola vez**, en un servidor donde `yosoy` no exista.
2. **`migrations/seed_catalogos.sql`**, contra `yosoy`: los catálogos que el esquema no trae y sin los cuales la app no sirve.
   Son 15 especialidades (sin ellas el combo del registro de profesional queda vacío), los 4 estados de turno con id fijo
   (1 Pendiente, 2 Confirmado, 3 Cancelado, 4 Realizado: el código usa los números) y el tipo de consentimiento de
   telemedicina. Es **idempotente**: solo agrega lo que falta, así que se puede correr dos veces. Se niega a correr en `master`,
   `tempdb`, `model` y `msdb`: hay que pararse en la base de YoSoy con `-d yosoy`.
3. `docs/produccion/02-usuario-app.sql` (el login `yosoy_app` de la API; la contraseña se pasa por la línea de comandos, nunca
   en el archivo) y `docs/produccion/03-usuario-admin.sql` (el primer Configurador). Detalle en [`SERVIDOR.md`](SERVIDOR.md),
   sección "Base de datos".

```powershell
# En la PC: subir el archivo
scp migrations\seed_catalogos.sql agustin@179.199.155.50:/home/agustin/
```

```bash
# En el VPS. Te pide la contraseña: no la escribas con -P.
sqlcmd -S localhost -d yosoy -U <usuario-administrador> -C -I -b -f 65001 -i ~/seed_catalogos.sql
# Verificar: 15 especialidades (o más, si la base ya tenía las suyas), 4 estados y 1 tipo de consentimiento
sqlcmd -S localhost -d yosoy -U <usuario-administrador> -C -I -Q "SELECT (SELECT COUNT(*) FROM Especialidad) AS especialidades, (SELECT COUNT(*) FROM EstadoTurno) AS estados, (SELECT COUNT(*) FROM TipoConsentimiento) AS tipos"
```

El seed **no** va en la base de pruebas de los tests de integración: ahí los tests crean sus propios datos (ver
[`DESARROLLO_LOCAL.md`](DESARROLLO_LOCAL.md), "Correr los tests de integración").

## Consentimiento de WhatsApp (migración 16): orden del despliegue

El consentimiento de WhatsApp ([`PROGRESO_CONSENTIMIENTO_WHATSAPP.md`](PROGRESO_CONSENTIMIENTO_WHATSAPP.md)) agrega una tabla (`ConsentimientoWhatsApp`, migración 16) y cambia lo
que la API le manda al servicio de WhatsApp. Está **dormido por defecto**: la llave `Mensajeria__AvisosPorWhatsApp` viene **apagada**, y con ella apagada los cuatro avisos de turno por
evento no salen por WhatsApp y el consentimiento no se ofrece en ninguna pantalla (ni la casilla del registro, ni la tarjeta del perfil, ni el link `wa.me`).

**La migración 16 solo hace falta si vas a prender la llave.** Con la llave apagada, **la API no toca la tabla `ConsentimientoWhatsApp`**: ni en el registro de pacientes, ni en el perfil, ni
en los envíos, ni cuando llega una BAJA. Se puede publicar sin haberla aplicado y todo anda igual. Está probado contra una base **sin** esa tabla (la suite de pruebas entera, salvo lo que es
de la tabla misma; ver [`DESARROLLO_LOCAL.md`](DESARROLLO_LOCAL.md), "Probar contra una base sin la tabla de la migración 16"). Producción hoy tiene 44 tablas (sin la 16):
**para esta versión no hace falta aplicarla**.

### Si querés prender la llave: el orden importa

1. **Backup de la base** (sección "Migraciones").
2. **Aplicar `migrations/add_consentimiento_whatsapp.sql`** a `yosoy`, con el login administrador y `-I -b -f 65001` (es el mismo comando de
   "Aplicar la migración", con este archivo). Es idempotente, solo **agrega** una tabla, dos claves foráneas y dos índices, y no toca ningún
   dato. **Antes** de prender la llave: con la llave prendida la API lee y escribe esa tabla (con la llave apagada, o con la API anterior, la ignoran).
   **Verificar:** la consulta que trae el encabezado del script da `1, 2, 2`, y `SELECT COUNT(*) FROM sys.tables` da `45`.
3. **Variables** (en `/opt/yosoy/config/.env`; nombres en [`SERVIDOR.md`](SERVIDOR.md), "Variables de entorno"): `Mensajeria__AvisosPorWhatsApp=true` y, opcionales,
   `Mensajeria__WhatsAppNumeroClinica` (el número que atiende `whatsapp-service`; **sin él no se muestra ningún link `wa.me`**) y, si querés
   otro saludo, `Mensajeria__WhatsAppTextoInicio` (por defecto `Hola`). La versión del texto del consentimiento
   (`Mensajeria__ConsentimientoWhatsApp__VersionTexto`) queda en `v0-provisorio`: no hace falta tocarla.
4. **Publicar la API** (pasos 1 a 4 de arriba) o, si ya está publicada, reiniciarla para que lea el `.env`.
5. **Probar** con un paciente de prueba, mirando el log:
   - `curl -s https://<dominio>/consentimiento-whatsapp/texto` devuelve `"disponible":true` y `"version":"v0-provisorio"` (con la llave apagada devuelve `"disponible":false`).
   - En el registro aparece el bloque "Avisos por WhatsApp (opcional)" con la casilla **sin tildar**; en el perfil, la tarjeta "Avisos por
     WhatsApp" en **Desactivados**. Activar y desactivar desde el perfil anda, y la tarjeta cambia de estado sin recargar.
   - El log no trae teléfonos: `sudo journalctl -u yosoy-api --since '10 minutes ago' --no-pager | grep -E '[0-9]{10}'` no tiene que mostrar
     el número de ningún paciente.
   - Si cargaste `WhatsAppNumeroClinica`: el perfil muestra "Abrir WhatsApp" y el link abre un chat con **ese** número (comprobalo con el
     teléfono en la mano). Si no lo cargaste, no tiene que verse ningún link.
6. **El servicio de WhatsApp** ([más abajo](#despliegue-de-whatsapp-service-con-baileys-flujo-manual)) se despliega **después** de esta API, no
   antes: una vez prendido Baileys, los avisos salen según lo que acepta cada paciente.

**Si algo falla**

- **Con la llave apagada** (por defecto) la tabla no se usa: la API anda igual aunque la migración no esté aplicada. Si ves en el log una queja de la base por `ConsentimientoWhatsApp`
  con la llave apagada, es un defecto: avisá.
- **Con la llave prendida y sin la tabla** (la migración no se aplicó), la API arranca igual: aceptar y revocar contestan error y los avisos por WhatsApp quedan en
  "Error" y no salen (se prefiere no mandar a mandar sin saber si hay consentimiento); los avisos de la campanita y del mail siguen. Aplicá la migración o apagá la llave.
- **Rollback de la API** (sección "Rollback"): la API anterior ignora la tabla, y **no hay que borrarla** (es el registro de quién aceptó
  recibir WhatsApp; el encabezado de la migración dice qué hacer si de verdad hace falta). **Ojo:** la API anterior **no conoce las
  revocaciones**, así que si ya hay pacientes que revocaron y se vuelve atrás, apagá el canal (`Mensajeria__CanalActivo=Simulado`, reiniciar
  la API) hasta volver a desplegar esta versión.
- **Apagar la llave después de haberla prendido** no borra ni revoca nada: las aceptaciones quedan en la tabla tal cual, dormidas, hasta que se vuelva a prender.

**Antes de usarlo con pacientes reales:** el texto `v0-provisorio` es un borrador que **no validó** el equipo legal. Un texto nuevo es una
versión nueva (un archivo `Services/Mensajeria/Consentimiento/<versión>.txt` y `Mensajeria__ConsentimientoWhatsApp__VersionTexto`); **al
cambiar de versión, los avisos por WhatsApp se detienen hasta que cada paciente vuelva a aceptar** (es a propósito). Conviene hacerlo antes
de que haya aceptaciones que perder.

## WhatsApp en la API: `Mensajeria__CanalActivo` y recordatorios

Esta versión de la API cambia tres cosas de WhatsApp (qué se decidió y por qué: [`PROGRESO_WHATSAPP_SOLO_BAILEYS.md`](PROGRESO_WHATSAPP_SOLO_BAILEYS.md)):

1. **`Mensajeria__CanalActivo` solo acepta `Simulado` y `Baileys`.** `WhatsAppWebJs` (el nombre del motor viejo) **ya no existe, y la API no arranca** con ese valor:
   falla al empezar con un mensaje que dice qué cambiar. **Antes de publicar**, mirá qué tiene el servidor (esto imprime solo esa línea, no el resto del archivo):
   ```bash
   sudo grep -n '^Mensajeria__CanalActivo=' /opt/yosoy/config/.env
   ```
   - Si dice **`WhatsAppWebJs`**: cambialo a `Baileys`. La API le habla al servicio por HTTP y no sabe qué motor hay detrás: con el servicio viejo sigue andando igual. O a
     `Simulado` si no querés que salga nada por WhatsApp (el chat simulado y la bandeja siguen andando).
   - Si dice `Simulado` o `Baileys`, o no hay ninguna línea: no hay nada que cambiar.
2. **No hay recordatorios por WhatsApp.** El recordatorio de 24 horas sale solo por **mail y campanita** (como hasta ahora); nada lo manda por WhatsApp, ni por configuración
   ni por defecto. Las filas viejas de `Recordatorio` con canal `Chat` quedan como historial (no se tocan). Los avisos por evento (solicitud, turno propuesto, confirmado y
   cancelado) dependen de la llave del punto 3.
3. **Los cuatro avisos de turno por evento NO salen por WhatsApp por defecto.** La llave `Mensajeria__AvisosPorWhatsApp` viene **apagada**. Apagada: no sale ninguno
   por WhatsApp (el mail y la campanita siguen igual) y todo el consentimiento de WhatsApp queda dormido (no se ofrece en el registro ni en el perfil, no hay link
   `wa.me`). Las respuestas del asistente a quien le escribe **no** dependen de esta llave. Para prenderla: `Mensajeria__AvisosPorWhatsApp=true` en el `.env` y reiniciar la API,
   con la migración 16 aplicada antes ([Consentimiento de WhatsApp](#consentimiento-de-whatsapp-migración-16-orden-del-despliegue)).

Orden recomendado: **(1)** el valor de `Mensajeria__CanalActivo` en `.env`, **(2)** publicar la API (secciones de arriba), **(3)** recién después desplegar el servicio nuevo y
vincular el número ([más abajo](#despliegue-de-whatsapp-service-baileys)).

## Cambios de configuración

- Las variables están en `/opt/yosoy/config/` (`.env`, `video.env`, `signaling.env`, `whatsapp.env`), con permisos
  `600` y dueño `yosoy`. Se editan con `sudo` y **no se imprimen**: nada de `cat` ni `echo` de esos archivos en una
  terminal compartida ni en un chat.
- Después de cambiar una variable de la API: `sudo systemctl restart yosoy-api` (la regla `sudoers` de `agustin` lo
  permite sin contraseña) y mirar el `journalctl`.
- Los secretos de video (señalización, TURN, clave de cifrado de las emociones) los crea y mantiene
  `deploy/video/generar-secretos.sh`; no se inventan a mano. La clave de cifrado de las emociones **no se rota** sola.
- Los nombres de las variables y para qué sirven: [`SERVIDOR.md`](SERVIDOR.md), "Variables de entorno".

## Otros componentes

| Componente | Cómo se actualiza |
|---|---|
| `signaling-server` | Reemplazar `/opt/yosoy/signaling/app`, `npm ci --omit=dev` y reiniciar el servicio. Paso b de [`DESPLIEGUE_VIDEO.md`](DESPLIEGUE_VIDEO.md). |
| `whatsapp-service` | `whatsapp-service/README.md`. Reiniciarlo corta la sesión de WhatsApp unos segundos. Desplegarlo (Baileys, único motor) y limpiar lo viejo: [más abajo](#despliegue-de-whatsapp-service-baileys). |
| Caddy | Editar `/etc/caddy/Caddyfile` o `video.caddy`, **validar** (`sudo caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile`) y recién ahí `sudo systemctl reload caddy`. |
| coturn | `/etc/turnserver.conf`; el secreto lo escribe `generar-secretos.sh`. |

## Qué no hacer

- **No publicar con `--self-contained false`** ni usar `publish-vps/` como salida.
- **No correr migraciones con `yosoy_app`**, ni sin `-I`.
- **No desplegar sin el backup `app-anterior-<fecha>`** ni sin el `diff -rq`.
- **No usar el `date` del servidor** para armar la `FECHA`: viene de la PC.
- **No dejar la carpeta del publish ni el `.tar.gz` en el repositorio**: van a `D:\Claude\Publish-YoSoy-<fecha>` y a
  `%TEMP%`.
- **No imprimir ni pegar el contenido de ningún `.env`**.

## Al terminar

- Anotar el commit desplegado y las migraciones aplicadas.
- Borrar del home del VPS el `Publish-YoSoy-<fecha>` y su `.tar.gz` (el backup `app-anterior-<fecha>` se queda unos
  días).
- Si la versión cambió algo del servidor (puertos, servicios, carpetas, variables), actualizar
  [`SERVIDOR.md`](SERVIDOR.md).

# Despliegue de whatsapp-service (Baileys)

Cómo se despliega `whatsapp-service` en el VPS. **Baileys** (WhatsApp Web sin navegador, no oficial) es el **único motor**: `whatsapp-web.js`, Puppeteer y
Chromium se sacaron del código. Es manual, como el de la API. Qué hace el servicio y todas sus variables:
[`whatsapp-service/README.md`](../whatsapp-service/README.md). Cómo está armado el servidor: [`SERVIDOR.md`](SERVIDOR.md). Qué se decidió y qué te toca
a vos: [`PROGRESO_WHATSAPP_SOLO_BAILEYS.md`](PROGRESO_WHATSAPP_SOLO_BAILEYS.md) y [`PROGRESO_BAILEYS.md`](PROGRESO_BAILEYS.md).

> **Estado: todavía no se desplegó.** Según [`SERVIDOR.md`](SERVIDOR.md), el servicio que corre hoy en el VPS es el **viejo** (whatsapp-web.js + Chromium,
> `MemoryMax=2G`): este flujo lo reemplaza y, al final ("10. Limpieza de lo viejo"), se saca lo que ya no hace falta. Todo se probó con un socket simulado y
> la red bloqueada; **nada corrió contra WhatsApp**. El primer despliegue es también la primera vez que habla con WhatsApp de verdad: hacelo con tiempo y
> con el teléfono del número dedicado a mano.

- **Número dedicado de la clínica, nunca un teléfono personal.** Baileys no es la API oficial y **WhatsApp puede bloquear el
  número** (riesgo asumido). Por eso el servicio solo le escribe a quien le escribió primero o aceptó, con pausas y topes.
- **Sin recordatorios por WhatsApp.** La API ya no los manda (siguen por mail y campanita); lo que sale por WhatsApp son las respuestas del asistente y
  del equipo y los avisos por evento (ver [`PROGRESO_WHATSAPP_SOLO_BAILEYS.md`](PROGRESO_WHATSAPP_SOLO_BAILEYS.md)).
- **Sin secretos en este archivo.** Ningún comando de acá imprime un secreto; si alguno lo hiciera, pará.
- **En el VPS** se trabaja como `agustin` (con `sudo`). Los pasos tienen verificación y hay rollback.
- **No hace falta Chromium** ni sus paquetes. Si el servicio nunca se instaló, seguí `whatsapp-service/README.md` ("Primer deploy").

## Antes de empezar

1. **`main` actualizado y limpio** en la PC, y anotá el commit que vas a desplegar.
2. **Pruebas del servicio** (Node 22 o más nuevo): `cd whatsapp-service && npm test`. Todas pasan, sin red y sin WhatsApp.
3. **El número dedicado**, con WhatsApp ya activo en un teléfono que tengas a mano para escanear el QR.
4. **El valor de `Mensajeria__CanalActivo` en la API** ([WhatsApp en la API](#whatsapp-en-la-api-mensajeria__canalactivo-y-recordatorios)): la API nueva no
   arranca con `WhatsAppWebJs`. Publicá primero esa API (con `Baileys` o `Simulado`), y recién después este servicio.
5. **Consentimiento.** Con la llave `Mensajeria__AvisosPorWhatsApp` apagada (por defecto) los avisos de turno no salen por WhatsApp y el consentimiento está dormido (no hace falta la
   migración 16). Si la **prendés** (migración 16: [más arriba](#consentimiento-de-whatsapp-migración-16-orden-del-despliegue)), la API manda `optIn:true` **solo a quien aceptó**; sin
   aceptación, solo reciben WhatsApp quienes le escribieron primero al número de la clínica. Desplegá **primero** esa API con su migración y recién después prendé este canal. Y **antes de
   usarlo con pacientes reales**, el texto del consentimiento (`v0-provisorio`) tiene que estar validado por el equipo legal (`PROGRESO_CONSENTIMIENTO_WHATSAPP.md`, "Pasos manuales").
6. **Topes iniciales bajos** para un número nuevo (abajo, paso 5). Después se suben de a poco, durante semanas.
7. **Horario.** Reiniciar el servicio corta WhatsApp unos segundos; los mensajes que estén en la cola se pierden.

## 1. Empaquetar en la PC

Sin `node_modules` (se instala en el VPS). La carpeta incluye `deploy/` con la unit.

```powershell
cd D:\Claude\YoSoy\YoSoy\whatsapp-service
$fecha = Get-Date -Format yyyyMMdd
tar --exclude=node_modules -czf "$env:TEMP\whatsapp-service-${fecha}.tar.gz" .
scp "$env:TEMP\whatsapp-service-${fecha}.tar.gz" agustin@179.199.155.50:/home/agustin/
$fecha                    # anotá el valor: es la FECHA que se usa en el VPS
```

## 2. En el VPS: instalar al lado, sin tocar lo que corre

`FECHA` es la de la PC. El código nuevo va a `app-nueva/`, **al lado** de `app/`, y se prueba ahí con el servicio viejo todavía andando:

```bash
FECHA=20261016                                                    # <- la de la PC
sudo mkdir /opt/yosoy/whatsapp/app-nueva                          # si ya existe, algo quedó de antes: mirá qué es y NO sigas
sudo tar -xzf ~/whatsapp-service-$FECHA.tar.gz -C /opt/yosoy/whatsapp/app-nueva
sudo chown -R yosoy:yosoy /opt/yosoy/whatsapp/app-nueva
sudo -u yosoy bash -c '
  cd /opt/yosoy/whatsapp/app-nueva &&
  HOME=/opt/yosoy/whatsapp/cache \
  npm ci --omit=dev --cache /opt/yosoy/whatsapp/cache/npm
'
ls /opt/yosoy/whatsapp/app-nueva/index.js /opt/yosoy/whatsapp/app-nueva/deploy/whatsapp-service.service
```

`npm ci` usa el `package-lock.json` tal cual: instala Baileys en la versión exacta (6.7.24) con `libsignal` desde el registro, sin dependencias por git y
**sin bajar ningún Chromium** (son 207 paquetes, no 429). Si en el VPS no existe `/opt/yosoy/whatsapp/cache`, crealo antes
(`sudo install -d -o yosoy -g yosoy /opt/yosoy/whatsapp/cache`): es solo la caché de npm para instalar. Probá el código nuevo **en Linux** (acá se corren las
pruebas de permisos que en Windows se saltean), todavía sin tocar nada de lo que corre:

```bash
sudo -u yosoy bash -c 'cd /opt/yosoy/whatsapp/app-nueva && npm test 2>&1 | tail -n 12'
# "fail 0" y unas pocas "skipped" (las que necesitan el repositorio o la carpeta docs/, que acá no están). Si falla alguna, NO sigas.
```

Las pruebas usan carpetas temporales y la red de pruebas bloqueada: no tocan la sesión ni hablan con WhatsApp.

## 3. Backup

```bash
sudo mkdir -p /opt/yosoy/backups/whatsapp-$FECHA
sudo cp -a /etc/systemd/system/whatsapp-service.service /opt/yosoy/backups/whatsapp-$FECHA/       # la unit actual (la vieja)
sudo cp -a /opt/yosoy/config/whatsapp.env /opt/yosoy/backups/whatsapp-$FECHA/                     # copia, sin imprimirla
sudo ls -la /opt/yosoy/backups/whatsapp-$FECHA/                                                   # los dos archivos, whatsapp.env en 600
```

El código viejo no se copia: se **renombra** en el paso 4 (`app-anterior-$FECHA`), que es su backup. La sesión del servicio viejo
(`/opt/yosoy/whatsapp/sesion`) no se toca en este flujo: se borra recién en el paso 10. Si el servicio no estaba instalado, no hay nada que respaldar: salteá
este paso y los de `app-anterior`.

## 4. Corte y reemplazo

La carpeta de la sesión nueva, **700 y dueño `yosoy`** (si es otra cosa, el servicio se niega a arrancar y lo dice). Después se corta,
se intercambian las carpetas y se instala la unit:

```bash
sudo install -d -m 700 -o yosoy -g yosoy /opt/yosoy/whatsapp/sesion-baileys
sudo systemctl stop whatsapp-service
sudo mv /opt/yosoy/whatsapp/app /opt/yosoy/whatsapp/app-anterior-$FECHA
sudo mv /opt/yosoy/whatsapp/app-nueva /opt/yosoy/whatsapp/app
sudo cp /opt/yosoy/whatsapp/app/deploy/whatsapp-service.service /etc/systemd/system/whatsapp-service.service
sudo sed -i 's/\r$//' /etc/systemd/system/whatsapp-service.service     # por si el archivo llegó con finales de línea de Windows
sudo systemd-analyze verify /etc/systemd/system/whatsapp-service.service          # sin errores (puede avisar de cosas ajenas)
sudo systemctl daemon-reload
```

La unit tiene la carpeta de la sesión y el límite de memoria nuevo. El secreto compartido sigue en `/opt/yosoy/config/whatsapp.env` (el mismo archivo, no se
cambia). **No arranques todavía**: primero los topes.

## 5. Topes iniciales

Un número nuevo que de golpe manda muchos mensajes es lo que WhatsApp bloquea. Arrancá bajo y subí de a poco (semanas):

```bash
sudo sh -c 'printf "\nWHATSAPP_TOPE_DIARIO=30\nWHATSAPP_TOPE_POR_HORA=10\n" >> /opt/yosoy/config/whatsapp.env'
sudo stat -c '%a %U:%G %n' /opt/yosoy/config/whatsapp.env          # sigue en 600 yosoy:yosoy
```

(Esas dos variables y las demás: `whatsapp-service/README.md`, "Baileys: protecciones contra el bloqueo del número".) No se imprime el
archivo: tiene el secreto.

**Ojo: las respuestas del asistente gastan estos mismos topes** (cada mensaje cuenta, sea un aviso o una respuesta). Pedir un turno por el asistente lleva
unos cinco mensajes: con 30 por día alcanza para unas seis charlas, y con 10 por hora, a la tercera charla de esa hora los mensajes esperan en la cola. Si el consultorio recibe más,
subí `WHATSAPP_TOPE_DIARIO` de a poco: contestarle a quien **escribe primero** es el tráfico de menor riesgo. Por persona hay dos topes: 3 por día para un aviso suelto y
30 si ella está conversando (escribió hace menos de 24 horas; `WHATSAPP_TOPE_EN_CONVERSACION`), así una charla no se corta a la mitad.

## 6. Arrancar y verificar (todavía sin vincular)

```bash
sudo systemctl start whatsapp-service
sudo systemctl is-active whatsapp-service                                   # active
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:3002/salud         # 503: sin vincular todavía, y es lo correcto
curl -s http://127.0.0.1:3002/salud                                         # "estado":"requiere_vincular","motivo":"esperando_escaneo","qrPendiente":true
sudo ss -ltnp | grep ':3002 '                                               # 127.0.0.1:3002, nunca 0.0.0.0 ni *
sudo stat -c '%a %U:%G %n' /opt/yosoy/whatsapp/sesion-baileys /opt/yosoy/whatsapp/sesion-baileys/datos      # 700 yosoy:yosoy los dos
sudo journalctl -u whatsapp-service --since '10 minutes ago' --no-pager
# "Carpeta de la sesión de WhatsApp lista", "se abre una ventana de vinculación por QR (1 de 2)"; sin excepciones.
# Si dice "Aviso: estas variables de entorno ya no se usan ...", son variables del servicio viejo que quedaron (solo los nombres): se sacan en el paso 10.
sudo journalctl -u whatsapp-service -o cat | grep -E '[0-9]{7,}'            # no tiene que devolver nada (ni teléfonos ni códigos QR)
sudo systemd-analyze security whatsapp-service --no-pager | tail -n 3       # el puntaje de exposición, a título informativo
```

Si el servicio no arranca, mirá el `journalctl`: los problemas de configuración (carpeta de la sesión mal puesta, API_BASE_URL, una
variable inválida, un `WHATSAPP_BACKEND=webjs` que quedó) dicen qué variable es y salen en la primera línea. Si el servicio se cae en bucle, systemd lo deja de
intentar a los 5 intentos en 5 minutos (a propósito: no hay que golpear a WhatsApp en bucle).

## 7. Vincular el número dedicado

Con el servicio andando y el teléfono del número dedicado en la mano. Tres formas (detalle en `whatsapp-service/README.md`, "Baileys:
vincular el número (QR)"): desde el **Configurador** (sección WhatsApp, rol administrador; funciona aunque la API siga en
`Mensajeria__CanalActivo=Simulado`), por **HTTP** local, o por **la terminal** con `vincular.js` (con el servicio parado). En el
teléfono: WhatsApp → Ajustes → Dispositivos vinculados → Vincular un dispositivo. El QR se renueva solo; la ventana dura unos
minutos y el servicio abre hasta dos solas.

```bash
curl -s http://127.0.0.1:3002/salud     # "estado":"conectado" (200); si dice qr_vencido, "Desvincular y cambiar número" abre otra ventana
sudo systemctl restart whatsapp-service && sleep 10 && curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:3002/salud     # 200: la sesión se guardó y no pide QR
```

## 8. Prender el canal en la API

Recién con `"estado":"conectado"` confirmado, en `/opt/yosoy/config/.env` (el principal, no `whatsapp.env`):

```
Mensajeria__CanalActivo=Baileys
```

Y `sudo systemctl restart yosoy-api`; mirá el `journalctl` (sin excepciones). La API y `whatsapp-service` ya comparten el secreto por el
drop-in `whatsapp.conf`. **Probá primero con tu propio teléfono**: escribile un mensaje al número de la clínica (así queda con
constancia) y mirá que el asistente conteste. Revisá que el mensaje salga con las pausas, que el log no tenga teléfonos
completos ni texto de mensajes, y que `curl -s http://127.0.0.1:3002/salud` cuente el envío en `cola.enviadosHoy`.

## 9. Apagado

`sudo systemctl stop whatsapp-service` tiene que terminar en 1 a 3 segundos (no los 20 de `TimeoutStopSec`), y el journal tiene que decir
`SIGTERM recibido, apagando...` y `Apagado completo.`. Al volver a arrancar, `/salud` vuelve a `conectado` **sin pedir QR**.

## 10. Limpieza de lo viejo (whatsapp-web.js y Chromium)

**Solo cuando el servicio nuevo lleve unos días estable** y ya no pienses volver atrás (el rollback del paso 11 necesita lo que acá se borra). Es todo manual y no
urge: lo viejo no estorba, solo ocupa lugar y es superficie que mantener.

1. **Desvincular el dispositivo viejo del teléfono**, antes de borrar nada: en el teléfono del número de la clínica, WhatsApp → Ajustes → Dispositivos
   vinculados. Quedan dos: el de Baileys (el que se usa) y el de whatsapp-web.js (suele figurar como un navegador). Cerrá el viejo: así se corta del lado de WhatsApp.
2. **El código y los backups viejos:**
   ```bash
   sudo ls -d /opt/yosoy/whatsapp/app-anterior-* /opt/yosoy/backups/whatsapp-*     # mirá qué hay y de qué fecha
   sudo rm -rf /opt/yosoy/whatsapp/app-anterior-$FECHA                             # el código viejo (con su node_modules y Chromium dentro)
   ```
   (`/opt/yosoy/backups/whatsapp-$FECHA` tiene la unit vieja y `whatsapp.env`: dejalo unos días más y después borralo con `sudo rm -rf`.)
3. **La sesión vieja** (es una credencial de la cuenta: `LocalAuth` de whatsapp-web.js) y la **caché de Chromium**:
   ```bash
   sudo rm -rf /opt/yosoy/whatsapp/sesion                       # la sesión vieja. NO es la de Baileys (esa es sesion-baileys/)
   sudo du -sh /opt/yosoy/whatsapp/cache/* /opt/yosoy/whatsapp/cache/.[a-z]* 2>/dev/null   # mirá qué ocupa: Chromium, su perfil y la caché de npm
   sudo rm -rf /opt/yosoy/whatsapp/cache && sudo install -d -o yosoy -g yosoy /opt/yosoy/whatsapp/cache   # se vacía y queda lista para el próximo npm ci
   ```
4. **Las variables viejas** de `/opt/yosoy/config/whatsapp.env`: el log al arrancar te dijo sus nombres ("Aviso: estas variables de entorno ya no se usan"). Se
   sacan sin imprimir el archivo:
   ```bash
   sudo sed -i '/^WHATSAPP_BACKEND=/d;/^WHATSAPP_SESSION_PATH=/d;/^WHATSAPP_WEB_VERSION=/d;/^WHATSAPP_DEBUG=/d;/^PUPPETEER_/d' /opt/yosoy/config/whatsapp.env
   sudo stat -c '%a %U:%G %n' /opt/yosoy/config/whatsapp.env     # sigue en 600 yosoy:yosoy
   sudo systemctl restart whatsapp-service && sleep 5 && sudo journalctl -u whatsapp-service --since '1 minute ago' --no-pager | grep -c 'ya no se usan'   # 0
   ```
5. **Los paquetes de Chromium** (opcional: son librerías, no servicios, y ocupan poco; sacarlos es solo orden). `libnss3` y `libasound2t64` los usan muchos programas, así
   que **mirá primero qué se iría**:
   ```bash
   sudo apt remove --dry-run libatk1.0-0t64 libatk-bridge2.0-0t64 libgtk-3-0t64 libgbm1 libasound2t64 libnss3     # solo simula
   ```
   Si la lista incluye algo que no reconocés, **no sigas**. Si solo se van esas librerías: repetí el comando sin `--dry-run`.
6. **AppArmor / sysctl:** si en algún momento tocaste `kernel.apparmor_restrict_unprivileged_userns` o un perfil de AppArmor para el sandbox de Chromium, ya no hace
   falta (el servicio viejo arrancaba Chromium con `--no-sandbox`, así que probablemente no hayas tocado nada).
7. Actualizar [`SERVIDOR.md`](SERVIDOR.md) (la fila de `whatsapp-service`, las carpetas y la memoria): ya describe el estado final.

## 11. Rollback

Vuelve al servicio **viejo** (whatsapp-web.js + Chromium). **Solo mientras no hayas hecho la limpieza del paso 10** (necesita `app-anterior-<fecha>`, la unit y el
`whatsapp.env` del backup, y la sesión vieja). La sesión de Baileys **no se borra** (queda por si se vuelve a intentar).

```bash
FECHA=20261016                       # la del backup; si abriste otra sesión: sudo ls -d /opt/yosoy/whatsapp/app-anterior-*
sudo systemctl stop whatsapp-service
sudo mv /opt/yosoy/whatsapp/app /opt/yosoy/whatsapp/app-baileys-$FECHA
sudo mv /opt/yosoy/whatsapp/app-anterior-$FECHA /opt/yosoy/whatsapp/app
sudo cp -a /opt/yosoy/backups/whatsapp-$FECHA/whatsapp-service.service /etc/systemd/system/whatsapp-service.service
sudo cp -a /opt/yosoy/backups/whatsapp-$FECHA/whatsapp.env /opt/yosoy/config/whatsapp.env
sudo systemctl daemon-reload
sudo systemctl start whatsapp-service
curl -s http://127.0.0.1:3002/salud               # {"ok":true}: el viejo contesta así mientras el proceso responda
```

**La API no se toca:** `Mensajeria__CanalActivo=Baileys` le habla al servicio por HTTP y no sabe qué motor hay detrás, así que anda igual con el viejo (o poné
`Simulado` si no se va a usar WhatsApp) y `sudo systemctl restart yosoy-api`. La sesión vieja (`/opt/yosoy/whatsapp/sesion`) no se tocó, así que debería seguir
vinculada; si WhatsApp la cerró, vuelve a pedir QR como siempre (Configurador, "Vincular el chip"). **Nunca los dos servicios a la vez**: usan el mismo puerto y los
dos contestarían los mensajes. El dispositivo de Baileys queda en la lista "Dispositivos vinculados" del teléfono: si no se va a usar más, desvincularlo desde ahí.

Cuando todo esté estable unos días, `app-anterior-<fecha>` y `app-baileys-<fecha>` se pueden borrar (paso 10).

## Qué no hacer

- **No usar un teléfono personal** para el servicio. Y cuidar el teléfono del número dedicado: WhatsApp desvincula los dispositivos
  vinculados si el teléfono principal pasa unos 14 días sin conectarse, y reinstalar WhatsApp con ese número en otro teléfono también los
  desvincula. En esos casos el servicio queda en `requiere_vincular` (`sesion_cerrada`) y hay que volver a escanear un QR.
- **No copiar la carpeta `sesion-baileys` a ningún lado sin cifrarla**, ni pegarla en un chat o un ticket: es una credencial de la cuenta.
- **No apuntar `WHATSAPP_BAILEYS_SESSION_PATH` adentro de `app/`** (cada despliegue la pisaría) ni al repositorio: el servicio no arranca.
- **No subir los topes de golpe** en un número nuevo.
- **No imprimir ni pegar `whatsapp.env`**: tiene el secreto compartido con la API.
- **No dejar `app-nueva/` a medias**: si algo falla antes del corte, borrala (`sudo rm -rf /opt/yosoy/whatsapp/app-nueva`) y empezá de nuevo.
- **No correr los dos servicios a la vez** (el viejo y el nuevo): usan el mismo puerto y la misma cuenta de WhatsApp.

## Al terminar

- Anotar el commit desplegado y la fecha del backup.
- Borrar del home del VPS el `.tar.gz` de `whatsapp-service`.
- Actualizar [`SERVIDOR.md`](SERVIDOR.md) si algo cambió respecto de lo que dice (carpetas, variables, memoria usada).
- Decidir y anotar lo del backup de la sesión de Baileys (`PROGRESO_BAILEYS.md`, "Qué falta").
