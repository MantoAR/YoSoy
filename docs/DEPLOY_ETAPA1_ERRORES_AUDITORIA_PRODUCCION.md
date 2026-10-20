# Despliegue a producción: etapa 1 (errores con código y auditoría inalterable)

Runbook para llevar a producción la **etapa 1** de la integración de la rama `nuevo-circuito1` de Alearmas01: el registro de errores con código, la versión del esquema y la auditoría de solo agregar (migraciones **18** y **19**). Se
ejecuta **a mano, en dos máquinas** (la PC con PowerShell y el VPS por SSH), con el flujo de siempre de [`DEPLOY.md`](DEPLOY.md) y el mismo formato que [`DEPLOY_CHAT_PERSONAL_PRODUCCION.md`](DEPLOY_CHAT_PERSONAL_PRODUCCION.md). Escrito el
2026-10-20 contra la rama `integracion/nuevo-circuito1-etapa1` (sale de `main` en `4742d20`); **antes de armar el paquete tiene que estar mergeada en `main`** (paso 0).

- **Se escribió sin acceso al VPS y se ejecutó a mano el 20/10/2026** (base con 46 tablas, migraciones 18 y 19, verificador de esquema apagado, rollback en `/opt/yosoy/app-anterior-e1-20261020`; el estado, en [`SERVIDOR.md`](SERVIDOR.md)). Lo que se probó antes, y cómo, está en "Qué se verificó y qué no", al final; lo que no, también.
- **Sin secretos en este archivo.** Ningún comando imprime un secreto ni el contenido de un `.env`; si alguno lo hiciera, pará.
- Cada paso dice **dónde** se corre, el **comando exacto**, qué **esperar** y cómo **volver atrás**.

## Qué sale a producción (y qué no)

| Qué | Cómo queda |
|---|---|
| **Migración 18** (`add_registro_errores_version_esquema.sql`) | Tablas nuevas `VersionEsquema` (registra las migraciones 1 a 17 como detectadas) y `LogError`. Solo agrega: la base pasa de 44 a **46 tablas** |
| **Migración 19** (`add_auditoria_inalterable.sql`) | `LogAuditoria` gana `Categoria`, `Espacio`, `Hash` y `HashAnterior`; **tres triggers**: cada registro nuevo se encadena con el hash del anterior y **`LogAuditoria` y `LogError` ya no se pueden modificar ni borrar**. Las filas viejas de acceso a datos clínicos pasan a la categoría `Clinica` (es lo único que toca de lo existente, y es la última vez que se puede) |
| **API** | Errores con un **código corto** (`Código: 7F3A-91C2`) que el usuario ve en pantalla y soporte busca en `LogError`; archivo diario `logs/errores-AAAAMMDD.log` como respaldo. El verificador de migraciones **viene apagado** |
| Roles, permisos, módulos, alta por invitación | **No** (etapa 2). Nadie cambia de rol, el login es el de siempre |
| Circuito de atención (consentimientos opcionales, evolución por enfoque, interruptores por sala) | **No** (etapa 3) |
| Usuario `yosoy_app` endurecido (`DENY UPDATE, DELETE`) y cambio de cadena de conexión | **No**: paso posterior y separado, escrito al final (Apéndice A) |
| Migración 16 (consentimiento de WhatsApp) | **No hace falta**, sigue sin aplicarse (dormida). Este despliegue no la toca ni la da por hecha |

Lo que cambia para quien opera:
- **Una fila de `LogAuditoria` no se corrige ni se borra nunca más** (ni con SQL a mano: el trigger lo rechaza con el error 50018). Tampoco `LogError` (50019). Un usuario que figura en esa tabla **tampoco se puede borrar** (`FK_LogAuditoria_Usuario`, que ya existía: la
  diferencia es que antes se podían borrar primero sus filas de auditoría).
- Cada escritura de auditoría toma un bloqueo corto sobre la tabla mientras calcula el hash: se hacen de a una. Con el volumen de esta aplicación no se nota.
- Para depurar la auditoría vieja algún día hace falta un administrador de la base que desactive el trigger a mano (y eso queda en el registro de SQL Server). No hay plazo de conservación definido.

## Convenciones

- **[PC]** es PowerShell en tu PC. **[VPS]** es una sesión SSH que ya abriste (`ssh agustin@179.199.155.50`): pegá las líneas ahí. No uses `ssh servidor 'comando con sudo'`. Los comandos del VPS que **cambian algo** son **una sola línea** con
  `&&`: si un paso falla, los siguientes no corren. **Pegá un comando por vez**, y corré `sudo -v` antes de empezar.
- `FECHA` es **la de la PC** (`Get-Date -Format yyyyMMdd`). `TAG` es `e1`. Lo primero en la sesión SSH (y de nuevo si la cerrás):
  ```bash
  FECHA=YYYYMMDD; TAG=e1; export PATH="$PATH:/opt/mssql-tools18/bin"      # FECHA: la de la PC
  ```
- `<usuario-administrador>` es el login administrador de SQL Server (el que creó la base), **no** `yosoy_app` (que no puede crear tablas ni triggers). `sqlcmd` y `sudo` te piden la contraseña: no la escribas en el comando (`-P`).
- Las migraciones se corren **siempre con `-I -b -f 65001`**: `-I` (índices filtrados y triggers), `-b` (corta si algo falla) y `-f 65001` (acentos).

## El orden, de un vistazo

| Paso | Qué | Dónde | Corta el servicio | Se deshace |
|---|---|---|---|---|
| 0 | El código que vas a desplegar | PC | No | — |
| 1 | Backup de la base | PC y VPS | No | — (solo lee y copia) |
| 2 | Estado antes (base y auditoría) | VPS | No | — (solo lee) |
| 3 | Migraciones 18 y 19 | PC y VPS | No | Sí (3.5) |
| 4 | Publicar la API | PC y VPS | La API, unos segundos | Sí (4.4) |
| 5 | Verificar | VPS y navegador | No | — |
| 6 | Operación | VPS | — | — |
| A | **Para después:** `yosoy_app` sin permiso de modificar la auditoría | VPS | No | Sí |

**El orden importa: migraciones primero, API después.** Las dos migraciones son compatibles con la API anterior (que inserta en `LogAuditoria` como siempre y no conoce `LogError`), así que entre el paso 3 y el 4 nada se rompe; la API nueva, en cambio, no
necesita las migraciones para arrancar (si falta `LogError` solo escribe el error en el archivo y en el log del servicio), pero sin ellas no hay códigos en la base. Elegí un horario de poco uso (la API se reinicia).

---

## Paso 0. [PC] El código que vas a desplegar

```powershell
cd D:\Claude\YoSoy\YoSoy
git switch main; git pull --ff-only; git status --short; git log -1 --oneline
(Get-ChildItem migrations\add_registro_errores_version_esquema.sql, migrations\add_auditoria_inalterable.sql | Measure-Object).Count
$fecha = Get-Date -Format yyyyMMdd; $fecha                  # anotá el valor: es la FECHA que se usa en el VPS
New-Item -ItemType Directory -Force 'D:\Claude\Backups\yosoy-produccion' | Out-Null
```

**Esperado:** `git status` sin líneas; `git log -1` es el merge del PR de la etapa 1 o uno posterior; el conteo da `2`. Si da `0`, tu `main` todavía no la trae: mergeá el PR antes de seguir.

## Paso 1. Backup de la base

Es el mismo procedimiento del paso 1.1 de [`DEPLOY_CHAT_PERSONAL_PRODUCCION.md`](DEPLOY_CHAT_PERSONAL_PRODUCCION.md) (backup en el VPS con `CHECKSUM`, `RESTORE VERIFYONLY`, copia a tu PC con comparación de la huella SHA-256 y recién entonces borrado de las copias del servidor), con `TAG=e1`.
Resumido:

**[VPS]**

```bash
sudo mkdir -p /var/opt/mssql/backup && sudo chown mssql:mssql /var/opt/mssql/backup && sqlcmd -S localhost -U <usuario-administrador> -C -Q "BACKUP DATABASE [yosoy] TO DISK = N'/var/opt/mssql/backup/yosoy-pre-$TAG-$FECHA.bak' WITH CHECKSUM, INIT; RESTORE VERIFYONLY FROM DISK = N'/var/opt/mssql/backup/yosoy-pre-$TAG-$FECHA.bak' WITH CHECKSUM;"
```

```bash
sudo cp /var/opt/mssql/backup/yosoy-pre-$TAG-$FECHA.bak ~/ && sudo chown "$USER": ~/yosoy-pre-$TAG-$FECHA.bak && ls -la ~/yosoy-pre-$TAG-$FECHA.bak && sha256sum ~/yosoy-pre-$TAG-$FECHA.bak
```

**[PC]**

```powershell
scp "agustin@179.199.155.50:/home/agustin/yosoy-pre-e1-${fecha}.bak" 'D:\Claude\Backups\yosoy-produccion\'
Get-FileHash "D:\Claude\Backups\yosoy-produccion\yosoy-pre-e1-${fecha}.bak" -Algorithm SHA256; (Get-Item "D:\Claude\Backups\yosoy-produccion\yosoy-pre-e1-${fecha}.bak").Length
```

**Esperado:** `The backup set on file 1 is valid.`; la misma huella y el mismo tamaño que `sha256sum` y `ls -la`. **Recién entonces, [VPS]**, borrar las copias del servidor:

```bash
sudo rm -f /var/opt/mssql/backup/yosoy-pre-$TAG-$FECHA.bak ~/yosoy-pre-$TAG-$FECHA.bak && sudo ls -la /var/opt/mssql/backup/
```

**Por qué importa más que en otros despliegues:** la migración 19 vuelve de solo agregar a la auditoría, y **ese cambio de categoría de las filas viejas no se puede deshacer con SQL** (después del trigger ya no se puede modificar nada). Si algo saliera mal, el backup es la
única forma de volver a la categoría original. **Ojo:** el VPS tiene SQL Server 2025 y tu PC 2019: este `.bak` se restaura en el VPS, no en tu instancia.

## Paso 2. [VPS] Estado antes (todo de solo lectura)

```bash
sqlcmd -S localhost -d yosoy -U <usuario-administrador> -C -I -Q "SELECT (SELECT COUNT(*) FROM sys.tables) AS tablas, (SELECT COUNT(*) FROM sys.tables WHERE name IN ('VersionEsquema','LogError')) AS nuevas, (SELECT COUNT(*) FROM sys.triggers WHERE name LIKE 'TR[_]Log%') AS triggers, (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.LogAuditoria') AND name IN ('Categoria','Espacio','Hash','HashAnterior')) AS columnas_nuevas, (SELECT COUNT(*) FROM LogAuditoria) AS filas_de_auditoria"
```

**Esperado:** `44  0  0  0  <N>`. Anotá `N`: después tiene que ser `N` más lo que se haya registrado desde entonces (nunca menos). Si `tablas` da `46`, `nuevas` da `2` y `triggers` da `3`, las migraciones ya están aplicadas: seguí al paso 4.
Además, la huella de lo que ya está guardado (la migración no tiene que cambiar ni una de estas columnas):

```bash
sqlcmd -S localhost -d yosoy -U <usuario-administrador> -C -I -h -1 -W -Q "SET NOCOUNT ON; SELECT CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', (SELECT IdLog, IdUsuario, Accion, Entidad, IdEntidad, Detalle, FechaHora FROM LogAuditoria WHERE IdLog <= (SELECT MAX(IdLog) FROM LogAuditoria) ORDER BY IdLog FOR XML PATH)), 2)"
```

Anotala. Si entre el antes y el después entran registros nuevos la huella cambia: para compararla, corré el "después" (paso 5.1) con el **mismo** tope: reemplazá `(SELECT MAX(IdLog) FROM LogAuditoria)` por el número de `IdLog` máximo que tenía la base *antes* (consultalo con
`SELECT MAX(IdLog) FROM LogAuditoria` y anotalo ahora). Si no querés compararla, no pasa nada: la migración no modifica esas columnas (se comprobó en el ensayo).

## Paso 3. Migraciones 18 y 19

### 3.1 [PC] Subir los archivos

```powershell
scp D:\Claude\YoSoy\YoSoy\migrations\add_registro_errores_version_esquema.sql D:\Claude\YoSoy\YoSoy\migrations\add_auditoria_inalterable.sql agustin@179.199.155.50:/home/agustin/
```

### 3.2 [VPS] Aplicar la 18 (idempotente)

```bash
sqlcmd -S localhost -d yosoy -U <usuario-administrador> -C -I -b -f 65001 -i ~/add_registro_errores_version_esquema.sql
```

**Esperado:** `Migración 18 aplicada.`, una tabla de **migraciones obligatorias faltantes que sale vacía** (`0 rows affected`) y la lista de versiones: 1 a 15 y 17 como `Detectada`, la 18 como `Script`, **sin la 16** (la tabla de WhatsApp no existe: es lo esperado). Si la tabla de faltantes trae
alguna fila, **pará**: esa migración anterior no está aplicada en producción; decime cuál antes de seguir.

### 3.3 [VPS] Aplicar la 19 (idempotente)

```bash
sqlcmd -S localhost -d yosoy -U <usuario-administrador> -C -I -b -f 65001 -i ~/add_auditoria_inalterable.sql
```

**Esperado:** `Migración 19 aplicada.` y sin errores. **Desde este momento la auditoría ya es de solo agregar.** Volver a correr el 3.2 y el 3.3 es seguro: no cambian nada.

### 3.4 [VPS] Comprobar

```bash
sqlcmd -S localhost -d yosoy -U <usuario-administrador> -C -I -W -Q "SELECT (SELECT COUNT(*) FROM sys.tables) AS tablas, (SELECT COUNT(*) FROM sys.triggers WHERE name IN ('TR_LogAuditoria_Encadenar','TR_LogAuditoria_SoloAgregar','TR_LogError_SoloAgregar')) AS triggers, (SELECT COUNT(*) FROM VersionEsquema) AS versiones, (SELECT COUNT(*) FROM LogAuditoria WHERE Categoria = 'Clinica') AS clinicas, (SELECT COUNT(*) FROM LogAuditoria) AS filas_de_auditoria; EXEC sp_VerificarCadenaAuditoria"
```

**Esperado:** `46  3  18  <C>  <N>` (tablas, triggers, versiones, filas ya clasificadas como clínicas, filas de auditoría: `N` o más) y, de la verificación, `Íntegra  0  NULL  NULL` (0 registros revisados: los anteriores no tienen hash; desde ahora cada uno se encadena). La API anterior sigue
andando igual: cada ingreso o acción que audite entra con su hash.

### 3.5 Volver atrás (solo las migraciones)

**Normalmente no hace falta**: la API anterior inserta en `LogAuditoria` sin problemas con las dos migraciones aplicadas (se probó). Si de verdad hubiera que sacar la protección (por ejemplo, para corregir algo en la auditoría con un administrador), con el backup del paso 1 hecho:

```bash
sqlcmd -S localhost -d yosoy -U <usuario-administrador> -C -I -b -Q "DROP TRIGGER TR_LogAuditoria_SoloAgregar; DROP TRIGGER TR_LogError_SoloAgregar; DROP TRIGGER TR_LogAuditoria_Encadenar; DROP PROCEDURE sp_VerificarCadenaAuditoria; DROP FUNCTION dbo.fn_HashAuditoria; DELETE FROM VersionEsquema WHERE Numero = 19"
```

Eso devuelve la auditoría a lo de antes (editable y sin hash nuevo); las columnas `Categoria`, `Espacio`, `Hash` y `HashAnterior` pueden quedar (la API anterior no las usa), y **la reclasificación de las filas viejas a `Clinica` no se deshace**
(solo restaurando el backup). Para sacar también las tablas nuevas: `DROP TABLE dbo.LogError; DROP TABLE dbo.VersionEsquema;`. Se pierde lo registrado ahí, ningún dato de negocio. Probado en el ensayo.

## Paso 4. Publicar la API

### 4.1 [PC] Publicar y subir (el flujo de siempre: autocontenido, fuera del repositorio)

```powershell
cd D:\Claude\YoSoy\YoSoy
$fecha = Get-Date -Format yyyyMMdd; $tag = 'e1'
dotnet publish YoSoy.csproj -c Release -r linux-x64 --self-contained true -o "D:\Claude\Publish-YoSoy-${fecha}-${tag}"
tar -czf "$env:TEMP\Publish-YoSoy-${fecha}-${tag}.tar.gz" -C "D:\Claude\Publish-YoSoy-${fecha}-${tag}" .
scp "$env:TEMP\Publish-YoSoy-${fecha}-${tag}.tar.gz" agustin@179.199.155.50:/home/agustin/
```

**Esperado:** el `publish` termina sin errores (los avisos de nulabilidad `CS86xx` son normales); el paquete trae el binario `YoSoy`, `appsettings.json` y `PANTALLAS\`, y ningún `.env`.

### 4.2 [VPS] Desempaquetar y comprobar que está completo

```bash
test -f ~/Publish-YoSoy-$FECHA-$TAG.tar.gz && mkdir ~/Publish-YoSoy-$FECHA-$TAG && tar -xzf ~/Publish-YoSoy-$FECHA-$TAG.tar.gz -C ~/Publish-YoSoy-$FECHA-$TAG && ls -la ~/Publish-YoSoy-$FECHA-$TAG/YoSoy ~/Publish-YoSoy-$FECHA-$TAG/appsettings.json
```

**Esperado:** dos archivos listados. Si el `.tar.gz` no está o el `mkdir` dice que ya existe, la `FECHA` está mal o quedó algo de antes: **no sigas**.

### 4.3 [VPS] Backup `app-anterior-<tag>-<fecha>`, corte y arranque

```bash
test -f ~/Publish-YoSoy-$FECHA-$TAG/YoSoy && sudo test ! -e /opt/yosoy/app-anterior-$TAG-$FECHA && sudo cp -a /opt/yosoy/app /opt/yosoy/app-anterior-$TAG-$FECHA && sudo diff -rq /opt/yosoy/app /opt/yosoy/app-anterior-$TAG-$FECHA && echo "backup idéntico" && sudo systemctl stop yosoy-api && sudo find /opt/yosoy/app -mindepth 1 -delete && sudo cp -a ~/Publish-YoSoy-$FECHA-$TAG/. /opt/yosoy/app/ && sudo chown -R yosoy:yosoy /opt/yosoy/app && sudo chmod +x /opt/yosoy/app/YoSoy && sudo systemctl start yosoy-api && sleep 6 && systemctl is-active yosoy-api && curl -s -o /dev/null -w "%{http_code} %{redirect_url}\n" http://localhost:5000/
```

**Esperado:** `backup idéntico`, `active` y `302 http://localhost:5000/pantallas/index.html`. La cadena se frena **antes** de parar la API si el publish no está desempaquetado, si el backup ya existe o si `diff -rq` encuentra diferencias.
Si se corta **después** de `systemctl stop`, la API quedó parada: corré el rollback (4.4). **No hace falta tocar el `.env`**: esta etapa no define variables nuevas.

### 4.4 Rollback de la API

```bash
sudo test -d /opt/yosoy/app-anterior-$TAG-$FECHA && sudo systemctl stop yosoy-api && sudo find /opt/yosoy/app -mindepth 1 -delete && sudo cp -a /opt/yosoy/app-anterior-$TAG-$FECHA/. /opt/yosoy/app/ && sudo chown -R yosoy:yosoy /opt/yosoy/app && sudo systemctl start yosoy-api && sleep 6 && systemctl is-active yosoy-api && curl -s -o /dev/null -w "%{http_code} %{redirect_url}\n" http://localhost:5000/
```

**Esperado:** `active` y `302 ...`. Las migraciones no se deshacen con esto (3.5): la API anterior anda con ellas aplicadas. Los `app-anterior-*` y el `Publish-YoSoy-*` del home no se borran hasta pasados unos días.

## Paso 5. Verificar

### 5.1 [VPS] La API y la base

```bash
sudo journalctl -u yosoy-api --since '5 minutes ago' --no-pager | grep -iE 'error|exception|fail|Zona horaria|Esquema'
```

**Esperado:** la línea `Zona horaria de la clínica: ...` y **ninguna excepción**. No tiene que aparecer ninguna línea de `Esquema` (el verificador está apagado).

Entrá al sitio (como cualquier usuario) y después:

```bash
sqlcmd -S localhost -d yosoy -U <usuario-administrador> -C -I -W -Q "SELECT TOP 3 IdLog, Accion, Categoria, CASE WHEN Hash IS NULL THEN 'sin hash' ELSE 'con hash' END AS hash FROM LogAuditoria ORDER BY IdLog DESC; EXEC sp_VerificarCadenaAuditoria"
```

**Esperado:** los registros nuevos (el ingreso que acabás de hacer, si tu versión de login audita, o cualquier otra acción auditada, como abrir una historia clínica) aparecen **con hash**, y la verificación dice `Íntegra` con un número de registros revisados mayor que 0.

### 5.2 [VPS] La inmutabilidad (una prueba que no toca nada real)

```bash
sqlcmd -S localhost -d yosoy -U <usuario-administrador> -C -I -W -Q "UPDATE LogAuditoria SET Detalle = Detalle WHERE IdLog = (SELECT MAX(IdLog) FROM LogAuditoria)"
```

**Esperado:** el error `Msg 50018 ... RN-18: el registro de auditoría es de solo lectura. No se puede modificar ni borrar.` y nada más. Si **no** da error, el trigger no quedó: volvé al 3.3.

### 5.3 [VPS] Un error con código (opcional)

Un error 500 real no se provoca a propósito. Si alguien ve en pantalla `Código: XXXX-XXXX`, soporte lo busca así:

```bash
sqlcmd -S localhost -d yosoy -U <usuario-administrador> -C -I -W -Q "SELECT FechaHora, Metodo, Ruta, StatusCode, TipoError, Mensaje FROM LogError WHERE Codigo = 'XXXX-XXXX'"
```

(`Detalle` trae la traza, con DNI, CUIL, teléfonos y correos enmascarados.) Si la base no responde en ese momento, el mismo código está en `/opt/yosoy/app/logs/errores-AAAAMMDD.log` (una línea JSON por error; ojo que esa carpeta se reemplaza en cada despliegue, la
anterior queda en `app-anterior-*`). Para que el archivo viva fuera de la carpeta de la app: crear una carpeta (`sudo install -d -o yosoy -g yosoy /opt/yosoy/logs`) y definir `RegistroErrores__Carpeta=/opt/yosoy/logs` en el `.env`; no hace falta para este despliegue.

### 5.4 [PC] Desde internet

```powershell
curl.exe -s -o NUL -w "%{http_code}`n" https://yosoytelemedicina.tech/pantallas/index.html
```

**Esperado:** `200`. Entrá con tu usuario y recorré una pantalla que audite (por ejemplo, abrir un paciente): tiene que seguir funcionando igual que antes.

## Paso 6. Operación

- **Verificar la cadena** cuando quieras: `EXEC sp_VerificarCadenaAuditoria;` (devuelve `Íntegra` o `Rota` con el primer registro que falla y por qué).
- **Un usuario que ya actuó no se puede borrar** (tiene filas en `LogAuditoria`). Si hay que darlo de baja, se pasa a `Estado = 'Inactivo'` (lo que hace la pantalla de usuarios), no se borra.
- **Prender el aviso de migraciones faltantes** (opcional, más adelante): definir `Esquema__Verificar=true` en el `.env` y reiniciar. Con la base completa no cambia nada; si falta una migración obligatoria, **toda la API (menos `/pantallas`) responde 503** con el detalle de cuál falta,
  y se recupera sola unos 30 segundos después de aplicarla. Por eso viene apagado.

---

## Apéndice A. Para después, aparte: `yosoy_app` sin permiso de modificar la auditoría

**No forma parte de este despliegue.** Hoy `yosoy_app` es `db_datareader` + `db_datawriter` (`docs/produccion/02-usuario-app.sql`) y **no** puede crear ni alterar objetos ni desactivar triggers, así que los triggers de la migración 19 ya lo frenan. Lo que agrega este
paso es una segunda capa: un `DENY` a nivel de permiso, por si algún día el usuario de la API tuviera más permisos que hoy. La rama de Alearmas01 trae un script (`Scripts/crear_usuario_aplicacion.sql`) que además **crea el login con otra contraseña y pide cambiar la cadena de conexión**; como en
producción `yosoy_app` ya existe y la API ya se conecta con él, **no hace falta nada de eso**: alcanza con los `DENY`. Cuando lo decidas:

**[VPS]** Estado antes (qué permisos tiene hoy):

```bash
sqlcmd -S localhost -d yosoy -U <usuario-administrador> -C -I -W -Q "SELECT dp.name AS usuario, o.name AS objeto, p.permission_name AS permiso, p.state_desc AS estado FROM sys.database_permissions p JOIN sys.database_principals dp ON dp.principal_id = p.grantee_principal_id LEFT JOIN sys.objects o ON o.object_id = p.major_id WHERE dp.name = 'yosoy_app'; SELECT r.name AS rol FROM sys.database_role_members m JOIN sys.database_principals r ON r.principal_id = m.role_principal_id JOIN sys.database_principals u ON u.principal_id = m.member_principal_id WHERE u.name = 'yosoy_app'"
```

**Esperado:** los roles `db_datareader` y `db_datawriter`, y ningún permiso suelto. Si apareciera `db_owner` o `db_ddladmin`, ese es el problema real: con esos roles `yosoy_app` sí podría desactivar un trigger.

**[VPS]** Aplicar (con el login administrador; no cambia contraseñas ni la cadena de conexión):

```bash
sqlcmd -S localhost -d yosoy -U <usuario-administrador> -C -I -b -Q "DENY UPDATE, DELETE ON dbo.LogAuditoria TO yosoy_app; DENY UPDATE, DELETE ON dbo.LogError TO yosoy_app; DENY UPDATE, DELETE ON dbo.VersionEsquema TO yosoy_app; GRANT VIEW DEFINITION TO yosoy_app"
```

(`VIEW DEFINITION` solo lo necesita el verificador de migraciones, si algún día lo prendés: busca tablas, columnas y triggers por nombre.) **Comprobar** que la API sigue funcionando (el paso 5.1: un ingreso nuevo tiene que quedar con hash) y que `yosoy_app` no puede modificar:

```bash
sqlcmd -S localhost -d yosoy -U <usuario-administrador> -C -I -W -Q "EXECUTE AS USER = 'yosoy_app'; UPDATE LogAuditoria SET Detalle = Detalle WHERE IdLog = 0; REVERT"
```

**Esperado:** `The UPDATE permission was denied on the object 'LogAuditoria'`. **Volver atrás:** `REVOKE UPDATE, DELETE ON dbo.LogAuditoria FROM yosoy_app;` (y lo mismo para `LogError` y `VersionEsquema`).

---

## Qué se verificó y qué no

**Verificado en la PC (SQL Server 2019 Express), antes de escribir esto:**

| Qué | Cómo |
|---|---|
| Las migraciones sobre una base con la **forma de producción** (44 tablas, la 17 aplicada, sin la 16) con 8 filas de auditoría de ejemplo | Una base descartable armada con el `01-produccion.sql` de `main`, borrada al terminar. Resultado: 46 tablas, 3 triggers, 18 versiones, 6 filas pasaron a `Clinica`, la huella de las columnas originales de `LogAuditoria` **idéntica** antes y después, la 18 no informó migraciones faltantes |
| Idempotencia | Las dos migraciones corridas dos veces, sin errores ni filas duplicadas |
| La API anterior sigue andando con las migraciones aplicadas | `INSERT` de una fila y de varias a la vez con las columnas de siempre: quedan con hash y encadenadas |
| El trigger de solo agregar y la cadena | `UPDATE` y `DELETE` rechazados con 50018; con el trigger desactivado a mano, `sp_VerificarCadenaAuditoria` detecta el cambio y el borrado (`Rota`), y vuelve a `Íntegra` al deshacer el cambio |
| El rollback del 3.5 | Corrido en el ensayo: la auditoría vuelve a ser editable, el `INSERT` simple anda y `DROP TABLE` de las dos tablas deja 44; después se volvieron a aplicar las dos migraciones sin errores |
| El Apéndice A | Probado en la base de ensayo con un usuario sin login y los mismos roles que `yosoy_app`: antes del `DENY`, el trigger ya rechazaba el `UPDATE` (50018) y el usuario no podía desactivarlo (error 1088); después, el `UPDATE` da el 229 (`permission was denied`) y el `INSERT` sigue andando |
| La suite de la API, con la forma de la base de `yosoy_test` (47 tablas, con migraciones 18 y 19) | `dotnet test YoSoy.Tests` con `ConnectionStrings__YoSoy` apuntando a `yosoy_test` y `Email__Password` en un espacio (es lo que arma `Scripts\correr-tests.ps1 -Base yosoy_test`): **1848 de 1848** |
| La suite de la API, con la forma de producción (44 tablas + las 2 nuevas = 46, sin la 16, con la 17) | `.\Scripts\crear-base-de-pruebas.ps1 -Base yosoy_test_prod -SinConsentimientoWhatsApp` y `dotnet test YoSoy.Tests --filter 'Tabla!=ConsentimientoWhatsApp'` con la variable apuntando a esa base (`Scripts\correr-tests.ps1 -Base yosoy_test_prod -Filtro 'Tabla!=ConsentimientoWhatsApp'`): **1803 de 1803** (la base se borró después) |
| `whatsapp-service` | `cd whatsapp-service; npm test`: 454 pruebas, 448 pasan, 0 fallan, 6 omitidas (sin cambios en esta etapa) |

**No verificado:**
- **Nada contra el SQL Server 2025 del VPS** (la PC tiene 2019). Las migraciones usan solo `CREATE OR ALTER`, `HASHBYTES`, T-SQL común, triggers `INSTEAD OF` y un cursor: nada específico de una versión, pero no se corrió allá.
- Cómo se comporta el bloqueo del trigger de encadenado con la **carga real**: en la PC se probaron 20 escrituras simultáneas (cadena íntegra, ningún antecesor repetido).
- Cuántas filas tiene hoy `LogAuditoria` en producción y cuánto tarda la migración 19 en reclasificarlas (es un `UPDATE` único sobre la tabla; con decenas de miles de filas, segundos).
- El permiso de escritura de la carpeta `logs/` bajo `/opt/yosoy/app` para el usuario `yosoy`: si no pudiera escribir, la API lo avisa en su log y sigue (el código lo captura); los códigos igual van a `LogError`.
- Que `yosoy_app` hoy tenga exactamente los roles que dice `docs/SERVIDOR.md` (el Apéndice A empieza por comprobarlo).
