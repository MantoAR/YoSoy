-- Pacientes
INSERT INTO Paciente (Nombre, Dni, FechaNacimiento, Domicilio, Telefono, Mail, Estado)
VALUES 
('Carlos Rodríguez', '25111222', '1988-03-10', 'Av. Rivadavia 500', '3513001001', 'carlos@yosoy.com', 'Activo'),
('Laura Fernández',  '30222333', '1995-07-22', 'Bv. San Juan 1200', '3513002002', 'laura@yosoy.com',  'Activo'),
('Diego Martínez',   '28333444', '1991-11-05', 'Calle 9 de Julio 300', '3513003003', 'diego@yosoy.com', 'Activo')

-- Usuarios
INSERT INTO Usuario (Username, PasswordHash, Estado, Mail, IntentosFallidos)
VALUES
('carlos.rodriguez', UPPER(CONVERT(NVARCHAR(64), HASHBYTES('SHA2_256', '1234'), 2)), 'Activo', 'carlos@yosoy.com', 0),
('laura.fernandez',  UPPER(CONVERT(NVARCHAR(64), HASHBYTES('SHA2_256', '1234'), 2)), 'Activo', 'laura@yosoy.com',  0),
('diego.martinez',   UPPER(CONVERT(NVARCHAR(64), HASHBYTES('SHA2_256', '1234'), 2)), 'Activo', 'diego@yosoy.com',  0)


SELECT IdPaciente, Nombre FROM Paciente WHERE Dni IN ('25111222','30222333','28333444')
SELECT IdUsuario, Username FROM Usuario WHERE Username IN ('carlos.rodriguez','laura.fernandez','diego.martinez')

-- Vincular usuarios con pacientes
INSERT INTO UsuarioPaciente (IdUsuario, IdPaciente) VALUES (13, 8)
INSERT INTO UsuarioPaciente (IdUsuario, IdPaciente) VALUES (14, 9)
INSERT INTO UsuarioPaciente (IdUsuario, IdPaciente) VALUES (15, 10)


select *
from PacienteProfesional

-- Asignar los 3 pacientes al profesional Juan García (IdProfesional = 1)
INSERT INTO PacienteProfesional (IdPaciente, IdProfesional, FechaAsignacion, EstadoAsignacion)
VALUES (8, 1, GETDATE(), 'Confirmada')
INSERT INTO PacienteProfesional (IdPaciente, IdProfesional, FechaAsignacion, EstadoAsignacion)
VALUES (9, 1, GETDATE(), 'Confirmada')
INSERT INTO PacienteProfesional (IdPaciente, IdProfesional, FechaAsignacion, EstadoAsignacion)
VALUES (10, 1, GETDATE(), 'Confirmada')