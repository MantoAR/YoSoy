# Despliegue a producción: grabación y transcripción local de la videollamada

Runbook para llevar a producción la **grabación de audio y transcripción local** de las sesiones de video (consentimiento
por finalidad, subida de fragmentos, worker en Python con `faster-whisper` corriendo en el propio VPS, sin mandar audio
a ningún tercero). Qué hace y su contrato: [`VIDEOLLAMADAS.md`](VIDEOLLAMADAS.md), sección "Grabación y transcripción
local". Cambia **la base (migraciones 28 y 29)**, **la API**, suma un **worker en Python** (nuevo, instalado aparte con
`deploy/transcripcion/instalar.sh`) y un **drop-in de systemd**. Se ejecuta **a mano, en dos máquinas** (la PC con
PowerShell y el VPS por SSH), con el flujo de siempre de [`DEPLOY.md`](DEPLOY.md) y el mismo formato que
[`DEPLOY_CIERRE_SESION_PRODUCCION.md`](DEPLOY_CIERRE_SESION_PRODUCCION.md). Escrito el 2026-10-27 contra la rama
`transcripcion-local`; **antes de armar el paquete tiene que estar mergeada en `main`** (paso 0).

- **Se escribió sin acceso al VPS y no se ejecutó contra él.** Lo que se probó, y cómo, está al final.
- **Sin secretos en este archivo.** Las contraseñas las escribís vos cuando el comando las pide (nunca en la línea de
  comandos).
- Cada paso dice **dónde** se corre, el **comando exacto**, qué **esperar** y cómo **volver atrás**.
- Da por hecho que **la videollamada y las emociones ya están en producción** ([`DESPLIEGUE_VIDEO.md`](DESPLIEGUE_VIDEO.md)):
  `/opt/yosoy/config/video.env` ya existe, con `Video__SignalingSecreto`, `Video__TurnSecreto` y
  `Video__Emociones__ClaveCifrado` puestos. Este despliegue **agrega** claves ahí; no toca las que ya hay.

## Qué sale a producción (y qué no)

| Qué | Cómo queda |
|---|---|
| **Migración 28** (`add_transcripcion_local.sql`) | Columna `Finalidad` en `ConsentimientoVideo` (con default `'emociones'`, así las filas viejas no cambian de sentido) y la tabla nueva `TranscripcionSesion`. Aditiva, idempotente, en una transacción |
| **Migración 29** (`add_grabacion_video.sql`) | Dos columnas nuevas en `SesionClinica` (`TranscripcionAnuladaUtc`, `TranscripcionAnuladaMotivo`), nullable. Aditiva, idempotente, en una transacción; depende de la 28 |
| **API** | Consentimiento por finalidad (`emociones` / `grabacion`), `POST /video/grabacion/fragmento` (sube audio, nunca por HTTP se expone), encolado de la grabación a transcripción, `TranscripcionBackgroundService` (nuevo, en el mismo proceso que la API), `POST /sesiones-clinicas/{id}/transcripcion/reintentar`, la vista de sesiones clínicas devuelve `estadoTranscripcion`/`transcripcion` con el mismo gateo que las emociones |
| **Worker de transcripción** | Python + `faster-whisper`, en un venv aparte (`/opt/yosoy/whisper/venv`), instalado por `deploy/transcripcion/instalar.sh` (nuevo). Corre como subproceso de `yosoy-api`, nunca como su propio servicio |
| **systemd** | Drop-in nuevo `yosoy-api.service.d/transcripcion.conf` (lo instala `instalar.sh`) |
| **No cambia** | Caddy (sin cambios: ver paso 4.4), el Configurador (no ve nada de esto, igual que las emociones), `whatsapp-service`, el cifrado de emociones y el consentimiento de la sala de video que ya está en `main` |
| **Arranca deshabilitado** | `Video__Transcripcion__Habilitada` empieza en `false` en el código (`VideoTranscripcionOptions`); **`instalar.sh` la pone en `true`** al terminar. Sin correr `instalar.sh`, la grabación/transcripción queda "no disponible" y la videollamada sigue andando igual que hoy |

## Lo que cambia para el equipo (avisalo antes)

- El paciente ve un consentimiento **nuevo y separado** del de emociones: el de **grabación**. Son independientes;
  puede aceptar uno, el otro, los dos o ninguno.
- Con el worker habilitado, la sesión queda grabada (audio, no video) mientras dure el consentimiento, y la
  transcripción aparece en la historia clínica unos minutos después de cerrar la sesión. **El Configurador no ve
  nada de esto**, igual que con las emociones.
- Si el paciente revoca el consentimiento de grabación a mitad de sesión, se deja de grabar y la transcripción de esa
  sesión queda anulada (no se pierde lo ya grabado del lado del servidor, pero no se transcribe).
- El modelo por defecto (`small`) en un VPS de 2 vCPU puede tardar **más que la duración de la sesión** en
  transcribir una grabación larga; es async y no bloquea nada, pero avisá que "Transcribiendo…" puede tardar.

## Convenciones

- **[PC]** es PowerShell en tu PC. **[VPS]** es una sesión SSH que ya abriste (`ssh agustin@179.199.155.50`). Los
  comandos del VPS que **cambian algo** son **una sola línea** con `&&`. **Pegá un comando por vez**, y corré
  `sudo -v` antes de empezar.
- `FECHA` es la de la PC (`Get-Date -Format yyyyMMdd`). `TAG` es `trl`. Lo primero en la sesión SSH:
  ```bash
  FECHA=YYYYMMDD; TAG=trl; export PATH="$PATH:/opt/mssql-tools18/bin"      # FECHA: la de la PC
  ```

## El orden, de un vistazo

| Paso | Qué | Dónde | Corta el servicio | Se deshace |
|---|---|---|---|---|
| 0 | El código que vas a desplegar | PC | No | — |
| 1 | Backup de la base (verificado) | VPS y PC | No | — |
| 2 | **Migraciones 28 y 29** (antes del paquete) | PC y VPS | No | Sí (8.2) |
| 3 | Publicar y subir la API | PC y VPS | La API, unos segundos | Sí (8.1) |
| 4 | Instalar el worker (`instalar.sh`) | PC y VPS | No | Sí (8.1, desinstala solo al volver la API anterior) |
| 5 | Verificar | VPS | No | — |
| 6 | Prueba en el navegador | Navegador | No | — |
| 7 | Estado en la base, cambiar modelo, deshabilitar | VPS | No (deshabilitar tampoco) | — |
| 8 | Rollback | VPS | La API, unos segundos | — |

**Aplicá las migraciones antes de publicar el paquete.** La API degrada bien al revés (si se publica primero, el
consentimiento de grabación y la subida de fragmentos responden "falta aplicar la migración" y la pantalla lo
muestra), pero nada de esto puede guardar hasta que la 28 y la 29 estén.

## Paso 0. [PC] El código que vas a desplegar

```powershell
cd D:\Claude\YoSoy\YoSoy
git switch main; git pull --ff-only; git status --short; git log -1 --oneline
(Get-ChildItem deploy\transcripcion\instalar.sh, deploy\yosoy-api.service.d\transcripcion.conf, transcripcion\transcribir.py, transcripcion\requirements.txt, migrations\add_transcripcion_local.sql, migrations\add_grabacion_video.sql | Measure-Object).Count
Select-String -Path Services\VerificadorEsquema.cs -Pattern 'add_transcripcion_local.sql','add_grabacion_video.sql'
$fecha = Get-Date -Format yyyyMMdd; $fecha                  # anotá el valor: es la FECHA que se usa en el VPS
```

**Esperado:** `git status` sin líneas; el conteo da `6` y el `Select-String` muestra las dos líneas (28 y 29). Si no,
tu `main` todavía no la trae: mergeá el PR antes de seguir.

## Paso 1. Backup de la base (verificado antes de tocar nada)

Es el procedimiento del paso 1 de [`DEPLOY_USUARIOS_PRODUCCION.md`](DEPLOY_USUARIOS_PRODUCCION.md) (backup con
`CHECKSUM`, verificación, copia a tu PC con comparación de la huella SHA-256 y recién entonces borrado de las copias
del servidor). **El backup queda solo en tu PC, fuera de cualquier repositorio.**

**[VPS]**
```bash
sudo mkdir -p /var/opt/mssql/backup && sudo chown mssql:mssql /var/opt/mssql/backup && sqlcmd -S localhost -U sa -C -Q "BACKUP DATABASE [yosoy] TO DISK = N'/var/opt/mssql/backup/yosoy-pre-$TAG-$FECHA.bak' WITH CHECKSUM, INIT; RESTORE VERIFYONLY FROM DISK = N'/var/opt/mssql/backup/yosoy-pre-$TAG-$FECHA.bak' WITH CHECKSUM;"
```

**Esperado:** `The backup set on file 1 is valid.` Si no dice **valid**, **no sigas**.

## Paso 2. Migraciones 28 y 29

### 2.1 [PC] Subir los archivos

```powershell
scp D:\Claude\YoSoy\YoSoy\migrations\add_transcripcion_local.sql D:\Claude\YoSoy\YoSoy\migrations\add_grabacion_video.sql agustin@179.199.155.50:/home/agustin/
```

### 2.2 [VPS] Aplicarlas en orden (idempotentes, cada una en su transacción)

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -b -f 65001 -i ~/add_transcripcion_local.sql
```

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -b -f 65001 -i ~/add_grabacion_video.sql
```

**Esperado:** las dos corren sin error (la segunda exige que la primera ya esté: si falta, para con un mensaje claro
en vez de romper algo). Volver a correr cualquiera de las dos es seguro. Si una falla a la mitad, no queda nada
aplicado de esa.

### 2.3 [VPS] Comprobar (si esto no da lo esperado, **no publiques la API**)

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -W -Q "SELECT (SELECT MAX(Numero) FROM VersionEsquema) AS ultima_version, (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ConsentimientoVideo') AND name = 'Finalidad') AS finalidad, (SELECT COUNT(*) FROM sys.tables WHERE name = 'TranscripcionSesion') AS tabla_transcripcion, (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SesionClinica') AND name IN ('TranscripcionAnuladaUtc','TranscripcionAnuladaMotivo')) AS columnas_anulacion"
```

**Esperado:** `29  1  1  2`.

## Paso 3. Publicar la API y las pantallas

Es el flujo de siempre: autocontenido, fuera del repositorio; se publica el proyecto, **no** la solución.

### 3.1 [PC] Publicar y subir

```powershell
cd D:\Claude\YoSoy\YoSoy
$fecha = Get-Date -Format yyyyMMdd; $tag = 'trl'
dotnet publish YoSoy.csproj -c Release -r linux-x64 --self-contained true -o "D:\Claude\Publish-YoSoy-${fecha}-${tag}"
tar -czf "$env:TEMP\Publish-YoSoy-${fecha}-${tag}.tar.gz" -C "D:\Claude\Publish-YoSoy-${fecha}-${tag}" .
scp "$env:TEMP\Publish-YoSoy-${fecha}-${tag}.tar.gz" agustin@179.199.155.50:/home/agustin/
```

**Esperado:** el `publish` termina sin errores; el paquete trae el binario `YoSoy`, `appsettings.json` y `PANTALLAS\`,
y ningún `.env`.

### 3.2 [VPS] Desempaquetar y comprobar que está completo

```bash
test -f ~/Publish-YoSoy-$FECHA-$TAG.tar.gz && mkdir ~/Publish-YoSoy-$FECHA-$TAG && tar -xzf ~/Publish-YoSoy-$FECHA-$TAG.tar.gz -C ~/Publish-YoSoy-$FECHA-$TAG && ls -la ~/Publish-YoSoy-$FECHA-$TAG/YoSoy
```

**Esperado:** el binario listado.

### 3.3 [VPS] Backup `app-anterior-<tag>-<fecha>`, corte y arranque

```bash
test -f ~/Publish-YoSoy-$FECHA-$TAG/YoSoy && sudo test ! -e /opt/yosoy/app-anterior-$TAG-$FECHA && sudo cp -a /opt/yosoy/app /opt/yosoy/app-anterior-$TAG-$FECHA && sudo diff -rq /opt/yosoy/app /opt/yosoy/app-anterior-$TAG-$FECHA && echo "backup idéntico" && sudo systemctl stop yosoy-api && sudo find /opt/yosoy/app -mindepth 1 -delete && sudo cp -a ~/Publish-YoSoy-$FECHA-$TAG/. /opt/yosoy/app/ && sudo chown -R yosoy:yosoy /opt/yosoy/app && sudo chmod +x /opt/yosoy/app/YoSoy && sudo systemctl start yosoy-api && sleep 6 && systemctl is-active yosoy-api && curl -s -o /dev/null -w "%{http_code} %{redirect_url}\n" http://localhost:5000/
```

**Esperado:** `backup idéntico`, `active` y `302 .../pantallas/index.html`. Si se corta **después** de
`systemctl stop`, la API quedó parada: corré el rollback (8.1). **No hace falta tocar el `.env`.** La transcripción
sigue deshabilitada por ahora: el paso 4 la activa.

## Paso 4. Instalar el worker de transcripción

`deploy/transcripcion/instalar.sh` **no viaja dentro del paquete publicado**: necesita su propia copia del repo (para
leer `transcripcion/transcribir.py` y `transcripcion/requirements.txt` tal como están en la rama). El VPS no tiene un
clon del repo, así que se manda solo lo que hace falta, conservando las rutas relativas.

### 4.1 [PC] Empaquetar y subir

```powershell
cd D:\Claude\YoSoy\YoSoy
tar -czf "$env:TEMP\transcripcion-instalar-${fecha}.tar.gz" deploy\transcripcion\instalar.sh deploy\yosoy-api.service.d\transcripcion.conf transcripcion\transcribir.py transcripcion\requirements.txt
scp "$env:TEMP\transcripcion-instalar-${fecha}.tar.gz" agustin@179.199.155.50:/home/agustin/
```

### 4.2 [VPS] Desempaquetar y correr

```bash
mkdir -p ~/transcripcion-instalar-$FECHA && tar -xzf ~/transcripcion-instalar-$FECHA.tar.gz -C ~/transcripcion-instalar-$FECHA && cd ~/transcripcion-instalar-$FECHA && sudo bash deploy/transcripcion/instalar.sh
```

**Esperado:** termina con `Listo. Reiniciá yosoy-api...` y **ningún** `[FALLÓ]`. La primera corrida descarga el
modelo `small` (puede tardar varios minutos con 2 vCPU; no hace falta red después de la primera vez). Si algo falla,
el script dice exactamente qué verificación no pasó: no reinicies `yosoy-api` hasta que la corrida completa dé sin
fallos (podés volver a correrlo: es idempotente).

**Nota para quien lo corre:** si preferís instalar menos (por ejemplo, un modelo más chico porque el VPS es de 2
vCPU), podés fijar `YS_MODELO=base` antes del comando (`YS_MODELO=base sudo -E bash deploy/transcripcion/instalar.sh`)
en vez del `small` por defecto. Ver paso 7.2 para cambiarlo después de instalado.

### 4.3 [VPS] Reiniciar la API para que tome `video.env`

```bash
sudo systemctl restart yosoy-api && sleep 6 && systemctl is-active yosoy-api
```

**Esperado:** `active`.

### 4.4 Caddy: no hace falta tocar nada

Se investigó si el límite de tamaño de Caddy podía bloquear la subida de fragmentos de audio: **no hay cambio que
hacer**. `deploy/caddy/video.caddy` no define ningún límite de cuerpo, Caddy 2 no tiene un tope por defecto, y el
límite real ya lo fija la propia API por pedido
(`Endpoints/GrabacionVideoEndpoints.cs`: `IHttpMaxRequestBodySizeFeature.MaxRequestBodySize = o.MaxBytesFragmento + 64 KB`,
hoy 2 MB + margen), independiente de cualquier configuración de Kestrel o de Caddy. Si ya tenés `video.caddy`
integrado del despliegue de video, no hay nada nuevo que copiar.

### 4.5 systemd: el drop-in nuevo

`instalar.sh` ya instaló `yosoy-api.service.d/transcripcion.conf` (`ReadWritePaths` para
`/opt/yosoy/grabaciones` y `/opt/yosoy/whisper`) e hizo `daemon-reload`; no hace falta ningún paso manual. **Sí
conviene verificar a mano, una sola vez,** que el `MemoryMax`/`TasksMax` de la unidad base de `yosoy-api` (si los
tiene; no están en este repo) dejan lugar para el modelo cargado en memoria (el `small` ocupa un poco más de 1 GB
residente) además de la propia API — el proceso de Python es hijo de `yosoy-api` y comparte su cgroup:

```bash
sudo systemctl show yosoy-api -p MemoryMax,MemoryHigh,TasksMax
```

Si alguno de esos valores es bajo (menor a, digamos, 2 GB de `MemoryMax`), avisá antes de seguir: se soluciona
subiéndolo en un drop-in propio de la unidad base, fuera de este paquete.

## Paso 5. Verificar

### 5.1 [VPS] El servicio

```bash
sudo journalctl -u yosoy-api --since '5 minutes ago' --no-pager | grep -iE 'error|exception|fail|Hosting environment'
```

**Esperado:** `Hosting environment: Production` y **ninguna excepción**.

### 5.2 [VPS] El worker quedó habilitado

No hay una línea de log de "habilitado" (solo loguea una vez si está **deshabilitado**), así que se verifica por la
ausencia de ese aviso más el valor en el archivo:

```bash
sudo journalctl -u yosoy-api --since '10 minutes ago' --no-pager | grep 'transcripción local DESHABILITADA'; sudo grep '^Video__Transcripcion__Habilitada=' /opt/yosoy/config/video.env
```

**Esperado:** el primer comando **no devuelve nada** (si devuelve una línea, quedó deshabilitado: revisá el paso 4) y
el segundo muestra `Video__Transcripcion__Habilitada=true`.

## Paso 6. [Navegador] Prueba con un paciente de prueba

Seguí "[Probar a mano (dos navegadores)](VIDEOLLAMADAS.md#probar-a-mano-dos-navegadores)" de `VIDEOLLAMADAS.md`
**con un paciente de prueba** (no uses uno real), punto por punto (consentimiento de grabación, indicador "●
Grabando…", reconexión, "Guardar en HC", y con el worker instalado: el punto 6 es el que importa acá — esperar un
ciclo y ver "Transcribiendo…" primero y el texto después). Mientras esperás el primer ciclo real, podés seguirlo en
vivo:

```bash
sudo journalctl -u yosoy-api -f | grep 'Video: transcripción'
```

**Esperado**, tras cerrar la sesión de prueba: una línea `Video: transcripción N lista (...)` en los minutos
siguientes (el intervalo del worker es de 30 s; el modelo `small` en CPU puede tardar más que la duración del audio).

## Paso 7. Estado en la base, cambiar el modelo, deshabilitar

### 7.1 [VPS] Ver el estado de las transcripciones recientes

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -W -Q "SELECT TOP 10 IdTranscripcion, Estado, Intentos, Error, DuracionAudioSeg, DuracionProcesoSeg, CreadoUtc FROM TranscripcionSesion ORDER BY IdTranscripcion DESC"
```

### 7.2 [VPS] Cambiar el modelo (por ejemplo, a `base` si `small` tarda demasiado en 2 vCPU)

```bash
sudo sed -i 's/^Video__Transcripcion__Modelo=.*/Video__Transcripcion__Modelo=base/' /opt/yosoy/config/video.env && cd ~/transcripcion-instalar-$FECHA && YS_MODELO=base sudo -E bash deploy/transcripcion/instalar.sh && sudo systemctl restart yosoy-api
```

**Esperado:** `instalar.sh` descarga/verifica el modelo nuevo (`base`) y vuelve a dar `Listo.` sin fallos; después del
`restart`, las transcripciones siguientes usan el modelo nuevo (las que ya estaban `lista` no se reprocesan).

### 7.3 [VPS] Deshabilitar (volver a "no disponible", sin tocar nada más)

```bash
sudo sed -i 's/^Video__Transcripcion__Habilitada=.*/Video__Transcripcion__Habilitada=false/' /opt/yosoy/config/video.env && sudo systemctl restart yosoy-api
```

**Esperado:** aparece la línea `Video: transcripción local DESHABILITADA...` en el log, y no se procesa nada nuevo
(lo que ya estaba `lista` sigue visible en la historia clínica). Si volvés a correr `instalar.sh` después de esto,
va a mostrar `[FALLÓ]` en la verificación de `Habilitada=true`: es esperado y no afecta nada (las demás
verificaciones siguen pasando); no hace falta volver a correrlo solo para deshabilitar.

## Paso 8. Rollback

### 8.1 [VPS] Volver a la API anterior (la base puede quedar como está: la API anterior ignora las columnas y la
tabla nuevas)

```bash
sudo systemctl stop yosoy-api && sudo find /opt/yosoy/app -mindepth 1 -delete && sudo cp -a /opt/yosoy/app-anterior-$TAG-$FECHA/. /opt/yosoy/app/ && sudo chown -R yosoy:yosoy /opt/yosoy/app && sudo systemctl start yosoy-api && sleep 6 && systemctl is-active yosoy-api
```

El drop-in `transcripcion.conf` y el worker instalado pueden quedarse: una API vieja que nunca llama al worker no
los usa para nada, y `ReadWritePaths` de más no rompe nada bajo `ProtectSystem=strict`. Si igual querés sacarlos:

```bash
sudo rm -f /etc/systemd/system/yosoy-api.service.d/transcripcion.conf && sudo systemctl daemon-reload && sudo systemctl restart yosoy-api
```

### 8.2 [VPS] Sacar las migraciones (solo si hace falta; **después** de 8.1; no hay un script de "deshacer" para
estas dos)

No se escribió un script de deshacer porque, a diferencia de otras migraciones, **no hace falta uno**: la 28 y la 29
solo agregan una columna con default, una tabla nueva y dos columnas nullable, y ninguna versión de la API anterior a
esta rama las lee ni las escribe — quedan ahí sin usarse, igual que pasa con cualquier columna nueva no usada todavía.
Si en algún momento se necesitara borrarlas igual (por ejemplo, para limpiar antes de un cambio de diseño), es un
`ALTER TABLE ... DROP` manual fuera de este runbook, con su propio backup y ensayo primero.

## Qué se verificó y qué no

- **Verificado:** `dotnet test` y `npm test` (en `signaling-server/`) en verde con estos cambios; `instalar.sh` con
  `shellcheck` (limpio) y con una corrida funcional completa simulando root/systemd con variables de entorno
  (creación del venv, instalación de dependencias, copia del script, descarga y carga del modelo, permisos de
  escritura, edición no destructiva de `video.env` preservando claves y permisos existentes, e idempotencia en una
  segunda corrida). Esa corrida encontró un bug real — `faster-whisper==1.0.3` no declara `requests` como
  dependencia propia y una versión reciente de `huggingface-hub` dejó de traerla indirectamente, rompiendo la carga
  del modelo con `ModuleNotFoundError: No module named 'requests'` — corregido agregando `requests` a
  `transcripcion/requirements.txt`.
- **No verificado:** `instalar.sh` dentro de un contenedor Ubuntu 24.04 limpio (el daemon de Docker no estaba
  disponible en esta corrida) ni nada contra el VPS real ni contra la base `yosoy`; una videollamada real de punta a
  punta con dos personas; el `MemoryMax`/`TasksMax` real de la unidad `yosoy-api` en el VPS (no está en este
  repositorio: verificar a mano en el paso 4.5).
