/* ============================================================================
   deshacer-migracion-32.sql — Saca de la base lo que agregó la migración 32 (propuesta automática de profesional y horario).

   CUÁNDO: solo si se vuelve a la API anterior y se quiere dejar la base exactamente como antes, sin restaurar el backup. La API anterior ignora
   la tabla y las columnas, así que lo normal es dejarlas.

   QUÉ PIERDE: todas las propuestas hechas (quién, cuándo, a quién y cómo respondió) y el estado de «asignación por resolver» de cada formulario.
   Los turnos que ya se crearon al aceptar una propuesta NO se tocan (son turnos comunes), ni los vínculos paciente-profesional. Los SELECT de abajo
   cuentan lo que se pierde, por si querés anotarlo. Las propuestas Pendientes no se pueden deshacer en limpio: los pacientes que las tenían vuelven a
   esperar a que la recepción les asigne un profesional.

   REQUISITOS: la API de esta versión parada o ya reemplazada por la anterior.
   Todo en una sola transacción: si algo falla, no cambia nada.
     sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i docs/produccion/deshacer-migracion-32.sql
   ============================================================================ */
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.PropuestaAsignacion', 'U') IS NOT NULL
BEGIN
    PRINT N'Propuestas (se pierden) por estado:';
    EXEC(N'SELECT Estado, COUNT(*) AS propuestas FROM dbo.PropuestaAsignacion GROUP BY Estado');
END
IF COL_LENGTH('dbo.InformeAdmision', 'AsignacionEstado') IS NOT NULL
BEGIN
    PRINT N'Formularios con estado de asignación automática (pierden ese dato):';
    EXEC(N'SELECT COUNT(*) AS formularios FROM dbo.InformeAdmision WHERE AsignacionEstado IS NOT NULL');
END
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID('dbo.PropuestaAsignacion', 'U') IS NOT NULL DROP TABLE dbo.PropuestaAsignacion;

    IF COL_LENGTH('dbo.InformeAdmision', 'AsignacionMotivo') IS NOT NULL ALTER TABLE dbo.InformeAdmision DROP COLUMN AsignacionMotivo;
    IF COL_LENGTH('dbo.InformeAdmision', 'AsignacionEstado') IS NOT NULL ALTER TABLE dbo.InformeAdmision DROP COLUMN AsignacionEstado;

    DELETE FROM dbo.VersionEsquema WHERE Numero = 32;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO

PRINT N'Migración 32 deshecha.';
GO
