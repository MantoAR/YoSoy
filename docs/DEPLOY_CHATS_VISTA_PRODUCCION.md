# Despliegue a producción: Chats v2, Fase 1 (vista de WhatsApp)

Runbook para llevar a producción la **Fase 1 de Chats v2**: poder **vincular una conversación de WhatsApp con un paciente** (a mano, confirmando, con buscador y con una sugerencia por teléfono que nunca vincula sola), **desvincular**, ver el **teléfono en formato WhatsApp** (`+54 9 351 206-6471`), el **interruptor de IA** (global en Configuración, y por conversación en Chats), **quién responde** (Bot / IA / Persona) con el botón **Tomar control**, y el panel del contacto con datos reales (y **Ver ficha** solo en Administración). Se saca el botón **"Configurar vista"** (no hacía nada). **La IA todavía no existe: los interruptores solo guardan y muestran la decisión; nada responde con IA.** Cambia **la API y las pantallas** (van dentro del paquete) y **la base en una columna** (la **migración 25**, **opcional**). Se ejecuta **a mano, en dos máquinas** (la PC con PowerShell y el VPS por SSH), con el flujo de siempre de [`DEPLOY.md`](DEPLOY.md) y el mismo formato que [`DEPLOY_VISTA_UNICA_PRODUCCION.md`](DEPLOY_VISTA_UNICA_PRODUCCION.md) y [`DEPLOY_USUARIOS_PRODUCCION.md`](DEPLOY_USUARIOS_PRODUCCION.md). Escrito el 2026-10-21 contra la rama `feat/chats-vista-whatsapp` (sale de `main` en `a5649b7`); **antes de armar el paquete tiene que estar mergeada en `main`** (paso 0).

- **Se escribió sin acceso al VPS y todavía no se ejecutó.** Lo que se probó antes, y cómo, está en "Qué se verificó y qué no", al final; lo que no, también.
- **Sin secretos en este archivo.** Ningún comando imprime un secreto ni el contenido de un `.env`; las contraseñas las escribís vos cuando el comando las pide (nunca en la línea de comandos).
- Cada paso dice **dónde** se corre, el **comando exacto**, qué **esperar** y cómo **volver atrás**.
- **La recepcionista (solo CRM) sigue viendo solo chats** y puede vincular y desvincular (decisión del dueño); **no** ve Configuración, Usuarios, Auditoría ni "Ver ficha".

## Qué sale a producción (y qué no)

| Qué | Cómo queda |
|---|---|
| **Vincular y desvincular** | `PUT` / `DELETE /mensajeria/conversaciones/{id}/vinculo` (rol CRM, que incluye Administración). Vincular es **siempre una acción explícita** de una persona, con una confirmación que muestra **nombre completo, últimos 3 dígitos del DNI y año de nacimiento**. Falla bien: paciente inexistente 404, paciente **inactivo** 409 (`PACIENTE_INACTIVO`), conversación **ya vinculada a otro** 409 (`YA_VINCULADA`; hay que desvincular primero). Es **atómico** (el `UPDATE` solo pega si la conversación sigue sin paciente) y deja **una fila de auditoría administrativa** en la misma transacción. Al vincular o desvincular se **reinicia el flujo del asistente** (paso y datos de contexto) |
| **Buscador de pacientes** | `GET /mensajeria/pacientes/buscar?q=` (rol CRM): por **DNI completo** (7 u 8 números) o por **nombre** (al menos 3 letras, sin mayúsculas ni tildes). Solo **activos**, hasta 10, y por cada uno **solo** nombre, `•••` + últimos 3 dígitos del DNI, año de nacimiento y **cuántas otras conversaciones** tiene (solo el número). **Sin** domicilio, mail, teléfono, fecha de nacimiento completa ni nada clínico. **Límite de 30 pedidos por minuto por IP.** **No se audita la búsqueda** (lo escrito es un dato personal) |
| **Sugerencia por teléfono** | En el panel de una conversación **sin paciente**: los pacientes activos cuyo teléfono cargado coincide con el de la conversación, comparando con `TelefonoArgentino` (guiones, espacios, paréntesis, `+54`, el `9`, el `0` y el `15`: `351-206-6471`, `(351) 15 206-6471`, `+54 9 351 206 6471`, `0351 15 2066471`). **Solo sugiere**: leerla no cambia nada y la pantalla dice que es una coincidencia sugerida. Si hay dos pacientes con el mismo teléfono (una familia), salen los dos |
| **Auditoría** | **Administrativa** (la ve el Configurador), entidad `ConversacionVinculo` (no `Conversacion`: esa entidad cae en la auditoría **clínica**), **solo ids**: `Conversación #12, paciente #45, origen busqueda` (o `sugerencia`). **Nunca** el número, el nombre ni el DNI. Idem para el interruptor de IA (`ConversacionIa`) y el global (`ConfiguracionOrganizacion`) |
| **Teléfono en formato WhatsApp** | La lista, el encabezado, el panel y Leads muestran `+54 9 351 206-6471`. **Es solo formato**: lo guardado no cambia (ver "Descartado por ahora" más abajo). Un número cuyo código de área no figura en la lista oficial de ENACOM **no** se muestra como argentino (se ve en dígitos, sin `+54 9`) |
| **Interruptor de IA** | **Global**: Configuración › **Asistente de chats** (`GET` / `PUT /configuracion/chats-ia`, **solo Configurador**, que incluye la sesión Administración; el CRM puro recibe 403). Es una fila de `ConfiguracionOrganizacion` (`Chats.IaActiva`), con quién y cuándo lo cambió. **Por conversación**: en el panel del chat (CRM y Administración), columna `Conversacion.IaActiva` (migración 25). **Los dos arrancan apagados.** Regla: con el global apagado, ninguna conversación responde con IA, aunque la tenga prendida. **Desvincular apaga la IA de esa conversación** |
| **Quién responde / Tomar control** | El panel dice **Bot**, **IA** o **Persona** (lo calcula el servidor; "IA" no aparece hasta que exista). **Tomar control** apaga la IA de esa conversación y, en modo **Asistente**, pasa una conversación `Activa` a "Escalada a persona" (el mecanismo que ya frena al asistente); en modo **Personal** solo apaga la IA. **El asistente de reglas no depende del interruptor de IA y no cambia** |
| **Migración 25 (opcional)** | `Conversacion.IaActiva BIT NOT NULL DEFAULT 0` y la versión 25 en `VersionEsquema`, en **una transacción**, idempotente. **La API anda sin ella** (no la lee ni la escribe; el interruptor por conversación responde 409 "falta la migración 25"): se puede aplicar **antes o después** de publicar. Deshacer: `docs/produccion/deshacer-migracion-25.sql` |
| **Carrera de `Guardar`** | `ConversacionDAO.Guardar` ya no escribe `IdPaciente` a ciegas: un mensaje que entra (o el asistente) con una copia vieja **no pisa un vínculo hecho a mano ni deshace una desvinculación**. El comportamiento del asistente no cambia |
| **Pantallas** | Panel del lead con "Vincular con un paciente", sugerencias, "Desvincular", "Quién responde", "Tomar control", interruptor de IA y "Ver ficha" (solo Administración, hacia Usuarios); Configuración › Asistente de chats. **Sale "Configurar vista"** (era un texto deshabilitado: no guardaba nada, no hay tabla ni dato que borrar) |
| **No cambia** | El **CRM puro**: ingreso, menú (sin Leads) y permisos. El **Configurador solo** (sigue en 403 en Chats, como decidió la D2). **Profesional, Paciente y Facturación** (403 en todo `/mensajeria` y en el interruptor global). El QR de WhatsApp. El asistente de reglas. `whatsapp-service`, Caddy, variables de entorno (`.env`): **no se tocan** |

## Descartado por ahora: la migración 26 (`Conversacion.TelefonoWhatsApp`)

Se había propuesto guardar el **número completo** que manda el webhook (el JID de WhatsApp sin el `@`, con el código de país) en una columna nueva, para no mostrar un `+54 9` falso en un número extranjero. **Quedó descartada por ahora, por decisión del dueño, con este dato real de producción (consulta de solo lectura, 21/10/2026):** hay **3 conversaciones** (2 de WhatsApp y 1 del simulador), **todas de 10 dígitos numéricos con el prefijo 351**; **ningún número extranjero**. Hoy la migración no cierra ningún caso real. **El límite sigue documentado en una prueba** (`TelefonoWhatsAppTests.Limite_conocido_un_numero_extranjero_cuyos_10_ultimos_digitos_parecen_argentinos_se_muestra_argentino_sin_el_numero_original`): un número de otro país cuyos 10 últimos dígitos empiecen con un código de área argentino (por ejemplo +1 351 555 1234, que queda guardado `3515551234`) se muestra como argentino, porque lo guardado son solo los últimos 10 dígitos y no se puede distinguir.

Lo que **sí** evita hoy un "+54 9" falso: un número de 10 dígitos se muestra como argentino **solo si** empieza con un código de área que figura en la lista oficial de ENACOM (300 códigos); si no (por ejemplo +1 212 → `2125551234`), se muestra en dígitos, sin país. **Cuándo reabrirlo:** apenas la consulta del paso 2 muestre un número que no sea de 10 dígitos o un prefijo que no sea argentino (la función de formato ya acepta el número original, así que la migración no cambiaría la pantalla). Reversa si algún día se hace: `ALTER TABLE Conversacion DROP COLUMN TelefonoWhatsApp`; no toca la columna `Telefono`.

## Lo que se verificó en producción antes de desplegar (21/10/2026)

Consultas **de solo lectura** (las de los pasos 2.1 a 2.3, corridas por Agustín el 21/10/2026; **volvelas a correr el día del despliegue**: son la condición para seguir):

| Qué | Resultado | Por qué importa |
|---|---|---|
| Tabla `ConfiguracionOrganizacion` | **existe** | El interruptor global es una fila ahí. Sin la tabla, `PUT /configuracion/chats-ia` respondería 503 |
| Versión de esquema | **24** | La 25 se aplica encima, sin saltos |
| Conversaciones | **3** (2 WhatsApp, 1 Simulado), **todas de 10 dígitos numéricos con prefijo 351**, ninguna extranjera | Todas se muestran como `+54 9 351 …`; justifica descartar la migración 26 |
| Pacientes activos | **3**, **2 con teléfono**, **los 2 con símbolos** en el número | La sugerencia por teléfono tiene que entender esos formatos (guiones, espacios, paréntesis, `+54`, `9`, `15`): hay pruebas con los cuatro formatos reales de carga |

## Lo que cambia para el equipo (avisalo antes)

- **Quien atiende los chats (recepcionista o administrador):** el teléfono se ve como `+54 9 351 206-6471`. En el panel del lead hay un botón **Vincular con un paciente**: se busca por nombre o DNI, se **mira** nombre, DNI parcial y año de nacimiento y se **confirma**. Si el teléfono coincide con el de un paciente aparece como **sugerencia**: no queda vinculado hasta que alguien lo confirme, y **conviene preguntarle a la persona quién es**. Vincular mal es un riesgo real de privacidad: se le podrían mostrar o confirmar datos de otro paciente. **Desvincular** deja la conversación como una persona sin identificar (las notas y etiquetas se conservan).
- **Nuevo en el panel:** "Quién responde" (Bot / Persona), **Tomar control** y el interruptor "IA en esta conversación". **La IA todavía no responde mensajes**; el interruptor solo guarda la decisión.
- **El administrador:** Configuración ▾ tiene **Asistente de chats** (interruptor global, apagado) y, en el panel de un paciente vinculado, **Ver ficha**.
- **Ya no existe "Configurar vista"** en el panel del lead (no hacía nada).

## Convenciones

- **[PC]** es PowerShell en tu PC. **[VPS]** es una sesión SSH que ya abriste (`ssh agustin@179.199.155.50`): pegá las líneas ahí. No uses `ssh servidor 'comando con sudo'`. Los comandos del VPS que **cambian algo** son **una sola línea** con `&&`. **Pegá un comando por vez**, y corré `sudo -v` antes de empezar.
- `FECHA` es **la de la PC** (`Get-Date -Format yyyyMMdd`). `TAG` es `cv`. Lo primero en la sesión SSH (y de nuevo si la cerrás):
  ```bash
  FECHA=YYYYMMDD; TAG=cv; export PATH="$PATH:/opt/mssql-tools18/bin"      # FECHA: la de la PC
  ```
- El login administrador de SQL Server es **`sa`**; `sqlcmd` te pide la contraseña: no la escribas en el comando (`-P`).

## El orden, de un vistazo

| Paso | Qué | Dónde | Corta el servicio | Se deshace |
|---|---|---|---|---|
| 0 | El código que vas a desplegar | PC | No | — |
| 1 | Backup de la base (verificado) | VPS y PC | No | — |
| 2 | Estado antes (solo lectura) | VPS | No | — |
| 3 | Migración 25 (opcional) | PC y VPS | No | Sí (7.3) |
| 4 | Publicar y subir la API; backup de la app anterior, corte y arranque | PC y VPS | La API, unos segundos | Sí (7.1) |
| 5 | Verificar | VPS y navegador | No | — |
| 6 | Sesiones abiertas | — | — | — |
| 7 | Rollback | VPS | La API, unos segundos | — |

**Elegí un horario de poco uso:** la API se reinicia unos segundos. `whatsapp-service` no se reinicia y sigue conectado. La migración es una columna con valor por defecto (solo metadatos: no recorre la tabla), pero igual va con **backup verificado** primero.

---

## Paso 0. [PC] El código que vas a desplegar

```powershell
cd D:\Claude\YoSoy\YoSoy
git switch main; git pull --ff-only; git status --short; git log -1 --oneline
(Get-ChildItem Services\Mensajeria\TelefonoWhatsApp.cs, Services\Mensajeria\AreasArgentinas.cs, Endpoints\ChatsVinculoEndpoints.cs, migrations\add_conversacion_ia.sql, docs\produccion\deshacer-migracion-25.sql, PANTALLAS\Pantalla_Configurador\js\chats-ia.js, docs\DEPLOY_CHATS_VISTA_PRODUCCION.md | Measure-Object).Count
Select-String -Path Services\VerificadorEsquema.cs -Pattern 'add_conversacion_ia.sql' -Quiet
$fecha = Get-Date -Format yyyyMMdd; $fecha                  # anotá el valor: es la FECHA que se usa en el VPS
```

**Esperado:** `git status` sin líneas; `git log -1` es el merge del PR de esta mejora o uno posterior; el conteo da `7` y `Select-String` da `True`. Si no, tu `main` todavía no la trae: mergeá el PR antes de seguir.

## Paso 1. Backup de la base (verificado antes de tocar nada)

Es el procedimiento del paso 1 de [`DEPLOY_USUARIOS_PRODUCCION.md`](DEPLOY_USUARIOS_PRODUCCION.md) (backup en el VPS con `CHECKSUM`, **verificación antes de seguir**, copia a tu PC con comparación de la huella SHA-256 y recién entonces borrado de las copias del servidor). **El backup queda solo en tu PC, fuera de cualquier repositorio.**

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
scp "agustin@179.199.155.50:/home/agustin/yosoy-pre-cv-${fecha}.bak" 'D:\Claude\Backups\yosoy-produccion\'
Get-FileHash "D:\Claude\Backups\yosoy-produccion\yosoy-pre-cv-${fecha}.bak" -Algorithm SHA256; (Get-Item "D:\Claude\Backups\yosoy-produccion\yosoy-pre-cv-${fecha}.bak").Length
```

**Esperado:** la misma huella y el mismo tamaño que `sha256sum` y `ls -la`. **Recién entonces, [VPS]**, borrar las copias del servidor:

```bash
sudo rm -f /var/opt/mssql/backup/yosoy-pre-$TAG-$FECHA.bak ~/yosoy-pre-$TAG-$FECHA.bak && sudo ls -la /var/opt/mssql/backup/
```

**Ojo:** el VPS tiene SQL Server 2025 y tu PC 2019: este `.bak` se restaura en el VPS, no en tu instancia.

## Paso 2. [VPS] Estado antes (todo de solo lectura)

### 2.1 La forma de los teléfonos de las conversaciones

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -W -Q "SELECT Canal, LEN(Telefono) AS largo, COUNT(*) AS cantidad, SUM(CASE WHEN Telefono NOT LIKE '%[^0-9]%' THEN 1 ELSE 0 END) AS solo_digitos FROM Conversacion GROUP BY Canal, LEN(Telefono) ORDER BY cantidad DESC"
sqlcmd -S localhost -d yosoy -U sa -C -I -W -Q "SELECT LEFT(Telefono, 3) AS prefijo, COUNT(*) AS cantidad FROM Conversacion WHERE LEN(Telefono) = 10 GROUP BY LEFT(Telefono, 3) ORDER BY cantidad DESC"
```

**Esperado (según el estado del 21/10/2026):** `WhatsApp  10  2  2`, `Simulado  10  1  1` y un solo prefijo, `351`, con `3`. Solo conteos: ningún número completo sale en la respuesta. **Si aparece un largo distinto de 10 o un prefijo que no sea argentino, no frena el despliegue** (la pantalla los muestra en dígitos, sin `+54 9`), pero es la señal para reabrir la migración 26 (ver arriba).

### 2.2 Pacientes, tabla de configuración y versión del esquema

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -W -Q "SELECT (SELECT COUNT(*) FROM Paciente WHERE ISNULL(Estado, 'Activo') = 'Activo') AS pacientes_activos, (SELECT COUNT(*) FROM Paciente WHERE ISNULL(Estado, 'Activo') = 'Activo' AND Telefono IS NOT NULL AND LTRIM(RTRIM(Telefono)) <> '') AS con_telefono, (SELECT COUNT(*) FROM Paciente WHERE ISNULL(Estado, 'Activo') = 'Activo' AND Telefono LIKE '%[^0-9]%') AS con_simbolos_en_el_telefono, (SELECT COUNT(*) FROM sys.tables WHERE name = 'ConfiguracionOrganizacion') AS tabla_configuracion, (SELECT MAX(Numero) FROM VersionEsquema) AS version_esquema, (SELECT COUNT(*) FROM sys.tables) AS tablas"
```

**Esperado (21/10/2026):** `3  2  2  1  24  54`. **Condición para seguir:** `tabla_configuracion = 1` y `version_esquema = 24` (si la tabla no existe, el interruptor global no se puede guardar; si la versión es otra, **pará** y revisá qué migraciones faltan: ver [`CATALOGO_SCRIPTS_SQL.md`](CATALOGO_SCRIPTS_SQL.md)).

### 2.3 Que todavía no hay nada de esto

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -W -Q "SELECT (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Conversacion') AND name = 'IaActiva') AS columna_ia, (SELECT COUNT(*) FROM LogAuditoria WHERE Entidad IN ('ConversacionVinculo', 'ConversacionIa')) AS auditoria_de_vinculos, (SELECT COUNT(*) FROM ConfiguracionOrganizacion WHERE Clave = 'Chats.IaActiva') AS global_guardado"
```

**Esperado:** `0  0  0`.

## Paso 3. Migración 25 (opcional)

Es **opcional**: la API nueva anda sin ella. La recomendación es aplicarla **antes** de publicar, así el interruptor de IA por conversación anda desde el primer momento. Si preferís publicar primero, el interruptor por conversación responde "falta aplicar la migración 25" hasta que se aplique (sin errores en el resto).

### 3.1 [PC] Subir el archivo

```powershell
scp D:\Claude\YoSoy\YoSoy\migrations\add_conversacion_ia.sql agustin@179.199.155.50:/home/agustin/
```

### 3.2 [VPS] Aplicarla (idempotente, en una transacción)

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -b -f 65001 -i ~/add_conversacion_ia.sql
```

**Esperado:** `Migración 25 aplicada.` y una fila con `conversaciones = 3` y `con_ia_prendida = 0`. Sin errores. Volver a correrla es seguro: no cambia nada. Si falla a la mitad, **no queda nada aplicado** (la transacción se deshace).

### 3.3 [VPS] Comprobar (si esto no da lo esperado, **no publiques la API**)

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -W -Q "SELECT (SELECT COUNT(*) FROM sys.tables) AS tablas, (SELECT MAX(Numero) FROM VersionEsquema) AS ultima_version, (SELECT Origen FROM VersionEsquema WHERE Numero = 25) AS origen, (SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Conversacion') AND name = 'IaActiva') AS acepta_nulos, (SELECT COUNT(*) FROM Conversacion WHERE IaActiva = 1) AS con_ia_prendida; EXEC sp_VerificarCadenaAuditoria"
```

**Esperado:** `54  25  Script  0  0` y la cadena `Íntegra`.

## Paso 4. Publicar la API y las pantallas

### 4.1 [PC] Publicar y subir (el flujo de siempre: autocontenido, fuera del repositorio)

```powershell
cd D:\Claude\YoSoy\YoSoy
$fecha = Get-Date -Format yyyyMMdd; $tag = 'cv'
dotnet publish YoSoy.csproj -c Release -r linux-x64 --self-contained true -o "D:\Claude\Publish-YoSoy-${fecha}-${tag}"
tar -czf "$env:TEMP\Publish-YoSoy-${fecha}-${tag}.tar.gz" -C "D:\Claude\Publish-YoSoy-${fecha}-${tag}" .
scp "$env:TEMP\Publish-YoSoy-${fecha}-${tag}.tar.gz" agustin@179.199.155.50:/home/agustin/
```

**Esperado:** el `publish` termina sin errores (los avisos de nulabilidad `CS86xx` son normales); el paquete trae el binario `YoSoy`, `appsettings.json` y `PANTALLAS\` (con `Pantalla_Configurador\js\chats-ia.js`), y ningún `.env`.

### 4.2 [VPS] Desempaquetar y comprobar que está completo

```bash
test -f ~/Publish-YoSoy-$FECHA-$TAG.tar.gz && mkdir ~/Publish-YoSoy-$FECHA-$TAG && tar -xzf ~/Publish-YoSoy-$FECHA-$TAG.tar.gz -C ~/Publish-YoSoy-$FECHA-$TAG && ls -la ~/Publish-YoSoy-$FECHA-$TAG/YoSoy ~/Publish-YoSoy-$FECHA-$TAG/PANTALLAS/Pantalla_Configurador/js/chats-ia.js
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
sudo journalctl -u yosoy-api --since '5 minutes ago' --no-pager | grep -iE 'error|exception|fail|Zona horaria|Hosting environment'; systemctl is-active whatsapp-service
```

**Esperado:** la línea `Zona horaria de la clínica: ...`, **`Hosting environment: Production`**, **ninguna excepción** y `active` para `whatsapp-service` (no se tocó y no se reinició).

### 5.2 [VPS] Lo nuevo exige sesión (solo lectura, sin sesión)

```bash
curl -s -o /dev/null -w "buscar sin sesion: %{http_code}\n" "http://localhost:5000/mensajeria/pacientes/buscar?q=abc"; curl -s -o /dev/null -w "vinculo sin sesion: %{http_code}\n" -X PUT http://localhost:5000/mensajeria/conversaciones/1/vinculo; curl -s -o /dev/null -w "ia sin sesion: %{http_code}\n" -X PUT http://localhost:5000/mensajeria/conversaciones/1/ia; curl -s -o /dev/null -w "tomar control sin sesion: %{http_code}\n" -X POST http://localhost:5000/mensajeria/conversaciones/1/tomar-control; curl -s -o /dev/null -w "ia global sin sesion: %{http_code}\n" http://localhost:5000/configuracion/chats-ia
```

**Esperado:** `401` las cinco veces.

### 5.3 [Navegador] El administrador (con Ctrl+F5 antes: ver paso 6)

Ingresá por la pestaña **Configurador** (o **CRM**). **Probá sobre la conversación del simulador** (la que figura como "Chat simulado") **o una de prueba; no vincules ni cambies conversaciones de pacientes reales.** Para ver lo que pasa de punta a punta hace falta un paciente de prueba; si no hay, **llegá hasta la pantalla de confirmación y tocá Cancelar**: eso no cambia nada.

1. **Chats:** el teléfono se ve como `+54 9 351 …` en la lista, el encabezado y el panel del lead. En el panel del lead **ya no está "Configurar vista"**.
2. En una conversación **sin paciente**: botón **Vincular con un paciente** y, si algún paciente tiene un teléfono que coincide, **"Coincidencias sugeridas por teléfono"** con el nombre, `DNI •••` y el año. **Ver la sugerencia no vincula nada.**
3. **Vincular con un paciente:** escribí 2 letras (pide al menos 3), después un apellido: salen los activos (sin DNI completo). Elegí uno: la confirmación muestra **nombre, `•••` + 3 dígitos del DNI y año de nacimiento**, la advertencia **"Confirmá la identidad con la persona que escribe"** y, en modo **Asistente**, la advertencia roja de que el asistente va a tratar a quien escribe como ese paciente. Si abrís una sugerencia, dice **"coincidencia sugerida"**. **Cancelá** (o, sobre el paciente de prueba, confirmá).
4. Con el vínculo hecho (solo sobre el paciente de prueba): el panel muestra el nombre, DNI, el teléfono del paciente **tal como lo cargaron**, el WhatsApp de la conversación, el profesional, el próximo turno y **Ver ficha** (lleva a Usuarios con ese paciente). **Desvincular** pide confirmación y deja la conversación sin identificar; **desvinculá siempre al terminar la prueba.**
5. **Quién responde:** dice **Bot** (modo Asistente) o **Persona** (modo Personal, o una conversación escalada). **Tomar control** pide confirmación; el interruptor **IA en esta conversación** arranca apagado y avisa "La IA global está apagada".
6. **Configuración ▾ › Asistente de chats:** dice **Apagada**. **No lo prendas** salvo que quieras probar el guardado: si lo hacés, apagalo de nuevo (cada cambio queda en la auditoría).

### 5.4 [Navegador] La recepcionista (solo CRM)

Con su usuario, por la pestaña **CRM** (o pedile que ingrese y te cuente; **no cambies nada de su cuenta**):

1. Se abre en **Inicio** y el menú es **idéntico a antes** (Inicio · Conversaciones ▾ · Casos ▾...). **No** hay Leads, Configuración, Usuarios ni Auditoría. Pegá `.../dashboard.html#chatsia` en la barra: no abre el interruptor global.
2. En **Chats** ve el teléfono en formato WhatsApp, **puede** buscar y vincular (misma confirmación) y desvincular, y ve "Quién responde", "Tomar control" y el interruptor de IA de la conversación. **No** tiene **Ver ficha**.

### 5.5 [VPS] El interruptor global es solo del Configurador (solo lectura)

Con la contraseña de la **recepcionista** (se escribe cuando la pide; no queda en el historial) un token de CRM puro **no** puede ver el interruptor global:

```bash
read -rsp "Contraseña de la recepcionista: " PW; echo; T=$(curl -s -X POST http://127.0.0.1:5000/login -H 'Content-Type: application/json' -d "{\"username\":\"USUARIO_RECEPCIONISTA\",\"password\":\"$PW\",\"modulo\":\"crm\"}" | python3 -c "import sys,json; print(json.load(sys.stdin)['token'])"); unset PW; curl -s -o /dev/null -w 'recepcionista -> interruptor global de IA: %{http_code}\n' http://127.0.0.1:5000/configuracion/chats-ia -H "Authorization: Bearer $T"; curl -s -o /dev/null -w 'recepcionista -> buscar pacientes: %{http_code}\n' "http://127.0.0.1:5000/mensajeria/pacientes/buscar?q=abc" -H "Authorization: Bearer $T"; unset T
```

**Esperado:** `403` para el interruptor global y `200` para el buscador. (Que la sesión de Administración puede usar **ambos** lo comprueba el paso 5.3 y las pruebas automáticas.)

### 5.6 [VPS] La auditoría y su cadena

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -W -Q "SELECT TOP 8 IdLog, Categoria, Accion, Entidad, IdEntidad, Detalle FROM LogAuditoria WHERE Entidad IN ('ConversacionVinculo', 'ConversacionIa', 'ConfiguracionOrganizacion') ORDER BY IdLog DESC; EXEC sp_VerificarCadenaAuditoria"
```

**Esperado:** si hiciste el ensayo del paso 5.3, una fila `Administrativa` por cada vincular y desvincular (`Conversación #N, paciente #M, origen busqueda`), **sin número de teléfono, sin nombre y sin DNI**, y `Íntegra`. Si no hiciste cambios, ninguna fila y `Íntegra`.

## Paso 6. Sesiones abiertas

Los tokens ya emitidos **no cambian y siguen valiendo hasta que vencen (8 horas)**, con **exactamente lo que tenían**: un token de CRM solo sigue sin poder tocar el interruptor global, y uno de Configurador solo sigue sin ver chats. Nadie queda con acceso de más. **Recargar la pestaña (Ctrl+F5)** para que el navegador baje las pantallas nuevas: una pestaña vieja sigue andando con la pantalla de antes (el panel viejo no usa nada de lo nuevo y la API nueva es compatible).

## Paso 7. Rollback

### 7.1 Volver a la API anterior (lo normal)

La API anterior **ignora** la columna nueva (no la lee ni la escribe) y no conoce las rutas nuevas, así que **no hace falta tocar la base**:

```bash
sudo test -d /opt/yosoy/app-anterior-$TAG-$FECHA && sudo systemctl stop yosoy-api && sudo find /opt/yosoy/app -mindepth 1 -delete && sudo cp -a /opt/yosoy/app-anterior-$TAG-$FECHA/. /opt/yosoy/app/ && sudo chown -R yosoy:yosoy /opt/yosoy/app && sudo systemctl start yosoy-api && sleep 6 && systemctl is-active yosoy-api && curl -s -o /dev/null -w "%{http_code} %{redirect_url}\n" http://localhost:5000/
```

**Esperado:** `active` y `302 ...`. **Qué se pierde:** nada de datos. **Qué hay que saber:** los **vínculos hechos a mano se conservan** (son el mismo `IdPaciente` que escribe el asistente); con la API anterior, **en modo Asistente, el asistente trata a esa persona como el paciente vinculado** (como siempre pasa con una conversación identificada) y el **panel viejo** muestra los datos reales del paciente. Si algún vínculo hecho a mano fue un error, **desvinculalo antes de volver atrás** (con la API anterior ya no hay botón; sería a mano en la base: `UPDATE Conversacion SET IdPaciente = NULL, PasoActual = 'Inicio', DatosContexto = NULL WHERE IdConversacion = N`). El interruptor global (la fila de `ConfiguracionOrganizacion`) y los de cada conversación **quedan guardados y se ignoran**.

### 7.2 Restaurar el backup (la vuelta atrás completa)

Solo si hay que dejar **toda** la base como estaba antes del paso 3. **Se pierde todo lo escrito en la base desde el backup** (turnos, mensajes, auditoría posterior). Con la copia del backup otra vez en el VPS (la tenés en la PC: `scp` al revés):

```bash
sudo systemctl stop yosoy-api && sudo cp ~/yosoy-pre-$TAG-$FECHA.bak /var/opt/mssql/backup/ && sudo chown mssql:mssql /var/opt/mssql/backup/yosoy-pre-$TAG-$FECHA.bak && sqlcmd -S localhost -U sa -C -Q "ALTER DATABASE [yosoy] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE [yosoy] FROM DISK = N'/var/opt/mssql/backup/yosoy-pre-$TAG-$FECHA.bak' WITH REPLACE, CHECKSUM; ALTER DATABASE [yosoy] SET MULTI_USER;"
```

Y después la API anterior (7.1, sin el `systemctl stop` inicial: ya está parada) y volver a verificar con el paso 2.

### 7.3 Sacar solo la columna (sin restaurar)

`docs/produccion/deshacer-migracion-25.sql` (ensayado: ver el final): borra la restricción por defecto, la columna `IaActiva` y la versión 25, en una transacción, y cuenta antes cuántas conversaciones la tenían prendida. No toca ningún otro dato (ni el interruptor global). Con la API nueva puesta no hace falta volver atrás primero (la API anda sin la columna; el interruptor por conversación responde 409), pero **lo prolijo es hacerlo con la API anterior ya puesta**:

```powershell
scp D:\Claude\YoSoy\YoSoy\docs\produccion\deshacer-migracion-25.sql agustin@179.199.155.50:/home/agustin/
```

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -b -f 65001 -i ~/deshacer-migracion-25.sql
```

**Esperado:** `Migración 25 deshecha en la base.` y una fila con `54` tablas y `24` como última versión.

---

## Límites conocidos

- **La IA no existe todavía.** Los dos interruptores guardan la decisión y la pantalla lo dice; ninguna conversación responde con IA. El asistente de reglas ("Bot") **no depende** del interruptor global: apagarlo no frena al bot (decisión del dueño). Lo que frena al bot en una conversación es **Tomar control**.
- **La migración 26 (número completo del webhook) quedó descartada** (ver arriba): un número extranjero cuyos 10 últimos dígitos empiecen con un código de área argentino se muestra como argentino. Está fijado en una prueba.
- **El asistente (modo Asistente) puede identificar a alguien por su cuenta** (un único paciente con ese teléfono, o el DNI que la persona escribe); eso **no cambia**. Lo nuevo es que una persona también puede vincular a mano, y en ese modo el asistente va a tratar a quien escribe como ese paciente (la confirmación lo advierte en rojo).
- **Desvincular no puede deshacer lo que ya se escribió con el nombre del paciente**: las notificaciones internas (la campanita) ya generadas conservan su texto. Las **notas y etiquetas** de la conversación se **conservan** a propósito (no se borran datos). Lo que sí se revierte: `IdPaciente`, el flujo del asistente (paso y datos de contexto), la IA de la conversación y los datos del panel.
- **El teléfono del paciente (texto libre) se muestra tal cual** y puede ser un fijo; el `+54 9` es solo de la conversación de WhatsApp.
- **La búsqueda de pacientes no se audita** (el texto buscado es un dato personal); lo que se limita es la cantidad de pedidos (30 por minuto por IP). Vincular y desvincular **sí** se auditan.
- **Una escritura vieja de otra parte (el asistente) todavía puede reponer el paso o el contexto del flujo** de una conversación recién desvinculada; no pisa el vínculo ni la IA. Es inofensivo: el asistente vuelve a pedir identificarse.
- **Ver ficha** existe solo en la sesión Administración (lleva a Usuarios, del Configurador). El CRM puro no lo tiene: no hay adónde llevarlo, y el panel ya muestra lo que su rol puede ver.
- **El botón "+" de Participantes** (deshabilitado, "Próximamente") no se tocó.

---

## Qué se verificó y qué no

**Verificado en la PC (SQL Server 2019 Express, node 24, Chromium con Playwright):**

| Qué | Cómo |
|---|---|
| Autorización con tokens reales | `ChatsVinculoBaseRealTests`: cada ruta nueva por 7 identidades (Administración, CRM puro, Configurador solo, Profesional, Paciente, Facturación, sin sesión): CRM puro y Administración entran, el resto 403, sin sesión 401; el interruptor global al revés (Configurador sí, CRM no). Además `ChatsSoloCrmTests` recorre por reflexión **todas** las rutas de `/mensajeria` y falla si una queda abierta |
| Vincular y desvincular | Éxito, doble vínculo al mismo paciente (sin cambios y sin fila de auditoría nueva), a otro paciente (409 y el vínculo no se pisa), paciente inexistente, inactivo, conversación inexistente, ids y cuerpos inválidos (400); reinicio del flujo del asistente; notas conservadas; la fila de auditoría es **administrativa** y no cae en la clínica, y no trae número, nombre ni DNI |
| Buscador y sugerencias | Solo las 5 propiedades, sin DNI completo ni teléfono; solo activos; comodines (`%`, `_`) como texto; mínimo de caracteres; otras conversaciones; sugerencias con los 4 formatos reales de carga del teléfono y con familia (dos pacientes); **leer la sugerencia no vincula**; `origen sugerencia` solo si de verdad coincide |
| Interruptores | Global y por conversación apagados al principio, `efectiva` solo con los dos prendidos, "quién responde" nunca dice IA, tomar control en modo Asistente y Personal, sin la migración 25 responde 409 y el panel dice "no disponible" |
| **La carrera de `Guardar`** | Pruebas deterministas, de estrés con dos hilos (60 vueltas), de dos personas vinculando a la vez y **de punta a punta por el webhook real** (mensajes entrando mientras se vincula, 25 vueltas). **Con el `Guardar` viejo fallan 5 de ellas; con el arreglo pasan todas** |
| La migración 25 | `Migracion25Tests` sobre una base descartable con la forma de producción (54 tablas, versión 24): agrega la columna apagada y la versión, idempotente, **transaccional** (una falla a la mitad no deja nada), deshacer y volver a migrar. Y **ensayo con `sqlcmd` real**: migrar, migrar de nuevo, deshacer, volver a migrar, todo con salida 0 |
| Teléfono | Tabla de áreas de **ENACOM** (300 códigos: 1 de 2 dígitos, 38 de 3, 261 de 4) con prueba de las ciudades principales (11, 351, 341, 261, 221, 223, 381, 299 y los de 4 dígitos 2966, 2901, 2202, 3435); números extranjeros; el límite conocido |
| Las pantallas | `ChatsVistaPantallaTests` (lee el código y lo compila con node) y **un navegador real (Chromium) contra la API de verdad y una base descartable**, en modo Asistente y en modo Personal, con Administración, CRM puro y Configurador puro: **66 y 62 comprobaciones**, sin errores de consola |
| Todo lo demás sigue igual | La suite completa, y la suite sobre la **forma de producción** (54 tablas, versión 24, sin la columna de la 25) con las pruebas que necesitan la 25 filtradas: ver el informe de la rama para las cuentas finales |

**No verificado (no se pudo desde la PC):** nada de esto se ejecutó en el VPS ni contra la base real de producción. Con un WhatsApp real no se probó (el simulador y el webhook con su secreto sí). Con Edge o Safari no se probó (Chromium sí). SQL Server 2025 del VPS: la migración y las consultas son T-SQL común y se ensayaron en 2019. Los datos del paso "Lo que se verificó en producción" salen de la consulta que corrió Agustín el 21/10/2026.
