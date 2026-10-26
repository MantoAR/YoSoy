-- Consentimientos dentro del circuito (octubre 2026). Es la migracion 28. Aditiva, idempotente y en una sola transaccion.
-- Requiere la 18 (VersionEsquema) y las tablas Consentimiento, Tutor y Paciente, nada mas.
--
--   sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i migrations/add_consentimientos_circuito.sql
--
-- Es OPCIONAL: la API de esta version anda sin ella. Sin la migracion NO se exige la firma del tutor a un menor ni la aceptacion de la videollamada (queda como antes) y
-- la API lo avisa en el log; con ella se aplican las dos reglas. Se puede aplicar antes o despues de publicar la API, pero las reglas nuevas solo rigen con ella.
--
-- Deshacer (SOLO si todavia no se uso; con aceptaciones o firmas de tutor guardadas el script se niega a borrarlas):
--   docs/produccion/deshacer-migracion-28.sql
--
-- Que hace:
--  1. Consentimiento.IdTutorFirmante: quien firmo el consentimiento de tratamiento y telemedicina de una persona MENOR de edad (o con tutor cargado). Las filas que ya existen
--     quedan en NULL (firmadas por la propia persona): para un menor, la API pide que la firma sea de su tutor.
--  2. Tabla AceptacionVideollamada: la aceptacion, SESION POR SESION, de usar la camara y el microfono en la videollamada (turno o entrevista de admision). Una fila por
--     aceptacion, con la fecha/hora (UTC) y la version y el hash del texto que la persona leyo. Se puede revocar (RevocadoUtc). Un indice unico filtrado garantiza una sola
--     aceptacion vigente por sesion y paciente. Es DISTINTA del consentimiento de la lectura de emociones (tabla ConsentimientoVideo), que no se toca.
--  3. Registra la version 28 en VersionEsquema, con Origen 'Script'.
-- No toca ninguna fila existente.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.VersionEsquema', 'U') IS NULL OR OBJECT_ID('dbo.Consentimiento', 'U') IS NULL OR OBJECT_ID('dbo.Tutor', 'U') IS NULL OR OBJECT_ID('dbo.Paciente', 'U') IS NULL
    RAISERROR(N'Falta VersionEsquema, Consentimiento, Tutor o Paciente: aplica antes las migraciones anteriores (ver docs/CATALOGO_SCRIPTS_SQL.md).', 16, 1);
GO

BEGIN TRY
    BEGIN TRANSACTION;

    -- 1) Quien firmo por un menor
    IF COL_LENGTH('dbo.Consentimiento', 'IdTutorFirmante') IS NULL
        ALTER TABLE dbo.Consentimiento ADD IdTutorFirmante INT NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Consentimiento_TutorFirmante')
        EXEC(N'ALTER TABLE dbo.Consentimiento ADD CONSTRAINT FK_Consentimiento_TutorFirmante FOREIGN KEY (IdTutorFirmante) REFERENCES dbo.Tutor(IdTutor)');

    -- 2) La aceptacion de la videollamada, por sesion
    IF OBJECT_ID('dbo.AceptacionVideollamada', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.AceptacionVideollamada (
            IdAceptacion    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AceptacionVideollamada PRIMARY KEY,
            IdUsuario       INT NULL CONSTRAINT FK_AceptacionVideollamada_Usuario REFERENCES dbo.Usuario(IdUsuario),
            IdPaciente      INT NOT NULL CONSTRAINT FK_AceptacionVideollamada_Paciente REFERENCES dbo.Paciente(IdPaciente),
            Origen          NVARCHAR(10) NOT NULL,
            IdOrigen        INT NOT NULL,
            VersionTexto    NVARCHAR(40) NOT NULL,
            HashTexto       CHAR(64) NOT NULL,
            DecididoUtc     DATETIME2(3) NOT NULL CONSTRAINT DF_AceptacionVideollamada_Decidido DEFAULT (SYSUTCDATETIME()),
            RevocadoUtc     DATETIME2(3) NULL,
            IdUsuarioRevoca INT NULL CONSTRAINT FK_AceptacionVideollamada_UsuarioRevoca REFERENCES dbo.Usuario(IdUsuario),
            CONSTRAINT CK_AceptacionVideollamada_Origen CHECK (Origen IN (N'turno', N'admision'))
        );
        CREATE UNIQUE INDEX UX_AceptacionVideollamada_Vigente ON dbo.AceptacionVideollamada (Origen, IdOrigen, IdPaciente) WHERE RevocadoUtc IS NULL;
        CREATE INDEX IX_AceptacionVideollamada_Paciente ON dbo.AceptacionVideollamada (IdPaciente, DecididoUtc);
    END

    -- 3) Registro de version
    IF NOT EXISTS (SELECT 1 FROM dbo.VersionEsquema WHERE Numero = 28)
        INSERT INTO dbo.VersionEsquema (Numero, Archivo, Origen) VALUES (28, N'add_consentimientos_circuito.sql', 'Script');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO

PRINT N'Migracion 28 aplicada.';
SELECT (SELECT COUNT(*) FROM dbo.AceptacionVideollamada) AS aceptaciones, (SELECT COUNT(*) FROM dbo.Consentimiento) AS consentimientos;
GO
