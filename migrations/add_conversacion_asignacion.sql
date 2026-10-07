-- Pertenencia de conversaciones: quién puede ver/tomar/devolver un chat escalado.
-- Idempotente. Requiere add_mensajeria.sql.
--
-- Por qué una columna propia en Conversacion y no Caso.IdProfesionalAsignado (que ya
-- existe, Fase 2): hoy no hay ningún Caso activo en esta base -- "Tomar" un escalamiento
-- quedaría sin poder guardar nada para casi cualquier conversación real. Es la relación de
-- "quién atiende este hilo de chat", separada a propósito de "quién es el profesional
-- tratante del paciente" (eso sigue siendo Caso/PacienteProfesional, sin cambios). Ver
-- CAMBIOS_AGUS.md, "Pertenencia de conversaciones".
IF COL_LENGTH('dbo.Conversacion', 'IdProfesionalAsignado') IS NULL
    ALTER TABLE Conversacion ADD IdProfesionalAsignado INT NULL REFERENCES Profesional(IdProfesional);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Conversacion_ProfesionalAsignado')
    CREATE INDEX IX_Conversacion_ProfesionalAsignado ON Conversacion(IdProfesionalAsignado);
GO
