select *
from TipoConsentimiento

INSERT INTO TipoConsentimiento (Descripcion, Estado) VALUES
('Consentimiento Informado - Uso del Sistema', 'Activo'),
('Consentimiento Informado - Tratamiento de Datos de Salud', 'Activo');

SELECT * FROM TipoConsentimiento;

CREATE TABLE InformeAdmision (
    IdInforme INT IDENTITY(1,1) PRIMARY KEY,
    IdPaciente INT NOT NULL FOREIGN KEY REFERENCES Paciente(IdPaciente),
    IdEspecialidad INT NOT NULL FOREIGN KEY REFERENCES Especialidad(IdEspecialidad),
    MotivoConsulta NVARCHAR(MAX),
    TratamientoAnterior BIT DEFAULT 0,
    TratamientoAnteriorDetalle NVARCHAR(500),
    RecibeMedicacion BIT DEFAULT 0,
    MedicacionDetalle NVARCHAR(500),
    GustosIntereses NVARCHAR(MAX),
    PreferenciasAtencion NVARCHAR(MAX),
    TipoEntrevista NVARCHAR(50),
    RedApoyo NVARCHAR(MAX),
    ObjetivosPaciente NVARCHAR(MAX),
    Estado NVARCHAR(50) DEFAULT 'Pendiente',
    FechaCreacion DATETIME DEFAULT GETDATE()
);

SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'InformeAdmision';


--------------------
ALTER TABLE InformeAdmision ADD 
    FechaEntrevista DATETIME NULL,
    ObservacionesEntrevista NVARCHAR(MAX) NULL,
    IdProfesionalDerivado INT NULL;