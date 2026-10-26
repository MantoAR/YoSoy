/* ============================================================================
   deshacer-migracion-27.sql - Saca de la base lo que agrego la migracion 27 (alta clinica y derivacion interna asistidas desde el cierre de sesion).

   CUANDO: casi nunca. Lo normal es dejar lo agregado (la API anterior lo ignora) o volver a la API anterior sin tocar la base.

   SE NIEGA A BORRAR DATOS REALES. Si ya hay altas clinicas, casos en 'En alta' o derivaciones nacidas del cierre, el script se detiene sin cambiar nada y dice cuantas
   hay: son historia clinica y circuito del paciente, no se pierden en silencio. Si de verdad queres quitarlas, exportalas primero y borralas a mano; despues volve a correr
   este script.

   QUE HACE (sin datos): quita el indice y las columnas nuevas de Derivacion, borra la tabla AltaClinica (vacia), devuelve el CHECK de Caso.Estado a sus cinco valores de
   antes y borra la version 27.
   REQUISITOS: la API de esta version parada o ya reemplazada por la anterior.
   Todo en una sola transaccion: si algo falla, no cambia nada.
     sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i docs/produccion/deshacer-migracion-27.sql
   ============================================================================ */
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

DECLARE @altas INT = 0, @casosEnAlta INT = 0, @derivaciones INT = 0;
IF OBJECT_ID('dbo.AltaClinica', 'U') IS NOT NULL SELECT @altas = COUNT(*) FROM dbo.AltaClinica;
SELECT @casosEnAlta = COUNT(*) FROM dbo.Caso WHERE Estado = N'En alta';
IF COL_LENGTH('dbo.Derivacion', 'Origen') IS NOT NULL EXEC sp_executesql N'SELECT @n = COUNT(*) FROM dbo.Derivacion WHERE Origen = N''Cierre''', N'@n INT OUTPUT', @n = @derivaciones OUTPUT;
PRINT CONCAT(N'Altas clinicas: ', @altas, N' | casos en alta: ', @casosEnAlta, N' | derivaciones del cierre: ', @derivaciones);
IF @altas > 0 OR @casosEnAlta > 0 OR @derivaciones > 0
    RAISERROR(N'Hay altas clinicas, casos en alta o derivaciones del cierre: no se borran en silencio. Exportalas y borralas a mano antes de deshacer la migracion 27.', 16, 1);
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Derivacion_CasoEnCurso' AND object_id = OBJECT_ID('dbo.Derivacion')) DROP INDEX UX_Derivacion_CasoEnCurso ON dbo.Derivacion;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Derivacion_Caso') ALTER TABLE dbo.Derivacion DROP CONSTRAINT FK_Derivacion_Caso;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Derivacion_Sesion') ALTER TABLE dbo.Derivacion DROP CONSTRAINT FK_Derivacion_Sesion;
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Derivacion_Origen') ALTER TABLE dbo.Derivacion DROP CONSTRAINT CK_Derivacion_Origen;
    IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_Derivacion_Origen') ALTER TABLE dbo.Derivacion DROP CONSTRAINT DF_Derivacion_Origen;
    IF COL_LENGTH('dbo.Derivacion', 'Origen') IS NOT NULL ALTER TABLE dbo.Derivacion DROP COLUMN Origen;
    IF COL_LENGTH('dbo.Derivacion', 'IdCaso') IS NOT NULL ALTER TABLE dbo.Derivacion DROP COLUMN IdCaso;
    IF COL_LENGTH('dbo.Derivacion', 'Observaciones') IS NOT NULL ALTER TABLE dbo.Derivacion DROP COLUMN Observaciones;
    IF COL_LENGTH('dbo.Derivacion', 'IdSesion') IS NOT NULL ALTER TABLE dbo.Derivacion DROP COLUMN IdSesion;
    IF COL_LENGTH('dbo.Derivacion', 'RespuestaUtc') IS NOT NULL ALTER TABLE dbo.Derivacion DROP COLUMN RespuestaUtc;
    IF COL_LENGTH('dbo.Derivacion', 'IdUsuarioRespuesta') IS NOT NULL ALTER TABLE dbo.Derivacion DROP COLUMN IdUsuarioRespuesta;
    IF COL_LENGTH('dbo.Derivacion', 'ResolucionUtc') IS NOT NULL ALTER TABLE dbo.Derivacion DROP COLUMN ResolucionUtc;
    IF COL_LENGTH('dbo.Derivacion', 'IdUsuarioResolucion') IS NOT NULL ALTER TABLE dbo.Derivacion DROP COLUMN IdUsuarioResolucion;

    IF OBJECT_ID('dbo.AltaClinica', 'U') IS NOT NULL DROP TABLE dbo.AltaClinica;

    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Caso_Estado' AND parent_object_id = OBJECT_ID('dbo.Caso') AND definition LIKE N'%En alta%')
    BEGIN
        ALTER TABLE dbo.Caso DROP CONSTRAINT CK_Caso_Estado;
        ALTER TABLE dbo.Caso ADD CONSTRAINT CK_Caso_Estado CHECK (Estado IN (N'Nuevo', N'Derivado', N'Asignado', N'En atención', N'Cerrado'));
    END

    DELETE FROM dbo.VersionEsquema WHERE Numero = 27;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO

PRINT N'Migracion 27 deshecha en la base. Falta volver a la API anterior si todavia no se hizo.';
SELECT (SELECT COUNT(*) FROM sys.tables) AS tablas, (SELECT MAX(Numero) FROM VersionEsquema) AS ultima_version;
GO
