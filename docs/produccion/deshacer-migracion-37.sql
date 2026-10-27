/* ============================================================================
   deshacer-migracion-37.sql — Saca lo que agregó la migración 37 (asistencia y abandono).

   ATENCIÓN: borra los seguimientos y los abandonos de tratamiento confirmados. Usar solo antes
   de usarlo en serio (pruebas). Los turnos marcados Ausente siguen Ausentes (eso es de la 33).

   Cómo correrlo:
     sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i docs/produccion/deshacer-migracion-37.sql
   ============================================================================ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.TR_AbandonoTratamiento_SoloAgregar', N'TR') IS NOT NULL DROP TRIGGER dbo.TR_AbandonoTratamiento_SoloAgregar;
IF OBJECT_ID(N'dbo.AbandonoTratamiento', N'U') IS NOT NULL DROP TABLE dbo.AbandonoTratamiento;
IF OBJECT_ID(N'dbo.SeguimientoAsistencia', N'U') IS NOT NULL DROP TABLE dbo.SeguimientoAsistencia;

DELETE up FROM dbo.UsuarioPrivilegio up JOIN dbo.Privilegio pr ON pr.IdPrivilegio = up.IdPrivilegio WHERE pr.Clave = 'gestion_pacientes';
DELETE pp FROM dbo.PermisoPrivilegio pp JOIN dbo.Privilegio pr ON pr.IdPrivilegio = pp.IdPrivilegio WHERE pr.Clave = 'gestion_pacientes';
DELETE FROM dbo.Privilegio WHERE Clave = 'gestion_pacientes';
DELETE FROM dbo.ConfiguracionOrganizacion WHERE Clave LIKE N'Asistencia.%';

IF COL_LENGTH(N'dbo.Turno', N'FechaAsistenciaUtc') IS NOT NULL ALTER TABLE dbo.Turno DROP COLUMN FechaAsistenciaUtc;
IF COL_LENGTH(N'dbo.Turno', N'IdUsuarioAsistencia') IS NOT NULL ALTER TABLE dbo.Turno DROP COLUMN IdUsuarioAsistencia;
IF COL_LENGTH(N'dbo.Turno', N'MotivoNoCobrable') IS NOT NULL ALTER TABLE dbo.Turno DROP COLUMN MotivoNoCobrable;
IF COL_LENGTH(N'dbo.Turno', N'Cobrable') IS NOT NULL ALTER TABLE dbo.Turno DROP COLUMN Cobrable;

DELETE FROM dbo.VersionEsquema WHERE Numero = 37;
COMMIT TRANSACTION;
PRINT N'Migración 37 deshecha.';
