# Despliegue a producción: agenda (feriados, bloqueos, inasistencia) y matrícula

Rama `AAA1`, migración 33. Mismo flujo y mismas convenciones que [`DEPLOY_USUARIOS_PRODUCCION.md`](DEPLOY_USUARIOS_PRODUCCION.md):

- **[PC]** es PowerShell; **[VPS]** es la sesión SSH;
- un comando por vez;
- el backup queda solo en tu PC.

Acá van los comandos que cambian y el orden. `TAG=am`.

- **Cambia la base:** cinco tablas nuevas y un estado de turno. Es aditiva: no modifica ni borra datos.
- **Orden obligatorio: primero la migración 33, después la API.** La API nueva lee esas tablas para calcular los horarios: sin la migración, la reserva de turnos falla. La API anterior anda igual con la base ya migrada, así que correr la migración antes no corta nada.
- **Sin variables nuevas obligatorias.** Las opcionales:
  - `Matriculas__AvisosHabilitados` (por defecto `true`);
  - `Matriculas__HoraAviso` (por defecto `8`, hora de la clínica).
- **Sin cambios en** `whatsapp-service`, Caddy ni el `.env`.
- **El esquema consolidado** (`docs/produccion/01-produccion.sql`) **todavía no trae la 26.** Una base nueva armada con ese esquema necesita correr después esta migración.

## Qué cambia para cada uno

| Quién | Qué ve |
|---|---|
| Configurador | Configuración › **Feriados**; Usuarios › **Matrículas** (cargar, renovar, secundarias, "Enviar avisos ahora"); Registrar usuario pide el **vencimiento de la matrícula** |
| Profesional | Mis horarios de trabajo: aviso de matrícula, **Bloqueos de agenda**, **Feriados** con "Trabajo este feriado"; en el calendario y en Inicio, **Registrar inasistencia**; avisos de matrícula en la campanita |
| Profesional auditor | Auditoría › **Matrículas** (solo lectura) y los avisos de vencimiento de todos |
| Paciente | No se le ofrecen horarios en feriados, bloqueos ni después del vencimiento de la matrícula del profesional |
| CRM con Gestión de agendas | Puede ver y cargar bloqueos de cualquier profesional (`/profesionales/{id}/bloqueos`) |

**Después de migrar, todos los profesionales quedan con su matrícula "Sin vencimiento cargado".** No se les corta la agenda. El Configurador tiene que cargar las fechas en Usuarios › Matrículas.

## Pasos

| Paso | Qué | Dónde |
|---|---|---|
| 0 | `nuevo-circuito2` mergeada en `main` | GitHub |
| 1 | Backup verificado de la base y copia a tu PC | VPS y PC |
| 2 | Migración 33 | VPS |
| 3 | Publicar, subir y cambiar la API | PC y VPS |
| 4 | Verificar | VPS y navegador |
| 5 | Vuelta atrás, si hace falta | VPS |

### 1. Backup

Es el paso 1 de `DEPLOY_USUARIOS_PRODUCCION.md` tal cual, con `TAG=am`. **No sigas sin** `The backup set on file 1 is valid.` y sin la huella SHA-256 comparada en tu PC.

### 2. Migración 33

[PC] Desde la carpeta del repo, copiá al VPS el script y su vuelta atrás:

```powershell
scp .\migrations\add_agenda_feriados_bloqueos_matriculas.sql .\docs\produccion\deshacer-migracion-33.sql agustin@179.199.155.50:/home/agustin/
```

[VPS] Correlo con `sa`. `sqlcmd` te pide la contraseña: no la pongas en el comando.

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -b -f 65001 -i ~/add_agenda_feriados_bloqueos_matriculas.sql
```

**Esperado:** "Migración 33 aplicada." y una fila con `matriculas_cargadas`, `sin_vencimiento` (las mismas) y `id_estado_ausente`.

[VPS] Verificá:

```bash
sqlcmd -S localhost -d yosoy -U sa -C -I -W -Q "SELECT (SELECT MAX(Numero) FROM VersionEsquema) AS ultima_version, (SELECT COUNT(*) FROM Feriado) AS feriados, (SELECT COUNT(*) FROM ProfesionalMatricula) AS matriculas; EXEC sp_VerificarCadenaAuditoria"
```

**Esperado:**
- `ultima_version` 26;
- `feriados` 0;
- `matriculas` igual a la cantidad de profesionales con matrícula;
- la cadena de auditoría íntegra.

### 3. API

Es el paso de publicar y cambiar la API de `DEPLOY_USUARIOS_PRODUCCION.md`, con `TAG=am`: publish en la PC, `scp`, backup de `/opt/yosoy/app` en `app-anterior-am-FECHA` y el cambio en una sola línea.

**Esperado:** `backup idéntico`, `active` y `302`.

### 4. Verificar

1. Con el Configurador:
   - en Usuarios › Matrículas aparecen todos los profesionales con "Sin vencimiento cargado";
   - cargá **el vencimiento real** de cada uno, a partir de la constancia de cada matrícula.
2. Configuración › Feriados: cargá los feriados que falten del año, tomados del calendario oficial.
3. [VPS] Mirá el log. Desde las 8:00 tiene que aparecer una línea por día:

   ```bash
   journalctl -u yosoy-api --since today | grep "Matrículas"
   ```

4. La página pública de reserva de un profesional sigue ofreciendo horarios: hasta que se cargue un vencimiento, nada cambia.

### 5. Vuelta atrás

| Qué falla | Qué hacer |
|---|---|
| La API nueva | El rollback de la API de siempre, a `app-anterior-am-FECHA`. La base migrada no molesta a la API anterior |
| Hay que sacar la migración | [VPS] `sqlcmd -S localhost -d yosoy -U sa -C -I -b -f 65001 -i ~/deshacer-migracion-33.sql`, con la API anterior ya puesta. Se pierden feriados, bloqueos y vencimientos; los turnos Ausente vuelven a Confirmado |
| Todo | Restaurar el backup del paso 1, con el procedimiento 7.2 de `DEPLOY_USUARIOS_PRODUCCION.md` |
