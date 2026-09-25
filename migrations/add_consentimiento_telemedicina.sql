-- RN-02: consentimiento informado para atención por telemedicina.
-- Idempotente: se puede correr más de una vez sin error ni datos duplicados.

-- 1) Consentimiento.FechaFirma pasa de DATE a DATETIME para guardar también la hora
--    de la firma. Es el primer script que cambia el tipo de una columna existente,
--    así que solo altera si el tipo ACTUAL todavía es DATE (una segunda corrida no hace
--    nada). Las fechas existentes quedan a las 00:00. No hay índices, defaults ni
--    constraints sobre esa columna que haya que recrear.
IF EXISTS (SELECT 1
           FROM sys.columns c
           JOIN sys.types t ON t.user_type_id = c.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.Consentimiento')
             AND c.name = 'FechaFirma'
             AND t.name = 'date')
    ALTER TABLE Consentimiento ALTER COLUMN FechaFirma DATETIME NOT NULL;
GO

-- 2) Tipo específico de telemedicina. El código lo busca por Descripcion
--    (Consentimiento:TipoRequerido en appsettings.json), nunca por id.
IF NOT EXISTS (SELECT 1 FROM TipoConsentimiento
               WHERE Descripcion = N'Consentimiento Informado - Atención por Telemedicina')
    INSERT INTO TipoConsentimiento (Descripcion, Estado)
    VALUES (N'Consentimiento Informado - Atención por Telemedicina', 'Activo');
GO
