# Despliegue a producción: alta clínica y derivación interna asistidas desde el cierre

Runbook para llevar a producción el **alta clínica** y la **derivación interna asistida** (qué hacen: [`ALTA_DERIVACION_CIERRE.md`](ALTA_DERIVACION_CIERRE.md)). Cambia **la base (migración 27)**, **la API** y **las pantallas** (van dentro del paquete). Se ejecuta **a mano, en dos máquinas** (la PC con PowerShell y el VPS por SSH), con el mismo formato y los mismos pasos que [`DEPLOY_CIERRE_SESION_PRODUCCION.md`](DEPLOY_CIERRE_SESION_PRODUCCION.md): **este runbook solo dice qué cambia de cada paso**. Escrito el 2026-10-26 contra la rama `feat/alta-derivacion` (apilada sobre `feat/cierre-sesion`, que tiene que estar mergeada **antes**: la 27 necesita la 26 y el cierre). **No se ejecutó.**

- **Sin secretos en este archivo.** Las contraseñas las escribís vos cuando el comando las pide.
- **Qué NO cambia:** `whatsapp-service`, Caddy, el `.env` (no hay variables nuevas), el consentimiento y las emociones, el alta administrativa (`PUT /pacientes/{id}/alta`) y la «solicitud de cambio» de siempre de Admisión.

## Qué sale a producción

| Qué | Cómo queda |
|---|---|
| **Migración 27** (`add_alta_derivacion_cierre.sql`) | `Caso.Estado` admite «En alta»; tabla nueva `AltaClinica`; 8 columnas nuevas y un índice en `Derivacion`. Aditiva, idempotente, en una transacción; **no toca ninguna fila**. Registra la versión 27. **Opcional para la API, necesaria para usar el alta y la derivación asistidas** (sin ella responden «falta aplicar la migración 27») |
| **API** | `POST/GET /casos/{id}/alta-clinica`, `GET /casos/{id}/destinos-derivacion`, `POST /casos/{id}/derivar`, `PUT /derivaciones/{id}/responder`, `GET /derivaciones/{id}/informe`, `GET /derivaciones/traspasos-pendientes`, `POST /derivaciones/{id}/traspasar` y `/resolver`, `GET /profesionales/{id}/avisos-clinicos`, `PUT /avisos-clinicos/{id}/leida`. Las acciones viejas de Admisión se niegan sobre derivaciones del cierre |
| **Pantallas** | Los dos asistentes de la sala; la campanita del profesional (aceptar / rechazar); «Traspasos por confirmar» en CRM › Derivaciones |

## Lo que cambia para el equipo (avisalo antes)

- Con **Alta** o **Derivación** en el cierre aparece un botón que abre un **asistente**; recién con el botón «Confirmar…» se aplica. El alta pasa el caso a **«En alta»** (sigue abierto y a cargo del profesional; deja de contar en su carga). **No** inactiva al paciente.
- La **derivación** avisa por la campanita (y por mail, sin datos del paciente) al destino, al origen y a Admisión. **El caso no cambia de manos hasta que Admisión confirma el traspaso** en CRM › Derivaciones › «Traspasos por confirmar».
- **Quien confirma el traspaso es CRM / Administración** (el rol que ya reasigna casos), no el profesional con el privilegio Admisión.

## El orden (igual que el del cierre de sesión)

| Paso | Qué |
|---|---|
| 0 | [PC] El código: `main` con ambas ramas mergeadas; `Select-String -Path Services\VerificadorEsquema.cs -Pattern 'add_alta_derivacion_cierre.sql' -Quiet` da `True` y existen `Endpoints\AltaDerivacionEndpoints.cs`, `PANTALLAS\Pantalla_Configurador\js\traspasos.js` y `migrations\add_alta_derivacion_cierre.sql` |
| 1 | Backup verificado de la base (paso 1 del runbook del cierre) |
| 2 | **Migración 27 (antes del paquete)** |
| 3 | Publicar la API y subir el paquete (paso 3 del runbook del cierre, `TAG=ad`) |
| 4 | Verificar |
| 5 | Prueba en el navegador |
| 6 | Rollback |

`FECHA=YYYYMMDD; TAG=ad; export PATH="$PATH:/opt/mssql-tools18/bin"` al abrir la sesión SSH.

## Paso 2. Migración 27

```powershell
# [PC]
scp D:\Claude\YoSoy\YoSoy\migrations\add_alta_derivacion_cierre.sql agustin@179.199.155.50:/home/agustin/
```
```bash
# [VPS] aplicarla (idempotente, en una transacción)
sqlcmd -S localhost -d yosoy -U sa -C -I -b -f 65001 -i ~/add_alta_derivacion_cierre.sql
```
**Esperado:** `Migracion 27 aplicada.` y una fila `altas = 0`, `derivaciones_del_cierre = 0`. Comprobar (si no da lo esperado, **no publiques la API**):
```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -W -Q "SELECT (SELECT MAX(Numero) FROM VersionEsquema) AS ultima_version, (SELECT COUNT(*) FROM sys.tables WHERE name = 'AltaClinica') AS tabla_alta, (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Derivacion') AND name IN ('Origen','IdCaso','Observaciones','IdSesion','RespuestaUtc','IdUsuarioRespuesta','ResolucionUtc','IdUsuarioResolucion')) AS columnas, (SELECT COUNT(*) FROM Derivacion WHERE Origen <> N'Solicitud') AS derivaciones_cambiadas, (SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_Caso_Estado' AND definition LIKE N'%En alta%') AS ck_con_en_alta"
```
**Esperado:** `27  1  8  0  1`.

## Paso 3. Publicar (el proyecto, **no** la solución)

```powershell
cd D:\Claude\YoSoy\YoSoy
$fecha = Get-Date -Format yyyyMMdd; $tag = 'ad'
dotnet publish YoSoy.csproj -c Release -r linux-x64 --self-contained true -o "D:\Claude\Publish-YoSoy-${fecha}-${tag}"
tar -czf "$env:TEMP\Publish-YoSoy-${fecha}-${tag}.tar.gz" -C "D:\Claude\Publish-YoSoy-${fecha}-${tag}" .
scp "$env:TEMP\Publish-YoSoy-${fecha}-${tag}.tar.gz" agustin@179.199.155.50:/home/agustin/
```
Desempaquetar, backup `app-anterior-ad-<fecha>`, corte y arranque: **los mismos comandos del paso 3.2 y 3.3 del runbook del cierre** con `TAG=ad` (comprobá que el paquete trae `PANTALLAS/Pantalla_Configurador/js/traspasos.js`).

## Paso 4. Verificar (VPS, solo lectura, sin sesión)

```bash
sudo journalctl -u yosoy-api --since '5 minutes ago' --no-pager | grep -iE 'error|exception|fail|Hosting environment'; systemctl is-active whatsapp-service
for r in "POST casos/1/alta-clinica" "POST casos/1/derivar" "PUT derivaciones/1/responder" "GET derivaciones/traspasos-pendientes" "POST derivaciones/1/traspasar" "GET profesionales/1/avisos-clinicos"; do set -- $r; curl -s -o /dev/null -w "$2 sin sesion: %{http_code}\n" -X $1 http://localhost:5000/$2; done; curl -s -o /dev/null -w "traspasos.js: %{http_code}\n" http://localhost:5000/pantallas/Pantalla_Configurador/js/traspasos.js
```
**Esperado:** `Hosting environment: Production`, ninguna excepción, `active` para `whatsapp-service`, `401` las seis primeras y `200` la última.

## Paso 5. Prueba en el navegador (con pacientes y profesionales de prueba)

1. Cerrar una sesión con **Alta**: completar el asistente, ver el resumen, **Confirmar el alta de…** (con el cierre ya guardado). El caso figura «En alta» en *Mis casos*; el paciente sigue activo.
2. Cerrar otra con **Derivación** al profesional B: formulario con destinos; confirmar. Campanita de **B** (derivación con Aceptar/Rechazar), de **A** (constancia) y de **Admisión** (bandeja de chats, si la mensajería está activa).
3. B **acepta**; en CRM › Derivaciones › **Traspasos por confirmar** aparece la tarea; **Confirmar traspaso** → el caso y el vínculo pasan a B (con historial). Repetir con rechazo de B: la tarea pide decidir y se «da por resuelta».

## Paso 6. Rollback

* **API:** volver `/opt/yosoy/app-anterior-ad-<fecha>` (comando 7.1 del runbook del cierre). La base puede quedar como está.
* **Base:** `docs/produccion/deshacer-migracion-27.sql` (con `scp` previo). **Se niega a borrar** altas clínicas, casos «En alta» o derivaciones del cierre: si ya se usaron, no se deshace en silencio (exportalas y borralas a mano antes).

## Qué se verificó y qué no

Verificado contra `yosoy_test` y Chromium real: la API completa, la migración (idempotencia, deshacer, transacción, forma de producción), los asistentes con la API simulada y de punta a punta con la API real. **No** se probó contra el VPS ni contra `yosoy`, ni el mail real (en las pruebas el envío se reemplaza por un doble), ni el modal de confirmación de CRM en un navegador real.
