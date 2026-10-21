# Despliegue a producción: pantalla Usuarios del Configurador, restablecer contraseña y cambio obligatorio al primer ingreso

Runbook para llevar a producción la **Fase 2, PR 2**: la pantalla **Configurador › Usuarios** como **lista única** (pacientes, profesionales y equipo, con buscador, filtros y menú de acciones), **restablecer la contraseña** de una cuenta con una **clave temporal**, el **cambio obligatorio de la clave al primer ingreso** (migración **24**) y la **asignación de profesionales a un paciente** desde la lista. Se ejecuta **a mano, en dos máquinas** (la PC con PowerShell y el VPS por SSH), con el flujo de siempre de [`DEPLOY.md`](DEPLOY.md) y el mismo formato que [`DEPLOY_ETAPA2_ROLES_PERMISOS_PRODUCCION.md`](DEPLOY_ETAPA2_ROLES_PERMISOS_PRODUCCION.md). Escrito el 2026-10-20 contra la rama `feat/usuarios-lista` (sale de `main` en `6e9c458`); **antes de armar el paquete tiene que estar mergeada en `main`** (paso 0).

- **Se escribió sin acceso al VPS y todavía no se ejecutó.** Lo que se probó antes, y cómo, está en "Qué se verificó y qué no", al final; lo que no, también.
- **Sin secretos en este archivo.** Ningún comando imprime un secreto ni el contenido de un `.env`; las contraseñas las escribís vos cuando el comando las pide (nunca en la línea de comandos). La clave temporal de la prueba del paso 5 se ve en pantalla una sola vez y no se copia a ningún archivo.
- Cada paso dice **dónde** se corre, el **comando exacto**, qué **esperar** y cómo **volver atrás**.
- **Cambia la base** (una columna) y **la API, el ingreso y las pantallas**. Exige el backup verificado del paso 1.

## Qué sale a producción (y qué no)

| Qué | Cómo queda |
|---|---|
| **Migración 24** (`add_debe_cambiar_password.sql`) | **Una columna:** `Usuario.DebeCambiarPassword` (`BIT NOT NULL`, por defecto 0). Aditiva e idempotente; registra la versión 24 (origen `Script`). Las cuentas que ya existen quedan en 0: **a nadie se le pide nada al desplegar**. No crea tablas: la base sigue con **54 tablas** y pasa de la versión 23 a la **24** |
| **API: lista de usuarios** | `GET /usuarios/lista?q=&tipo=&estado=&pagina=&cantidad=` (solo Configurador): une `Usuario` con `UsuarioPaciente` y `UsuarioProfesional`. Búsqueda y paginación en el servidor. Sin datos clínicos ni el hash de la contraseña |
| **API: restablecer contraseña** | `POST /usuarios/{id}/restablecer-password` (solo Configurador, **nunca sobre uno mismo**): clave temporal de 14 caracteres que cumple la política, guardada con el mismo hash del sistema, `DebeCambiarPassword = 1`, cuenta desbloqueada. La clave se devuelve **una sola vez**, con `Cache-Control: no-store`; no queda en logs ni en la auditoría. Límite: 10 pedidos cada 10 minutos por IP |
| **API: ingreso** | Con la clave temporal el ingreso **verifica la contraseña y el bloqueo como siempre**, pero **no entrega sesión**: devuelve `requiereCambioPassword` y un **token de 5 minutos** que solo sirve en `POST /sesion/cambiar-password` (se rechaza en todo lo demás). Elegida la clave nueva (política de siempre, distinta de la temporal) se entrega la sesión normal por el mismo camino del ingreso. Aplica a **todos los tipos de cuenta, pacientes incluidos** |
| **API: asignar profesionales** | `GET`, `POST` y `DELETE /usuarios/pacientes/{id}/profesionales` (solo Configurador): uno o varios, paciente y profesionales activos, todo o nada, respeta `UNIQUE(IdPaciente, IdProfesional)`, **rechaza que el Configurador se asigne a sí mismo**. Los endpoints de **Admisión no cambian** |
| **Pantallas** (van dentro del paquete de la API) | **Configurador › Usuarios** pasa a ser una sola lista con chips de tipo y de estado (bloqueada, clave temporal, sin cuenta), buscador, filtros y paginación, y un menú de acciones por fila (restablecer contraseña, permisos, reenviar invitación, crear usuario de un profesional sin cuenta, asignar profesionales, link público, dar de baja o de alta). El **ingreso** suma el paso **Elegí tu clave nueva**. La pantalla de dos listas de **Admisión** (Asignaciones) no cambia |
| Auditoría administrativa | Cuatro acciones nuevas: `Restablecimiento de contraseña`, `Cambio de contraseña`, `Asignación de profesional` y `Desasignación de profesional` (estas dos llevan la entidad `Paciente`, no `PacienteProfesional`, para que aparezcan en la auditoría **administrativa** que ve el Configurador y no en la clínica). **Nunca la clave ni su hash** |
| **No se toca** | El rol y el menú del CRM, Profesional y Paciente; el flujo público "olvidé mi contraseña"; Grupos, Reglas de derivación y Coberturas; los endpoints `/pacientes`, `/profesionales`, `/usuarios/accesos` y los de Admisión (`POST` y `DELETE /pacientes/{id}/profesionales`) |
| `whatsapp-service`, Caddy, variables de entorno (`.env`) | **No se tocan.** La API no define variables nuevas (existe una opcional, `UsuariosAdmin__RestablecerPorDiezMinutos`, para cambiar el límite; no hace falta cargarla) |

## Lo que cambia para el equipo (avisalo antes)

- **Usuarios** ya no tiene dos columnas (pacientes y profesionales): es una lista con todos. Para ver solo a los pacientes o solo a los profesionales, se usan los botones de tipo. Los que todavía no tienen usuario están en el filtro **Sin cuenta**.
- **Quien recibe una clave temporal** entra con ella y **la primera vez tiene que elegir una clave nueva**: es normal y es el único camino (sin elegirla no hay sesión).
- **La clave temporal se le pasa a la persona por un medio seguro**, vos: el sistema no se la manda por mail. Se ve **una sola vez** en la pantalla; si se cierra sin copiarla hay que restablecer otra vez.
- **Restablecer no cierra las sesiones que la persona ya tenía abiertas** (ver "Límites conocidos"): si el motivo es que alguien más pudo haber entrado a la cuenta, esa sesión sigue valiendo hasta que venza (8 horas).

## Convenciones

- **[PC]** es PowerShell en tu PC. **[VPS]** es una sesión SSH que ya abriste (`ssh agustin@179.199.155.50`): pegá las líneas ahí. No uses `ssh servidor 'comando con sudo'`. Los comandos del VPS que **cambian algo** son **una sola línea** con `&&`. **Pegá un comando por vez**, y corré `sudo -v` antes de empezar.
- `FECHA` es **la de la PC** (`Get-Date -Format yyyyMMdd`). `TAG` es `u24`. Lo primero en la sesión SSH (y de nuevo si la cerrás):
  ```bash
  FECHA=YYYYMMDD; TAG=u24; export PATH="$PATH:/opt/mssql-tools18/bin"      # FECHA: la de la PC
  ```
- El login administrador de SQL Server es **`sa`** (no `yosoy_app`, que no puede alterar tablas). `sqlcmd` te pide la contraseña: no la escribas en el comando (`-P`).
- Las migraciones se corren **siempre con `-I -b -f 65001`**: `-I` (índices filtrados y triggers), `-b` (corta si algo falla) y `-f 65001` (acentos).

## El orden, de un vistazo

| Paso | Qué | Dónde | Corta el servicio | Se deshace |
|---|---|---|---|---|
| 0 | El código que vas a desplegar | PC | No | — |
| 1 | Backup de la base, verificado | PC y VPS | No | — (solo lee y copia) |
| 2 | Estado antes (solo lectura) | VPS | No | — |
| 3 | Migración 24 | PC y VPS | No | Sí (7.3) |
| 4 | Publicar la API y las pantallas | PC y VPS | La API, unos segundos | Sí (7.1) |
| 5 | Verificar | VPS y navegador | No | — |
| 6 | Sesiones abiertas | — | — | — |
| 7 | Rollback | VPS | — | — |

**El orden importa: la migración primero, la API después.** La API vieja **ignora** la columna nueva y sigue andando con la base migrada (se probó: ver el final), así que entre el paso 3 y el 4 nada se rompe. La API **nueva** lee `DebeCambiarPassword` en **cada ingreso**: sin la migración 24, **nadie podría ingresar**. Por eso va antes, y por eso el paso 3 comprueba que la columna existe antes de seguir. Elegí un horario de poco uso: la API se reinicia unos segundos.

---

## Paso 0. [PC] El código que vas a desplegar

```powershell
cd D:\Claude\YoSoy\YoSoy
git switch main; git pull --ff-only; git status --short; git log -1 --oneline
(Get-ChildItem migrations\add_debe_cambiar_password.sql, docs\produccion\deshacer-migracion-24.sql, PANTALLAS\Pantalla_Configurador\js\usuarios.js, Endpoints\UsuariosAdminEndpoints.cs | Measure-Object).Count
$fecha = Get-Date -Format yyyyMMdd; $fecha                  # anotá el valor: es la FECHA que se usa en el VPS
New-Item -ItemType Directory -Force 'D:\Claude\Backups\yosoy-produccion' | Out-Null
```

**Esperado:** `git status` sin líneas; `git log -1` es el merge del PR de esta mejora o uno posterior; el conteo da `4`. Si da menos, tu `main` todavía no la trae: mergeá el PR antes de seguir.

## Paso 1. Backup de la base (verificado antes de tocar nada)

Es el procedimiento del paso 1.1 de [`DEPLOY_CHAT_PERSONAL_PRODUCCION.md`](DEPLOY_CHAT_PERSONAL_PRODUCCION.md): backup en el VPS con `CHECKSUM`, **verificación antes de seguir**, copia a tu PC con comparación de la huella SHA-256 y recién entonces borrado de las copias del servidor. **El backup queda solo en tu PC, fuera de cualquier repositorio.**

**[VPS]** Backup y verificación (te pide la contraseña de `sa`):

```bash
sudo mkdir -p /var/opt/mssql/backup && sudo chown mssql:mssql /var/opt/mssql/backup && sqlcmd -S localhost -U sa -C -Q "BACKUP DATABASE [yosoy] TO DISK = N'/var/opt/mssql/backup/yosoy-pre-$TAG-$FECHA.bak' WITH CHECKSUM, INIT; RESTORE VERIFYONLY FROM DISK = N'/var/opt/mssql/backup/yosoy-pre-$TAG-$FECHA.bak' WITH CHECKSUM;"
```

**Esperado:** `BACKUP DATABASE successfully processed ... pages` y `The backup set on file 1 is valid.` Si no dice **valid**, **no sigas**: repetí el backup.

```bash
sudo cp /var/opt/mssql/backup/yosoy-pre-$TAG-$FECHA.bak ~/ && sudo chown "$USER": ~/yosoy-pre-$TAG-$FECHA.bak && ls -la ~/yosoy-pre-$TAG-$FECHA.bak && sha256sum ~/yosoy-pre-$TAG-$FECHA.bak
```

**[PC]** Bajarlo y comparar:

```powershell
scp "agustin@179.199.155.50:/home/agustin/yosoy-pre-u24-${fecha}.bak" 'D:\Claude\Backups\yosoy-produccion\'
Get-FileHash "D:\Claude\Backups\yosoy-produccion\yosoy-pre-u24-${fecha}.bak" -Algorithm SHA256; (Get-Item "D:\Claude\Backups\yosoy-produccion\yosoy-pre-u24-${fecha}.bak").Length
```

**Esperado:** la misma huella y el mismo tamaño que `sha256sum` y `ls -la`. **Recién entonces, [VPS]**, borrar las copias del servidor:

```bash
sudo rm -f /var/opt/mssql/backup/yosoy-pre-$TAG-$FECHA.bak ~/yosoy-pre-$TAG-$FECHA.bak && sudo ls -la /var/opt/mssql/backup/
```

**Por qué no se puede saltear:** la migración es chica y reversible (7.3), pero la API nueva toca cómo entra **todo** el equipo y los pacientes. Si algo sale mal, la única vuelta atrás completa es este backup (7.2). **Ojo:** el VPS tiene SQL Server 2025 y tu PC 2019: este `.bak` se restaura en el VPS, no en tu instancia.

## Paso 2. [VPS] Estado antes (todo de solo lectura)

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -W -Q "SELECT (SELECT COUNT(*) FROM sys.tables) AS tablas, (SELECT MAX(Numero) FROM VersionEsquema) AS ultima_version, (SELECT COUNT(*) FROM Usuario) AS usuarios, CASE WHEN COL_LENGTH('dbo.Usuario', 'DebeCambiarPassword') IS NULL THEN 'no' ELSE 'si' END AS ya_tiene_la_columna; EXEC sp_VerificarCadenaAuditoria"
```

**Esperado (según el estado del 20/10/2026):** `54  23  <N>  no` y la cadena `Íntegra`. Si ya dice `si` y la versión `24`, la migración ya está aplicada: seguí al paso 4. Si `ultima_version` da menos de 23, **pará**: faltan las migraciones de la etapa 2 y esta API las necesita.

## Paso 3. Migración 24

### 3.1 [PC] Subir el archivo

```powershell
scp D:\Claude\YoSoy\YoSoy\migrations\add_debe_cambiar_password.sql agustin@179.199.155.50:/home/agustin/
```

### 3.2 [VPS] Aplicarla (idempotente)

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -b -f 65001 -i ~/add_debe_cambiar_password.sql
```

**Esperado:** `Migración 24 aplicada.` y una fila con `cuentas = <N>` y `con_cambio_pendiente = 0`. Sin errores. Volver a correrla es seguro: no cambia nada.

### 3.3 [VPS] Comprobar (si esto no da lo esperado, **no publiques la API**)

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -W -Q "SELECT (SELECT COUNT(*) FROM sys.tables) AS tablas, (SELECT MAX(Numero) FROM VersionEsquema) AS ultima_version, (SELECT Origen FROM VersionEsquema WHERE Numero = 24) AS origen, (SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Usuario') AND name = 'DebeCambiarPassword') AS acepta_nulos, (SELECT COUNT(*) FROM Usuario WHERE DebeCambiarPassword = 1) AS con_cambio_pendiente; EXEC sp_VerificarCadenaAuditoria"
```

**Esperado:** `54  24  Script  0  0` y la cadena `Íntegra`.

## Paso 4. Publicar la API y las pantallas

### 4.1 [PC] Publicar y subir (el flujo de siempre: autocontenido, fuera del repositorio)

```powershell
cd D:\Claude\YoSoy\YoSoy
$fecha = Get-Date -Format yyyyMMdd; $tag = 'u24'
dotnet publish YoSoy.csproj -c Release -r linux-x64 --self-contained true -o "D:\Claude\Publish-YoSoy-${fecha}-${tag}"
tar -czf "$env:TEMP\Publish-YoSoy-${fecha}-${tag}.tar.gz" -C "D:\Claude\Publish-YoSoy-${fecha}-${tag}" .
scp "$env:TEMP\Publish-YoSoy-${fecha}-${tag}.tar.gz" agustin@179.199.155.50:/home/agustin/
```

**Esperado:** el `publish` termina sin errores (los avisos de nulabilidad `CS86xx` son normales); el paquete trae el binario `YoSoy`, `appsettings.json` y `PANTALLAS\` (con `Pantalla_Configurador\js\usuarios.js`), y ningún `.env`.

### 4.2 [VPS] Desempaquetar y comprobar que está completo

```bash
test -f ~/Publish-YoSoy-$FECHA-$TAG.tar.gz && mkdir ~/Publish-YoSoy-$FECHA-$TAG && tar -xzf ~/Publish-YoSoy-$FECHA-$TAG.tar.gz -C ~/Publish-YoSoy-$FECHA-$TAG && ls -la ~/Publish-YoSoy-$FECHA-$TAG/YoSoy ~/Publish-YoSoy-$FECHA-$TAG/PANTALLAS/Pantalla_Configurador/js/usuarios.js
```

**Esperado:** dos archivos listados. Si el `.tar.gz` no está o el `mkdir` dice que ya existe, la `FECHA` está mal o quedó algo de antes: **no sigas**.

### 4.3 [VPS] Backup `app-anterior-<tag>-<fecha>`, corte y arranque

```bash
test -f ~/Publish-YoSoy-$FECHA-$TAG/YoSoy && sudo test ! -e /opt/yosoy/app-anterior-$TAG-$FECHA && sudo cp -a /opt/yosoy/app /opt/yosoy/app-anterior-$TAG-$FECHA && sudo diff -rq /opt/yosoy/app /opt/yosoy/app-anterior-$TAG-$FECHA && echo "backup idéntico" && sudo systemctl stop yosoy-api && sudo find /opt/yosoy/app -mindepth 1 -delete && sudo cp -a ~/Publish-YoSoy-$FECHA-$TAG/. /opt/yosoy/app/ && sudo chown -R yosoy:yosoy /opt/yosoy/app && sudo chmod +x /opt/yosoy/app/YoSoy && sudo systemctl start yosoy-api && sleep 6 && systemctl is-active yosoy-api && curl -s -o /dev/null -w "%{http_code} %{redirect_url}\n" http://localhost:5000/
```

**Esperado:** `backup idéntico`, `active` y `302 http://localhost:5000/pantallas/index.html`. La cadena se frena **antes** de parar la API si el publish no está desempaquetado, si el backup ya existe o si `diff -rq` encuentra diferencias. Si se corta **después** de `systemctl stop`, la API quedó parada: corré el rollback (7.1). **No hace falta tocar el `.env`.**

## Paso 5. Verificar

### 5.1 [VPS] El servicio y el entorno

```bash
sudo journalctl -u yosoy-api --since '5 minutes ago' --no-pager | grep -iE 'error|exception|fail|Zona horaria|Hosting environment|Esquema'; systemctl is-active whatsapp-service
```

**Esperado:** la línea `Zona horaria de la clínica: ...`, **`Hosting environment: Production`**, **ninguna excepción** y `active` para `whatsapp-service` (no se tocó). Si la verificación de esquema está prendida (`Esquema__Verificar=true`; viene apagada) y avisa que falta la **migración 24**, el paso 3 no quedó aplicado: volvé a él. Con ella apagada, la falta de la columna se nota en el ingreso (error 500 con su código): la comprobación 5.2 la detecta enseguida.

### 5.2 [VPS] Un ingreso común sigue andando (la contraseña se escribe cuando la pide, no queda en el historial)

Reemplazá `USUARIO` por el tuyo (el administrador) y corré:

```bash
read -rsp "Contraseña: " PW; echo; curl -s -X POST http://127.0.0.1:5000/login -H 'Content-Type: application/json' -d "{\"username\":\"USUARIO\",\"password\":\"$PW\"}" | python3 -c "import sys,json; d=json.load(sys.stdin); print({k:v for k,v in d.items() if k in ('rol','modulo','requiereModulo','requiereCambioPassword','codigo','mensaje')}, [m['clave'] for m in d.get('modulos',[])])"; unset PW
```

**Esperado:** `requiereModulo: True` y los módulos `['crm', 'configurador']` (como antes), y **ningún** `requiereCambioPassword`. Nadie quedó con la marca puesta.

### 5.3 [Navegador] La lista (Ctrl+F5 antes: ver paso 6)

Ingresá por la pestaña **Configurador**. **Esta parte no cambia datos.**

1. Se abre en **Usuarios**: una sola lista con todas las cuentas, cada una con su chip de tipo (Paciente, Profesional, Equipo) y de estado.
2. **Buscador:** escribí un apellido, un DNI o una matrícula: la lista se acota (tarda una fracción de segundo). **Filtros:** probá **Pacientes**, **Profesionales** y **Equipo**, y en el desplegable **Sin confirmar** y **Sin cuenta**.
3. Tu propia fila (dice "vos") **no** ofrece **Restablecer contraseña** en su menú (el engranaje de la derecha).

### 5.4 [Navegador] Restablecer y primer ingreso, **sobre una cuenta de prueba**

**Usá una cuenta de prueba, nunca la de una persona real**: si no hay una, creá un paciente de prueba desde **Registrate** (la pantalla de ingreso) con un nombre que lo diga (por ejemplo, "Prueba Usuarios") o dalo de alta como equipo desde **Registrar usuario**. Esta prueba **cambia la contraseña de esa cuenta**.

1. En **Usuarios**, buscá la cuenta de prueba → engranaje → **Restablecer contraseña** → confirmá. Aparece la **clave temporal** con el aviso **"Se ve una sola vez"**. Copiala con el botón (o anotala): al cerrar no se puede volver a ver. **Listo.** La fila ahora muestra el chip **Clave temporal**.
2. En **otra ventana de incógnito** (así no se pisa tu sesión), ingresá con el usuario de la cuenta de prueba y esa clave. En lugar de entrar, aparece **Elegí tu clave nueva**:
   - Probá una clave de menos de 6 caracteres: la rechaza. Probá repetirla distinta: la rechaza. Probá poner **la misma temporal**: la rechaza ("tiene que ser distinta de la temporal").
   - Poné una clave nueva (y anotala si la cuenta se va a seguir usando): **entra** a su pantalla, como cualquier ingreso.
3. Cerrá sesión y volvé a ingresar con la **clave nueva**: entra directo, sin pedir nada. Con la temporal: "Usuario o contraseña incorrectos".
4. De vuelta en la lista (botón **Actualizar**), la fila ya no tiene el chip **Clave temporal**.
5. **Auditoría** (menú → Auditoría): aparecen **Restablecimiento de contraseña** (con tu usuario y la cuenta afectada) y **Cambio de contraseña**. **En ninguno** aparece la clave.

**Si algo sale mal en este punto:** la API registra el error con un código corto. Para volver atrás la **cuenta de prueba** basta con restablecerle la contraseña de nuevo; para volver atrás el despliegue, el paso 7.

### 5.5 [Navegador] Asignar profesionales, **sobre un paciente de prueba**

1. En **Usuarios**, buscá el paciente de prueba → engranaje → **Asignar profesionales**. Se abre la ventana con los profesionales activos para elegir y, arriba, los ya asignados (solo nombre, matrícula y especialidad).
2. Buscá a un profesional, tildalo y **Asignar**: pasa a la lista de asignados. **Quitar** lo saca (pide confirmación).
3. (Opcional) Con la sesión del profesional asignado, el paciente aparece entre los suyos. **Quitá la asignación al terminar** si fue una prueba.
4. En **Auditoría** quedan **Asignación de profesional** y **Desasignación de profesional**.

### 5.6 [VPS] Que lo nuevo es solo del Configurador (solo lectura, sin sesión)

```bash
curl -s -o /dev/null -w "lista sin sesion: %{http_code}\n" http://localhost:5000/usuarios/lista; curl -s -o /dev/null -w "restablecer sin sesion: %{http_code}\n" -X POST http://localhost:5000/usuarios/1/restablecer-password; curl -s -o /dev/null -w "cambiar clave sin sesion: %{http_code}\n" -X POST http://localhost:5000/sesion/cambiar-password -H 'Content-Type: application/json' -d '{}'
```

**Esperado:** `401`, `401` y `401`. (Que CRM, Profesional y Paciente reciben `403` en todo lo nuevo está probado con tokens reales en las pruebas automáticas; ver el final.)

### 5.7 [VPS] La cadena de auditoría

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -W -Q "SELECT TOP 6 IdLog, Accion, Entidad, Categoria FROM LogAuditoria ORDER BY IdLog DESC; EXEC sp_VerificarCadenaAuditoria"
```

**Esperado:** las acciones de la prueba (`Restablecimiento de contraseña`, `Cambio de contraseña`, `Ingreso`...) todas con categoría `Administrativa`, y `Íntegra`.

## Paso 6. Sesiones abiertas

Los tokens que ya están emitidos **no cambian y siguen valiendo**: nadie tiene que volver a ingresar. Sí hace falta **recargar la pestaña** (Ctrl+F5) para que el navegador baje las pantallas nuevas (la lista de Usuarios y el ingreso); una pestaña vieja del Configurador sigue mostrando las dos columnas de antes, que **siguen funcionando** (la API que usa no cambió).

## Paso 7. Rollback

### 7.1 Volver a la API anterior (lo normal)

La API anterior **ignora** la columna nueva (no la lee ni la escribe), así que **no hace falta tocar la base**. Se probó: ver el final.

```bash
sudo test -d /opt/yosoy/app-anterior-$TAG-$FECHA && sudo systemctl stop yosoy-api && sudo find /opt/yosoy/app -mindepth 1 -delete && sudo cp -a /opt/yosoy/app-anterior-$TAG-$FECHA/. /opt/yosoy/app/ && sudo chown -R yosoy:yosoy /opt/yosoy/app && sudo systemctl start yosoy-api && sleep 6 && systemctl is-active yosoy-api && curl -s -o /dev/null -w "%{http_code} %{redirect_url}\n" http://localhost:5000/
```

**Esperado:** `active` y `302 ...`. Vuelven las dos columnas de Usuarios y el ingreso de siempre. **Qué se pierde, y es lo único que importa:** una cuenta que tenga la clave temporal puesta **entra con ella directamente** con la API anterior (no se le pide elegir una nueva). Antes de volver atrás, listá esas cuentas y, si hay alguna, avisales o restablecé la clave de nuevo cuando vuelva la API nueva:

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -W -Q "SELECT IdUsuario, Username FROM Usuario WHERE DebeCambiarPassword = 1 ORDER BY IdUsuario"
```

### 7.2 Restaurar el backup (la vuelta atrás completa)

Solo si hay que dejar **toda** la base como estaba antes del paso 3. **Se pierde todo lo escrito en la base desde el backup** (turnos, mensajes, altas, auditoría posterior). Con la API parada y la copia del backup otra vez en el VPS (la tenés en la PC: `scp` al revés):

```bash
sudo systemctl stop yosoy-api && sudo cp ~/yosoy-pre-$TAG-$FECHA.bak /var/opt/mssql/backup/ && sudo chown mssql:mssql /var/opt/mssql/backup/yosoy-pre-$TAG-$FECHA.bak && sqlcmd -S localhost -U sa -C -Q "ALTER DATABASE [yosoy] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE [yosoy] FROM DISK = N'/var/opt/mssql/backup/yosoy-pre-$TAG-$FECHA.bak' WITH REPLACE, CHECKSUM; ALTER DATABASE [yosoy] SET MULTI_USER;"
```

Y después la API anterior (7.1, sin el `systemctl stop` inicial: ya está parada) y volver a verificar con el paso 2.

### 7.3 Sacar solo la columna (sin restaurar)

`docs/produccion/deshacer-migracion-24.sql` (probado en el ensayo): borra la columna y su restricción y la versión 24, y lista antes las cuentas que tenían la clave temporal puesta. No toca ningún otro dato. **Hacelo con la API anterior ya puesta** (la nueva falla sin la columna):

```powershell
scp D:\Claude\YoSoy\YoSoy\docs\produccion\deshacer-migracion-24.sql agustin@179.199.155.50:/home/agustin/
```

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -b -f 65001 -i ~/deshacer-migracion-24.sql
```

**Esperado:** `Migración 24 deshecha en la base.` y una fila con `54` tablas y `23` como última versión.

---

## Límites conocidos (decisiones del dueño; quedan escritas para no redescubrirlas)

- **Restablecer no cierra las sesiones activas de esa cuenta.** No hay revocación de tokens: un token ya emitido vale hasta que vence (8 horas). Si el motivo de restablecer es que alguien ajeno pudo haber entrado, esa sesión sigue abierta. Cerrarlas necesitaría una marca de "sesiones válidas desde" por usuario consultada en cada pedido: es un cambio de arquitectura, fuera de este PR.
- **La clave temporal no vence.** Sirve hasta que la persona la cambie (o hasta que se restablezca otra). Mientras tanto, la cuenta tiene una clave que conoce quien la copió: pasala por un medio seguro y pedile que ingrese pronto.
- **"Olvidé mi contraseña" (mail) no baja la marca.** Si una cuenta tiene la clave temporal puesta y la persona la restablece por el mail de recuperación (flujo público, que este PR no toca), la marca `DebeCambiarPassword` queda en 1 y en el siguiente ingreso se le pide elegir otra clave una vez más. No es inseguro, solo redundante; se resuelve bajando la marca en ese flujo, en otro PR.
- **Dar de baja a un paciente o a un profesional baja su ficha, no su usuario** (así es desde antes). La lista de Usuarios muestra esas cuentas como **Inactivo** (cuenta la baja de cualquiera de las dos), pero el usuario sigue pudiendo ingresar si conoce su clave. No cambia en este PR.
- **Asignación y `EstadoAsignacion`.** La asignación desde Usuarios deja `EstadoAsignacion` vacío, **igual que el endpoint de Admisión** (`POST /pacientes/{id}/profesionales`). Varias consultas lo leen como "Confirmada" cuando está vacío (`ISNULL(..., 'Confirmada')`), pero **otras comparan `= 'Confirmada'` y no cuentan las filas vacías** (por ejemplo, los reportes del profesional). **Problema ya detectado y sin tocar:** `PacienteProfesionalDAO.ObtenerIdPacientesDeProfesional` **no filtra `EstadoAsignacion`**: una asignación rechazada sigue contando como "tratante" ahí. Es anterior a este PR y afecta también a Admisión; conviene resolverlo en un PR aparte.
- **El límite de restablecimientos (10 cada 10 minutos) es por IP**, igual que el resto de los límites de la API, y se cuenta desde que arranca el servicio (en memoria). Si trabajan varias personas del equipo desde la misma red, comparten el contador.

---

## Qué se verificó y qué no

**Verificado en la PC (SQL Server 2019 Express, node 24, Chromium con Playwright):**

| Qué | Cómo |
|---|---|
| La migración 24 sobre una base con **la forma exacta de producción** | `MigracionDebeCambiarPasswordTests`: una base descartable armada con `docs/produccion/01-produccion.sql`, **sin la tabla de la migración 16** (54 tablas), con la 17, y sin la columna ni la versión 24. Después de la migración: la columna existe (`bit`, `NOT NULL`, restricción por defecto en 0), las cuentas que ya existían quedan en 0 con su contraseña intacta, una cuenta nueva sin decir nada queda en 0, la versión 24 queda registrada **una sola vez** con origen `Script`, el verificador de esquema la da por aplicada y **siguen 54 tablas**. La base se borra al terminar |
| Idempotencia | La migración corrida tres veces: una sola fila de versión, una sola columna, y un valor puesto a mano después (`DebeCambiarPassword = 1`) no se pisa |
| El camino de vuelta atrás de la base | `docs/produccion/deshacer-migracion-24.sql`: deja la base sin la columna, sin la restricción y sin la versión, con 54 tablas y todas las cuentas intactas; también es idempotente y la migración se puede volver a aplicar después |
| El esquema consolidado | `01-produccion.sql` trae el mismo bloque de la 24: el bloque aplicado a una base sin ella da el mismo resultado que la migración |
| La API contra la base | `UsuariosAdminBaseRealTests`: la lista (tipos, estados, bloqueada, sin cuenta, búsqueda por usuario, nombre, DNI, matrícula y mail, comodines tomados literales, paginación en el servidor, filtros inválidos, sin hash ni datos clínicos); restablecer (clave de 14 caracteres que cumple la política, mismo hash que el ingreso, `no-store`, desbloqueo, todos los tipos de cuenta, **nunca sobre uno mismo**, cuenta inexistente o inactiva, auditoría con quién y a quién **sin la clave**, la clave **nunca en los logs** ni en ningún registro de auditoría, límite de pedidos con 429); el ingreso (con la clave temporal no hay sesión, solo el token de cambio; **el token de cambio solo sirve en su ruta** y se rechaza en el resto; la clave mala o la cuenta bloqueada no lo reciben; la política, "distinta de la temporal", la sesión normal con sus módulos, la pestaña elegida, el mismo token no sirve dos veces, auditoría sin secretos); la asignación (varios profesionales, `UNIQUE`, todo o nada, paciente y profesionales activos, **auto-asignación rechazada**, quitar, auditoría, solo nombre y matrícula); y la **autorización**: CRM, Profesional, Profesional auditor, Paciente y Facturación reciben **403** en todo lo nuevo; sin sesión, con el token de selección o con el de cambio de clave, **401** |
| Las pantallas | `UsuariosPantallaTests` (cableado, que la clave no toca el almacenamiento del navegador, que el JavaScript compila) y **Chromium real contra una base descartable con la API real**: 51 verificaciones, todas bien (lista de 25 con paginación, búsqueda por nombre, matrícula, DNI y usuario, filtros de tipo y de estado, el filtro Sin cuenta, el menú de acciones, restablecer con la clave que se copia y desaparece del DOM al cerrar, el primer ingreso con el paso **Elegí tu clave nueva** y sus tres rechazos, la entrada a la pantalla del paciente, la clave temporal que deja de servir, la asignación y la baja de dos profesionales, la auditoría, y sin scroll horizontal ni errores de JavaScript en una ventana angosta). El script no se versiona |
| Resultados de las suites | **API en `yosoy_test`:** **2062 pasadas, 0 fallidas, 0 omitidas** (`Scripts\correr-tests.ps1`). **Forma de producción** (base nueva `crear-base-de-pruebas.ps1 -SinConsentimientoWhatsApp`: 54 tablas, con la 17): **2017 pasadas, 0 fallidas** (`correr-tests.ps1 -Base <base> -Filtro 'Tabla!=ConsentimientoWhatsApp'`). **Sin las migraciones 16 y 17** (`-SinConsentimientoWhatsApp -SinMigracion17`): **1982 pasadas, 0 fallidas** (`-Filtro 'Tabla!=ConsentimientoWhatsApp&Migracion!=17'`). **`whatsapp-service` (`npm test`):** 454 pruebas, 448 pasan, 0 fallan, 6 omitidas (sin cambios). Las bases nuevas se borraron al terminar |
| La API **anterior** con la base ya migrada (la vuelta atrás 7.1) | La batería de pruebas de `main` (`6e9c458`, sin esta mejora) contra una base de la forma de producción **con la migración 24 aplicada**: **1901 pasadas, 11 omitidas, 0 fallidas** (las 11 son las de siempre sin la tabla del consentimiento de WhatsApp y sin `node_modules` en una copia limpia). La API anterior no lee ni escribe la columna |
| La API **nueva** sin la migración (por qué el orden importa) | Contra una base de la forma de producción **sin** la columna: `POST /login` responde **500** con su código de error (el ingreso lee `DebeCambiarPassword`). Por eso la migración va antes y el paso 3.3 la comprueba |

**No verificado:**
- **Nada contra el SQL Server 2025 del VPS.** La migración es un `ALTER TABLE ... ADD ... DEFAULT` y un `INSERT`, T-SQL común; no se corrió allá.
- **Con los datos reales de producción.** Las pruebas usan bases con datos propios; el paso 5 es la comprobación a ojo con la cuenta de prueba.
- **El navegador del equipo (Safari, celulares).** Solo Chromium; el panel es de uso en PC.
- **La entrega de la clave temporal a la persona.** Es manual: el sistema no la manda por mail ni por WhatsApp. El paso de copiar y de pasarla por un medio seguro depende de quien restablece.
