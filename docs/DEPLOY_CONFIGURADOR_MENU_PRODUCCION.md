# Despliegue a producción: menú nuevo del Configurador

Runbook para llevar a producción el rediseño del menú del panel del **Configurador**: sin "Inicio", con **Configuración** (WhatsApp con el código QR de conexión y Especialidades), **Usuarios** (Registrar usuario, Permisos, Usuarios, Solicitudes de registro) y **Auditoría**. Es **solo la API** (y las pantallas que van dentro del paquete): **sin migraciones, sin cambios de API, sin cambios de seguridad, sin variables nuevas**. Se ejecuta **a mano, en dos máquinas** (la PC con PowerShell y el VPS por SSH), con el flujo de siempre de [`DEPLOY.md`](DEPLOY.md) y el mismo formato que [`DEPLOY_WHATSAPP_ESTADOS_PRODUCCION.md`](DEPLOY_WHATSAPP_ESTADOS_PRODUCCION.md). Escrito el 2026-10-20 contra la rama `feat/configurador-menu` (sale de `main` en `d3b63d4`); **antes de armar el paquete tiene que estar mergeada en `main`** (paso 0).

- **Se escribió sin acceso al VPS y se ejecutó a mano el 20/10/2026** (API desde `main` `5670d15`, sin migraciones, rollback en `/opt/yosoy/app-anterior-cm-20261020`; el estado, en [`SERVIDOR.md`](SERVIDOR.md)). Lo que se probó antes, y cómo, está en "Qué se verificó y qué no", al final; lo que no, también.
- **Sin secretos en este archivo.** Ningún comando imprime un secreto ni el contenido de un `.env`.
- Cada paso dice **dónde** se corre, el **comando exacto**, qué **esperar** y cómo **volver atrás**.

## Qué sale a producción (y qué no)

| Qué | Cómo queda |
|---|---|
| **Pantallas** (van dentro del paquete de la API) | El menú del Configurador: **Configuración ▾** (WhatsApp, Especialidades), **Usuarios ▾** (Registrar usuario, Permisos, Usuarios, Solicitudes de registro) y **Auditoría**. Sin "Inicio" ni su resumen. La pantalla que se llamaba "Cuentas" ahora se llama **Usuarios** (solo el nombre; su contenido es el mismo). Al ingresar se aterriza en **Usuarios** |
| Fuera del menú (el código, los endpoints y los datos **siguen**) | Grupos, Reglas de derivación, Coberturas, Privilegios, Roles y Recuperación de contraseña. Un enlace directo a cualquiera de ellas (`#grupos`, `#recuperacion`, `#inicio`…) lleva a **Usuarios**, sin error ni pantalla en blanco. "Recuperación de contraseña" era una entrada sin pantalla ni endpoint: no se pierde nada. Los **privilegios** (Admisión, Gestión de agendas) se asignan desde **Permisos**, como hasta ahora |
| Grupo **CRM ▾** en el Configurador | Preparado y **apagado**: aparece solo si la sesión trae el permiso `crm`. Hoy el token del Configurador no lo trae (cada módulo tiene su token), así que **no se ve** |
| **CRM, Profesional y Paciente** | **Sin cambios.** La recepcionista del CRM ve exactamente el menú de hoy |
| API | **Sin cambios.** Ningún endpoint, permiso ni regla de acceso cambia: el menú es solo lo que se muestra; el servidor sigue controlando cada pedido |
| Migraciones | **No hay.** La base queda exactamente como está |
| `whatsapp-service`, Caddy, variables de entorno (`.env`) | **No se tocan** |

## Convenciones

- **[PC]** es PowerShell en tu PC. **[VPS]** es una sesión SSH que ya abriste (`ssh agustin@179.199.155.50`): pegá las líneas ahí. No uses `ssh servidor 'comando con sudo'`. Los comandos del VPS que **cambian algo** son **una sola línea** con `&&`. **Pegá un comando por vez**, y corré `sudo -v` antes de empezar.
- `FECHA` es **la de la PC** (`Get-Date -Format yyyyMMdd`). `TAG` es `cm`. Lo primero en la sesión SSH (y de nuevo si la cerrás):
  ```bash
  FECHA=YYYYMMDD; TAG=cm      # FECHA: la de la PC
  ```

## El orden, de un vistazo

| Paso | Qué | Dónde | Corta el servicio | Se deshace |
|---|---|---|---|---|
| 0 | El código que vas a desplegar | PC | No | — |
| 1 | Publicar y subir la API | PC | No | — |
| 2 | Backup de la app anterior, corte y arranque | VPS | La API, unos segundos | Sí (paso 5) |
| 3 | Verificar | VPS y navegador | No | — |
| 4 | Sesiones abiertas | — | — | — |
| 5 | Rollback | VPS | La API, unos segundos | — |

**No hay backup de la base ni paso de migraciones porque no se toca la base.** Lo único que se reemplaza es `/opt/yosoy/app`, y de eso queda copia. Elegí un horario de poco uso: la API se reinicia unos segundos. `whatsapp-service` no se reinicia y sigue conectado.

---

## Paso 0. [PC] El código que vas a desplegar

```powershell
cd D:\Claude\YoSoy\YoSoy
git switch main; git pull --ff-only; git status --short; git log -1 --oneline
(Get-ChildItem YoSoy.Tests\ConfiguradorMenuTests.cs, docs\DEPLOY_CONFIGURADOR_MENU_PRODUCCION.md | Measure-Object).Count
Select-String -Path PANTALLAS\shared\shell.js -Pattern 'grupoDeChats' -Quiet
$fecha = Get-Date -Format yyyyMMdd; $fecha                  # anotá el valor: es la FECHA que se usa en el VPS
```

**Esperado:** `git status` sin líneas; `git log -1` es el merge del PR de esta mejora o uno posterior; el conteo da `2` y `Select-String` da `True`. Si no, tu `main` todavía no la trae: mergeá el PR antes de seguir.

## Paso 1. [PC] Publicar y subir (el flujo de siempre: autocontenido, fuera del repositorio)

```powershell
cd D:\Claude\YoSoy\YoSoy
$fecha = Get-Date -Format yyyyMMdd; $tag = 'cm'
dotnet publish YoSoy.csproj -c Release -r linux-x64 --self-contained true -o "D:\Claude\Publish-YoSoy-${fecha}-${tag}"
tar -czf "$env:TEMP\Publish-YoSoy-${fecha}-${tag}.tar.gz" -C "D:\Claude\Publish-YoSoy-${fecha}-${tag}" .
scp "$env:TEMP\Publish-YoSoy-${fecha}-${tag}.tar.gz" agustin@179.199.155.50:/home/agustin/
```

**Esperado:** el `publish` termina sin errores (los avisos de nulabilidad `CS86xx` son normales); el paquete trae el binario `YoSoy`, `appsettings.json` y `PANTALLAS\` (con `shared\shell.js`), y ningún `.env`.

## Paso 2. [VPS] Backup de la app anterior, corte y arranque

Desempaquetar y comprobar que está completo:

```bash
test -f ~/Publish-YoSoy-$FECHA-$TAG.tar.gz && mkdir ~/Publish-YoSoy-$FECHA-$TAG && tar -xzf ~/Publish-YoSoy-$FECHA-$TAG.tar.gz -C ~/Publish-YoSoy-$FECHA-$TAG && ls -la ~/Publish-YoSoy-$FECHA-$TAG/YoSoy ~/Publish-YoSoy-$FECHA-$TAG/PANTALLAS/shared/shell.js
```

**Esperado:** dos archivos listados. Si el `.tar.gz` no está o el `mkdir` dice que ya existe, la `FECHA` está mal o quedó algo de antes: **no sigas**.

Backup `app-anterior-cm-<fecha>`, corte y arranque:

```bash
test -f ~/Publish-YoSoy-$FECHA-$TAG/YoSoy && sudo test ! -e /opt/yosoy/app-anterior-$TAG-$FECHA && sudo cp -a /opt/yosoy/app /opt/yosoy/app-anterior-$TAG-$FECHA && sudo diff -rq /opt/yosoy/app /opt/yosoy/app-anterior-$TAG-$FECHA && echo "backup idéntico" && sudo systemctl stop yosoy-api && sudo find /opt/yosoy/app -mindepth 1 -delete && sudo cp -a ~/Publish-YoSoy-$FECHA-$TAG/. /opt/yosoy/app/ && sudo chown -R yosoy:yosoy /opt/yosoy/app && sudo chmod +x /opt/yosoy/app/YoSoy && sudo systemctl start yosoy-api && sleep 6 && systemctl is-active yosoy-api && curl -s -o /dev/null -w "%{http_code} %{redirect_url}\n" http://localhost:5000/
```

**Esperado:** `backup idéntico`, `active` y `302 http://localhost:5000/pantallas/index.html`. La cadena se frena **antes** de parar la API si el publish no está desempaquetado, si el backup ya existe o si `diff -rq` encuentra diferencias. Si se corta **después** de `systemctl stop`, la API quedó parada: corré el rollback (paso 5). **No hace falta tocar el `.env`.**

## Paso 3. Verificar

### 3.1 [VPS] El servicio y el entorno

```bash
sudo journalctl -u yosoy-api --since '5 minutes ago' --no-pager | grep -iE 'error|exception|fail|Hosting environment'; systemctl is-active whatsapp-service
```

**Esperado:** **`Hosting environment: Production`**, ninguna excepción, y `active` para `whatsapp-service` (no se tocó y no se reinició).

### 3.2 [VPS] Las pantallas nuevas llegaron

```bash
curl -s http://localhost:5000/pantallas/shared/shell.js | grep -c 'grupoDeChats'; curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5000/pantallas/Pantalla_Configurador/dashboard.html
```

**Esperado:** un número mayor que `0` (el `shell.js` nuevo) y `200`.

### 3.3 [Navegador] A ojo, módulo por módulo (con Ctrl+F5 antes: ver paso 4)

Si usás las cuentas de siempre, con una sesión de cada una. **Ninguna de estas pruebas cambia datos.**

1. **Configurador** (ingresá por la pestaña **Configurador**):
   - Se abre directo en **Usuarios**. La barra tiene **Configuración ▾**, **Usuarios ▾** y **Auditoría**, y **no** tiene "Inicio" ni "CRM".
   - **Configuración ▾**: **WhatsApp** y **Especialidades**. **Usuarios ▾**: **Registrar usuario**, **Permisos**, **Usuarios**, **Solicitudes de registro**.
   - **WhatsApp** muestra el estado y, si hace falta, el código QR (**no** desvincules ni pidas un código con el número conectado: alcanza con mirar el estado).
   - **Permisos** abre la lista y **Editar permisos** muestra los privilegios (Admisión, Gestión de agendas) junto a cada rol. **Cerrá el cuadro con Cancelar.**
   - **Enlaces directos:** pegá en la barra de direcciones `.../Pantalla_Configurador/dashboard.html#grupos`, `#recuperacion`, `#inicio` y `#roles`: las cuatro terminan en **Usuarios**, sin error ni pantalla en blanco.
   - Hacé clic en el logo de YoSoy: va a **Usuarios**.
2. **CRM** (la recepcionista, o el administrador eligiendo el módulo **CRM**): el menú es **exactamente el de siempre**: **Inicio**, **Conversaciones ▾** (Chats, Escalamientos, Etiquetas de chats) y **Casos ▾**; con Gestión de agendas, también **Agendas ▾**. **Chats** abre la bandeja. Sin ninguna opción de configuración.
3. **Administrador con Configurador y CRM**: al ingresar por la pestaña Configurador entra con el menú nuevo (**sin** CRM: su token es del módulo Configurador). Menú de usuario → **Cambiar de módulo** → **CRM**: el menú del CRM de siempre. Esto no cambia: cada módulo sigue teniendo su token.
4. **Profesional** y **Paciente**: abren su panel como siempre (basta con ver que cargan y que el menú es el de antes).

### 3.4 [VPS] Que la API no cambió (solo lectura)

```bash
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5000/usuarios; curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5000/mensajeria/conversaciones
```

**Esperado:** `401` y `401` (sin sesión siguen pidiendo ingresar). Ocultar un menú no abre ni cierra ninguna ruta.

## Paso 4. Sesiones abiertas

No hace falta que nadie vuelva a ingresar: los tokens no cambian. Sí hace falta **recargar la pestaña** (Ctrl+F5) para que el navegador baje las pantallas nuevas; una pestaña vieja sigue andando con el menú de antes, que es compatible con la API (no cambió).

## Paso 5. Rollback

La API no cambió y la base tampoco, así que **no hace falta tocar nada más**:

```bash
sudo test -d /opt/yosoy/app-anterior-$TAG-$FECHA && sudo systemctl stop yosoy-api && sudo find /opt/yosoy/app -mindepth 1 -delete && sudo cp -a /opt/yosoy/app-anterior-$TAG-$FECHA/. /opt/yosoy/app/ && sudo chown -R yosoy:yosoy /opt/yosoy/app && sudo systemctl start yosoy-api && sleep 6 && systemctl is-active yosoy-api && curl -s -o /dev/null -w "%{http_code} %{redirect_url}\n" http://localhost:5000/
```

**Esperado:** `active` y `302 ...`. Vuelve el menú anterior del Configurador (con Inicio, Cuentas y las secciones ocultas). **Qué se pierde:** nada. Es solo el aspecto del menú: no hay datos ni auditoría de por medio. `whatsapp-service` no se tocó en ningún momento.

---

## Qué se verificó y qué no

**Verificado en la PC (SQL Server 2019 Express, node 24):**

| Qué | Cómo |
|---|---|
| El menú de cada módulo | `ConfiguradorMenuTests` carga el `shell.js` real con node y comprueba: el menú del Configurador (Configuración, Usuarios, Auditoría, sin Inicio, en ese orden y con esos ítems); que **ninguna** de las secciones ocultas (inicio, grupos, reglas de derivación, coberturas, privilegios, roles, recuperación) está en el menú; el grupo CRM solo con el permiso `crm` y solo cuando la mensajería está disponible; que los menús del CRM (con y sin Agendas), del Profesional (con Admisión y auditor) y del Paciente son los de siempre. El Profesional y el Paciente no ven chats |
| La sincronía con `dashboard.js` | Se evalúa el literal `SECCIONES_POR_MODULO` y se compara con los ids del menú (Configurador con y sin `crm`, CRM con y sin Gestión de agendas): son los mismos |
| Aterrizaje, marca y código conservado | Pruebas de texto: aterrizaje en Usuarios, un enlace a una sección fuera del menú cae ahí, la marca de la barra y `agregarGrupo` sin depender de "Inicio", y los `render*` ocultos siguen en el código |
| En un navegador real | Chromium con Playwright contra las pantallas reales (con la API simulada: no hay datos): el menú del Configurador, de `crm` y del Configurador con permiso `crm`; el aterrizaje; que `#grupos`, `#reglasderivacion`, `#coberturas`, `#privilegios`, `#roles`, `#recuperacion`, `#inicio`, `#escalamientos` y un enlace inexistente terminan en Usuarios con contenido (6 enlaces visibles abren su sección); la entrada directa por `#recuperacion`; que el CRM aterriza en Inicio y que `#usuarios`, `#grupos`, `#permisos` y `#whatsapp` le caen a Inicio; Chats y Escalamientos abren; **sin errores de JavaScript** en ninguna de las sesiones. Son 38 verificaciones, todas bien. El script no se versiona |
| Resultados de las suites | API en `yosoy_test`: **1959 pasadas, 0 fallidas, 0 omitidas** (`Scripts\correr-tests.ps1`). Variante con la **forma de producción** (sin la migración 16, con la 17; base nueva `-SinConsentimientoWhatsApp`): **1914 pasadas, 0 fallidas** (`correr-tests.ps1 -Base <base> -Filtro 'Tabla!=ConsentimientoWhatsApp'`). Sin las migraciones 16 y 17: **1879 pasadas, 0 fallidas**. `whatsapp-service` (`npm test`): **454 pruebas, 448 pasan, 0 fallan, 6 omitidas** (sin cambios). Las bases nuevas se borraron al terminar |

**No verificado:**
- **Con la API y los datos reales.** El recorrido de navegador usó una API simulada (listas vacías): el contenido de cada pantalla es el de siempre y no se tocó, pero lo que muestra con datos de producción se ve recién en el paso 3.3.
- **Un Configurador con el permiso `crm` en la sesión.** Hoy ningún token lo trae; el grupo CRM está probado con el permiso simulado, no con un ingreso real. Cuando un token lo traiga, hay que volver a mirar este menú.
- **El navegador del equipo (Safari, celulares).** Solo Chromium; el panel es de uso en PC.
