ALTER TABLE InformeAdmision ADD CodigoSala NVARCHAR(50) NULL;

CREATE TABLE Notificacion (
    IdNotificacion INT IDENTITY(1,1) PRIMARY KEY,
    IdPaciente INT NOT NULL FOREIGN KEY REFERENCES Paciente(IdPaciente),
    Titulo NVARCHAR(200),
    Mensaje NVARCHAR(MAX),
    Link NVARCHAR(300) NULL,
    Leida BIT DEFAULT 0,
    FechaCreacion DATETIME DEFAULT GETDATE()
);

INSERT INTO Profesional (Nombre, Apellido, Matricula, IdEspecialidad, Estado, Mail, Telefono)
SELECT 'Equipo', 'Admision', 'ADM-001', IdEspecialidad, 'Activo', 'admision@yosoy.com', '000-000-0000'
FROM Especialidad WHERE Nombre = 'Admision';

DELETE FROM Notificacion;
DELETE FROM InformeAdmision;