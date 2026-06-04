ALTER TABLE Evolucion
ADD TipoCierre NVARCHAR(20) NULL;
GO

ALTER TABLE Evolucion
ADD CONSTRAINT CK_Evolucion_TipoCierre
CHECK (TipoCierre IN ('Alta', 'Seguimiento', 'Derivación'));
GO
