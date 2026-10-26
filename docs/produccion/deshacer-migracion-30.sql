/* ============================================================================
   deshacer-migracion-30.sql — Saca de la base lo que agregó la migración 30 (Agenda.FechaInicio, Agenda.FechaFin y CK_Agenda_Vigencia).

   CUÁNDO: solo si se vuelve a la API anterior y se quiere dejar la base exactamente como antes, sin restaurar el backup. La API anterior ignora
   las dos columnas, así que lo normal es dejarlas.

   QUÉ PIERDE: desde y hasta qué día valía cada franja de "Mis horarios de trabajo" (vuelven a valer siempre). El SELECT de abajo cuenta lo que se
   pierde, por si querés anotarlo.

   REQUISITOS: la API de esta versión parada o ya reemplazada por la anterior (la nueva lee las dos columnas y sin ellas falla).
   Todo en una sola transacción: si algo falla, no cambia nada.
     sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i docs/produccion/deshacer-migracion-30.sql
   ============================================================================ */
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH('dbo.Agenda', 'FechaInicio') IS NOT NULL AND COL_LENGTH('dbo.Agenda', 'FechaFin') IS NOT NULL
BEGIN
    PRINT N'Franjas con fecha de inicio o de fin (vuelven a valer siempre):';
    EXEC(N'SELECT COUNT(*) AS franjas_con_vigencia FROM dbo.Agenda WHERE FechaInicio IS NOT NULL OR FechaFin IS NOT NULL');
END
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Agenda_Vigencia')
        ALTER TABLE dbo.Agenda DROP CONSTRAINT CK_Agenda_Vigencia;
    IF COL_LENGTH('dbo.Agenda', 'FechaFin') IS NOT NULL
        ALTER TABLE dbo.Agenda DROP COLUMN FechaFin;
    IF COL_LENGTH('dbo.Agenda', 'FechaInicio') IS NOT NULL
        ALTER TABLE dbo.Agenda DROP COLUMN FechaInicio;

    DELETE FROM dbo.VersionEsquema WHERE Numero = 30;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO

PRINT N'Migración 30 deshecha.';
GO
