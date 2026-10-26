# Despliegue a producción: cierre de sesión (el panel de después de la videollamada)

Runbook para llevar a producción el **cierre de sesión**: al terminar la videollamada, el profesional confirma que la sesión se realizó, el turno pasa a **Realizado**, el caso pasa a **En atención** si era la primera sesión y el panel le ofrece cómo seguir (seguimiento, alta o derivación) y agendar el próximo turno. Qué hace y su contrato: [`CIERRE_SESION.md`](CIERRE_SESION.md). Cambia **la base (migración 26)**, **la API** y **las pantallas** (van dentro del paquete). Se ejecuta **a mano, en dos máquinas** (la PC con PowerShell y el VPS por SSH), con el flujo de siempre de [`DEPLOY.md`](DEPLOY.md) y el mismo formato que [`DEPLOY_CHATS_VISTA_PRODUCCION.md`](DEPLOY_CHATS_VISTA_PRODUCCION.md). Escrito el 2026-10-26 contra la rama `feat/cierre-sesion`; **antes de armar el paquete tiene que estar mergeada en `main`** (paso 0), junto con la rama de la que sale (`fix/sala-actividad-inactividad`).

- **Se escribió sin acceso al VPS y no se ejecutó.** Lo que se probó, y cómo, está al final.
- **Sin secretos en este archivo.** Las contraseñas las escribís vos cuando el comando las pide (nunca en la línea de comandos).
- Cada paso dice **dónde** se corre, el **comando exacto**, qué **esperar** y cómo **volver atrás**.

## Qué sale a producción (y qué no)

| Qué | Cómo queda |
|---|---|
| **Migración 26** (`add_cierre_sesion.sql`) | Tres columnas nuevas en `SesionClinica` (`ClaveCierre`, `TurnoRealizadoUtc`, `IdEvolucion`) y tres índices filtrados. Aditiva, idempotente, en una transacción; **no toca ninguna fila existente**. Registra la versión 26 (origen `Script`). **Opcional para la API, obligatoria para usar el cierre nuevo** |
| **API** | `POST /sesiones-clinicas` acepta `claveCierre` y `confirmaRealizacion` (transacción única, idempotente, auditada); `PUT /sesiones-clinicas/{id}/evolucion` y `GET .../evoluciones-vinculables` (vínculo manual); las lecturas de sesiones devuelven `idEvolucion`. **Sin esos campos, todo igual que antes** |
| **Pantallas** | La sala del profesional: panel de cierre, selector de próximo turno, borrador local cifrado, vínculo con una evolución; el aviso de actividad que evita que la sesión del panel venza durante la llamada |
| **No cambia** | Paciente, CRM, Configurador, la sala del paciente, el consentimiento y las emociones, `whatsapp-service`, Caddy, variables de entorno. **No hay variables nuevas** |

## Lo que cambia para el equipo (avisalo antes)

- Al **finalizar la sesión** aparece el panel de cierre: hay que elegir si **se realizó**, y recién ahí el turno pasa a Realizado. **«Decidir después» no guarda nada.**
- Con **Alta** o **Derivación** el panel solo deja registrado el tipo y avisa que falta completarlo desde *Mis pacientes* / *Casos*: **todavía no hay asistente de alta ni formulario de derivación**.
- **«Agendar próximo turno»** envía una **propuesta**: el paciente tiene que aceptarla.
- Mientras escriben la nota, queda un **borrador cifrado en su equipo** (no en el servidor). Si cambian de equipo o de navegador, no lo ven.

## Convenciones

- **[PC]** es PowerShell en tu PC. **[VPS]** es una sesión SSH que ya abriste (`ssh agustin@179.199.155.50`). Los comandos del VPS que **cambian algo** son **una sola línea** con `&&`. **Pegá un comando por vez**, y corré `sudo -v` antes de empezar.
- `FECHA` es la de la PC (`Get-Date -Format yyyyMMdd`). `TAG` es `cs`. Lo primero en la sesión SSH:
  ```bash
  FECHA=YYYYMMDD; TAG=cs; export PATH="$PATH:/opt/mssql-tools18/bin"      # FECHA: la de la PC
  ```

## El orden, de un vistazo

| Paso | Qué | Dónde | Corta el servicio | Se deshace |
|---|---|---|---|---|
| 0 | El código que vas a desplegar | PC | No | — |
| 1 | Backup de la base (verificado) | VPS y PC | No | — |
| 2 | **Migración 26** (antes del paquete) | PC y VPS | No | Sí (7.2) |
| 3 | Publicar y subir la API | PC y VPS | La API, unos segundos | Sí (7.1) |
| 4 | Verificar | VPS | No | — |
| 5 | Prueba en el navegador | Navegador | No | — |
| 6 | Sesiones abiertas | — | — | — |
| 7 | Rollback | VPS | La API, unos segundos | — |

**Aplicá la migración antes de publicar el paquete.** La API degrada bien al revés (si se publica primero, el cierre nuevo responde «falta aplicar la migración 26» y la sala lo muestra), pero el panel no podría guardar hasta que la 26 esté.

## Paso 0. [PC] El código que vas a desplegar

```powershell
cd D:\Claude\YoSoy\YoSoy
git switch main; git pull --ff-only; git status --short; git log -1 --oneline
(Get-ChildItem Services\CierreSesionReglas.cs, DAOs\CierreSesionDAO.cs, Endpoints\CierreSesionEndpoints.cs, PANTALLAS\shared\borrador.js, migrations\add_cierre_sesion.sql | Measure-Object).Count
Select-String -Path Services\VerificadorEsquema.cs -Pattern 'add_cierre_sesion.sql' -Quiet
$fecha = Get-Date -Format yyyyMMdd; $fecha                  # anotá el valor: es la FECHA que se usa en el VPS
```

**Esperado:** `git status` sin líneas; el conteo da `5` y `Select-String` da `True`. Si no, tu `main` todavía no la trae: mergeá el PR antes de seguir.

## Paso 1. Backup de la base (verificado antes de tocar nada)

Es el procedimiento del paso 1 de [`DEPLOY_USUARIOS_PRODUCCION.md`](DEPLOY_USUARIOS_PRODUCCION.md) (backup con `CHECKSUM`, verificación, copia a tu PC con comparación de la huella SHA-256 y recién entonces borrado de las copias del servidor). **El backup queda solo en tu PC, fuera de cualquier repositorio.**

**[VPS]**
```bash
sudo mkdir -p /var/opt/mssql/backup && sudo chown mssql:mssql /var/opt/mssql/backup && sqlcmd -S localhost -U sa -C -Q "BACKUP DATABASE [yosoy] TO DISK = N'/var/opt/mssql/backup/yosoy-pre-$TAG-$FECHA.bak' WITH CHECKSUM, INIT; RESTORE VERIFYONLY FROM DISK = N'/var/opt/mssql/backup/yosoy-pre-$TAG-$FECHA.bak' WITH CHECKSUM;"
```

**Esperado:** `The backup set on file 1 is valid.` Si no dice **valid**, **no sigas**.

## Paso 2. Migración 26

### 2.1 [PC] Subir el archivo

```powershell
scp D:\Claude\YoSoy\YoSoy\migrations\add_cierre_sesion.sql agustin@179.199.155.50:/home/agustin/
```

### 2.2 [VPS] Aplicarla (idempotente, en una transacción)

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -b -f 65001 -i ~/add_cierre_sesion.sql
```

**Esperado:** `Migracion 26 aplicada.` y una fila con la cantidad de sesiones y `con_clave = NULL` (ninguna tiene clave todavía). Volver a correrla es seguro. Si falla a la mitad, no queda nada aplicado.

### 2.3 [VPS] Comprobar (si esto no da lo esperado, **no publiques la API**)

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -W -Q "SELECT (SELECT MAX(Numero) FROM VersionEsquema) AS ultima_version, (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SesionClinica') AND name IN ('ClaveCierre','TurnoRealizadoUtc','IdEvolucion')) AS columnas, (SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.SesionClinica') AND name IN ('UX_SesionClinica_Clave','UX_SesionClinica_Evolucion','IX_SesionClinica_Turno')) AS indices, (SELECT COUNT(*) FROM SesionClinica WHERE ClaveCierre IS NOT NULL OR TurnoRealizadoUtc IS NOT NULL OR IdEvolucion IS NOT NULL) AS filas_con_datos_nuevos"
```

**Esperado:** `26  3  3  0`.

## Paso 3. Publicar la API y las pantallas

### 3.1 [PC] Publicar y subir (el flujo de siempre: autocontenido, fuera del repositorio; se publica el proyecto, **no** la solución)

```powershell
cd D:\Claude\YoSoy\YoSoy
$fecha = Get-Date -Format yyyyMMdd; $tag = 'cs'
dotnet publish YoSoy.csproj -c Release -r linux-x64 --self-contained true -o "D:\Claude\Publish-YoSoy-${fecha}-${tag}"
tar -czf "$env:TEMP\Publish-YoSoy-${fecha}-${tag}.tar.gz" -C "D:\Claude\Publish-YoSoy-${fecha}-${tag}" .
scp "$env:TEMP\Publish-YoSoy-${fecha}-${tag}.tar.gz" agustin@179.199.155.50:/home/agustin/
```

**Esperado:** el `publish` termina sin errores (los avisos de nulabilidad `CS86xx` son normales); el paquete trae el binario `YoSoy`, `appsettings.json` y `PANTALLAS\` (con `shared\borrador.js`), y ningún `.env`.

### 3.2 [VPS] Desempaquetar y comprobar que está completo

```bash
test -f ~/Publish-YoSoy-$FECHA-$TAG.tar.gz && mkdir ~/Publish-YoSoy-$FECHA-$TAG && tar -xzf ~/Publish-YoSoy-$FECHA-$TAG.tar.gz -C ~/Publish-YoSoy-$FECHA-$TAG && ls -la ~/Publish-YoSoy-$FECHA-$TAG/YoSoy ~/Publish-YoSoy-$FECHA-$TAG/PANTALLAS/shared/borrador.js
```

**Esperado:** dos archivos listados.

### 3.3 [VPS] Backup `app-anterior-<tag>-<fecha>`, corte y arranque

```bash
test -f ~/Publish-YoSoy-$FECHA-$TAG/YoSoy && sudo test ! -e /opt/yosoy/app-anterior-$TAG-$FECHA && sudo cp -a /opt/yosoy/app /opt/yosoy/app-anterior-$TAG-$FECHA && sudo diff -rq /opt/yosoy/app /opt/yosoy/app-anterior-$TAG-$FECHA && echo "backup idéntico" && sudo systemctl stop yosoy-api && sudo find /opt/yosoy/app -mindepth 1 -delete && sudo cp -a ~/Publish-YoSoy-$FECHA-$TAG/. /opt/yosoy/app/ && sudo chown -R yosoy:yosoy /opt/yosoy/app && sudo chmod +x /opt/yosoy/app/YoSoy && sudo systemctl start yosoy-api && sleep 6 && systemctl is-active yosoy-api && curl -s -o /dev/null -w "%{http_code} %{redirect_url}\n" http://localhost:5000/
```

**Esperado:** `backup idéntico`, `active` y `302 .../pantallas/index.html`. Si se corta **después** de `systemctl stop`, la API quedó parada: corré el rollback (7.1). **No hace falta tocar el `.env`.**

## Paso 4. Verificar

### 4.1 [VPS] El servicio

```bash
sudo journalctl -u yosoy-api --since '5 minutes ago' --no-pager | grep -iE 'error|exception|fail|Hosting environment'; systemctl is-active whatsapp-service
```

**Esperado:** `Hosting environment: Production`, **ninguna excepción** y `active` para `whatsapp-service` (no se tocó).

### 4.2 [VPS] Lo nuevo exige sesión (solo lectura, sin sesión)

```bash
curl -s -o /dev/null -w "cierre sin sesion: %{http_code}\n" -X POST http://localhost:5000/sesiones-clinicas; curl -s -o /dev/null -w "vinculo sin sesion: %{http_code}\n" -X PUT http://localhost:5000/sesiones-clinicas/1/evolucion; curl -s -o /dev/null -w "vinculables sin sesion: %{http_code}\n" http://localhost:5000/sesiones-clinicas/1/evoluciones-vinculables; curl -s -o /dev/null -w "borrador.js: %{http_code}\n" http://localhost:5000/pantallas/shared/borrador.js
```

**Esperado:** `401` las tres primeras y `200` la última.

## Paso 5. [Navegador] Prueba con un paciente de prueba

Con un profesional y **un paciente de prueba** (no uses uno real): un turno **Confirmado** de hoy con sala, entrá a la sala, hablá un minuto y **Finalizar sesión**. Esperado, en este orden:

1. Aparece el panel de cierre; **ninguna opción viene elegida** y *Guardar cierre* está apagado hasta decidir.
2. **Sí, se realizó + Seguimiento**: *Agendar próximo turno* queda destacado y abre el selector con el paciente cargado. *Guardar cierre* → el resumen dice «El turno quedó como Realizado» (y, si era la primera sesión con un caso Asignado, «el caso pasó a En atención»). En *Inicio*, el turno figura Realizado.
3. Tocar *Guardar cierre* de nuevo no se puede; recargar y repetir el mismo cierre no duplica nada (una sola sesión en la historia).
4. **Decidir después** en otro turno: no cambia nada (el turno sigue Confirmado).
5. Un turno **Pendiente** o **Cancelado**: el servidor lo rechaza con un mensaje claro y no guarda nada.
6. Escribir una nota, **recargar** la página sin guardar: ofrece el borrador («Recuperaste un borrador…»).

## Paso 6. Sesiones abiertas

Quien tenga la sala abierta con la versión anterior sigue con el panel viejo hasta que recargue (Ctrl+F5). El servidor acepta los dos.

## Paso 7. Rollback

### 7.1 [VPS] Volver a la API anterior (la base puede quedar como está: la API anterior ignora las columnas nuevas)

```bash
sudo systemctl stop yosoy-api && sudo find /opt/yosoy/app -mindepth 1 -delete && sudo cp -a /opt/yosoy/app-anterior-$TAG-$FECHA/. /opt/yosoy/app/ && sudo chown -R yosoy:yosoy /opt/yosoy/app && sudo systemctl start yosoy-api && sleep 6 && systemctl is-active yosoy-api
```

### 7.2 [VPS] Sacar la migración (solo si hace falta; **después** de 7.1)

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -b -f 65001 -i ~/deshacer-migracion-26.sql
```

Antes: `scp D:\Claude\YoSoy\YoSoy\docs\produccion\deshacer-migracion-26.sql agustin@179.199.155.50:/home/agustin/`. Pierde las claves de cierre y los vínculos sesión-evolución; **no** cambia el estado de ningún turno ni caso (eso ya es historia clínica). El script cuenta lo que se pierde antes de borrarlo.

## Qué se verificó y qué no

- **Verificado (contra `yosoy_test` y con navegadores reales):** la API completa (feliz, idempotencia, concurrencia, rechazos, permisos, atomicidad con fallas provocadas), la migración (idempotencia, deshacer, forma de producción), el cifrado del borrador y el panel con Chromium (la API simulada). Detalle en [`CIERRE_SESION.md`](CIERRE_SESION.md).
- **No verificado:** nada contra el VPS ni contra la base `yosoy`; una videollamada real de punta a punta con dos personas; el panel en Firefox/Safari (el borrador usa IndexedDB y WebCrypto: si el navegador no los permite, la pantalla lo dice).
