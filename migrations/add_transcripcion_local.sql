SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.VersionEsquema', 'U') IS NULL OR OBJECT_ID('dbo.SesionClinica', 'U') IS NULL OR OBJECT_ID('dbo.ConsentimientoVideo', 'U') IS NULL
    RAISERROR(N'Falta VersionEsquema, SesionClinica o ConsentimientoVideo: aplica antes las migraciones anteriores (ver docs/CATALOGO_SCRIPTS_SQL.md).', 16, 1);
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH('dbo.ConsentimientoVideo', 'Finalidad') IS NULL
        EXEC(N'ALTER TABLE dbo.ConsentimientoVideo ADD Finalidad NVARCHAR(12) NOT NULL CONSTRAINT DF_ConsentimientoVideo_Finalidad DEFAULT (''emociones'')');

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_ConsentimientoVideo_Finalidad')
        EXEC(N'ALTER TABLE dbo.ConsentimientoVideo ADD CONSTRAINT CK_ConsentimientoVideo_Finalidad CHECK (Finalidad IN (''emociones'', ''grabacion''))');

    IF OBJECT_ID('dbo.TranscripcionSesion', 'U') IS NULL
        CREATE TABLE dbo.TranscripcionSesion (
            IdTranscripcion            INT          IDENTITY(1,1) NOT NULL CONSTRAINT PK_TranscripcionSesion PRIMARY KEY,
            IdSesion                    INT          NULL,
            Origen                      NVARCHAR(10) NOT NULL,
            IdOrigen                    INT          NOT NULL,
            IdConsentimientoVideo       INT          NOT NULL,
            Estado                      NVARCHAR(12) NOT NULL CONSTRAINT DF_TranscripcionSesion_Estado DEFAULT ('grabando'),
            Intentos                    INT          NOT NULL CONSTRAINT DF_TranscripcionSesion_Intentos DEFAULT (0),
            Error                       NVARCHAR(400) NULL,
            InicioPistaProfesionalMs    BIGINT       NULL,
            InicioPistaPacienteMs       BIGINT       NULL,
            Modelo                      NVARCHAR(40) NULL,
            DuracionAudioSeg            INT          NULL,
            DuracionProcesoSeg          INT          NULL,
            CreadoUtc                   DATETIME2(3) NOT NULL CONSTRAINT DF_TranscripcionSesion_CreadoUtc DEFAULT SYSUTCDATETIME(),
            ActualizadoUtc              DATETIME2(3) NOT NULL CONSTRAINT DF_TranscripcionSesion_ActualizadoUtc DEFAULT SYSUTCDATETIME(),
            CONSTRAINT CK_TranscripcionSesion_Origen CHECK (Origen IN ('turno', 'admision')),
            CONSTRAINT CK_TranscripcionSesion_Estado CHECK (Estado IN ('grabando', 'pendiente', 'procesando', 'lista', 'error', 'anulada'))
        );

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_TranscripcionSesion_Sesion')
        EXEC(N'ALTER TABLE dbo.TranscripcionSesion ADD CONSTRAINT FK_TranscripcionSesion_Sesion FOREIGN KEY (IdSesion) REFERENCES dbo.SesionClinica(IdSesion)');

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_TranscripcionSesion_ConsentimientoVideo')
        EXEC(N'ALTER TABLE dbo.TranscripcionSesion ADD CONSTRAINT FK_TranscripcionSesion_ConsentimientoVideo FOREIGN KEY (IdConsentimientoVideo) REFERENCES dbo.ConsentimientoVideo(IdConsentimientoVideo)');

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TranscripcionSesion_Estado' AND object_id = OBJECT_ID('dbo.TranscripcionSesion'))
        EXEC(N'CREATE INDEX IX_TranscripcionSesion_Estado ON dbo.TranscripcionSesion (Estado)');

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TranscripcionSesion_Origen' AND object_id = OBJECT_ID('dbo.TranscripcionSesion'))
        EXEC(N'CREATE INDEX IX_TranscripcionSesion_Origen ON dbo.TranscripcionSesion (Origen, IdOrigen)');

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_TranscripcionSesion_Grabando' AND object_id = OBJECT_ID('dbo.TranscripcionSesion'))
        EXEC(N'CREATE UNIQUE INDEX UX_TranscripcionSesion_Grabando ON dbo.TranscripcionSesion (Origen, IdOrigen) WHERE Estado = ''grabando''');

    IF NOT EXISTS (SELECT 1 FROM dbo.VersionEsquema WHERE Numero = 28)
        INSERT INTO dbo.VersionEsquema (Numero, Archivo, Origen) VALUES (28, N'add_transcripcion_local.sql', 'Script');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO
