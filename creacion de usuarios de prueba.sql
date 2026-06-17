INSERT INTO Especialidad (Nombre, Estado)
VALUES ('Psicología', 'Activo')

INSERT INTO Profesional 
(Nombre, Apellido, Dni, Sexo, FechaNacimiento, Mail, Telefono, Matricula, IdEspecialidad, Provincia, Estado, FechaSolicitud)
VALUES 
('Juan', 'García', '20123456', 'Masculino', '1985-03-15', 'juan@yosoy.com', '3511234567', 'MP12345', 1, 'Córdoba', 'Activo', GETDATE())

INSERT INTO Usuario (Username, PasswordHash, Estado, Mail, IntentosFallidos)
VALUES (
    'juan.garcia',
    UPPER(CONVERT(NVARCHAR(64), HASHBYTES('SHA2_256', '1234'), 2)),
    'Activo',
    'juan@yosoy.com',
    0
)

SELECT IdUsuario FROM Usuario WHERE Username = 'juan.garcia'
SELECT IdProfesional FROM Profesional WHERE Dni = '20123456'

--vinculo usuario con profesional

INSERT INTO UsuarioProfesional (IdUsuario, IdProfesional)
VALUES (1, 1)

--Verificacion

SELECT u.Username, u.Estado, p.Nombre, p.Apellido, p.Mail
FROM Usuario u
JOIN UsuarioProfesional up ON u.IdUsuario = up.IdUsuario
JOIN Profesional p ON up.IdProfesional = p.IdProfesional
WHERE u.IdUsuario = 1

-- Lo que sigue es para crear el usuario paciente
INSERT INTO Paciente
(Nombre, Dni, FechaNacimiento, Domicilio, Telefono, Mail, Estado)
VALUES
('María López', '30123456', '1990-05-20', 'Av. Vélez Sársfield 200', '3519876543', 'maria@yosoy.com', 'Activo')

INSERT INTO Usuario (Username, PasswordHash, Estado, Mail, IntentosFallidos)
VALUES (
    'maria.lopez',
    UPPER(CONVERT(NVARCHAR(64), HASHBYTES('SHA2_256', '1234'), 2)),
    'Activo',
    'maria@yosoy.com',
    0
)

SELECT IdUsuario FROM Usuario WHERE Username = 'maria.lopez'
SELECT IdPaciente FROM Paciente WHERE Dni = '30123456'

INSERT INTO UsuarioPaciente (IdUsuario, IdPaciente)
VALUES (2, 2)