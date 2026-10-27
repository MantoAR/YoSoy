-- Solicitudes de turno del paciente que esperan al profesional (octubre 2026). Es la migración 36. Aditiva, idempotente y en una sola transacción.
-- Requiere la 18 (la tabla VersionEsquema) y la tabla Turno, nada más.
--
--   sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i migrations/add_solicitud_turno_vencimiento.sql
--
-- Es OPCIONAL para que la API arranque: sin ella las solicitudes del paciente siguen quedando Pendientes hasta que el profesional las acepte o rechace (como siempre), pero NO vencen solas.
-- Aplicarla ANTES de publicar la API que la usa.
--
-- Deshacer (con la API anterior otra vez publicada):
--   docs/produccion/deshacer-migracion-36.sql
--
-- Qué hace:
--  1. Turno.SolicitudExpiraEn (DATETIME2(0), NULL): hasta cuándo puede el profesional responder una solicitud que hizo el paciente. La API la completa al crear la solicitud
--     (el menor entre 24 hs y el horario del turno menos 2 hs). Los turnos que ya existen quedan en NULL: no vencen.
--  2. Un índice para la pasada de vencimiento.
--  3. Registra la versión 36 en VersionEsquema, con Origen 'Script' como las anteriores.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.VersionEsquema', 'U') IS NULL OR OBJECT_ID('dbo.Turno', 'U') IS NULL
    RAISERROR(N'Falta VersionEsquema o Turno: aplicá antes las migraciones anteriores (ver docs/CATALOGO_SCRIPTS_SQL.md).', 16, 1);
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH('dbo.Turno', 'SolicitudExpiraEn') IS NULL ALTER TABLE dbo.Turno ADD SolicitudExpiraEn DATETIME2(0) NULL;

    -- En EXEC: la columna recién creada no existe todavía cuando se compila este lote.
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Turno_SolicitudExpiraEn' AND object_id = OBJECT_ID('dbo.Turno'))
        EXEC(N'CREATE INDEX IX_Turno_SolicitudExpiraEn ON dbo.Turno (SolicitudExpiraEn) WHERE SolicitudExpiraEn IS NOT NULL');

    IF NOT EXISTS (SELECT 1 FROM dbo.VersionEsquema WHERE Numero = 36)
        INSERT INTO dbo.VersionEsquema (Numero, Archivo, Origen) VALUES (36, N'add_solicitud_turno_vencimiento.sql', 'Script');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO

PRINT N'Migración 36 aplicada.';
GO
