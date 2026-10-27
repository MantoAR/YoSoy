-- Paciente «Derivado» a psiquiatría (octubre 2026). Es la migración 34. Aditiva, idempotente y en una sola transacción.
-- Requiere la 18 (la tabla VersionEsquema) y las tablas Paciente, Profesional, Caso, SesionClinica, Turno y Usuario, nada más.
--
--   sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i migrations/add_derivado_psiquiatra.sql
--
-- Es OPCIONAL para que la API arranque: sin ella nadie figura como derivado, el botón «Derivar a psiquiatra» del cierre responde que falta aplicarla y nada de los turnos cambia.
-- Aplicarla ANTES de publicar la API que la usa.
--
-- Deshacer (con la API anterior otra vez publicada):
--   docs/produccion/deshacer-migracion-34.sql
--
-- Qué hace:
--  1. Tabla DerivacionPsiquiatrica: una fila por cada vez que un profesional derivó a un paciente a psiquiatría (quién, cuándo, qué paciente, desde qué sesión o turno) y, si más
--     adelante recepción/CRM lo reabrió, quién y cuándo. El paciente figura como «Derivado» mientras tenga una fila SIN reapertura.
--     No toca Caso.Estado: «Derivado» ya es un estado del caso con otro significado (derivado por admisión a un profesional, esperando asignación) y mezclarlos rompería ese circuito.
--     Reabrir = marcar la fila: el caso nunca cambió, así que «vuelve a su estado anterior» sin tener que recordarlo.
--  2. Un índice único filtrado: un paciente no puede tener dos derivaciones sin reabrir (un doble clic o dos pedidos a la vez no duplican nada).
--  3. Registra la versión 34 en VersionEsquema, con Origen 'Script' como las anteriores.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.VersionEsquema', 'U') IS NULL OR OBJECT_ID('dbo.Paciente', 'U') IS NULL OR OBJECT_ID('dbo.Profesional', 'U') IS NULL OR OBJECT_ID('dbo.Caso', 'U') IS NULL
   OR OBJECT_ID('dbo.SesionClinica', 'U') IS NULL OR OBJECT_ID('dbo.Turno', 'U') IS NULL OR OBJECT_ID('dbo.Usuario', 'U') IS NULL
    RAISERROR(N'Falta VersionEsquema, Paciente, Profesional, Caso, SesionClinica, Turno o Usuario: aplicá antes las migraciones anteriores (ver docs/CATALOGO_SCRIPTS_SQL.md).', 16, 1);
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID('dbo.DerivacionPsiquiatrica', 'U') IS NULL
        CREATE TABLE dbo.DerivacionPsiquiatrica (
            IdDerivacionPsiquiatrica INT           IDENTITY(1,1) CONSTRAINT PK_DerivacionPsiquiatrica PRIMARY KEY,
            IdPaciente               INT           NOT NULL CONSTRAINT FK_DerivacionPsiquiatrica_Paciente    REFERENCES dbo.Paciente(IdPaciente),
            IdProfesional            INT           NOT NULL CONSTRAINT FK_DerivacionPsiquiatrica_Profesional REFERENCES dbo.Profesional(IdProfesional),
            IdCaso                   INT           NULL     CONSTRAINT FK_DerivacionPsiquiatrica_Caso        REFERENCES dbo.Caso(IdCaso),
            IdSesion                 INT           NULL     CONSTRAINT FK_DerivacionPsiquiatrica_Sesion      REFERENCES dbo.SesionClinica(IdSesion),
            IdTurno                  INT           NULL     CONSTRAINT FK_DerivacionPsiquiatrica_Turno       REFERENCES dbo.Turno(IdTurno),
            IdUsuario                INT           NULL     CONSTRAINT FK_DerivacionPsiquiatrica_Usuario     REFERENCES dbo.Usuario(IdUsuario),
            FechaUtc                 DATETIME2(0)  NOT NULL CONSTRAINT DF_DerivacionPsiquiatrica_Fecha DEFAULT SYSUTCDATETIME(),
            ReabiertaUtc             DATETIME2(0)  NULL,
            IdUsuarioReapertura      INT           NULL     CONSTRAINT FK_DerivacionPsiquiatrica_UsuarioReap REFERENCES dbo.Usuario(IdUsuario)
        );

    -- En un EXEC: la tabla recién creada no existe todavía cuando se compila este lote.
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_DerivacionPsiquiatrica_Vigente' AND object_id = OBJECT_ID('dbo.DerivacionPsiquiatrica'))
        EXEC(N'CREATE UNIQUE INDEX UX_DerivacionPsiquiatrica_Vigente ON dbo.DerivacionPsiquiatrica (IdPaciente) WHERE ReabiertaUtc IS NULL;');
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DerivacionPsiquiatrica_Paciente' AND object_id = OBJECT_ID('dbo.DerivacionPsiquiatrica'))
        EXEC(N'CREATE INDEX IX_DerivacionPsiquiatrica_Paciente ON dbo.DerivacionPsiquiatrica (IdPaciente, IdDerivacionPsiquiatrica);');

    IF NOT EXISTS (SELECT 1 FROM dbo.VersionEsquema WHERE Numero = 34)
        INSERT INTO dbo.VersionEsquema (Numero, Archivo, Origen) VALUES (34, N'add_derivado_psiquiatra.sql', 'Script');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO

PRINT N'Migración 34 aplicada.';
EXEC(N'SELECT COUNT(*) AS derivaciones FROM dbo.DerivacionPsiquiatrica');
GO
