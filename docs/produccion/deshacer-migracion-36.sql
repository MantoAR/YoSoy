/* ============================================================================
   deshacer-migracion-36.sql — Saca de la base lo que agregó la migración 36 (vencimiento de las solicitudes de turno del paciente).

   CUÁNDO: solo si se vuelve a la API anterior y se quiere dejar la base exactamente como antes. La API anterior ignora la columna, así que lo normal es dejarla.
   QUÉ PIERDE: la fecha hasta la que se podía responder cada solicitud pendiente. Los turnos y sus estados NO se tocan.
   REQUISITOS: todo en una sola transacción: si algo falla, no cambia nada.
     sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i docs/produccion/deshacer-migracion-36.sql
   ============================================================================ */
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Turno_SolicitudExpiraEn' AND object_id = OBJECT_ID('dbo.Turno'))
        DROP INDEX IX_Turno_SolicitudExpiraEn ON dbo.Turno;
    IF COL_LENGTH('dbo.Turno', 'SolicitudExpiraEn') IS NOT NULL ALTER TABLE dbo.Turno DROP COLUMN SolicitudExpiraEn;

    DELETE FROM dbo.VersionEsquema WHERE Numero = 36;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO

PRINT N'Migración 36 deshecha.';
GO
