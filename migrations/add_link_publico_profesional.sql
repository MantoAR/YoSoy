-- Fase 3a (Cambios-Agus), Paso 3: link público de reserva por profesional. Slug único, no
-- secuencial (nombre normalizado + sufijo aleatorio) y un flag de activación, apagado por
-- defecto. Lo activa el propio profesional desde "Mi perfil" y el administrador también
-- puede apagarlo. Idempotente.
--
-- Slug nullable (no todos los profesionales van a activar el link) con índice único
-- filtrado — igual que Caso.IdInforme en la Fase 2, un UNIQUE normal en SQL Server solo
-- permite un NULL en toda la tabla. SET QUOTED_IDENTIFIER ON es necesario para crear un
-- índice filtrado (error 1934 si no), misma lección aprendida ahí.
SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Profesional') AND name = 'Slug')
    ALTER TABLE Profesional ADD Slug NVARCHAR(80) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Profesional') AND name = 'LinkPublicoActivo')
    ALTER TABLE Profesional ADD LinkPublicoActivo BIT NOT NULL DEFAULT 0;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Profesional') AND name = 'UQ_Profesional_Slug')
    CREATE UNIQUE INDEX UQ_Profesional_Slug ON Profesional(Slug) WHERE Slug IS NOT NULL;
GO
