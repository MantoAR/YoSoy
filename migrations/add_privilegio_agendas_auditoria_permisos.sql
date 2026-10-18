-- Etapa 3 de roles y permisos (octubre 2026). Es la migración 18 de la tabla del README.
-- Idempotente: se puede correr más de una vez. Requiere las migraciones 16 y 17.
--
-- Qué hace:
--  1. Agrega el privilegio "Gestión de agendas", que solo admite el permiso CRM.
--  2. Deja registrado en la auditoría ADMINISTRATIVA todo cambio de roles, permisos y
--     privilegios de un usuario, se haga desde la pantalla o directo en la base. Lo hace la
--     base con triggers, así no depende de que el código se acuerde de registrarlo.
--     Quién lo hizo: la API pone su id en SESSION_CONTEXT('IdUsuario') antes de cambiar
--     (AccesosEdicionDAO). Si el cambio llega sin ese dato (por ejemplo, desde SSMS), queda
--     el inicio de sesión de SQL Server que lo hizo.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- ── 1) Privilegio Gestión de agendas (solo para CRM) ─────────
IF NOT EXISTS (SELECT 1 FROM Privilegio WHERE Clave = 'gestion_agendas')
    INSERT INTO Privilegio (Clave, Nombre, Descripcion)
    VALUES ('gestion_agendas', N'Gestión de agendas',
            N'Ver y modificar los turnos y los horarios de trabajo de todos los profesionales. No da acceso a historias clínicas.');
GO

INSERT INTO PermisoPrivilegio (IdPermiso, IdPrivilegio)
SELECT p.IdPermiso, pr.IdPrivilegio
FROM Permiso p
JOIN Privilegio pr ON pr.Clave = 'gestion_agendas'
WHERE p.Clave = 'crm'
  AND NOT EXISTS (SELECT 1 FROM PermisoPrivilegio x WHERE x.IdPermiso = p.IdPermiso AND x.IdPrivilegio = pr.IdPrivilegio);
GO

-- ── 2) Auditoría de cambios de permisos ───────────────────────
-- En los tres triggers: @Autor = el usuario de la aplicación que hizo el cambio; si no hay
-- (cambio hecho a mano en la base), @Origen agrega el inicio de sesión de SQL Server.
CREATE OR ALTER TRIGGER TR_UsuarioRol_Auditar ON UsuarioRol AFTER INSERT, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Autor INT = TRY_CAST(SESSION_CONTEXT(N'IdUsuario') AS INT);
    DECLARE @Espacio NVARCHAR(30) = CASE WHEN @Autor IS NULL THEN N'base' ELSE N'configurador' END;
    DECLARE @Origen NVARCHAR(160) = CASE WHEN @Autor IS NULL THEN N' · hecho directo en la base por ' + ORIGINAL_LOGIN() ELSE N'' END;

    INSERT INTO LogAuditoria (IdUsuario, Accion, Entidad, IdEntidad, Detalle, Categoria, Espacio)
    SELECT @Autor, N'Rol asignado', N'Usuario', i.IdUsuario,
           LEFT(N'Rol ' + ISNULL(r.Nombre, CONCAT(N'#', i.IdRol)) + @Origen, 500),
           'Administrativa', @Espacio
    FROM inserted i LEFT JOIN Rol r ON r.IdRol = i.IdRol;

    INSERT INTO LogAuditoria (IdUsuario, Accion, Entidad, IdEntidad, Detalle, Categoria, Espacio)
    SELECT @Autor, N'Rol quitado', N'Usuario', d.IdUsuario,
           LEFT(N'Rol ' + ISNULL(r.Nombre, CONCAT(N'#', d.IdRol)) + @Origen, 500),
           'Administrativa', @Espacio
    FROM deleted d LEFT JOIN Rol r ON r.IdRol = d.IdRol;
END;
GO

CREATE OR ALTER TRIGGER TR_UsuarioPermiso_Auditar ON UsuarioPermiso AFTER INSERT, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Autor INT = TRY_CAST(SESSION_CONTEXT(N'IdUsuario') AS INT);
    DECLARE @Espacio NVARCHAR(30) = CASE WHEN @Autor IS NULL THEN N'base' ELSE N'configurador' END;
    DECLARE @Origen NVARCHAR(160) = CASE WHEN @Autor IS NULL THEN N' · hecho directo en la base por ' + ORIGINAL_LOGIN() ELSE N'' END;

    INSERT INTO LogAuditoria (IdUsuario, Accion, Entidad, IdEntidad, Detalle, Categoria, Espacio)
    SELECT @Autor, N'Permiso asignado', N'Usuario', i.IdUsuario,
           LEFT(N'Permiso ' + ISNULL(p.Nombre, CONCAT(N'#', i.IdPermiso)) + @Origen, 500),
           'Administrativa', @Espacio
    FROM inserted i LEFT JOIN Permiso p ON p.IdPermiso = i.IdPermiso;

    INSERT INTO LogAuditoria (IdUsuario, Accion, Entidad, IdEntidad, Detalle, Categoria, Espacio)
    SELECT @Autor, N'Permiso quitado', N'Usuario', d.IdUsuario,
           LEFT(N'Permiso ' + ISNULL(p.Nombre, CONCAT(N'#', d.IdPermiso)) + @Origen, 500),
           'Administrativa', @Espacio
    FROM deleted d LEFT JOIN Permiso p ON p.IdPermiso = d.IdPermiso;
END;
GO

CREATE OR ALTER TRIGGER TR_UsuarioPrivilegio_Auditar ON UsuarioPrivilegio AFTER INSERT, DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Autor INT = TRY_CAST(SESSION_CONTEXT(N'IdUsuario') AS INT);
    DECLARE @Espacio NVARCHAR(30) = CASE WHEN @Autor IS NULL THEN N'base' ELSE N'configurador' END;
    DECLARE @Origen NVARCHAR(160) = CASE WHEN @Autor IS NULL THEN N' · hecho directo en la base por ' + ORIGINAL_LOGIN() ELSE N'' END;

    INSERT INTO LogAuditoria (IdUsuario, Accion, Entidad, IdEntidad, Detalle, Categoria, Espacio)
    SELECT @Autor, N'Privilegio asignado', N'Usuario', i.IdUsuario,
           LEFT(N'Privilegio ' + ISNULL(pv.Nombre, CONCAT(N'#', i.IdPrivilegio)) + N' en ' + ISNULL(p.Nombre, CONCAT(N'#', i.IdPermiso))
                + @Origen, 500),
           'Administrativa', @Espacio
    FROM inserted i
    LEFT JOIN Permiso p     ON p.IdPermiso = i.IdPermiso
    LEFT JOIN Privilegio pv ON pv.IdPrivilegio = i.IdPrivilegio;

    INSERT INTO LogAuditoria (IdUsuario, Accion, Entidad, IdEntidad, Detalle, Categoria, Espacio)
    SELECT @Autor, N'Privilegio quitado', N'Usuario', d.IdUsuario,
           LEFT(N'Privilegio ' + ISNULL(pv.Nombre, CONCAT(N'#', d.IdPrivilegio)) + N' en ' + ISNULL(p.Nombre, CONCAT(N'#', d.IdPermiso))
                + @Origen, 500),
           'Administrativa', @Espacio
    FROM deleted d
    LEFT JOIN Permiso p     ON p.IdPermiso = d.IdPermiso
    LEFT JOIN Privilegio pv ON pv.IdPrivilegio = d.IdPrivilegio;
END;
GO

-- ── 3) Registro de versión ────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM VersionEsquema WHERE Numero = 18)
    INSERT INTO VersionEsquema (Numero, Archivo, Origen) VALUES (18, N'add_privilegio_agendas_auditoria_permisos.sql', 'Script');
GO

-- Comprobación: tiene que listar Admisión (Profesional y Profesional auditor) y Gestión de agendas (CRM).
SELECT pv.Nombre AS Privilegio, p.Nombre AS Permiso
FROM PermisoPrivilegio pp
JOIN Privilegio pv ON pv.IdPrivilegio = pp.IdPrivilegio
JOIN Permiso p     ON p.IdPermiso = pp.IdPermiso
ORDER BY pv.Nombre, p.Nombre;
GO

PRINT N'Migración 18 aplicada.';
GO
