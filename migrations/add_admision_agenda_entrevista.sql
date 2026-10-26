-- Entrevistas de admisión con agenda propia (octubre 2026). Es la migración 26. Aditiva, idempotente y en una sola transacción.
-- Requiere la 18 (la tabla VersionEsquema) y las tablas Agenda, InformeAdmision y Profesional, nada más.
--
--   sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i migrations/add_admision_agenda_entrevista.sql
--
-- La API de esta versión la NECESITA (lee las dos columnas): aplicarla ANTES de publicar la API. La API anterior las ignora.
--
-- Deshacer (con la API anterior otra vez publicada):
--   docs/produccion/deshacer-migracion-26.sql
--
-- Qué hace:
--  1. Agenda.EsAdmision BIT NOT NULL, por defecto 0: marca una franja de "Mis horarios de trabajo" como de entrevistas de admisión. Las franjas que ya
--     existen quedan en 0 (turnos comunes, como hasta ahora). En una franja de admisión no se ofrecen turnos a los pacientes: ahí la API programa sola,
--     una detrás de otra, las primeras entrevistas que agenda ese profesional.
--  2. InformeAdmision.IdProfesionalEntrevista INT NULL (FK a Profesional): quién programó la entrevista; la grilla de admisiones lo muestra. Las admisiones
--     que ya existen quedan en NULL ("—" en la grilla).
--     Agregar una columna con valor por defecto o NULL es solo un cambio de metadatos: no recorre las tablas.
--  3. Registra la versión 26 en VersionEsquema, con Origen 'Script' como las anteriores.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.VersionEsquema', 'U') IS NULL OR OBJECT_ID('dbo.Agenda', 'U') IS NULL
   OR OBJECT_ID('dbo.InformeAdmision', 'U') IS NULL OR OBJECT_ID('dbo.Profesional', 'U') IS NULL
    RAISERROR(N'Falta VersionEsquema, Agenda, InformeAdmision o Profesional: aplicá antes las migraciones anteriores (ver docs/CATALOGO_SCRIPTS_SQL.md).', 16, 1);
GO

BEGIN TRY
    BEGIN TRANSACTION;

    -- ── 1) Franjas de admisión ────────────────────────────────
    IF COL_LENGTH('dbo.Agenda', 'EsAdmision') IS NULL
        ALTER TABLE dbo.Agenda ADD EsAdmision BIT NOT NULL CONSTRAINT DF_Agenda_EsAdmision DEFAULT (0);

    -- ── 2) Quién programó la entrevista ───────────────────────
    IF COL_LENGTH('dbo.InformeAdmision', 'IdProfesionalEntrevista') IS NULL
        ALTER TABLE dbo.InformeAdmision ADD IdProfesionalEntrevista INT NULL;

    -- La FK va en un EXEC: la columna recién agregada no existe todavía cuando se compila este lote.
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_InformeAdmision_ProfesionalEntrevista')
        EXEC(N'ALTER TABLE dbo.InformeAdmision ADD CONSTRAINT FK_InformeAdmision_ProfesionalEntrevista
               FOREIGN KEY (IdProfesionalEntrevista) REFERENCES dbo.Profesional (IdProfesional);');

    -- ── 3) Registro de versión ────────────────────────────────
    IF NOT EXISTS (SELECT 1 FROM dbo.VersionEsquema WHERE Numero = 26)
        INSERT INTO dbo.VersionEsquema (Numero, Archivo, Origen) VALUES (26, N'add_admision_agenda_entrevista.sql', 'Script');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO

PRINT N'Migración 26 aplicada.';
EXEC(N'SELECT COUNT(*) AS franjas, SUM(CAST(EsAdmision AS INT)) AS franjas_de_admision FROM dbo.Agenda');
GO
