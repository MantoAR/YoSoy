/* ============================================================================
   deshacer-migracion-34.sql — Saca de la base lo que agregó la migración 34 (paciente «Derivado» a psiquiatría).

   CUÁNDO: solo si se vuelve a la API anterior y se quiere dejar la base exactamente como antes, sin restaurar el backup. La API anterior ignora la tabla,
   así que lo normal es dejarla.

   QUÉ PIERDE: la constancia de cada derivación a psiquiatría (quién, cuándo, desde qué sesión) y de cada reapertura. Los pacientes que estaban «Derivados»
   dejan de figurar así y vuelven a poder reservar turnos. Los SELECT de abajo lo cuentan, por si querés anotarlo antes.

   REQUISITOS: la API de esta versión parada o ya reemplazada por la anterior. Todo en una sola transacción: si algo falla, no cambia nada.
     sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i docs/produccion/deshacer-migracion-34.sql
   ============================================================================ */
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.DerivacionPsiquiatrica', 'U') IS NOT NULL
BEGIN
    PRINT N'Derivaciones a psiquiatría (se pierden): vigentes y reabiertas:';
    EXEC(N'SELECT SUM(CASE WHEN ReabiertaUtc IS NULL THEN 1 ELSE 0 END) AS vigentes, SUM(CASE WHEN ReabiertaUtc IS NULL THEN 0 ELSE 1 END) AS reabiertas FROM dbo.DerivacionPsiquiatrica');
END
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID('dbo.DerivacionPsiquiatrica', 'U') IS NOT NULL DROP TABLE dbo.DerivacionPsiquiatrica;

    DELETE FROM dbo.VersionEsquema WHERE Numero = 34;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO

PRINT N'Migración 34 deshecha.';
GO
