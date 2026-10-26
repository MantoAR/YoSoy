/* ============================================================================
   deshacer-migracion-28.sql - Saca de la base lo que agrego la migracion 28 (consentimientos dentro del circuito): la tabla AceptacionVideollamada y la columna
   Consentimiento.IdTutorFirmante.

   CUANDO: casi nunca. Lo normal es dejar lo agregado (la API anterior lo ignora) o volver a la API anterior sin tocar la base.

   SE NIEGA A BORRAR DATOS REALES. Si ya hay aceptaciones de videollamada o firmas de un tutor guardadas, el script se detiene sin cambiar nada y dice cuantas hay: son
   constancia de consentimiento, no se pierden en silencio. Si de verdad queres quitarlas, exportalas primero y borralas a mano; despues volve a correr este script.

   QUE HACE (sin datos): borra la tabla AceptacionVideollamada (vacia), quita la columna IdTutorFirmante y su clave foranea, y borra la version 28.
   REQUISITOS: la API de esta version parada o ya reemplazada por la anterior.
   Todo en una sola transaccion: si algo falla, no cambia nada.
     sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i docs/produccion/deshacer-migracion-28.sql
   ============================================================================ */
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

DECLARE @aceptaciones INT = 0, @firmasTutor INT = 0;
IF OBJECT_ID('dbo.AceptacionVideollamada', 'U') IS NOT NULL SELECT @aceptaciones = COUNT(*) FROM dbo.AceptacionVideollamada;
IF COL_LENGTH('dbo.Consentimiento', 'IdTutorFirmante') IS NOT NULL EXEC sp_executesql N'SELECT @n = COUNT(*) FROM dbo.Consentimiento WHERE IdTutorFirmante IS NOT NULL', N'@n INT OUTPUT', @n = @firmasTutor OUTPUT;
PRINT CONCAT(N'Aceptaciones de videollamada: ', @aceptaciones, N' | firmas de tutor: ', @firmasTutor);
IF @aceptaciones > 0 OR @firmasTutor > 0
    RAISERROR(N'Hay aceptaciones de videollamada o firmas de tutor guardadas: no se borran en silencio. Exportalas y borralas a mano antes de deshacer la migracion 28.', 16, 1);
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID('dbo.AceptacionVideollamada', 'U') IS NOT NULL DROP TABLE dbo.AceptacionVideollamada;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Consentimiento_TutorFirmante') ALTER TABLE dbo.Consentimiento DROP CONSTRAINT FK_Consentimiento_TutorFirmante;
    IF COL_LENGTH('dbo.Consentimiento', 'IdTutorFirmante') IS NOT NULL ALTER TABLE dbo.Consentimiento DROP COLUMN IdTutorFirmante;

    DELETE FROM dbo.VersionEsquema WHERE Numero = 28;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO

PRINT N'Migracion 28 deshecha en la base. Falta volver a la API anterior si todavia no se hizo.';
SELECT (SELECT COUNT(*) FROM sys.tables) AS tablas, (SELECT MAX(Numero) FROM VersionEsquema) AS ultima_version;
GO
