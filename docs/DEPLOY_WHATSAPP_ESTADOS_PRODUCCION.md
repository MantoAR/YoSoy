# Despliegue a producción: WhatsApp con estados claros y código QR desde la app

Runbook para llevar a producción la mejora de la vinculación de WhatsApp: la pantalla **Configurador › WhatsApp** muestra el estado real del servicio (**Conectando…**, **Esperando que escanees el código**, **Hace falta vincular el número**, **El servicio necesita revisión**, **Conectado desde …**), tiene el botón **Mostrar código QR** para cuando el código venció, y la bandeja de **Chats** del módulo CRM avisa de forma permanente cuando WhatsApp está desconectado. Es **solo la API** (y las pantallas que van dentro del paquete): **sin migraciones, sin cambios en `whatsapp-service`, sin variables nuevas**. Se ejecuta **a mano, en dos máquinas** (la PC con PowerShell y el VPS por SSH), con el flujo de siempre de [`DEPLOY.md`](DEPLOY.md) y el mismo formato que [`DEPLOY_ETAPA2_ROLES_PERMISOS_PRODUCCION.md`](DEPLOY_ETAPA2_ROLES_PERMISOS_PRODUCCION.md). Escrito el 2026-10-20 contra la rama `feat/whatsapp-estados-y-qr` (sale de `main` en `56c17d9`); **antes de armar el paquete tiene que estar mergeada en `main`** (paso 0).

- **Se escribió sin acceso al VPS y todavía no se ejecutó.** Lo que se probó antes, y cómo, está en "Qué se verificó y qué no", al final; lo que no, también.
- **Sin secretos en este archivo.** Ningún comando imprime un secreto ni el contenido de un `.env`. El número de la clínica no figura en ningún comando.
- Cada paso dice **dónde** se corre, el **comando exacto**, qué **esperar** y cómo **volver atrás**.

## Qué sale a producción (y qué no)

| Qué | Cómo queda |
|---|---|
| **API** | `GET /mensajeria/whatsapp/estado` suma `estado`, `motivo`, `desde` e `intentosDeReconexion`. Ruta nueva `POST /mensajeria/whatsapp/vincular` (Configurador): pide un código QR, con **enfriamiento de 30 s** en el servidor y auditoría. Ruta nueva `GET /mensajeria/conexion-whatsapp` (módulo CRM): solo dice si WhatsApp está conectado, **sin número ni QR** |
| **Pantallas** (van dentro del paquete de la API) | Configurador › WhatsApp con los estados y el botón nuevos; cuadro propio para **Desvincular** (avisa que se borra la sesión y que hará falta un QR nuevo); aviso permanente en **Chats** y "Enviar por" deshabilitado mientras WhatsApp está desconectado |
| Migraciones | **No hay.** La base queda exactamente como está (54 tablas, versión 23) |
| `whatsapp-service` | **No se toca.** No se reinicia, no se vuelve a vincular, no se publica nada de él. Su comportamiento de reintento, ventanas de QR y apagado ordenado sigue igual |
| Variables de entorno (`.env`) | **Ninguna nueva** |
| Caddy | **No se toca** |

Lo que cambia para quien opera:
- La pantalla deja de mostrar "Desconectado / Generando el código QR" sin salida: mientras el servicio **abre o reintenta** la conexión dice **Conectando…** (sin saltos entre estados) y, cuando el código venció, **ofrece el botón** para pedir uno.
- Si el servicio necesita revisión (cuenta restringida, sesión reemplazada, versión rechazada, sesión inválida), la pantalla **solo informa el motivo**, sin botones: un código QR no lo arregla y el servicio no reintenta solo.
- **"Generar uno nuevo" y "Mostrar código QR" esperan 30 s entre un pedido y el siguiente** (también si el pedido viene de otra pestaña): pedir códigos en bucle es lo que puede terminar en un bloqueo del número. Es el único freno nuevo y no afecta a un servicio conectado.
- El equipo de **Chats** ve un aviso arriba de la bandeja cuando WhatsApp no está conectado, que aclara que **la vinculación la hace el Configurador**. Con el canal simulado no aparece nada.

## Convenciones

- **[PC]** es PowerShell en tu PC. **[VPS]** es una sesión SSH que ya abriste (`ssh agustin@179.199.155.50`): pegá las líneas ahí. No uses `ssh servidor 'comando con sudo'`. Los comandos del VPS que **cambian algo** son **una sola línea** con `&&`. **Pegá un comando por vez**, y corré `sudo -v` antes de empezar.
- `FECHA` es **la de la PC** (`Get-Date -Format yyyyMMdd`). `TAG` es `ws`. Lo primero en la sesión SSH (y de nuevo si la cerrás):
  ```bash
  FECHA=YYYYMMDD; TAG=ws      # FECHA: la de la PC
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

**No hay backup de la base ni paso de migraciones porque no se toca la base.** Lo único que se reemplaza es `/opt/yosoy/app`, y de eso queda copia. Elegí un horario de poco uso: la API se reinicia unos segundos. **`whatsapp-service` no se reinicia y sigue conectado** mientras tanto (los mensajes entrantes que lleguen justo en esos segundos reintentan solos).

---

## Paso 0. [PC] El código que vas a desplegar

```powershell
cd D:\Claude\YoSoy\YoSoy
git switch main; git pull --ff-only; git status --short; git log -1 --oneline
(Get-ChildItem PANTALLAS\shared\whatsapp-estado.js, docs\DEPLOY_WHATSAPP_ESTADOS_PRODUCCION.md | Measure-Object).Count
$fecha = Get-Date -Format yyyyMMdd; $fecha                  # anotá el valor: es la FECHA que se usa en el VPS
```

**Esperado:** `git status` sin líneas; `git log -1` es el merge del PR de esta mejora o uno posterior; el conteo da `2`. Si da menos, tu `main` todavía no la trae: mergeá el PR antes de seguir.

## Paso 1. [PC] Publicar y subir (el flujo de siempre: autocontenido, fuera del repositorio)

```powershell
cd D:\Claude\YoSoy\YoSoy
$fecha = Get-Date -Format yyyyMMdd; $tag = 'ws'
dotnet publish YoSoy.csproj -c Release -r linux-x64 --self-contained true -o "D:\Claude\Publish-YoSoy-${fecha}-${tag}"
tar -czf "$env:TEMP\Publish-YoSoy-${fecha}-${tag}.tar.gz" -C "D:\Claude\Publish-YoSoy-${fecha}-${tag}" .
scp "$env:TEMP\Publish-YoSoy-${fecha}-${tag}.tar.gz" agustin@179.199.155.50:/home/agustin/
```

**Esperado:** el `publish` termina sin errores (los avisos de nulabilidad `CS86xx` son normales); el paquete trae el binario `YoSoy`, `appsettings.json` y `PANTALLAS\` (con `shared\whatsapp-estado.js`), y ningún `.env`.

## Paso 2. [VPS] Backup de la app anterior, corte y arranque

Desempaquetar y comprobar que está completo:

```bash
test -f ~/Publish-YoSoy-$FECHA-$TAG.tar.gz && mkdir ~/Publish-YoSoy-$FECHA-$TAG && tar -xzf ~/Publish-YoSoy-$FECHA-$TAG.tar.gz -C ~/Publish-YoSoy-$FECHA-$TAG && ls -la ~/Publish-YoSoy-$FECHA-$TAG/YoSoy ~/Publish-YoSoy-$FECHA-$TAG/PANTALLAS/shared/whatsapp-estado.js
```

**Esperado:** dos archivos listados. Si el `.tar.gz` no está o el `mkdir` dice que ya existe, la `FECHA` está mal o quedó algo de antes: **no sigas**.

Backup `app-anterior-<tag>-<fecha>`, corte y arranque:

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

### 3.2 [VPS] El módulo compartido llega y el estado trae los datos nuevos

```bash
curl -s -o /dev/null -w "%{http_code} %{content_type}\n" http://localhost:5000/pantallas/shared/whatsapp-estado.js; curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5000/mensajeria/whatsapp/estado; curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:5000/mensajeria/whatsapp/vincular; curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5000/mensajeria/conexion-whatsapp
```

**Esperado:** `200` con un tipo de JavaScript, y después `401`, `401` y `401` (sin sesión las tres rutas nuevas o modificadas piden ingresar; ninguna imprime datos).

### 3.3 [Navegador] Lo importante, a ojo (con sesión de Configurador, ingresada de nuevo)

1. **Configurador › WhatsApp** con el número conectado: el chip dice **Conectado (+número)** y debajo **Conectado desde `<fecha y hora>`**. El botón **Desvincular y cambiar número** abre un **cuadro de la aplicación** (no el del navegador) que dice que se borra la sesión de WhatsApp y que habrá que escanear un código QR nuevo. **Cancelalo**: no hace falta desvincular para verificar.
2. **Chats** (módulo CRM, con otra sesión o cambiando de módulo): con WhatsApp conectado **no aparece ningún aviso** y "Enviar por" está habilitado.
3. **Comprobación opcional del aviso** (corta WhatsApp unos segundos; hacela en un momento de poco uso o salteala): `sudo systemctl stop whatsapp-service`. En unos segundos la pantalla de WhatsApp dice **Servicio no disponible** y **Chats** muestra el aviso permanente con "Enviar por" deshabilitado y la aclaración de que la vinculación la hace el Configurador. Después `sudo systemctl start whatsapp-service`: pasa por **Conectando…** y vuelve a **Conectado** sin pedir un QR (la sesión se conserva). **No hace falta probar "Mostrar código QR" en producción**: con el número conectado el botón no se ofrece, y la ruta responde `409 ya_vinculado` si se la llama igual.

### 3.4 [VPS] Que quedó auditado lo que corresponde (solo lectura)

```bash
sqlcmd -S localhost -d yosoy -U <usuario-administrador> -C -I -Q "SELECT TOP 5 FechaHora, Accion FROM LogAuditoria WHERE Entidad = 'WhatsApp' ORDER BY FechaHora DESC"
```

**Esperado:** no aparece ningún "Pedido de código QR" ni "Código QR nuevo" si no usaste esos botones. `sqlcmd` te pide la contraseña (no la escribas en el comando).

## Paso 4. Sesiones abiertas

No hace falta que nadie vuelva a ingresar: los tokens no cambian. Sí hace falta **recargar la pestaña** (Ctrl+F5) para que el navegador baje las pantallas nuevas; una pestaña vieja sigue andando con la pantalla de antes, que es compatible con la API nueva.

## Paso 5. Rollback

La API anterior **ignora** los campos y rutas nuevas y la base no cambió, así que **no hace falta tocar nada más**:

```bash
sudo test -d /opt/yosoy/app-anterior-$TAG-$FECHA && sudo systemctl stop yosoy-api && sudo find /opt/yosoy/app -mindepth 1 -delete && sudo cp -a /opt/yosoy/app-anterior-$TAG-$FECHA/. /opt/yosoy/app/ && sudo chown -R yosoy:yosoy /opt/yosoy/app && sudo systemctl start yosoy-api && sleep 6 && systemctl is-active yosoy-api && curl -s -o /dev/null -w "%{http_code} %{redirect_url}\n" http://localhost:5000/
```

**Esperado:** `active` y `302 ...`. Vuelve la pantalla de WhatsApp de antes (con sus límites: "Desconectado / Generando QR" sin botón cuando el código vence). **Qué se pierde:** nada de datos. Las auditorías "Pedido de código QR de WhatsApp" y "Código QR nuevo de WhatsApp" que se hayan registrado **quedan** (la auditoría es de solo agregar). `whatsapp-service` no se tocó en ningún momento, así que sigue conectado.

---

## Qué se verificó y qué no

**Verificado en la PC (SQL Server 2019 Express):**

| Qué | Cómo |
|---|---|
| Rutas nuevas y modificadas, con un `whatsapp-service` falso | Pruebas automáticas: `estado` trae `estado`/`motivo`/`desde`/`intentosDeReconexion` y sigue andando si solo falla `/salud`; `vincular` da 401 sin sesión y 403 al profesional y al CRM, abre la ventana y **audita sin número ni QR**, respeta el enfriamiento (429 con los segundos que faltan, sin llamar al servicio), no abre otro código si ya hay uno esperando, con `nuevo` reemplaza el código (relee el estado justo antes y, si entre medio se escaneó y ya quedó conectado, responde 409 `ya_vinculado` sin desvincular; no desvincula nunca un número vinculado), da 409 si ya está vinculado / necesita revisión / está conectando, 503 sin excepción con el servicio caído y tiene límite de pedidos por IP; `conexion-whatsapp` solo la ve el CRM (401/403 al resto), no trae número ni QR y con el canal simulado dice `aplica:false` sin llamar al servicio |
| La traducción de estados | Pruebas con `node` sobre `PANTALLAS/shared/whatsapp-estado.js` (la misma función que usan las dos pantallas): "Conectando…" para `iniciando`/`reconectando`/`detenido`, el botón en `qr_vencido` y `sesion_cerrada`, el QR con "Generar uno nuevo" en `esperando_escaneo`, solo texto para `requiere_atencion`, un estado desconocido mostrado como tal ("Estado no reconocido (…)", sin botones), el texto "Lleva más de N minutos sin conectar" pasados 5 minutos en `iniciando`/`reconectando`, y una API vieja sin `estado` |
| Las pantallas usan eso como corresponde | Pruebas de texto sobre `dashboard.js`, `bandeja.js`, `shell.js` y los `.html` (el cuadro de desvincular es el propio de la aplicación, el enfriamiento, el aviso y "Enviar por" deshabilitado). **No** son pruebas de navegador |
| Resultados de las suites | API en `yosoy_test`: **1948 pasadas, 0 fallidas, 0 omitidas**. Con la **forma de producción** (sin la migración 16, con la 17): **1903 pasadas, 0 fallidas**. `whatsapp-service`: **454 pruebas, 448 pasan, 0 fallan, 6 omitidas** (sin cambios) |

**No verificado:**
- **Las pantallas en un navegador real.** El recorrido visual (aspecto del aviso, la cuenta regresiva del botón, el cuadro de desvincular) no tiene prueba automática; el paso 3.3 es la comprobación a ojo.
- **Un `whatsapp-service` real en cada estado.** Los estados se probaron con un servicio falso que responde lo que documenta `whatsapp-service`; no se forzó en un servicio real una cuenta restringida ni una sesión reemplazada.
- **"Mostrar código QR" contra WhatsApp de verdad.** La ruta llama al `POST /vincular` que `whatsapp-service` ya tenía; no se probó con un teléfono escaneando.
- **Nada contra el SQL Server 2025 del VPS** (esta mejora no toca la base; solo la auditoría agrega filas).
