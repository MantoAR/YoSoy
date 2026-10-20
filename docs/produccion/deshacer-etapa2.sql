/* ============================================================================
   deshacer-etapa2.sql — Saca de la base lo que agregaron las migraciones 20 a 23 (roles, permisos y privilegios, alta por invitación).

   CUÁNDO: solo si hay que volver la BASE a como estaba antes de la etapa 2 SIN restaurar el backup. Lo normal es lo contrario: volver a la API anterior
   (que ignora todo esto y sigue andando con la base migrada) y dejar la base como está. Restaurar el backup del despliegue es la vuelta atrás completa y la
   que se recomienda si ya se asignaron permisos o se dieron de alta usuarios por invitación.

   QUÉ PIERDE: los permisos, privilegios e invitaciones (todo lo asignado desde la etapa 2), las columnas nuevas de Usuario, Profesional, Especialidad y Rol, y
   la clasificación Disciplina/Tipo de Especialidad. NO toca ningún dato de pacientes, profesionales, turnos, historias ni mensajes. LogAuditoria queda como
   está (es de solo agregar): las asignaciones de permisos y su quite quedan registradas ahí.

   REQUISITOS: backup hecho; la API de la etapa 2 parada o ya reemplazada por la anterior (la nueva no arranca el login sin estas tablas). Se corre con el
   login administrador, igual que las migraciones:
     sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i docs\produccion\deshacer-etapa2.sql
   Si una cuenta del equipo se dio de alta por invitación (estado Pendiente, contraseña sin elegir), queda en la tabla Usuario sin permisos: con la API anterior
   entra como "configurador" apenas tenga contraseña, así que revisá la lista que imprime el primer SELECT antes de seguir.
   ============================================================================ */
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
GO

PRINT N'Cuentas creadas por invitación o por el alta de equipo (Pendiente o con CUIL): revisarlas antes de volver a la API anterior.';
SELECT IdUsuario, Estado FROM Usuario WHERE Estado = 'Pendiente' OR Cuil IS NOT NULL ORDER BY IdUsuario;
GO

-- ── Migración 23 y 22: asignaciones e invitaciones ──────────────────────────
IF OBJECT_ID('dbo.UsuarioPrivilegio', 'U') IS NOT NULL DELETE FROM UsuarioPrivilegio;
IF OBJECT_ID('dbo.UsuarioPermiso', 'U') IS NOT NULL DELETE FROM UsuarioPermiso;
IF OBJECT_ID('dbo.UsuarioRol', 'U') IS NOT NULL AND COL_LENGTH('dbo.Rol', 'Clave') IS NOT NULL
    DELETE FROM UsuarioRol WHERE IdRol IN (SELECT IdRol FROM Rol WHERE Clave IS NOT NULL);
GO
IF OBJECT_ID('dbo.InvitacionCuenta', 'U') IS NOT NULL DROP TABLE InvitacionCuenta;
GO

-- ── Migración 21: triggers de auditoría de permisos ─────────────────────────
IF OBJECT_ID('dbo.TR_UsuarioRol_Auditar', 'TR') IS NOT NULL DROP TRIGGER TR_UsuarioRol_Auditar;
IF OBJECT_ID('dbo.TR_UsuarioPermiso_Auditar', 'TR') IS NOT NULL DROP TRIGGER TR_UsuarioPermiso_Auditar;
IF OBJECT_ID('dbo.TR_UsuarioPrivilegio_Auditar', 'TR') IS NOT NULL DROP TRIGGER TR_UsuarioPrivilegio_Auditar;
GO

-- ── Migración 20: roles, permisos y privilegios ─────────────────────────────
IF OBJECT_ID('dbo.vw_UsuarioAccesos', 'V') IS NOT NULL DROP VIEW vw_UsuarioAccesos;
IF OBJECT_ID('dbo.TR_UsuarioPermiso_RequiereProfesional', 'TR') IS NOT NULL DROP TRIGGER TR_UsuarioPermiso_RequiereProfesional;
GO
IF OBJECT_ID('dbo.UsuarioPrivilegio', 'U') IS NOT NULL DROP TABLE UsuarioPrivilegio;
IF OBJECT_ID('dbo.UsuarioPermiso', 'U') IS NOT NULL DROP TABLE UsuarioPermiso;
IF OBJECT_ID('dbo.PermisoPrivilegio', 'U') IS NOT NULL DROP TABLE PermisoPrivilegio;
IF OBJECT_ID('dbo.Privilegio', 'U') IS NOT NULL DROP TABLE Privilegio;
IF OBJECT_ID('dbo.Permiso', 'U') IS NOT NULL DROP TABLE Permiso;
IF OBJECT_ID('dbo.ConfiguracionOrganizacion', 'U') IS NOT NULL DROP TABLE ConfiguracionOrganizacion;
IF OBJECT_ID('dbo.Organizacion', 'U') IS NOT NULL DROP TABLE Organizacion;
GO

-- Rol: las filas que creó la migración (con Clave) se van; las que ya existían (sin Clave) quedan.
IF COL_LENGTH('dbo.Rol', 'Clave') IS NOT NULL
BEGIN
    EXEC(N'DELETE FROM Rol WHERE Clave IS NOT NULL AND NOT EXISTS (SELECT 1 FROM UsuarioRol ur WHERE ur.IdRol = Rol.IdRol)');
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Rol') AND name = 'UX_Rol_Clave') DROP INDEX UX_Rol_Clave ON Rol;
    ALTER TABLE Rol DROP COLUMN Clave;
END
GO

-- Usuario
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Usuario') AND name = 'UX_Usuario_Cuil') DROP INDEX UX_Usuario_Cuil ON Usuario;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Usuario_Cuil') ALTER TABLE Usuario DROP CONSTRAINT CK_Usuario_Cuil;
GO
IF COL_LENGTH('dbo.Usuario', 'Cuil') IS NOT NULL ALTER TABLE Usuario DROP COLUMN Cuil;
IF COL_LENGTH('dbo.Usuario', 'Apellido') IS NOT NULL ALTER TABLE Usuario DROP COLUMN Apellido;
IF COL_LENGTH('dbo.Usuario', 'Nombre') IS NOT NULL ALTER TABLE Usuario DROP COLUMN Nombre;
IF COL_LENGTH('dbo.Usuario', 'Telefono') IS NOT NULL ALTER TABLE Usuario DROP COLUMN Telefono;
GO

-- Profesional
IF COL_LENGTH('dbo.Profesional', 'MatriculaEspecialista') IS NOT NULL ALTER TABLE Profesional DROP COLUMN MatriculaEspecialista;
IF COL_LENGTH('dbo.Profesional', 'JurisdiccionMatricula') IS NOT NULL ALTER TABLE Profesional DROP COLUMN JurisdiccionMatricula;
GO

-- Especialidad
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Especialidad_Tipo') ALTER TABLE Especialidad DROP CONSTRAINT CK_Especialidad_Tipo;
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_Especialidad_Tipo') ALTER TABLE Especialidad DROP CONSTRAINT DF_Especialidad_Tipo;
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_Especialidad_ReqMatEsp') ALTER TABLE Especialidad DROP CONSTRAINT DF_Especialidad_ReqMatEsp;
GO
IF COL_LENGTH('dbo.Especialidad', 'RequiereMatriculaEspecialista') IS NOT NULL ALTER TABLE Especialidad DROP COLUMN RequiereMatriculaEspecialista;
IF COL_LENGTH('dbo.Especialidad', 'Tipo') IS NOT NULL ALTER TABLE Especialidad DROP COLUMN Tipo;
IF COL_LENGTH('dbo.Especialidad', 'Disciplina') IS NOT NULL ALTER TABLE Especialidad DROP COLUMN Disciplina;
GO

DELETE FROM VersionEsquema WHERE Numero IN (20, 21, 22, 23);
GO

PRINT N'Etapa 2 deshecha en la base. Falta volver a la API anterior si todavía no se hizo.';
SELECT (SELECT COUNT(*) FROM sys.tables) AS tablas, (SELECT MAX(Numero) FROM VersionEsquema) AS ultima_version;
GO
