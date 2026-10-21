# Despliegue a producción: vista única de Administración (Configurador + CRM) y Leads

Runbook para llevar a producción el **PR 3 del rediseño del Configurador y el CRM**: la **vista única**. Quien tiene los permisos **Configurador y CRM** (hoy, el administrador) entra directo a **una sola sesión, "Administración"**, sin selector de módulo, con **CRM ▾** (Chats, Leads, Etiquetas, Escalamientos, Casos, Derivaciones y, con el privilegio, Turnos y Agendas), **Configuración ▾** (WhatsApp con el código QR y Especialidades), **Usuarios ▾** (Registrar usuario, Permisos, Usuarios, Solicitudes de registro) y **Auditoría**; y la pantalla nueva **Leads**. Es **solo la API y las pantallas** (van dentro del paquete): **sin migraciones, sin cambios de base, sin variables nuevas**. Se ejecuta **a mano, en dos máquinas** (la PC con PowerShell y el VPS por SSH), con el flujo de siempre de [`DEPLOY.md`](DEPLOY.md) y el mismo formato que [`DEPLOY_USUARIOS_PRODUCCION.md`](DEPLOY_USUARIOS_PRODUCCION.md). Escrito el 2026-10-20 contra la rama `feat/vista-unica` (sale de `main` en `73ae5e6`); **antes de armar el paquete tiene que estar mergeada en `main`** (paso 0).

- **Se escribió sin acceso al VPS y todavía no se ejecutó.** Lo que se probó antes, y cómo, está en "Qué se verificó y qué no", al final; lo que no, también.
- **Sin secretos en este archivo.** Ningún comando imprime un secreto ni el contenido de un `.env`; las contraseñas las escribís vos cuando el comando las pide (nunca en la línea de comandos).
- Cada paso dice **dónde** se corre, el **comando exacto**, qué **esperar** y cómo **volver atrás**.
- **La recepcionista (solo CRM) no cambia en nada**: mismo ingreso, mismo menú, solo chats. Es una regla dura de este cambio y el paso 3 la comprueba.

## Qué sale a producción (y qué no)

| Qué | Cómo queda |
|---|---|
| **API: sesión de Administración** | `POST /login` y `POST /sesion/modulo`: quien tiene **los dos permisos** (Configurador y CRM) recibe una sola sesión, `administracion`. El token lleva `modulo = administracion`, los roles `configurador` y `crm`, y los privilegios de esos dos permisos (Gestión de agendas). **No** lleva Profesional, auditor, Admisión ni Facturación. Se arma **siempre en el servidor, desde la base**: el cliente no aporta roles. Pedir `crm`, `configurador` o `administracion` siendo la unión da Administración; pedir `administracion` sin tener **ambos** permisos da 403 `MODULO_NO_HABILITADO`. Con solo CRM o solo Configurador, **idéntico a antes** |
| **API: Leads** | `GET /mensajeria/leads` (rol CRM, que incluye Administración): un lead es **cualquier conversación**. Búsqueda, filtros por tipo, estado y etiqueta, paginación en el servidor, misma pertenencia que `/conversaciones`. Respuesta reducida: **sin el texto de los mensajes, sin el paso del bot y sin ids de paciente ni de profesional**; el teléfono completo sí. Solo lectura y sin auditoría de lectura (igual que `/conversaciones`) |
| **Pantallas** | El menú y el panel de Administración (reusan el mismo shell y la misma bandeja de chats: no se copió ninguna pantalla); aterriza en **Chats** si la mensajería está disponible y, si no, en **Usuarios**; la pantalla **Leads**; sin "Cambiar de módulo" para quien tiene solo la unión (si tiene además otro módulo, el selector ofrece "Administración" como **una** opción). El panel **actualiza solo** la sesión guardada de un administrador (un pedido por carga) |
| **No cambia** | El **CRM puro**: ingreso, menú y permisos. El **Configurador solo**. **Profesional, Paciente y Facturación**. Los endpoints existentes (`/mensajeria/conversaciones` incluido). El QR de WhatsApp: vincular y desvincular siguen siendo **solo del rol Configurador** (el CRM puro recibe 403) y la conexión de WhatsApp (`/mensajeria/conexion-whatsapp`), **solo del rol CRM** |
| Sigue **oculto en el menú, sin borrar nada** (código, endpoints y datos intactos) | Grupos, Reglas de derivación, Coberturas, Privilegios, Roles, Recuperación de contraseña e Inicio |
| Migraciones, `whatsapp-service`, Caddy, variables de entorno (`.env`) | **No se tocan.** La base queda exactamente como está |

## Lo que cambia para el equipo (avisalo antes)

- **El administrador** deja de elegir entre Configurador y CRM: entra a **una sola vista** (da igual la pestaña CRM o Configurador) y ve los chats, Leads, la configuración, los usuarios y la auditoría juntos. Si no se le abre sola, que recargue la pantalla con Ctrl+F5.
- **La recepcionista** no nota nada: ingresa por la pestaña CRM como siempre y ve lo de siempre. **No tiene Leads.**
- **Quien tenga un módulo más** (por ejemplo Profesional): desde el menú de usuario → **Cambiar de módulo** ve **Administración** como una sola opción.

## Convenciones

- **[PC]** es PowerShell en tu PC. **[VPS]** es una sesión SSH que ya abriste (`ssh agustin@179.199.155.50`): pegá las líneas ahí. No uses `ssh servidor 'comando con sudo'`. Los comandos del VPS que **cambian algo** son **una sola línea** con `&&`. **Pegá un comando por vez**, y corré `sudo -v` antes de empezar.
- `FECHA` es **la de la PC** (`Get-Date -Format yyyyMMdd`). `TAG` es `vu`. Lo primero en la sesión SSH (y de nuevo si la cerrás):
  ```bash
  FECHA=YYYYMMDD; TAG=vu; export PATH="$PATH:/opt/mssql-tools18/bin"      # FECHA: la de la PC
  ```
- El login administrador de SQL Server es **`sa`**; `sqlcmd` te pide la contraseña: no la escribas en el comando (`-P`). Acá solo se usa para **leer**.

## El orden, de un vistazo

| Paso | Qué | Dónde | Corta el servicio | Se deshace |
|---|---|---|---|---|
| 0 | El código que vas a desplegar | PC | No | — |
| 1 | Quién tiene la unión y quién es CRM puro (solo lectura) | VPS | No | — |
| 2 | Publicar y subir la API; backup de la app anterior, corte y arranque | PC y VPS | La API, unos segundos | Sí (paso 5) |
| 3 | Verificar | VPS y navegador | No | — |
| 4 | Sesiones abiertas | — | — | — |
| 5 | Rollback | VPS | La API, unos segundos | — |

**No hay backup de la base ni migraciones porque no se toca la base.** Lo único que se reemplaza es `/opt/yosoy/app`, y de eso queda copia. Elegí un horario de poco uso: la API se reinicia unos segundos. `whatsapp-service` no se reinicia y sigue conectado.

---

## Paso 0. [PC] El código que vas a desplegar

```powershell
cd D:\Claude\YoSoy\YoSoy
git switch main; git pull --ff-only; git status --short; git log -1 --oneline
(Get-ChildItem Services\Leads.cs, PANTALLAS\Pantalla_Configurador\js\leads.js, YoSoy.Tests\LeadsTests.cs, docs\DEPLOY_VISTA_UNICA_PRODUCCION.md | Measure-Object).Count
Select-String -Path Services\Accesos.cs -Pattern 'Administracion' -Quiet
$fecha = Get-Date -Format yyyyMMdd; $fecha                  # anotá el valor: es la FECHA que se usa en el VPS
```

**Esperado:** `git status` sin líneas; `git log -1` es el merge del PR de esta mejora o uno posterior; el conteo da `4` y `Select-String` da `True`. Si no, tu `main` todavía no la trae: mergeá el PR antes de seguir.

## Paso 1. [VPS] Quién tiene la unión y quién es CRM puro (solo lectura)

La sesión de Administración la reciben **solo** quienes tienen **los dos** permisos. Antes de desplegar conviene saber quiénes son y que la recepcionista no esté entre ellos:

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -W -Q "SELECT u.IdUsuario, u.Username, u.Estado, CASE WHEN SUM(CASE WHEN p.Clave = 'configurador' THEN 1 ELSE 0 END) > 0 THEN 'si' ELSE 'no' END AS configurador, CASE WHEN SUM(CASE WHEN p.Clave = 'crm' THEN 1 ELSE 0 END) > 0 THEN 'si' ELSE 'no' END AS crm FROM Usuario u JOIN UsuarioPermiso up ON up.IdUsuario = u.IdUsuario JOIN Permiso p ON p.IdPermiso = up.IdPermiso WHERE p.Clave IN ('configurador', 'crm') GROUP BY u.IdUsuario, u.Username, u.Estado ORDER BY u.IdUsuario"
```

**Esperado (según el estado del 20/10/2026):** el administrador con `configurador = si` y `crm = si` (**es el único que pasa a Administración**) y la recepcionista con `configurador = no` y `crm = si` (**no cambia**). Es una consulta que muestra nombres de usuario: no se guarda. Si la recepcionista aparece con `configurador = si`, **pará**: pasaría a la vista única y vería lo del Configurador; quitale el permiso desde Configurador › Permisos **antes** de seguir.

## Paso 2. Publicar la API y las pantallas

### 2.1 [PC] Publicar y subir (el flujo de siempre: autocontenido, fuera del repositorio)

```powershell
cd D:\Claude\YoSoy\YoSoy
$fecha = Get-Date -Format yyyyMMdd; $tag = 'vu'
dotnet publish YoSoy.csproj -c Release -r linux-x64 --self-contained true -o "D:\Claude\Publish-YoSoy-${fecha}-${tag}"
tar -czf "$env:TEMP\Publish-YoSoy-${fecha}-${tag}.tar.gz" -C "D:\Claude\Publish-YoSoy-${fecha}-${tag}" .
scp "$env:TEMP\Publish-YoSoy-${fecha}-${tag}.tar.gz" agustin@179.199.155.50:/home/agustin/
```

**Esperado:** el `publish` termina sin errores (los avisos de nulabilidad `CS86xx` son normales); el paquete trae el binario `YoSoy`, `appsettings.json` y `PANTALLAS\` (con `Pantalla_Configurador\js\leads.js`), y ningún `.env`.

### 2.2 [VPS] Desempaquetar y comprobar que está completo

```bash
test -f ~/Publish-YoSoy-$FECHA-$TAG.tar.gz && mkdir ~/Publish-YoSoy-$FECHA-$TAG && tar -xzf ~/Publish-YoSoy-$FECHA-$TAG.tar.gz -C ~/Publish-YoSoy-$FECHA-$TAG && ls -la ~/Publish-YoSoy-$FECHA-$TAG/YoSoy ~/Publish-YoSoy-$FECHA-$TAG/PANTALLAS/Pantalla_Configurador/js/leads.js
```

**Esperado:** dos archivos listados. Si el `.tar.gz` no está o el `mkdir` dice que ya existe, la `FECHA` está mal o quedó algo de antes: **no sigas**.

### 2.3 [VPS] Backup `app-anterior-<tag>-<fecha>`, corte y arranque

```bash
test -f ~/Publish-YoSoy-$FECHA-$TAG/YoSoy && sudo test ! -e /opt/yosoy/app-anterior-$TAG-$FECHA && sudo cp -a /opt/yosoy/app /opt/yosoy/app-anterior-$TAG-$FECHA && sudo diff -rq /opt/yosoy/app /opt/yosoy/app-anterior-$TAG-$FECHA && echo "backup idéntico" && sudo systemctl stop yosoy-api && sudo find /opt/yosoy/app -mindepth 1 -delete && sudo cp -a ~/Publish-YoSoy-$FECHA-$TAG/. /opt/yosoy/app/ && sudo chown -R yosoy:yosoy /opt/yosoy/app && sudo chmod +x /opt/yosoy/app/YoSoy && sudo systemctl start yosoy-api && sleep 6 && systemctl is-active yosoy-api && curl -s -o /dev/null -w "%{http_code} %{redirect_url}\n" http://localhost:5000/
```

**Esperado:** `backup idéntico`, `active` y `302 http://localhost:5000/pantallas/index.html`. La cadena se frena **antes** de parar la API si el publish no está desempaquetado, si el backup ya existe o si `diff -rq` encuentra diferencias. Si se corta **después** de `systemctl stop`, la API quedó parada: corré el rollback (paso 5). **No hace falta tocar el `.env`.**

## Paso 3. Verificar

### 3.1 [VPS] El servicio y el entorno

```bash
sudo journalctl -u yosoy-api --since '5 minutes ago' --no-pager | grep -iE 'error|exception|fail|Zona horaria|Hosting environment'; systemctl is-active whatsapp-service
```

**Esperado:** la línea `Zona horaria de la clínica: ...`, **`Hosting environment: Production`**, **ninguna excepción** y `active` para `whatsapp-service` (no se tocó y no se reinició).

### 3.2 [VPS] Lo nuevo exige sesión (solo lectura, sin sesión)

```bash
curl -s -o /dev/null -w "leads sin sesion: %{http_code}\n" http://localhost:5000/mensajeria/leads; curl -s -o /dev/null -w "conversaciones sin sesion: %{http_code}\n" http://localhost:5000/mensajeria/conversaciones; curl -s -o /dev/null -w "vincular sin sesion: %{http_code}\n" -X POST http://localhost:5000/mensajeria/whatsapp/vincular
```

**Esperado:** `401`, `401` y `401`.

### 3.3 [Navegador] El administrador (con Ctrl+F5 antes: ver paso 4)

Ingresá por la pestaña **Configurador** (o **CRM**: es lo mismo). **Esta parte no cambia datos.**

1. Entra **directo**, sin elegir módulo, y se abre en **Chats** (si la mensajería está disponible). La barra dice **CRM ▾ · Configuración ▾ · Usuarios ▾ · Auditoría**, sin Inicio. En el menú de usuario **no** hay "Cambiar de módulo".
2. **CRM ▾** lista, en este orden: **Chats, Leads, Etiquetas, Escalamientos, Casos, Derivaciones** (y **Turnos** y **Agendas** si tu usuario tiene el privilegio Gestión de agendas).
3. **Leads:** una tabla con todas las conversaciones (chip **Paciente** o **Sin identificar**, teléfono, estado, etiquetas, última actividad). Probá el buscador (un teléfono o un apellido) y los filtros de tipo, estado y etiqueta. **Hacé clic en una fila**: abre esa conversación en **Chats**. No hay botones para crear ni borrar leads, y no se ve el texto de los mensajes.
4. **Configuración ▾ › WhatsApp:** muestra el estado y, si hace falta, el código QR (**no** desvincules ni pidas un código con el número conectado: alcanza con mirar el estado). **Especialidades** abre.
5. **Usuarios ▾:** **Usuarios** muestra la lista única; **Permisos**, **Registrar usuario** y **Solicitudes de registro** abren. **Auditoría** abre.
6. **Enlaces directos:** pegá `.../Pantalla_Configurador/dashboard.html#grupos`, `#recuperacion` y `#inicio`: terminan en **Chats** (o **Usuarios** si no hay mensajería), sin error ni pantalla en blanco.
7. Si tu usuario tiene **otro módulo** (por ejemplo Profesional): menú de usuario → **Cambiar de módulo** muestra **Profesional** (desde Administración) o **Administración** como **una sola** opción (desde Profesional).

### 3.4 [Navegador] La recepcionista (solo CRM): tiene que ser idéntico a antes

Con su usuario, por la pestaña **CRM** (o pedile que ingrese y te cuente; **no cambies nada de su cuenta**):

1. Se abre en **Inicio**. La barra dice **Inicio · Conversaciones ▾ · Casos ▾** (y **Agendas ▾** si tiene el privilegio). **Conversaciones ▾** trae **Chats, Escalamientos, Etiquetas de chats**.
2. **No** hay **Leads**, ni **Configuración**, ni **Usuarios**, ni **Auditoría**, ni un grupo **CRM**. No hay "Cambiar de módulo".
3. Pegá `.../dashboard.html#leads` en la barra: no abre Leads (cae en su Inicio).
4. Chats funciona como siempre (la bandeja, responder, etiquetas).

### 3.5 [VPS] El QR sigue siendo solo del Configurador (solo lectura)

Con la contraseña de la **recepcionista** (se escribe cuando la pide; no queda en el historial) un token de CRM puro **no** puede ver el estado del QR, y sí la conexión:

```bash
read -rsp "Contraseña de la recepcionista: " PW; echo; T=$(curl -s -X POST http://127.0.0.1:5000/login -H 'Content-Type: application/json' -d "{\"username\":\"USUARIO_RECEPCIONISTA\",\"password\":\"$PW\",\"modulo\":\"crm\"}" | python3 -c "import sys,json; print(json.load(sys.stdin)['token'])"); unset PW; curl -s -o /dev/null -w 'recepcionista -> estado del QR: %{http_code}\n' http://127.0.0.1:5000/mensajeria/whatsapp/estado -H "Authorization: Bearer $T"; curl -s -o /dev/null -w 'recepcionista -> conexion de WhatsApp: %{http_code}\n' http://127.0.0.1:5000/mensajeria/conexion-whatsapp -H "Authorization: Bearer $T"; unset T
```

**Esperado:** `403` para el estado del QR y `200` para la conexión. (Que la sesión de Administración puede usar **ambos** lo comprueba el paso 3.3, punto 4, y las pruebas automáticas.)

### 3.6 [VPS] La cadena de auditoría

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -W -Q "SELECT TOP 5 IdLog, Accion, Espacio FROM LogAuditoria ORDER BY IdLog DESC; EXEC sp_VerificarCadenaAuditoria"
```

**Esperado:** los ingresos que acabás de hacer (`Ingreso`, con `administracion` en `Espacio` para el administrador y `crm` para la recepcionista) y `Íntegra`.

## Paso 4. Sesiones abiertas

Los tokens que ya están emitidos **no cambian y siguen valiendo hasta que vencen (8 horas)**, con **exactamente lo que tenían y nada más**: un token de Configurador solo sigue sin ver chats y uno de CRM solo sigue sin administrar. Nadie queda con acceso de más.

- **El administrador** con una sesión abierta antes del despliegue: al recargar la pantalla (Ctrl+F5), el panel **actualiza solo su sesión a Administración** con **un** pedido por carga. Si no puede (por ejemplo, el token ya venció o le quitaron un permiso), **borra la sesión entera** y lo lleva al ingreso con el aviso *"No pudimos actualizar tu sesión. Ingresá de nuevo con tu usuario y contraseña."* No reintenta.
- **La recepcionista**: no hace falta nada; su sesión no cambia.
- **Recargar la pestaña** (Ctrl+F5) para que el navegador baje las pantallas nuevas: una pestaña vieja sigue andando con el menú de antes, que es compatible con la API (no cambió para esos usuarios).

## Paso 5. Rollback

La API anterior **ignora** todo esto (no hay base de por medio) y vuelve al ingreso de siempre:

```bash
sudo test -d /opt/yosoy/app-anterior-$TAG-$FECHA && sudo systemctl stop yosoy-api && sudo find /opt/yosoy/app -mindepth 1 -delete && sudo cp -a /opt/yosoy/app-anterior-$TAG-$FECHA/. /opt/yosoy/app/ && sudo chown -R yosoy:yosoy /opt/yosoy/app && sudo systemctl start yosoy-api && sleep 6 && systemctl is-active yosoy-api && curl -s -o /dev/null -w "%{http_code} %{redirect_url}\n" http://localhost:5000/
```

**Esperado:** `active` y `302 ...`. **Qué se pierde:** nada de datos (no hay migración ni escritura). **Qué hay que hacer:** **volver a ingresar**. Un token `administracion` emitido con la API nueva **funciona con los roles en la API vieja** (lleva `configurador` y `crm`), pero el **front viejo lo maneja a medias**: no conoce el módulo `administracion` y muestra el menú del Configurador. Por eso, **después de volver atrás, todos los que ingresaron con la API nueva tienen que cerrar sesión y volver a ingresar** (el administrador vuelve a elegir entre Configurador y CRM, como antes). Las sesiones viejas (de antes del despliegue) siguen valiendo con su menú de siempre.

---

## Límites conocidos

- **Los tokens ya emitidos no se revocan.** Si a alguien le quitan un permiso, su sesión abierta vale hasta que vence (8 horas): el panel solo se actualiza al volver a ingresar o a recargar. Igual que antes.
- **El administrador con la unión ve los chats desde la misma sesión** (antes necesitaba cambiar a CRM). Es lo buscado: la sesión de Administración es la suma de lo que ya podía hacer.
- **Leads muestra el teléfono completo** (hace falta para las personas sin identificar) pero **nunca el texto de los mensajes**. Lo ve quien ya ve la bandeja de Chats.
- **Leads trae todas las conversaciones del servidor por pedido** y filtra y pagina en memoria: es suficiente para el volumen de hoy (la bandeja ya consulta la misma lista cada pocos segundos). Si el volumen crece, hay que paginar en la consulta SQL en otro cambio.
- **El CRM puro puede llamar a `GET /mensajeria/leads` por API** (es el mismo rol que ya lee `/conversaciones`): no tiene el ítem de menú, pero el endpoint le contestaría con lo que ya ve en la bandeja.

---

## Qué se verificó y qué no

**Verificado en la PC (SQL Server 2019 Express, node 24, Chromium con Playwright):**

| Qué | Cómo |
|---|---|
| La sesión unida en el servidor | `AccesoPorModuloTests` y `IngresoPorModuloBaseRealTests`: el administrador entra directo con una sola opción y sin selector; el token trae exactamente `modulo = administracion`, los roles `configurador` y `crm` y el privilegio de agendas (se decodifica el token real); pedir `crm`, `configurador` o `administracion` da la misma sesión; **sin los dos permisos, 403 `MODULO_NO_HABILITADO`** (solo CRM, solo Configurador, Facturación, CRM + Facturación, tanto con la pestaña como desde su propia sesión); unión + Profesional y unión + Facturación ofrecen **dos opciones** y cada token lleva solo lo suyo (sin el privilegio de Admisión en Administración); permiso quitado; tokens viejos de un solo módulo |
| **El CRM puro no cambia** | Prueba de ingreso de solo-CRM (con y sin pestaña): mismo módulo, rol, permisos y token de siempre; ve chats y la conexión de WhatsApp; **403** en usuarios, auditoría, el QR (estado, vincular y desvincular) y todo lo de Configurador. Prueba de menú: el del CRM puro idéntico con **todos sus textos** (Inicio, Conversaciones ▾ con Chats, Escalamientos y Etiquetas de chats, Casos ▾, Agendas ▾ con el privilegio), sin Leads |
| Autorización con tokens reales | Una matriz de 10 rutas por 7 identidades (Administración, CRM puro, Configurador solo, Profesional, Paciente, Facturación, sin sesión), todas con el resultado esperado (200, 403, 401 o "pasa la autorización" para el QR). Las políticas `Solo(Quien.*)` aceptan el token por sus roles (se prueba también sobre el token real): **no se tocó `Quien`** |
| Leads | `LeadsTests`: filtros y búsqueda (con tildes y por teléfono), tipo, estado, etiqueta, paginación, 400, la respuesta **sin el texto de los mensajes ni el paso del bot**, **sin auditoría de lectura** (igual que `/conversaciones`) y la autorización |
| El menú y las pantallas | `ConfiguradorMenuTests` (ejecuta `shell.js` con node): el menú de Administración con y sin chats, con y sin agendas, **sin las secciones ocultas**, Leads solo ahí, y las secciones de la pantalla coinciden con el menú; `ChatsSoloCrmTests`; `VistaUnicaSesionAntiguaTests` (ejecuta `ui.js` con node): la sesión vieja se reconoce, se actualiza con **un** pedido, y si falla se borra **entera** y se vuelve al ingreso con el motivo |
| En un navegador real | Chromium contra la API real y una base descartable: **58 de 58 verificaciones** (el administrador entra directo y aterriza en Chats, menú **CRM / Configuración / Usuarios / Auditoría** con el orden pedido, sin "Cambiar de módulo", Chats y Leads dentro del Configurador con filtros y clic que abre la conversación, WhatsApp y Usuarios andando, enlaces ocultos, la recepcionista idéntica y sin Leads ni QR, el Configurador solo, unión + Profesional con el selector de **una** opción "Administración", sesión vieja que se actualiza con un pedido, sesión inválida que vuelve al ingreso con el mensaje, sin scroll horizontal en ventana angosta y sin errores de JavaScript). El script no se versiona |
| Resultados de las suites | **API en `yosoy_test`:** **2129 pasadas, 0 fallidas, 0 omitidas** (`Scripts\correr-tests.ps1`). **Forma de producción** (base nueva `-SinConsentimientoWhatsApp`, con la 17): **2084 pasadas, 0 fallidas** (`crear-base-de-pruebas.ps1 -SinConsentimientoWhatsApp`, luego `correr-tests.ps1 -Base <base> -Filtro 'Tabla!=ConsentimientoWhatsApp'`). **Sin las migraciones 16 y 17:** **2048 pasadas y 1 falla ajena** (`-SinConsentimientoWhatsApp -SinMigracion17`, filtro `'Tabla!=ConsentimientoWhatsApp&Migracion!=17'`): `EmocionesBaseRealTests` buscaba la subcadena "sad" en un texto cifrado en base64 que la contenía por azar; la clase corrida 5 veces aislada pasó 5 de 5 (20/20 cada vez). Es una prueba anterior a este cambio y no se tocó. **`whatsapp-service` (`npm test`):** 454 pruebas, 448 pasan, 0 fallan, 6 omitidas (sin cambios). Las bases nuevas se borraron al terminar |

**No verificado:**
- **Nada contra el SQL Server 2025 del VPS** (este cambio no toca la base, pero las lecturas del paso 1 son T-SQL común y no se corrieron allá).
- **Con los datos reales de producción:** las pruebas usan bases con datos propios; el paso 3 es la comprobación a ojo con las cuentas reales.
- **El QR con el número real vinculado:** se prueba que la sesión de Administración **pasa** la autorización de vincular y desvincular y que el CRM puro recibe 403, pero con el servicio de WhatsApp simulado (no disponible); no se vincula ni desvincula nada.
- **El navegador del equipo (Safari, celulares).** Solo Chromium; el panel es de uso en PC.
- **El runbook no se ejecutó.**
