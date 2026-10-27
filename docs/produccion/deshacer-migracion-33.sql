/* ============================================================================
   deshacer-migracion-33.sql — Saca de la base lo que agregó la migración 33 (feriados, bloqueos, matrículas, estado Ausente).

   CUÁNDO: casi nunca. Lo normal es volver a la API anterior (ignora estas tablas y sigue andando con la base migrada).

   QUÉ PIERDE: los feriados cargados, los bloqueos de agenda, los vencimientos de matrícula y las matrículas secundarias.
   Los turnos marcados como Ausente vuelven a Confirmado (el primer SELECT los lista). La auditoría no se toca.

   REQUISITOS: la API de esta versión parada o ya reemplazada por la anterior.
     sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i docs/produccion/deshacer-migracion-33.sql
   ============================================================================ */
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

DECLARE @Ausente INT = (SELECT IdEstadoTurno FROM EstadoTurno WHERE Descripcion = N'Ausente');
DECLARE @Confirmado INT = (SELECT IdEstadoTurno FROM EstadoTurno WHERE Descripcion = N'Confirmado');

IF @Ausente IS NOT NULL
BEGIN
    PRINT N'Turnos marcados como Ausente (vuelven a Confirmado):';
    SELECT IdTurno, IdProfesional, Fecha, Hora FROM Turno WHERE IdEstadoTurno = @Ausente ORDER BY Fecha, Hora;
    BEGIN TRAN;
    UPDATE Turno SET IdEstadoTurno = @Confirmado WHERE IdEstadoTurno = @Ausente;
    DELETE FROM EstadoTurno WHERE IdEstadoTurno = @Ausente;
    COMMIT;
END
GO

IF OBJECT_ID('dbo.AvisoMatricula', 'U') IS NOT NULL       DROP TABLE dbo.AvisoMatricula;
IF OBJECT_ID('dbo.ProfesionalMatricula', 'U') IS NOT NULL DROP TABLE dbo.ProfesionalMatricula;
IF OBJECT_ID('dbo.BloqueoAgenda', 'U') IS NOT NULL        DROP TABLE dbo.BloqueoAgenda;
IF OBJECT_ID('dbo.FeriadoTrabajado', 'U') IS NOT NULL     DROP TABLE dbo.FeriadoTrabajado;
IF OBJECT_ID('dbo.Feriado', 'U') IS NOT NULL              DROP TABLE dbo.Feriado;
GO

DELETE FROM VersionEsquema WHERE Numero = 33;
GO

PRINT N'Migración 33 deshecha en la base. Falta volver a la API anterior si todavía no se hizo.';
SELECT (SELECT MAX(Numero) FROM VersionEsquema) AS ultima_version;
GO
