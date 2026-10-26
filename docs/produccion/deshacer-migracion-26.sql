/* ============================================================================
   deshacer-migracion-26.sql — Saca de la base lo que agregó la migración 26 (Agenda.EsAdmision e InformeAdmision.IdProfesionalEntrevista).

   CUÁNDO: solo si se vuelve a la API anterior y se quiere dejar la base exactamente como antes, sin restaurar el backup. La API anterior ignora
   las dos columnas, así que lo normal es dejarlas.

   QUÉ PIERDE: qué franjas de "Mis horarios de trabajo" eran de admisión (vuelven a ser franjas de turnos comunes) y quién programó cada
   entrevista de admisión. Las fechas de las entrevistas y todo lo demás no se tocan. Los SELECT de abajo cuentan lo que se pierde, por si
   querés anotarlo.

   REQUISITOS: la API de esta versión parada o ya reemplazada por la anterior (la nueva lee las dos columnas y sin ellas falla).
   Todo en una sola transacción: si algo falla, no cambia nada.
     sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i docs/produccion/deshacer-migracion-26.sql
   ============================================================================ */
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH('dbo.Agenda', 'EsAdmision') IS NOT NULL
BEGIN
    PRINT N'Franjas marcadas como de admisión (vuelven a ser comunes):';
    EXEC(N'SELECT COUNT(*) AS franjas_de_admision FROM dbo.Agenda WHERE EsAdmision = 1');
END
IF COL_LENGTH('dbo.InformeAdmision', 'IdProfesionalEntrevista') IS NOT NULL
BEGIN
    PRINT N'Admisiones con el profesional que programó la entrevista (se pierde ese dato):';
    EXEC(N'SELECT COUNT(*) AS con_entrevistador FROM dbo.InformeAdmision WHERE IdProfesionalEntrevista IS NOT NULL');
END
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_InformeAdmision_ProfesionalEntrevista')
        ALTER TABLE dbo.InformeAdmision DROP CONSTRAINT FK_InformeAdmision_ProfesionalEntrevista;
    IF COL_LENGTH('dbo.InformeAdmision', 'IdProfesionalEntrevista') IS NOT NULL
        ALTER TABLE dbo.InformeAdmision DROP COLUMN IdProfesionalEntrevista;

    IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_Agenda_EsAdmision')
        ALTER TABLE dbo.Agenda DROP CONSTRAINT DF_Agenda_EsAdmision;
    IF COL_LENGTH('dbo.Agenda', 'EsAdmision') IS NOT NULL
        ALTER TABLE dbo.Agenda DROP COLUMN EsAdmision;

    DELETE FROM dbo.VersionEsquema WHERE Numero = 26;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO

PRINT N'Migración 26 deshecha.';
GO
