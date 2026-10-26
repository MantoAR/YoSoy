/* ============================================================================
   deshacer-migracion-31.sql — Saca de la base lo que agregó la migración 31 (primeros pasos del paciente).

   CUÁNDO: solo si se vuelve a la API anterior y se quiere dejar la base exactamente como antes, sin restaurar el backup. La API anterior ignora
   estas columnas, así que lo normal es dejarlas.

   QUÉ PIERDE: la aclaración y la constancia de revisión de cada formulario, el horario y los profesionales preferidos, la constancia de
   revocación de cada consentimiento (la baja en sí NO se pierde: la fila sigue 'Inactivo') y la clave de evento de los avisos. Los SELECT de abajo
   cuentan lo que se pierde, por si querés anotarlo.

   REQUISITOS: la API de esta versión parada o ya reemplazada por la anterior (la nueva lee estas columnas y sin ellas falla).
   Todo en una sola transacción: si algo falla, no cambia nada.
     sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i docs/produccion/deshacer-migracion-31.sql
   ============================================================================ */
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH('dbo.InformeAdmision', 'FechaRevision') IS NOT NULL
BEGIN
    PRINT N'Formularios revisados o con preferencias (pierden esos datos):';
    EXEC(N'SELECT COUNT(*) AS revisados FROM dbo.InformeAdmision WHERE FechaRevision IS NOT NULL OR HorarioPreferido IS NOT NULL OR ProfesionalesPreferidos IS NOT NULL');
END
IF COL_LENGTH('dbo.Consentimiento', 'FechaRevocacion') IS NOT NULL
BEGIN
    PRINT N'Consentimientos con constancia de revocación (pierden la constancia, no la baja):';
    EXEC(N'SELECT COUNT(*) AS revocados FROM dbo.Consentimiento WHERE FechaRevocacion IS NOT NULL');
END
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Notificacion_Evento' AND object_id = OBJECT_ID('dbo.Notificacion'))
        DROP INDEX UX_Notificacion_Evento ON dbo.Notificacion;
    IF COL_LENGTH('dbo.Notificacion', 'Evento') IS NOT NULL ALTER TABLE dbo.Notificacion DROP COLUMN Evento;

    IF COL_LENGTH('dbo.Consentimiento', 'IdUsuarioRevocacion') IS NOT NULL ALTER TABLE dbo.Consentimiento DROP COLUMN IdUsuarioRevocacion;
    IF COL_LENGTH('dbo.Consentimiento', 'FechaRevocacion') IS NOT NULL ALTER TABLE dbo.Consentimiento DROP COLUMN FechaRevocacion;

    IF COL_LENGTH('dbo.InformeAdmision', 'ProfesionalesPreferidos') IS NOT NULL ALTER TABLE dbo.InformeAdmision DROP COLUMN ProfesionalesPreferidos;
    IF COL_LENGTH('dbo.InformeAdmision', 'HorarioPreferido') IS NOT NULL ALTER TABLE dbo.InformeAdmision DROP COLUMN HorarioPreferido;
    IF COL_LENGTH('dbo.InformeAdmision', 'FechaRevision') IS NOT NULL ALTER TABLE dbo.InformeAdmision DROP COLUMN FechaRevision;
    IF COL_LENGTH('dbo.InformeAdmision', 'IdUsuarioRevision') IS NOT NULL ALTER TABLE dbo.InformeAdmision DROP COLUMN IdUsuarioRevision;
    IF COL_LENGTH('dbo.InformeAdmision', 'AclaracionRecepcion') IS NOT NULL ALTER TABLE dbo.InformeAdmision DROP COLUMN AclaracionRecepcion;

    DELETE FROM dbo.VersionEsquema WHERE Numero = 31;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO

PRINT N'Migración 31 deshecha.';
GO
