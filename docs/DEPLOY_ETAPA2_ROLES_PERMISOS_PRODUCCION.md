# Despliegue a producción: etapa 2 (roles, permisos, módulos y alta de equipo por invitación)

Runbook para llevar a producción la **etapa 2** de la integración de la rama `nuevo-circuito1` de Alearmas01: el acceso del equipo pasa a salir de **roles, permisos y privilegios** (migraciones **20 a 23**), el ingreso con **módulos** (Configurador, CRM, Profesional, Facturación) y el **alta de usuarios del equipo por invitación**. Se ejecuta **a mano, en dos máquinas** (la PC con PowerShell y el VPS por SSH), con el flujo de siempre de [`DEPLOY.md`](DEPLOY.md) y el mismo formato que [`DEPLOY_ETAPA1_ERRORES_AUDITORIA_PRODUCCION.md`](DEPLOY_ETAPA1_ERRORES_AUDITORIA_PRODUCCION.md). Escrito el 2026-10-20 contra la rama `integracion/nuevo-circuito1-etapa2` (sale de `main` en `8086924`, con la etapa 1 ya desplegada); **antes de armar el paquete tiene que estar mergeada en `main`** (paso 0).

- **Se escribió sin acceso al VPS y se ejecutó a mano el 20/10/2026** (API desde `main` `9988d3c`, migraciones 20 a 23, 54 tablas, versión 23, cadena de auditoría `Íntegra`, rollback en `/opt/yosoy/app-anterior-e2-20261020`; el estado, en [`SERVIDOR.md`](SERVIDOR.md)). Lo que se probó antes, y cómo, está en "Qué se verificó y qué no", al final; lo que no, también.
- **Sin secretos en este archivo.** Ningún comando imprime un secreto ni el contenido de un `.env`; las contraseñas las escribís vos cuando el comando las pide (nunca en la línea de comandos).
- Cada paso dice **dónde** se corre, el **comando exacto**, qué **esperar** y cómo **volver atrás**.
- **Es un despliegue que cambia cómo entra todo el equipo.** Exige el backup del paso 1 y que **todos tengan que volver a ingresar** (paso 7).

## Qué sale a producción (y qué no)

| Qué | Cómo queda |
|---|---|
| **Migración 20** (`add_permisos_roles_privilegios.sql`) | Tablas nuevas `Permiso`, `Privilegio`, `PermisoPrivilegio`, `UsuarioPermiso`, `UsuarioPrivilegio`, `Organizacion`, `ConfiguracionOrganizacion`; columnas nuevas en `Rol`, `Usuario`, `Profesional` y `Especialidad`; la vista `vw_UsuarioAccesos` y el trigger que exige matrícula y especialidad al profesional. **Crea los 4 roles** (Profesional, CRM, Facturación, Configurador; hoy `Rol` está vacía) y los 5 permisos. Marca como "servicio" las especialidades Admisión, Admisión Infanto-Juvenil, Atención Domiciliaria, Centro de Rehabilitación Sociolaboral y Psicología Infanto-Juvenil si existieran (**Psicología no cambia**). No agrega especialidades |
| **Migración 21** (`add_privilegio_agendas_auditoria_permisos.sql`) | El privilegio **Gestión de agendas** (solo para CRM) y tres triggers que dejan en `LogAuditoria` todo cambio de roles, permisos y privilegios |
| **Migración 22** (`add_alta_equipo_invitaciones.sql`) | `Usuario.Telefono` e `InvitacionCuenta` (invitaciones; en la base solo queda el hash del token) |
| **Migración 23** (`mapear_usuarios_existentes_a_permisos.sql`) | **Datos.** Da permisos a los usuarios que ya existen, **una sola vez**: quien tiene ficha de profesional con matrícula y especialidad → `profesional`; quien **no** es paciente ni profesional **y está activo** (el administrador, `IdUsuario` 1) → `configurador` **y** `crm` (uno inactivo que no sea paciente ni profesional no recibe nada y se lista en la sección 3); los pacientes, nada. Con 5 usuarios queda: el admin con los dos módulos, los dos profesionales con "profesional", los pacientes sin permisos |
| **API** | Login por módulos (`requiereModulo`, token de selección de 5 minutos, `POST /sesion/modulo`, 403 `SIN_PERMISOS`); el bloqueo por intentos sigue siendo `PoliticaBloqueoLogin` (3 intentos, 15 minutos, UTC). Chats solo para CRM; turnos y agendas de todos solo para CRM **con** Gestión de agendas; admisiones, derivaciones y consentimientos solo para un profesional **con** Admisión; el Configurador administra (usuarios, permisos, privilegios, configuración y la auditoría administrativa) y no ve nada clínico. Alta de equipo por invitación (`/usuarios/equipo`, `/cuentas/*`) |
| **Pantallas** | Ingreso con pestañas Paciente, Profesional, CRM, Facturación y Configurador; selector de módulo; "Cambiar de módulo" en el menú de usuario; Configurador › Registrar usuario, Permisos, Privilegios, Cuentas; Facturación es una página "en construcción" |
| Circuito de atención (consentimientos opcionales de IA y transcripción, interruptores por sala, evolución por enfoque y addenda) | **No** (etapa 3). La lectura de emociones sigue como en `main`: cifrada y con el consentimiento por sala de video |
| Consentimiento de WhatsApp de Alearmas01 (recordatorios) | **No**: se descartó. Siguen sin recordatorios ni asistente por WhatsApp |
| `yosoy_app` endurecido y cambio de cadena de conexión | **No** (paso aparte, Apéndice A de [`DEPLOY_ETAPA1_ERRORES_AUDITORIA_PRODUCCION.md`](DEPLOY_ETAPA1_ERRORES_AUDITORIA_PRODUCCION.md)) |
| Migración 16 (consentimiento de WhatsApp de `main`) | **No hace falta**, sigue sin aplicarse |

## Lo que cambia para el equipo (avisalo antes)

- **Chats (`/mensajeria`)**: solo con el módulo **CRM**. Los profesionales **siguen con 403** en todo `/mensajeria` y no tienen el botón. Tu usuario `admin` queda con **Configurador y CRM**: al ingresar elegís con cuál trabajar y cambiás desde el menú de usuario, sin volver a ingresar. Trabajando como Configurador **no** ves los chats (es otro token).
- **Vincular el número de WhatsApp** (Configurador › WhatsApp) sigue siendo del **Configurador**, no del CRM.
- **Turnos y horarios de todos los profesionales**: hoy los ve y modifica el Configurador; después de esta etapa son de **CRM con el privilegio Gestión de agendas**, que **nadie tiene al terminar el despliegue** (paso 6). Lo mismo con las **admisiones, derivaciones y consentimientos**: pasan a un **profesional con el privilegio Admisión**, que tampoco lo tiene nadie. Hasta asignarlos, esas pantallas no están disponibles.
- **Un profesional sin matrícula o sin especialidad no recibe permiso y no puede ingresar** (403 `SIN_PERMISOS`). La migración 23 lo lista. En producción hoy no hay ninguno (los dos profesionales tienen matrícula y especialidad Psicología).
- Las **historias clínicas, evoluciones, eventos de crisis y reportes** dejan de verse desde el Configurador y desde CRM: los ve el profesional que atiende, y el **profesional auditor** (permiso que **no se asigna a nadie solo**).
- Ya no existe "el configurador entra a cualquier sala de video": a la sala de un turno entran el paciente y el profesional del turno; a la de una admisión, el paciente y un profesional con Admisión.

## Convenciones

- **[PC]** es PowerShell en tu PC. **[VPS]** es una sesión SSH que ya abriste (`ssh agustin@179.199.155.50`): pegá las líneas ahí. No uses `ssh servidor 'comando con sudo'`. Los comandos del VPS que **cambian algo** son **una sola línea** con `&&`. **Pegá un comando por vez**, y corré `sudo -v` antes de empezar.
- `FECHA` es **la de la PC** (`Get-Date -Format yyyyMMdd`). `TAG` es `e2`. Lo primero en la sesión SSH (y de nuevo si la cerrás):
  ```bash
  FECHA=YYYYMMDD; TAG=e2; export PATH="$PATH:/opt/mssql-tools18/bin"      # FECHA: la de la PC
  ```
- El login administrador de SQL Server es **`sa`** (no `yosoy_app`, que no puede crear tablas ni triggers). `sqlcmd` te pide la contraseña: no la escribas en el comando (`-P`).
- Las migraciones se corren **siempre con `-I -b -f 65001`**: `-I` (índices filtrados y triggers), `-b` (corta si algo falla) y `-f 65001` (acentos).

## El orden, de un vistazo

| Paso | Qué | Dónde | Corta el servicio | Se deshace |
|---|---|---|---|---|
| 0 | El código que vas a desplegar | PC | No | — |
| 1 | Backup de la base | PC y VPS | No | — (solo lee y copia) |
| 2 | Estado antes (solo lectura) | VPS | No | — |
| 3 | Migraciones 20 a 23 | PC y VPS | No | Sí (8.3) |
| 4 | Publicar la API | PC y VPS | La API, unos segundos | Sí (8.1) |
| 5 | Verificar | VPS, PC y navegador | No | — |
| 6 | Asignar privilegios (Gestión de agendas, Admisión) | Navegador | No | Sí |
| 7 | Sesiones abiertas | — | — | — |
| 8 | Rollback | VPS | — | — |

**El orden importa: migraciones primero, API después.** La API vieja ignora las tablas nuevas y sigue andando con la base migrada (se probó: ver el final), así que entre el paso 3 y el 4 nada se rompe. La API **nueva**, en cambio, **no deja ingresar al equipo sin las migraciones 20 a 23** (lee `UsuarioPermiso` en cada ingreso): por eso van antes. Elegí un horario de poco uso: la API se reinicia y **todos tienen que volver a ingresar**.

---

## Paso 0. [PC] El código que vas a desplegar

```powershell
cd D:\Claude\YoSoy\YoSoy
git switch main; git pull --ff-only; git status --short; git log -1 --oneline
(Get-ChildItem migrations\add_permisos_roles_privilegios.sql, migrations\add_privilegio_agendas_auditoria_permisos.sql, migrations\add_alta_equipo_invitaciones.sql, migrations\mapear_usuarios_existentes_a_permisos.sql | Measure-Object).Count
$fecha = Get-Date -Format yyyyMMdd; $fecha                  # anotá el valor: es la FECHA que se usa en el VPS
New-Item -ItemType Directory -Force 'D:\Claude\Backups\yosoy-produccion' | Out-Null
```

**Esperado:** `git status` sin líneas; `git log -1` es el merge del PR de la etapa 2 o uno posterior; el conteo da `4`. Si da menos, tu `main` todavía no la trae: mergeá el PR antes de seguir.

## Paso 1. Backup de la base

Es el procedimiento del paso 1.1 de [`DEPLOY_CHAT_PERSONAL_PRODUCCION.md`](DEPLOY_CHAT_PERSONAL_PRODUCCION.md): backup en el VPS con `CHECKSUM`, **verificación antes de borrar nada**, copia a tu PC con comparación de la huella SHA-256 y recién entonces borrado de las copias del servidor. **El backup queda solo en tu PC, fuera de cualquier repositorio.**

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
scp "agustin@179.199.155.50:/home/agustin/yosoy-pre-e2-${fecha}.bak" 'D:\Claude\Backups\yosoy-produccion\'
Get-FileHash "D:\Claude\Backups\yosoy-produccion\yosoy-pre-e2-${fecha}.bak" -Algorithm SHA256; (Get-Item "D:\Claude\Backups\yosoy-produccion\yosoy-pre-e2-${fecha}.bak").Length
```

**Esperado:** la misma huella y el mismo tamaño que `sha256sum` y `ls -la`. **Recién entonces, [VPS]**, borrar las copias del servidor:

```bash
sudo rm -f /var/opt/mssql/backup/yosoy-pre-$TAG-$FECHA.bak ~/yosoy-pre-$TAG-$FECHA.bak && sudo ls -la /var/opt/mssql/backup/
```

**Por qué no se puede saltear:** este despliegue cambia quién entra a qué. Si algo sale mal y ya se asignaron permisos o se dieron de alta cuentas, la única vuelta atrás completa es este backup (8.2). **Ojo:** el VPS tiene SQL Server 2025 y tu PC 2019: este `.bak` se restaura en el VPS, no en tu instancia.

## Paso 2. [VPS] Estado antes (todo de solo lectura)

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -W -Q "SELECT (SELECT COUNT(*) FROM sys.tables) AS tablas, (SELECT MAX(Numero) FROM VersionEsquema) AS ultima_version, (SELECT COUNT(*) FROM Rol) AS roles, (SELECT COUNT(*) FROM UsuarioRol) AS usuario_rol, (SELECT COUNT(*) FROM Usuario) AS usuarios, (SELECT COUNT(*) FROM UsuarioProfesional up JOIN Profesional p ON p.IdProfesional = up.IdProfesional WHERE LTRIM(RTRIM(ISNULL(p.Matricula, ''))) = '') AS profesionales_sin_matricula, (SELECT COUNT(*) FROM LogAuditoria) AS filas_de_auditoria; EXEC sp_VerificarCadenaAuditoria"
```

**Esperado (según la consulta del 20/10/2026):** `46  19  0  0  5  0  <N>` y la cadena `Íntegra`. Si `tablas` da `54` y `ultima_version` `23`, la etapa ya está aplicada: seguí al paso 4. Si **`profesionales_sin_matricula` no da 0**, esa persona **no va a poder ingresar** después de esta etapa hasta que le completes la matrícula (ver 3.4); si `roles` o `usuario_rol` no dan 0, **pará y avisame** (el mapeo asume que están vacías).

## Paso 3. Migraciones 20 a 23

### 3.1 [PC] Subir los archivos

```powershell
scp D:\Claude\YoSoy\YoSoy\migrations\add_permisos_roles_privilegios.sql D:\Claude\YoSoy\YoSoy\migrations\add_privilegio_agendas_auditoria_permisos.sql D:\Claude\YoSoy\YoSoy\migrations\add_alta_equipo_invitaciones.sql D:\Claude\YoSoy\YoSoy\migrations\mapear_usuarios_existentes_a_permisos.sql agustin@179.199.155.50:/home/agustin/
```

### 3.2 [VPS] Aplicar de a una, **en este orden** (todas idempotentes)

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -b -f 65001 -i ~/add_permisos_roles_privilegios.sql
```

**Esperado:** `Migración 20 aplicada.` y una fila con `4  5  1  2` (roles con clave, permisos, privilegios, permisos con privilegio). Sin errores.

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -b -f 65001 -i ~/add_privilegio_agendas_auditoria_permisos.sql
```

**Esperado:** `Migración 21 aplicada.` y la lista de privilegios por permiso: Admisión en Profesional y en Profesional auditor, y Gestión de agendas en CRM.

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -b -f 65001 -i ~/add_alta_equipo_invitaciones.sql
```

**Esperado:** `Migración 22 aplicada.`

### 3.3 [VPS] Aplicar el mapeo de usuarios (la 23) y **leer la salida**

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -b -f 65001 -i ~/mapear_usuarios_existentes_a_permisos.sql
```

**Esperado**, con los 5 usuarios de hoy (la salida son solo ids y claves):
- `Antes: usuarios con algún permiso asignado:` → `0`.
- `1) Cuántos usuarios tiene cada permiso:` → `profesional 2`, `auditor 0`, `crm 1`, `facturacion 0`, `configurador 1`.
- `2) Permisos de cada cuenta del equipo:` → el `IdUsuario 1` con `configurador, crm` y las dos cuentas de profesional con `profesional`. Los pacientes no aparecen (no son del equipo).
- `3) CUENTAS DEL EQUIPO SIN NINGÚN PERMISO` → **vacío** (si hubiera un usuario inactivo que no es paciente ni profesional, aparece con el motivo `inactivo, sin permisos`; no es un problema: el login lo rechaza igual). Si sale una fila, esa persona **no va a poder ingresar**: la columna `motivo` dice por qué (`profesional sin matrícula`, `profesional sin especialidad`...). Se arregla completándole la matrícula o la especialidad y asignándole el permiso en Configurador › Permisos.
- `4) Especialidades ... que pasaron a ser un "servicio"` → **vacío** (la especialidad de los dos profesionales es Psicología).

El mapeo es **una sola transacción**: la versión 23 solo queda registrada si todo funcionó; si algo falla se deshace todo y se puede corregir y repetir. Volver a correr este paso (y los anteriores) es seguro: la segunda vez no cambia nada, ni siquiera si ya modificaste permisos a mano.

### 3.4 [VPS] Comprobar

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -W -Q "SELECT (SELECT COUNT(*) FROM sys.tables) AS tablas, (SELECT MAX(Numero) FROM VersionEsquema) AS ultima_version, (SELECT COUNT(*) FROM UsuarioPermiso) AS permisos_asignados, (SELECT COUNT(*) FROM UsuarioPrivilegio) AS privilegios_asignados; SELECT IdUsuario, Username, Estado, Roles, Permisos, SinPermisos FROM vw_UsuarioAccesos WHERE TipoCuenta = 'Equipo' ORDER BY IdUsuario; EXEC sp_VerificarCadenaAuditoria"
```

**Esperado:** `54  23  4  0`; el admin con `Configurador` y `CRM y atención al cliente`, los dos profesionales con `Profesional`, `SinPermisos` en `0` para todos; y la cadena `Íntegra`. (Esta consulta muestra nombres de usuario: es la que ya tenés a mano en el VPS, no se guarda.)

## Paso 4. Publicar la API

### 4.1 [PC] Publicar y subir (el flujo de siempre: autocontenido, fuera del repositorio)

```powershell
cd D:\Claude\YoSoy\YoSoy
$fecha = Get-Date -Format yyyyMMdd; $tag = 'e2'
dotnet publish YoSoy.csproj -c Release -r linux-x64 --self-contained true -o "D:\Claude\Publish-YoSoy-${fecha}-${tag}"
tar -czf "$env:TEMP\Publish-YoSoy-${fecha}-${tag}.tar.gz" -C "D:\Claude\Publish-YoSoy-${fecha}-${tag}" .
scp "$env:TEMP\Publish-YoSoy-${fecha}-${tag}.tar.gz" agustin@179.199.155.50:/home/agustin/
```

**Esperado:** el `publish` termina sin errores (los avisos de nulabilidad `CS86xx` son normales); el paquete trae el binario `YoSoy`, `appsettings.json` y `PANTALLAS\` (con `activar-cuenta.html` y `Pantalla_Modulo\`), y ningún `.env`.

### 4.2 [VPS] Desempaquetar y comprobar que está completo

```bash
test -f ~/Publish-YoSoy-$FECHA-$TAG.tar.gz && mkdir ~/Publish-YoSoy-$FECHA-$TAG && tar -xzf ~/Publish-YoSoy-$FECHA-$TAG.tar.gz -C ~/Publish-YoSoy-$FECHA-$TAG && ls -la ~/Publish-YoSoy-$FECHA-$TAG/YoSoy ~/Publish-YoSoy-$FECHA-$TAG/PANTALLAS/activar-cuenta.html
```

**Esperado:** dos archivos listados. Si el `.tar.gz` no está o el `mkdir` dice que ya existe, la `FECHA` está mal o quedó algo de antes: **no sigas**.

### 4.3 [VPS] Backup `app-anterior-<tag>-<fecha>`, corte y arranque

```bash
test -f ~/Publish-YoSoy-$FECHA-$TAG/YoSoy && sudo test ! -e /opt/yosoy/app-anterior-$TAG-$FECHA && sudo cp -a /opt/yosoy/app /opt/yosoy/app-anterior-$TAG-$FECHA && sudo diff -rq /opt/yosoy/app /opt/yosoy/app-anterior-$TAG-$FECHA && echo "backup idéntico" && sudo systemctl stop yosoy-api && sudo find /opt/yosoy/app -mindepth 1 -delete && sudo cp -a ~/Publish-YoSoy-$FECHA-$TAG/. /opt/yosoy/app/ && sudo chown -R yosoy:yosoy /opt/yosoy/app && sudo chmod +x /opt/yosoy/app/YoSoy && sudo systemctl start yosoy-api && sleep 6 && systemctl is-active yosoy-api && curl -s -o /dev/null -w "%{http_code} %{redirect_url}\n" http://localhost:5000/
```

**Esperado:** `backup idéntico`, `active` y `302 http://localhost:5000/pantallas/index.html`. La cadena se frena **antes** de parar la API si el publish no está desempaquetado, si el backup ya existe o si `diff -rq` encuentra diferencias. Si se corta **después** de `systemctl stop`, la API quedó parada: corré el rollback (8.1). **No hace falta tocar el `.env`**: esta etapa no define variables nuevas.

## Paso 5. Verificar

### 5.1 [VPS] El servicio y el entorno

```bash
sudo journalctl -u yosoy-api --since '5 minutes ago' --no-pager | grep -iE 'error|exception|fail|Zona horaria|Hosting environment|Correo no configurado'
```

**Esperado:** la línea `Zona horaria de la clínica: ...`, **`Hosting environment: Production`** y **ninguna excepción**. Si dice `Development`, **pará**: en Desarrollo la API devuelve en pantalla el link de activación de las invitaciones; en producción nunca. Si aparece `Correo no configurado`, las invitaciones no van a poder enviarse por mail (se resuelve cargando `Email__From` y `Email__Password` en el `.env`, `DEPLOY.md` "Credenciales de correo"); no impide el resto.

### 5.2 [VPS] El login de cada rol (la contraseña se escribe cuando la pide, no queda en el historial)

Reemplazá `USUARIO` y corré uno por vez; con **la pestaña** (campo `modulo`) entra directo al módulo:

```bash
read -rsp "Contraseña: " PW; echo; curl -s -X POST http://127.0.0.1:5000/login -H 'Content-Type: application/json' -d "{\"username\":\"USUARIO\",\"password\":\"$PW\"}" | python3 -c "import sys,json; d=json.load(sys.stdin); print({k:(v if k!='token' and k!='tokenSeleccion' else '<token>') for k,v in d.items() if k in ('rol','modulo','requiereModulo','codigo','mensaje')}, [m['clave'] for m in d.get('modulos',[])])"; unset PW
```

**Esperado:**
- **El admin:** `requiereModulo: True` y los módulos `['crm', 'configurador']` (elige uno al ingresar).
- **Un profesional:** `modulo: 'profesional'`, `rol: 'profesional'` (entra directo; tiene un solo módulo).
- **Un paciente:** `rol: 'paciente'` (como siempre).
- Una contraseña incorrecta: nada (401 sin cuerpo); tres seguidas: `mensaje` con "Cuenta bloqueada" y los minutos (423).

### 5.3 [Navegador] Lo importante, a ojo

1. Ingresá como **admin**: te pide elegir **CRM** o **Configurador**. Elegí **CRM**: están **Chats** (y Casos y Derivaciones). Abrí el menú de usuario → **Cambiar de módulo** → **Configurador**: aparecen Registrar usuario, Permisos, Privilegios, Cuentas, **WhatsApp** y Auditoría, y **no** aparece Chats. Volvé a CRM.
2. Ingresá como **profesional**: no hay botón **Chats** ni cajón; el resto (agenda, pacientes, sala de video) anda como antes.
3. Ingresá como **paciente**: sin cambios.

### 5.4 [VPS] Los chats: solo CRM (se prueban las reglas, no las pantallas)

Con el mismo `read -rsp` del 5.2, el token del **profesional** tiene que dar 403 y el del admin **como CRM** tiene que dar 200:

```bash
read -rsp "Contraseña del PROFESIONAL: " PW; echo; T=$(curl -s -X POST http://127.0.0.1:5000/login -H 'Content-Type: application/json' -d "{\"username\":\"USUARIO_PROFESIONAL\",\"password\":\"$PW\"}" | python3 -c "import sys,json; print(json.load(sys.stdin)['token'])"); unset PW; curl -s -o /dev/null -w 'profesional -> /mensajeria/conversaciones: %{http_code}\n' http://127.0.0.1:5000/mensajeria/conversaciones -H "Authorization: Bearer $T"; unset T
```

**Esperado:** `403`. Y con el admin entrando con `"modulo":"crm"` en el cuerpo del login (como en 5.2), el mismo pedido da `200`; con `"modulo":"configurador"`, `403`.

### 5.5 [VPS y PC] El simulador sigue cerrado

```bash
curl -s -o /dev/null -w 'simulador, por el sitio público: %{http_code}\n' -X POST https://yosoytelemedicina.tech/mensajeria/simulado/entrante -H 'Content-Type: application/json' -d '{}'
```

```powershell
curl.exe -s -o NUL -w "simulador, desde tu PC: %{http_code}`n" -X POST https://yosoytelemedicina.tech/mensajeria/simulado/entrante -H "Content-Type: application/json" -d "{}"
```

**Esperado:** `404` las dos veces.

### 5.6 [VPS] La cadena de auditoría

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -W -Q "SELECT TOP 5 IdLog, Accion, Espacio, CASE WHEN Hash IS NULL THEN 'sin hash' ELSE 'con hash' END AS hash FROM LogAuditoria ORDER BY IdLog DESC; EXEC sp_VerificarCadenaAuditoria"
```

**Esperado:** los ingresos que acabás de hacer (`Ingreso`, `Ingreso a módulo`, con el módulo en `Espacio`), todos con hash, y `Íntegra` con más registros revisados que antes. (Los ingresos fallidos y los bloqueos también quedan; nunca la contraseña.)

## Paso 6. [Navegador] Asignar los privilegios que nadie tiene (Configurador › Privilegios)

Hasta hacerlo, esas pantallas no existen. Entrá como admin, módulo **Configurador**:
1. **Gestión de agendas** → agregá a tu usuario (así, trabajando como CRM, ves Turnos y Horarios de todos los profesionales).
2. **Admisión** → agregalo a **el profesional que va a hacer las admisiones** (si es uno de los dos de hoy, él mismo). Después tiene que volver a ingresar para que le llegue.
3. **Profesional auditor** no se asigna solo; si lo querés, Configurador › Permisos.

Cada asignación queda en la auditoría administrativa.

## Paso 7. Sesiones abiertas

Los tokens que ya están emitidos **no traen** el módulo ni los privilegios, pero **siguen valiendo** hasta que vencen (8 horas) con el rol de antes: un profesional sigue siendo profesional, un paciente sigue siendo paciente, un Configurador sigue administrando. **Lo que no tienen** es lo nuevo: un Configurador con una sesión vieja **no ve Chats** (le da 403, y la pantalla no ofrece el botón) y no puede cambiar de módulo; un profesional con sesión vieja no tiene Admisión. **Hay que cerrar sesión y volver a ingresar** (el menú de usuario → Salir), todos, el día del despliegue. No hace falta forzar nada en el servidor: un token viejo nunca da más de lo que daba. (Lo cubre `AccesoPorModuloTests`: los tokens anteriores a la etapa se siguen leyendo igual.)

## Paso 8. Rollback

### 8.1 Volver a la API anterior (lo normal)

La API anterior **ignora** las tablas y columnas nuevas y vuelve a deducir el rol de la ficha, así que **no hace falta tocar la base**. Se probó: la batería de pruebas de `main` (la API anterior) corrida contra una base con las migraciones 20 a 23 aplicadas da 1790 pasadas, 0 fallidas.

```bash
sudo test -d /opt/yosoy/app-anterior-$TAG-$FECHA && sudo systemctl stop yosoy-api && sudo find /opt/yosoy/app -mindepth 1 -delete && sudo cp -a /opt/yosoy/app-anterior-$TAG-$FECHA/. /opt/yosoy/app/ && sudo chown -R yosoy:yosoy /opt/yosoy/app && sudo systemctl start yosoy-api && sleep 6 && systemctl is-active yosoy-api && curl -s -o /dev/null -w "%{http_code} %{redirect_url}\n" http://localhost:5000/
```

**Esperado:** `active` y `302 ...`. Con la API anterior, el admin vuelve a entrar como Configurador y a ver los chats (es lo que hacía antes). **Qué se pierde:** nada de datos. Las cuentas de equipo dadas de alta por invitación (estado `Pendiente`) no pueden ingresar con la API anterior hasta que tengan contraseña; si quedó alguna, el primer `SELECT` de `docs/produccion/deshacer-etapa2.sql` las lista.

### 8.2 Restaurar el backup (la vuelta atrás completa)

Solo si hay que dejar **toda** la base como estaba antes del paso 3. **Se pierde todo lo escrito en la base desde el backup** (turnos, mensajes, altas, auditoría posterior). Con la API parada y la copia del backup otra vez en el VPS (la tenés en la PC: `scp` al revés):

```bash
sudo systemctl stop yosoy-api && sudo cp ~/yosoy-pre-$TAG-$FECHA.bak /var/opt/mssql/backup/ && sudo chown mssql:mssql /var/opt/mssql/backup/yosoy-pre-$TAG-$FECHA.bak && sqlcmd -S localhost -U sa -C -Q "ALTER DATABASE [yosoy] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE [yosoy] FROM DISK = N'/var/opt/mssql/backup/yosoy-pre-$TAG-$FECHA.bak' WITH REPLACE, CHECKSUM; ALTER DATABASE [yosoy] SET MULTI_USER;"
```

Y después la API anterior (8.1, desde `sudo systemctl stop`... pero sin el `stop` inicial: la API ya está parada) y volver a verificar con el paso 2.

### 8.3 Sacar solo lo de la etapa 2 de la base (sin restaurar)

`docs/produccion/deshacer-etapa2.sql` (probado en el ensayo): borra las asignaciones, las invitaciones y todo lo que agregaron las migraciones 20 a 23, y deja la base en la versión 19. Pierde los permisos, privilegios e invitaciones de la etapa; no toca datos de pacientes, profesionales ni turnos; la auditoría de las asignaciones queda (es de solo agregar). Con un backup hecho y la API anterior ya puesta:

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -b -f 65001 -i ~/deshacer-etapa2.sql
```

(Subilo antes con `scp docs\produccion\deshacer-etapa2.sql agustin@179.199.155.50:/home/agustin/`.)

---

## Qué se verificó y qué no

**Verificado en la PC (SQL Server 2019 Express):**

| Qué | Cómo |
|---|---|
| Las migraciones 20 a 23 sobre una base con **la forma exacta de producción** | Una base descartable armada con el `01-produccion.sql` de `main` (etapa 1): 46 tablas, migraciones 17, 18 y 19 aplicadas, sin la 16, `Rol` y `UsuarioRol` vacías, 5 usuarios (1 administrador sin ficha, 2 pacientes —uno inactivo—, 2 profesionales de Psicología con matrícula y especialidad) y 29 filas de auditoría con hash. Después de aplicarlas: 54 tablas, versión 23; el admin con `Configurador` y `CRM`, los dos profesionales con `Profesional`, los pacientes sin permisos, ninguna cuenta del equipo sin permiso, ningún privilegio asignado; la cadena de auditoría `Íntegra` (37 registros: las 29 de antes más las 8 asignaciones, que quedaron registradas) y las columnas de `Usuario`, `Profesional` y `Especialidad` que ya existían, idénticas (misma huella). La base se borró |
| Idempotencia | Las cuatro migraciones corridas una segunda vez: mismas huellas, mismos conteos, sin errores. Además, probado con una base de pruebas: si el Configurador le saca el CRM al admin y se repite la 23, no se lo devuelve |
| La API anterior sigue andando con la base migrada | La batería de pruebas de `main` (8086924) contra esa base con las migraciones aplicadas: 1790 pasadas, 11 omitidas (las que ya se omiten sin la tabla del consentimiento de WhatsApp), 0 fallidas |
| El camino de vuelta atrás de la base | `docs/produccion/deshacer-etapa2.sql` corrido sobre esa base: vuelve a 46 tablas y versión 19, la cadena `Íntegra`; las migraciones se volvieron a aplicar sin errores |
| Qué hace cada rol | Pruebas automáticas nuevas: ingreso por módulos de punta a punta (admin con dos módulos que elige y cambia sin volver a ingresar, token de selección que no abre nada más, permiso quitado, privilegio en el token, bloqueo por intentos), `/mensajeria` ruta por ruta (profesional, paciente y Configurador sin CRM: 403; CRM: entra; sin sesión: 401), alta de equipo por invitación, el mapeo de usuarios existentes sobre una base descartable creada por la prueba |
| Resultados de las suites | Ver el cuerpo del PR (comandos y cuentas) |

**No verificado:**
- **Nada contra el SQL Server 2025 del VPS.** Las migraciones usan T-SQL común, triggers, una vista (`FOR XML PATH`, no `STRING_AGG`) y `CREATE OR ALTER`; no hay nada específico de una versión, pero no se corrió allá.
- **El envío real de la invitación por mail** (en las pruebas el correo no está configurado; se probó todo el recorrido salvo el SMTP). Depende de `Email__From` y `Email__Password` del servidor.
- **Los datos reales de producción más allá de la consulta de solo lectura del 20/10/2026** (5 usuarios; los nombres de cuentas y roles reales no se guardaron en ningún lado). Si entre la consulta y el despliegue se creó un usuario, el paso 3.3 lo muestra.
- **Las pantallas en un navegador real**: se revisaron por código y con la sintaxis de los scripts; no hay prueba automática de navegador de este recorrido. El paso 5.3 es la comprobación a ojo.
- **El comportamiento con sesiones abiertas** se probó con tokens emitidos como los de antes (sin módulo ni privilegios), no con un navegador que tenga una sesión guardada.
