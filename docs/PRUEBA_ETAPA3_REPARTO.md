# Prueba de la etapa 3: reparto de funciones entre módulos

Qué se prueba:
- que el Configurador quede solo con Usuarios, Configuración y la auditoría administrativa;
- que el grupo CRM se vea en el módulo CRM;
- que Reportes, Clínico, Interacciones IA y la auditoría clínica queden para el Profesional auditor;
- que Admisión sea un privilegio del profesional y Gestión de agendas, uno de CRM;
- que los permisos se asignen por usuario desde la pantalla y queden auditados;
- que nada de esto dependa de ocultar menús: el servidor responde 403.

Requiere las etapas 1 y 2 aplicadas (migraciones 16 y 17). En los comandos, `TU_SERVIDOR` es el nombre de tu servidor (en SSMS: `SELECT @@SERVERNAME;`).

## 0. Rama

```bash
cd /d/YoSoy
git status                                  # sin cambios pendientes
git switch feature/permisos-etapa2          # con la etapa 2 ya commiteada
git switch -c feature/permisos-etapa3
git apply --3way /ruta/al/etapa3.patch      # o descomprimir el zip sobre la carpeta
git add -A
git commit -m "Etapa 3 de roles: reparto de funciones entre módulos"
```

## 1. Migración 18

1. Arrancá la API con `dotnet run`. **Esperado:** la consola avisa que falta la migración 18.
2. Corré la migración:
   ```bash
   sqlcmd -S 'TU_SERVIDOR' -d yosoy -E -C -f 65001 -i migrations/add_privilegio_agendas_auditoria_permisos.sql
   ```
3. **Esperado:** "Migración 18 aplicada." y una tabla con Admisión (Profesional y Profesional auditor) y Gestión de agendas (CRM y atención al cliente). A los 30 segundos se puede ingresar, sin reiniciar la API.

Si todavía no corriste `Scripts/crear_usuario_crm_demo.sql` (etapa 2), corrélo ahora: crea `crm.demo` / `1234`.

Antes de ingresar, recargá con **Ctrl+F5** para que el navegador no use los JS viejos.

## 2. Configurador: solo administración

Ingresá por la pestaña **Configurador**.

| Qué mirar | Esperado |
| --- | --- |
| Menú | Inicio · Usuarios (Permisos, Cuentas, Solicitudes de registro, Recuperación) · Configuración (Roles, Especialidades, Grupos, Reglas, Coberturas) · Auditoría |
| Ya no aparece | Clínico, Reportes, Interacciones IA, CRM, Solicitudes de cambio, Admisiones, Turnos, Agendas |
| Inicio | Solicitudes de registro, Recuperación, Usuarios sin permisos, Usuarios del equipo, Pacientes y Profesionales activos. Ningún dato clínico ni de chats |
| Usuarios › Cuentas | Elegí un paciente: solo DNI y "Dar de baja". **No** dice qué profesional lo atiende ni deja asignar |
| Auditoría | Título "Auditoría administrativa": ingresos, cambios de permisos, altas y bajas. **No** aparecen aperturas de historia clínica |

Ahora probá que el servidor también lo impide. Con la sesión del Configurador abierta, en la consola del navegador (F12):

```js
(await YS.fetch("/pacientes/1/historiaclinica")).status   // 403
(await YS.fetch("/evoluciones")).status                    // 403
(await YS.fetch("/reportes/atenciones")).status            // 403
(await YS.fetch("/mensajeria/conversaciones")).status      // 403
```

## 3. Permisos por usuario

En **Usuarios › Permisos**:

1. Abrí **Editar permisos** de `crm.demo`. **Esperado:** Profesional y Profesional auditor deshabilitados (no tiene ficha con matrícula), CRM marcado.
2. Marcá **Privilegio: Gestión de agendas** y guardá. **Esperado:** "Permisos guardados…" y en la tabla, Privilegios = "CRM y atención al cliente: Gestión de agendas".
3. Abrí **Editar permisos** de un profesional (el usuario de un profesional con matrícula). Marcá **Profesional auditor** y **Privilegio: Admisión**, y guardá.
4. Abrí los permisos de tu propio usuario y probá destildar Configurador. **Esperado:** no se puede (está deshabilitado, y el servidor también lo rechaza).
5. Volvé a **Auditoría**. **Esperado:** "Privilegio asignado · Privilegio Gestión de agendas en CRM y atención al cliente", "Permiso asignado · Permiso Profesional auditor", etc., con tu usuario y módulo `configurador`.

Cambio hecho a mano en la base (también queda registrado):

```sql
DECLARE @u INT = (SELECT IdUsuario FROM Usuario WHERE Username = 'crm.demo');
DELETE FROM UsuarioPrivilegio WHERE IdUsuario = @u;
SELECT TOP 3 FechaHora, IdUsuario, Accion, Detalle, Espacio FROM LogAuditoria ORDER BY IdLog DESC;
```

**Esperado:** "Privilegio quitado … · hecho directo en la base por TU_LOGIN", con `IdUsuario` vacío y Espacio `base`. Después volvé a darle Gestión de agendas desde la pantalla (paso 2).

## 4. CRM y atención al cliente

Cerrá sesión e ingresá con `crm.demo` / `1234` por la pestaña **CRM**.

| Qué mirar | Esperado |
| --- | --- |
| Pantalla | Panel de gestión (ya no la de "en construcción") |
| Menú | Inicio · Conversaciones (Chats, Escalamientos, Etiquetas) · Casos (Casos, Derivaciones) · Agendas (Turnos, Horarios) |
| Inicio | Chats sin leer, Chats escalados, Derivaciones por confirmar, Casos sin asignar, Turnos de hoy |
| Chats | La bandeja, con Asignar/Reasignar en las escaladas |

Si le sacás Gestión de agendas (paso 3) y volvés a ingresar, el grupo Agendas desaparece y `(await YS.fetch("/turnos")).status` da 403.

Desde la consola: `(await YS.fetch("/pacientes/1/historiaclinica")).status` → 403. CRM nunca ve la historia clínica.

## 5. Profesional con Admisión y auditor

Ingresá por **Profesional** con el profesional del paso 3.

1. **Esperado:** el menú de siempre más dos grupos: **Admisión** (Admisiones, Solicitudes de cambio, Asignaciones) y **Auditoría** (Reportes, Evoluciones, Eventos de crisis, Interacciones IA, Accesos clínicos). Llevan al panel de gestión, que mantiene el mismo menú: Inicio, Agenda, etc. vuelven al panel profesional.
2. **Admisión › Asignaciones:** al elegir un paciente se ve con quién está y se puede asignar o cambiar de profesional.
3. **Admisión › Admisiones:** programar una entrevista y "Iniciar entrevista" abre la sala con tu propia sesión (ya no se cambia el profesional guardado por "ADM-001").
4. **Auditoría › Reportes:** el selector tiene a todos los profesionales y la descarga trae todo.
5. **Auditoría › Evoluciones** (y Eventos de crisis, Interacciones IA): el listado de toda la organización, solo lectura.
6. Abrí la historia clínica de un paciente que **no** atendés, desde la consola (F12): `await YS.api("/pacientes/ID/historiaclinica")`, con el id de ese paciente. **Esperado:** la devuelve. En cambio, `(await YS.fetch("/eventoscrisis", { method: "POST", json: { idPaciente: ID, nivelRiesgo: "Bajo", acciones: "prueba" } })).status` da 403: el auditor lee, no escribe en historias ajenas.
7. **Auditoría › Accesos clínicos:** aparece "Apertura de historia clínica … como auditor" y los listados que abriste. No aparecen ingresos ni cambios de permisos (eso es del Configurador).

Un profesional sin Admisión ni auditor no ve esos grupos; si escribe a mano la dirección del panel de gestión, vuelve a su panel.

## 6. Reportes que estaban abiertos

Sin sesión, desde cualquier navegador:

```
http://localhost:5000/reportes/profesional/1/kpis
```

**Esperado:** 401. Antes de esta etapa respondía los datos (incluidas las emociones registradas de los pacientes en `/reportes/paciente/{id}/evolucion-emocional`).

## 7. Pruebas automáticas

```bash
dotnet test YoSoy.Tests
```

**Esperado:** pasan las de siempre más `RedistribucionEtapa3Tests`, y los casos nuevos de `CierreSeguridadTests` (el Configurador recibe 403 en la historia clínica, el auditor la lee pero no escribe, cada uno ve su parte de la auditoría, los reportes exigen sesión). Las pruebas de chats usan ahora un token de CRM.

## Limpieza (opcional)

Desde **Usuarios › Permisos**, sacale al profesional el permiso auditor y el privilegio Admisión. Queda registrado en la auditoría, como cualquier cambio real.
