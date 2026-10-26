SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.VersionEsquema', 'U') IS NULL OR OBJECT_ID('dbo.SesionClinica', 'U') IS NULL OR OBJECT_ID('dbo.TranscripcionSesion', 'U') IS NULL
    RAISERROR(N'Falta VersionEsquema, SesionClinica o TranscripcionSesion: aplica antes las migraciones anteriores (ver docs/CATALOGO_SCRIPTS_SQL.md).', 16, 1);
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH('dbo.SesionClinica', 'TranscripcionAnuladaUtc') IS NULL
        ALTER TABLE dbo.SesionClinica ADD TranscripcionAnuladaUtc DATETIME2(3) NULL;

    IF COL_LENGTH('dbo.SesionClinica', 'TranscripcionAnuladaMotivo') IS NULL
        ALTER TABLE dbo.SesionClinica ADD TranscripcionAnuladaMotivo NVARCHAR(20) NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_SesionClinica_TranscripcionAnuladaMotivo')
        EXEC(N'ALTER TABLE dbo.SesionClinica ADD CONSTRAINT CK_SesionClinica_TranscripcionAnuladaMotivo
            CHECK (TranscripcionAnuladaMotivo IS NULL OR TranscripcionAnuladaMotivo IN (''revocacion'', ''retencion''))');

    IF NOT EXISTS (SELECT 1 FROM dbo.VersionEsquema WHERE Numero = 29)
        INSERT INTO dbo.VersionEsquema (Numero, Archivo, Origen) VALUES (29, N'add_grabacion_video.sql', 'Script');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO
