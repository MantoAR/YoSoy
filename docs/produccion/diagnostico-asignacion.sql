/* ============================================================================
   diagnostico-asignacion.sql — Por qué un profesional NO es candidato de la asignación automática.   SOLO LECTURA.

   NO escribe nada: solo SELECT (nada de INSERT, UPDATE, DELETE ni cambios de esquema). No muestra datos de pacientes ni claves: solo nombre, especialidad, estado, agenda y la cantidad de turnos.

     sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i docs/produccion/diagnostico-asignacion.sql

   Antes de correrlo, poné en @Nombre (más abajo) un pedazo del nombre o del apellido del profesional, por ejemplo N'Alejandro'. Vacío = se listan TODOS los profesionales.

   Las reglas que aplica son las de Services/AsignacionAutomatica.cs (ProfesionalesBase, TieneAgendaAsignable y HorariosLibres), en el mismo orden:
     1. Estado = 'Activo'.
     2. Matrícula distinta de 'ADM-001' (el usuario «Equipo Admision»).
     3. Especialidad distinta de «Admisión» (sin importar mayúsculas ni tildes). Admisión entrevista, no atiende pacientes.
     4. La especialidad tiene que ser la «Especialidad solicitada» del formulario del paciente (si no, no es candidato PARA ESE paciente).
     5. Tiene que tener al menos una franja de agenda Disponible, con horas, que NO sea de admisión (Agenda.EsAdmision = 0, migración 29) y no vencida (Agenda.FechaFin, migración 30).
     6. De esas franjas salen los horarios; se descuentan los turnos activos y las propuestas pendientes de otras personas.
   ============================================================================ */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @Nombre NVARCHAR(100) = N'';   -- <<< el nombre o apellido a buscar; vacío = todos

DECLARE @tieneEsAdmision BIT = CASE WHEN COL_LENGTH('dbo.Agenda', 'EsAdmision') IS NOT NULL THEN 1 ELSE 0 END;
DECLARE @tieneVigencia   BIT = CASE WHEN COL_LENGTH('dbo.Agenda', 'FechaFin')  IS NOT NULL THEN 1 ELSE 0 END;

PRINT N'Migración 29 (Agenda.EsAdmision): ' + CASE WHEN @tieneEsAdmision = 1 THEN N'está' ELSE N'NO está (ninguna franja se marca de admisión)' END;
PRINT N'Migración 30 (Agenda.FechaInicio/FechaFin): ' + CASE WHEN @tieneVigencia = 1 THEN N'está' ELSE N'NO está (las franjas no vencen)' END;
PRINT N'';

-- 1) El profesional y el veredicto, regla por regla.
SELECT p.IdProfesional,
       LTRIM(RTRIM(p.Nombre + ISNULL(' ' + p.Apellido, N''))) AS Profesional,
       p.Matricula,
       e.Nombre AS Especialidad,
       p.Estado,
       CASE WHEN p.Estado = N'Activo' THEN N'ok' ELSE N'NO: no está Activo' END AS [1 Estado],
       CASE WHEN ISNULL(p.Matricula, N'') <> N'ADM-001' THEN N'ok' ELSE N'NO: es el usuario de Admisión (ADM-001)' END AS [2 Matricula],
       CASE WHEN ISNULL(e.Nombre, N'') COLLATE Latin1_General_CI_AI = N'admision' THEN N'NO: su especialidad es Admisión'
            WHEN e.IdEspecialidad IS NULL THEN N'NO: no tiene especialidad'
            ELSE N'ok' END AS [3 Especialidad],
       (SELECT COUNT(*) FROM dbo.Agenda a WHERE a.IdProfesional = p.IdProfesional) AS FranjasEnTotal
FROM dbo.Profesional p
LEFT JOIN dbo.Especialidad e ON e.IdEspecialidad = p.IdEspecialidad
WHERE @Nombre = N'' OR p.Nombre LIKE N'%' + @Nombre + N'%' OR p.Apellido LIKE N'%' + @Nombre + N'%'
ORDER BY p.Nombre, p.Apellido;

-- 2) Sus franjas de agenda (tipo y vigencia) y si la asignación las puede usar.
DECLARE @sqlFranjas NVARCHAR(MAX) = N'
SELECT p.IdProfesional,
       LTRIM(RTRIM(p.Nombre + ISNULL('' '' + p.Apellido, N''''))) AS Profesional,
       a.IdAgenda, a.Dia, a.HoraInicio, a.HoraFin, a.IntervaloMinutos, a.Disponible,
       ' + CASE WHEN @tieneEsAdmision = 1 THEN N'CASE WHEN a.EsAdmision = 1 THEN N''admisión'' ELSE N''atención'' END' ELSE N'N''atención (sin migración 29)''' END + N' AS Tipo,
       ' + CASE WHEN @tieneVigencia = 1 THEN N'a.FechaInicio, a.FechaFin' ELSE N'CAST(NULL AS DATE) AS FechaInicio, CAST(NULL AS DATE) AS FechaFin' END + N',
       CASE WHEN a.Disponible = 0 THEN N''NO: no está disponible''
            WHEN a.HoraInicio IS NULL OR a.HoraFin IS NULL THEN N''NO: sin horas''
            ' + CASE WHEN @tieneEsAdmision = 1 THEN N'WHEN a.EsAdmision = 1 THEN N''NO: es franja de admisión (no se ofrece a pacientes)''' ELSE N'' END + N'
            ' + CASE WHEN @tieneVigencia = 1 THEN N'WHEN a.FechaFin IS NOT NULL AND a.FechaFin < CAST(GETDATE() AS DATE) THEN N''NO: la vigencia ya terminó''' ELSE N'' END + N'
            ELSE N''ok: la asignación la puede usar'' END AS [Sirve para la asignación]
FROM dbo.Profesional p
JOIN dbo.Agenda a ON a.IdProfesional = p.IdProfesional
WHERE @N = N'''' OR p.Nombre LIKE N''%'' + @N + N''%'' OR p.Apellido LIKE N''%'' + @N + N''%''
ORDER BY p.Nombre, p.Apellido, a.IdAgenda;';
EXEC sp_executesql @sqlFranjas, N'@N NVARCHAR(100)', @N = @Nombre;

-- 3) Cuántos turnos futuros tiene (sin datos de los pacientes).
SELECT p.IdProfesional,
       LTRIM(RTRIM(p.Nombre + ISNULL(' ' + p.Apellido, N''))) AS Profesional,
       COUNT(t.IdTurno) AS TurnosFuturosActivos,
       MIN(t.Fecha) AS ProximoTurno
FROM dbo.Profesional p
LEFT JOIN dbo.Turno t ON t.IdProfesional = p.IdProfesional AND t.Fecha >= CAST(GETDATE() AS DATE)
     AND t.IdEstadoTurno IN (SELECT IdEstadoTurno FROM dbo.EstadoTurno WHERE Descripcion NOT IN (N'Cancelado', N'Rechazado'))
WHERE @Nombre = N'' OR p.Nombre LIKE N'%' + @Nombre + N'%' OR p.Apellido LIKE N'%' + @Nombre + N'%'
GROUP BY p.IdProfesional, p.Nombre, p.Apellido
ORDER BY p.Nombre, p.Apellido;

-- 4) Resumen: ¿puede ser candidato (sin mirar la especialidad pedida por un paciente)?
DECLARE @sqlResumen NVARCHAR(MAX) = N'
SELECT LTRIM(RTRIM(p.Nombre + ISNULL('' '' + p.Apellido, N''''))) AS Profesional, e.Nombre AS Especialidad,
       CASE WHEN p.Estado <> N''Activo'' THEN N''NO: no está Activo''
            WHEN ISNULL(p.Matricula, N'''') = N''ADM-001'' THEN N''NO: es el usuario de Admisión''
            WHEN ISNULL(e.Nombre, N'''') COLLATE Latin1_General_CI_AI = N''admision'' THEN N''NO: su especialidad es Admisión -> cambiarla a la que atiende, o no atiende por asignación automática''
            WHEN e.IdEspecialidad IS NULL THEN N''NO: no tiene especialidad''
            WHEN NOT EXISTS (SELECT 1 FROM dbo.Agenda a WHERE a.IdProfesional = p.IdProfesional AND a.Disponible = 1 AND a.HoraInicio IS NOT NULL AND a.HoraFin IS NOT NULL
                             ' + CASE WHEN @tieneEsAdmision = 1 THEN N'AND a.EsAdmision = 0' ELSE N'' END + N'
                             ' + CASE WHEN @tieneVigencia = 1 THEN N'AND (a.FechaFin IS NULL OR a.FechaFin >= CAST(GETDATE() AS DATE))' ELSE N'' END + N')
                 THEN N''NO: no tiene franjas de ATENCIÓN disponibles y vigentes (¿son todas de admisión?)''
            ELSE N''SÍ: es candidato para pacientes que pidan la especialidad '' + e.Nombre END AS Veredicto
FROM dbo.Profesional p
LEFT JOIN dbo.Especialidad e ON e.IdEspecialidad = p.IdEspecialidad
WHERE @N = N'''' OR p.Nombre LIKE N''%'' + @N + N''%'' OR p.Apellido LIKE N''%'' + @N + N''%''
ORDER BY p.Nombre, p.Apellido;';
EXEC sp_executesql @sqlResumen, N'@N NVARCHAR(100)', @N = @Nombre;
