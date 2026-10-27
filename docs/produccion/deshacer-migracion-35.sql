/* ============================================================================
   deshacer-migracion-35.sql — Saca de la base lo que agregó la migración 35 (ausencias con umbrales y alta estructurada con plan de control).

   CUÁNDO: solo si se vuelve a la API anterior y se quiere dejar la base exactamente como antes, sin restaurar el backup. La API anterior ignora las tablas,
   así que lo normal es dejarlas.

   QUÉ PIERDE: la constancia de las conexiones a la sala, las propuestas de abandono (confirmadas y descartadas), los controles del plan de alta y el informe
   de alta estructurado (el informe en texto libre y la entrada de la historia clínica NO se tocan). Los SELECT de abajo lo cuentan, por si querés anotarlo antes.

   SE NIEGA A CORRER (no cambia nada) si:
     · hay turnos en estado «Ausente» o casos en estado «Abandono»: son datos reales y no se pueden dejar sin su estado. Revisalos a mano antes.

   REQUISITOS: la API de esta versión parada o ya reemplazada por la anterior. Todo en una sola transacción: si algo falla, no cambia nada.
     sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i docs/produccion/deshacer-migracion-35.sql
   ============================================================================ */
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

DECLARE @ausentes INT = 0, @abandonos INT = 0;
SELECT @ausentes = COUNT(*) FROM dbo.Turno t JOIN dbo.EstadoTurno e ON e.IdEstadoTurno = t.IdEstadoTurno WHERE e.Descripcion = N'Ausente';
SELECT @abandonos = COUNT(*) FROM dbo.Caso WHERE Estado = N'Abandono';
PRINT N'Turnos Ausente: ' + CAST(@ausentes AS NVARCHAR(10)) + N' · casos en Abandono: ' + CAST(@abandonos AS NVARCHAR(10));
IF OBJECT_ID('dbo.PropuestaAbandono', 'U') IS NOT NULL EXEC(N'SELECT COUNT(*) AS propuestas_abandono FROM dbo.PropuestaAbandono');
IF OBJECT_ID('dbo.ControlSeguimiento', 'U') IS NOT NULL EXEC(N'SELECT COUNT(*) AS controles FROM dbo.ControlSeguimiento');
IF @ausentes > 0 OR @abandonos > 0
    RAISERROR(N'Hay turnos Ausente o casos en Abandono: no se deshace nada. Pasalos a otro estado a mano si de verdad querés volver atrás.', 16, 1);
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID('dbo.ControlSeguimiento', 'U') IS NOT NULL DROP TABLE dbo.ControlSeguimiento;
    IF OBJECT_ID('dbo.PropuestaAbandono', 'U') IS NOT NULL DROP TABLE dbo.PropuestaAbandono;
    IF OBJECT_ID('dbo.AvisoSeguimiento', 'U') IS NOT NULL DROP TABLE dbo.AvisoSeguimiento;
    IF OBJECT_ID('dbo.TurnoConexion', 'U') IS NOT NULL DROP TABLE dbo.TurnoConexion;

    IF COL_LENGTH('dbo.AltaClinica', 'InformeEstructurado') IS NOT NULL ALTER TABLE dbo.AltaClinica DROP COLUMN InformeEstructurado;
    IF COL_LENGTH('dbo.AltaClinica', 'IdEvolucion') IS NOT NULL ALTER TABLE dbo.AltaClinica DROP COLUMN IdEvolucion;

    -- Caso.Estado vuelve a la lista de la migración 27 (sin 'Abandono').
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Caso_Estado' AND parent_object_id = OBJECT_ID('dbo.Caso') AND definition LIKE N'%Abandono%')
        ALTER TABLE dbo.Caso DROP CONSTRAINT CK_Caso_Estado;
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Caso_Estado' AND parent_object_id = OBJECT_ID('dbo.Caso'))
        ALTER TABLE dbo.Caso ADD CONSTRAINT CK_Caso_Estado CHECK (Estado IN (N'Nuevo', N'Derivado', N'Asignado', N'En atención', N'En alta', N'Cerrado'));

    -- La fila 'Ausente' del catálogo solo se saca si nadie la usa (ya se comprobó arriba) .
    DELETE FROM dbo.EstadoTurno WHERE Descripcion = N'Ausente'
        AND NOT EXISTS (SELECT 1 FROM dbo.Turno t WHERE t.IdEstadoTurno = dbo.EstadoTurno.IdEstadoTurno);

    DELETE FROM dbo.VersionEsquema WHERE Numero = 35;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO

PRINT N'Migración 35 deshecha.';
GO
