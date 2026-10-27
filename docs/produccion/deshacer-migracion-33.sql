/* ============================================================================
   deshacer-migracion-33.sql — Saca de la base lo que agregó la migración 33 (Evolucion.TipoCierre).

   CUÁNDO: solo si se vuelve a la API anterior y se quiere dejar la base exactamente como antes, sin restaurar el backup. La API de esta versión
   funciona igual sin la columna, y la anterior la ignora, así que lo normal es dejarla.

   QUÉ PIERDE: el tipo de cierre guardado en las evoluciones creadas desde que se aplicó la migración. Los SELECT de abajo lo cuentan.

   REQUISITOS: ninguno especial. Todo en una sola transacción: si algo falla, no cambia nada.
     sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i docs/produccion/deshacer-migracion-33.sql
   ============================================================================ */
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH('dbo.Evolucion', 'TipoCierre') IS NOT NULL
BEGIN
    PRINT N'Evoluciones con tipo de cierre (pierden ese dato):';
    EXEC(N'SELECT COUNT(*) AS evoluciones FROM dbo.Evolucion WHERE TipoCierre IS NOT NULL');
END
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH('dbo.Evolucion', 'TipoCierre') IS NOT NULL ALTER TABLE dbo.Evolucion DROP COLUMN TipoCierre;

    DELETE FROM dbo.VersionEsquema WHERE Numero = 33;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO

PRINT N'Migración 33 deshecha.';
GO
