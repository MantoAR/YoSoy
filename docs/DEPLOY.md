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
   "Cambios de configuración").
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
# 44
```

**Registrar:** anotá qué migración se aplicó, cuándo y sobre qué commit en la tabla "Migraciones aplicadas" de
[`SERVIDOR.md`](SERVIDOR.md). Si la migración es un archivo nuevo, tiene que estar versionado en `migrations/`.

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
| `whatsapp-service` | `whatsapp-service/README.md`. Reiniciarlo corta la sesión de WhatsApp unos segundos. |
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
