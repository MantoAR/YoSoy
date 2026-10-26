-- Fecha de inicio y de fin de cada franja de "Mis horarios de trabajo" (octubre 2026). Es la migración 30. Aditiva, idempotente y en una sola transacción.
-- Requiere la 18 (la tabla VersionEsquema) y la tabla Agenda, nada más.
--
--   sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i migrations/add_vigencia_agenda.sql
--
-- La API de esta versión la NECESITA (lee las dos columnas): aplicarla ANTES de publicar la API. La API anterior las ignora.
--
-- Deshacer (con la API anterior otra vez publicada):
--   docs/produccion/deshacer-migracion-30.sql
--
-- Qué hace:
--  1. Agenda.FechaInicio y Agenda.FechaFin DATE NULL: desde qué día y hasta qué día vale la franja. NULL = sin límite de ese lado, así las franjas
--     que ya existen siguen valiendo siempre, como hasta ahora. Algunas bases viejas ya tienen estas dos columnas (sin uso): se respetan tal cual.
--     Agregar una columna NULL es solo un cambio de metadatos: no recorre la tabla.
--  2. CK_Agenda_Vigencia: la fecha de fin no puede ser anterior a la de inicio.
--  3. Registra la versión 30 en VersionEsquema, con Origen 'Script' como las anteriores.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.VersionEsquema', 'U') IS NULL OR OBJECT_ID('dbo.Agenda', 'U') IS NULL
    RAISERROR(N'Falta VersionEsquema o Agenda: aplicá antes las migraciones anteriores (ver docs/CATALOGO_SCRIPTS_SQL.md).', 16, 1);
GO

BEGIN TRY
    BEGIN TRANSACTION;

    -- ── 1) Vigencia de la franja ──────────────────────────────
    IF COL_LENGTH('dbo.Agenda', 'FechaInicio') IS NULL
        ALTER TABLE dbo.Agenda ADD FechaInicio DATE NULL;
    IF COL_LENGTH('dbo.Agenda', 'FechaFin') IS NULL
        ALTER TABLE dbo.Agenda ADD FechaFin DATE NULL;

    -- ── 2) Fin no anterior al inicio ──────────────────────────
    -- En un EXEC: las columnas recién agregadas no existen todavía cuando se compila este lote.
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Agenda_Vigencia')
        EXEC(N'ALTER TABLE dbo.Agenda ADD CONSTRAINT CK_Agenda_Vigencia
               CHECK (FechaInicio IS NULL OR FechaFin IS NULL OR FechaFin >= FechaInicio);');

    -- ── 3) Registro de versión ────────────────────────────────
    IF NOT EXISTS (SELECT 1 FROM dbo.VersionEsquema WHERE Numero = 30)
        INSERT INTO dbo.VersionEsquema (Numero, Archivo, Origen) VALUES (30, N'add_vigencia_agenda.sql', 'Script');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO

PRINT N'Migración 30 aplicada.';
EXEC(N'SELECT COUNT(*) AS franjas, COUNT(FechaInicio) AS con_inicio, COUNT(FechaFin) AS con_fin FROM dbo.Agenda');
GO
