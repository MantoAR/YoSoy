-- Migración 23: permisos de los usuarios que YA existen (el mapeo de la etapa 2 de roles y permisos).
-- Idempotente: se puede correr más de una vez y la segunda corrida no cambia nada. Requiere la 20, la 21 y la 22 (la 21 va antes para que
-- estas asignaciones queden en la auditoría administrativa: "hecho directo en la base por <login>").
-- Origen: rama nuevo-circuito1 de Alearmas01 (parte G de su migración "16"), con la regla de la etapa 2 que decidió el dueño.
--
-- Hasta hoy el rol se deducía en el login: ficha de paciente -> paciente; ficha de profesional -> profesional; cualquier otro -> configurador.
-- A partir de esta etapa el acceso sale de los permisos asignados (tablas UsuarioRol / UsuarioPermiso). Esta migración expresa "el mismo
-- acceso que hoy" con permisos, y le suma el módulo CRM al que hoy es Configurador (hoy atiende los chats):
--
--   Quién                                                          Qué recibe
--   -------------------------------------------------------------  -----------------------------------------------------------
--   Tiene ficha de profesional con matrícula no vacía y            rol y permiso "profesional"
--   especialidad (y no es paciente)
--   NO es paciente ni profesional y está ACTIVO (hoy, el            roles y permisos "configurador" Y "crm"
--   Configurador; "activo" es la misma condición del login:
--   Usuario.Estado = 'Activo')
--   NO es paciente ni profesional y NO está activo                 NADA: aparece en la sección 3 de la salida como "inactivo, sin permisos"
--   Es paciente (tiene ficha de paciente)                          nada: los pacientes entran por su flujo, sin permisos del equipo
--   Profesional SIN matrícula o SIN especialidad                   NADA: queda sin permiso y su ingreso da 403 SIN_PERMISOS. La salida
--                                                                  de esta migración lo lista (ids y motivo) para que el Configurador
--                                                                  complete la ficha y le asigne el permiso desde la pantalla Permisos
--
-- Nadie recibe "auditor" ni "admision" ni "gestion_agendas": se asignan a mano desde Configurador > Permisos o Privilegios.
-- El mapeo corre UNA sola vez (queda registrada la versión 23): repetir la migración no vuelve a dar permisos a quien el Configurador
-- se los sacó a propósito.
--
--   sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i migrations\mapear_usuarios_existentes_a_permisos.sql
--
-- Deshacer (con un backup antes; las asignaciones quedan registradas en LogAuditoria, que no se puede borrar):
--   DELETE FROM UsuarioPrivilegio; DELETE FROM UsuarioPermiso; DELETE FROM UsuarioRol WHERE IdRol IN (SELECT IdRol FROM Rol WHERE Clave IS NOT NULL);
--   (solo si todavía no se asignó nada a mano; después, restaurar el backup). La API anterior a la etapa 2 no mira estas tablas.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- Estado antes (para la salida).
PRINT N'Antes: usuarios con algún permiso asignado:';
SELECT COUNT(DISTINCT IdUsuario) AS usuarios_con_permiso FROM UsuarioPermiso;
GO

-- Todo el mapeo corre UNA sola vez: si la versión 23 ya está registrada no hace nada. Así, repetir la migración nunca le vuelve a dar
-- permisos a alguien a quien el Configurador se los sacó a propósito. (Para repetirlo a propósito: DELETE FROM VersionEsquema WHERE Numero = 23.)
-- Es una sola transacción: la versión 23 se registra únicamente si todo lo anterior funcionó; si algo falla se deshace todo (ROLLBACK) y la
-- versión no queda registrada, así que se puede corregir y volver a correr.
SET XACT_ABORT ON;
IF NOT EXISTS (SELECT 1 FROM VersionEsquema WHERE Numero = 23)
BEGIN
  BEGIN TRY
    BEGIN TRAN;
    DECLARE @RolProf INT = (SELECT IdRol FROM Rol WHERE Clave = 'profesional');
    DECLARE @PermProf INT = (SELECT IdPermiso FROM Permiso WHERE Clave = 'profesional');
    DECLARE @RolConf INT = (SELECT IdRol FROM Rol WHERE Clave = 'configurador');
    DECLARE @PermConf INT = (SELECT IdPermiso FROM Permiso WHERE Clave = 'configurador');
    DECLARE @RolCrm INT = (SELECT IdRol FROM Rol WHERE Clave = 'crm');
    DECLARE @PermCrm INT = (SELECT IdPermiso FROM Permiso WHERE Clave = 'crm');

    -- ── 1) Profesionales con matrícula y especialidad -> "profesional" ──────
    INSERT INTO UsuarioRol (IdUsuario, IdRol)
    SELECT DISTINCT up.IdUsuario, @RolProf
    FROM UsuarioProfesional up
    JOIN Profesional pr ON pr.IdProfesional = up.IdProfesional
    WHERE LTRIM(RTRIM(ISNULL(pr.Matricula, ''))) <> '' AND pr.IdEspecialidad IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM UsuarioPaciente x WHERE x.IdUsuario = up.IdUsuario)
      AND NOT EXISTS (SELECT 1 FROM UsuarioRol ur WHERE ur.IdUsuario = up.IdUsuario AND ur.IdRol = @RolProf);

    INSERT INTO UsuarioPermiso (IdUsuario, IdRol, IdPermiso)
    SELECT DISTINCT up.IdUsuario, @RolProf, @PermProf
    FROM UsuarioProfesional up
    JOIN Profesional pr ON pr.IdProfesional = up.IdProfesional
    WHERE LTRIM(RTRIM(ISNULL(pr.Matricula, ''))) <> '' AND pr.IdEspecialidad IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM UsuarioPaciente x WHERE x.IdUsuario = up.IdUsuario)
      AND NOT EXISTS (SELECT 1 FROM UsuarioPermiso x WHERE x.IdUsuario = up.IdUsuario AND x.IdPermiso = @PermProf);

    -- ── 2) Quien no es paciente ni profesional, y está activo -> "configurador" Y "crm" ────
    INSERT INTO UsuarioRol (IdUsuario, IdRol)
    SELECT u.IdUsuario, r.IdRol
    FROM Usuario u
    CROSS JOIN (VALUES (@RolConf), (@RolCrm)) AS r(IdRol)
    WHERE u.Estado = 'Activo'
      AND NOT EXISTS (SELECT 1 FROM UsuarioPaciente x WHERE x.IdUsuario = u.IdUsuario)
      AND NOT EXISTS (SELECT 1 FROM UsuarioProfesional x WHERE x.IdUsuario = u.IdUsuario)
      AND NOT EXISTS (SELECT 1 FROM UsuarioRol ur WHERE ur.IdUsuario = u.IdUsuario AND ur.IdRol = r.IdRol);

    INSERT INTO UsuarioPermiso (IdUsuario, IdRol, IdPermiso)
    SELECT u.IdUsuario, p.IdRol, p.IdPermiso
    FROM Usuario u
    CROSS JOIN (VALUES (@RolConf, @PermConf), (@RolCrm, @PermCrm)) AS p(IdRol, IdPermiso)
    WHERE u.Estado = 'Activo'
      AND NOT EXISTS (SELECT 1 FROM UsuarioPaciente x WHERE x.IdUsuario = u.IdUsuario)
      AND NOT EXISTS (SELECT 1 FROM UsuarioProfesional x WHERE x.IdUsuario = u.IdUsuario)
      AND NOT EXISTS (SELECT 1 FROM UsuarioPermiso x WHERE x.IdUsuario = u.IdUsuario AND x.IdPermiso = p.IdPermiso);

    -- ── 3) Registro de versión (en el mismo lote: si algo falló arriba, no queda registrada) ──
    INSERT INTO VersionEsquema (Numero, Archivo, Origen) VALUES (23, N'mapear_usuarios_existentes_a_permisos.sql', 'Script');
    COMMIT TRAN;
  END TRY
  BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRAN;
    THROW;
  END CATCH
END
GO

-- ══════════════════════════════════════════════════════════════
-- RESULTADO (para leer en la salida de sqlcmd). Solo ids, claves y conteos: ningún dato personal.
-- ══════════════════════════════════════════════════════════════
PRINT N'Migración 23 aplicada.';

PRINT N'1) Cuántos usuarios tiene cada permiso:';
SELECT p.Clave AS permiso, COUNT(up.IdUsuario) AS usuarios
FROM Permiso p LEFT JOIN UsuarioPermiso up ON up.IdPermiso = p.IdPermiso
GROUP BY p.Clave, p.IdPermiso ORDER BY p.IdPermiso;

PRINT N'2) Permisos de cada cuenta del equipo (por id):';
SELECT u.IdUsuario, u.Estado,
       ISNULL(STUFF((SELECT N', ' + p.Clave FROM UsuarioPermiso x JOIN Permiso p ON p.IdPermiso = x.IdPermiso
                     WHERE x.IdUsuario = u.IdUsuario ORDER BY p.Clave FOR XML PATH('')), 1, 2, N''), N'(ninguno)') AS permisos
FROM Usuario u
WHERE NOT EXISTS (SELECT 1 FROM UsuarioPaciente x WHERE x.IdUsuario = u.IdUsuario)
ORDER BY u.IdUsuario;

PRINT N'3) CUENTAS DEL EQUIPO SIN NINGÚN PERMISO (su ingreso da 403 SIN_PERMISOS hasta que el Configurador las arregle). Tiene que salir vacío, o con casos conocidos:';
SELECT u.IdUsuario, u.Estado, up.IdProfesional,
       CASE WHEN up.IdUsuario IS NULL AND ISNULL(u.Estado, '') <> 'Activo' THEN N'inactivo, sin permisos'
            WHEN up.IdUsuario IS NULL THEN N'no es profesional ni tiene permisos'
            WHEN LTRIM(RTRIM(ISNULL(pr.Matricula, ''))) = '' AND pr.IdEspecialidad IS NULL THEN N'profesional sin matrícula ni especialidad'
            WHEN LTRIM(RTRIM(ISNULL(pr.Matricula, ''))) = '' THEN N'profesional sin matrícula'
            WHEN pr.IdEspecialidad IS NULL THEN N'profesional sin especialidad'
            ELSE N'revisar' END AS motivo
FROM Usuario u
LEFT JOIN UsuarioProfesional up ON up.IdUsuario = u.IdUsuario
LEFT JOIN Profesional pr ON pr.IdProfesional = up.IdProfesional
WHERE NOT EXISTS (SELECT 1 FROM UsuarioPaciente x WHERE x.IdUsuario = u.IdUsuario)
  AND NOT EXISTS (SELECT 1 FROM UsuarioPermiso x WHERE x.IdUsuario = u.IdUsuario)
ORDER BY u.IdUsuario;

PRINT N'4) Especialidades de los profesionales que pasaron a ser un "servicio" (hay que asignarles una especialidad real):';
SELECT up.IdUsuario, pr.IdProfesional, e.Nombre AS especialidad
FROM UsuarioProfesional up
JOIN Profesional pr ON pr.IdProfesional = up.IdProfesional
JOIN Especialidad e ON e.IdEspecialidad = pr.IdEspecialidad
WHERE e.Tipo = 'Servicio';
GO
