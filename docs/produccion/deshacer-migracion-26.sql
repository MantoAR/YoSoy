/* ============================================================================
   deshacer-migracion-26.sql - Saca de la base lo que agrego la migracion 26 (cierre de sesion): SesionClinica.ClaveCierre, TurnoRealizadoUtc e IdEvolucion, y sus indices.

   CUANDO: casi nunca. Lo normal es dejar las columnas (la API anterior las ignora) o volver a la API anterior sin tocar la base.
   Esto solo hace falta si se quiere dejar la base exactamente como antes de la migracion, sin restaurar el backup.

   QUE PIERDE: la clave de cada cierre (despues de esto, repetir un cierre ya no se reconoce), cuando el cierre paso el turno a Realizado y los vinculos sesion-evolucion
   hechos a mano. Los primeros SELECT cuentan cuantas filas tienen cada dato, por si queres anotarlo. NO toca el estado de ningun turno ni de ningun caso (los que pasaron a
   Realizado / En atencion siguen asi: eso ya es historia clinica) ni las evoluciones.

   REQUISITOS: la API de esta version parada o ya reemplazada por la anterior.
   Todo en una sola transaccion: si algo falla, no cambia nada.
     sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i docs/produccion/deshacer-migracion-26.sql
   ============================================================================ */
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH('dbo.SesionClinica', 'ClaveCierre') IS NOT NULL
BEGIN
    PRINT N'Filas que pierden dato (con clave / con turno marcado / con evolucion vinculada):';
    EXEC(N'SELECT SUM(CASE WHEN ClaveCierre IS NULL THEN 0 ELSE 1 END) AS con_clave, SUM(CASE WHEN TurnoRealizadoUtc IS NULL THEN 0 ELSE 1 END) AS con_turno_marcado, SUM(CASE WHEN IdEvolucion IS NULL THEN 0 ELSE 1 END) AS con_evolucion FROM dbo.SesionClinica');
END
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_SesionClinica_Clave' AND object_id = OBJECT_ID('dbo.SesionClinica'))
        DROP INDEX UX_SesionClinica_Clave ON dbo.SesionClinica;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_SesionClinica_Evolucion' AND object_id = OBJECT_ID('dbo.SesionClinica'))
        DROP INDEX UX_SesionClinica_Evolucion ON dbo.SesionClinica;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SesionClinica_Turno' AND object_id = OBJECT_ID('dbo.SesionClinica'))
        DROP INDEX IX_SesionClinica_Turno ON dbo.SesionClinica;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_SesionClinica_Evolucion')
        ALTER TABLE dbo.SesionClinica DROP CONSTRAINT FK_SesionClinica_Evolucion;

    IF COL_LENGTH('dbo.SesionClinica', 'ClaveCierre') IS NOT NULL ALTER TABLE dbo.SesionClinica DROP COLUMN ClaveCierre;
    IF COL_LENGTH('dbo.SesionClinica', 'TurnoRealizadoUtc') IS NOT NULL ALTER TABLE dbo.SesionClinica DROP COLUMN TurnoRealizadoUtc;
    IF COL_LENGTH('dbo.SesionClinica', 'IdEvolucion') IS NOT NULL ALTER TABLE dbo.SesionClinica DROP COLUMN IdEvolucion;

    DELETE FROM dbo.VersionEsquema WHERE Numero = 26;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO

PRINT N'Migracion 26 deshecha en la base. Falta volver a la API anterior si todavia no se hizo.';
SELECT (SELECT COUNT(*) FROM sys.tables) AS tablas, (SELECT MAX(Numero) FROM VersionEsquema) AS ultima_version;
GO
