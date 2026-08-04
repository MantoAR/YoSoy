select *
from UsuarioRol

-- Ver qué IdRol corresponde al configurador
SELECT * 
FROM dbo.Rol;

-- Insertar roles
INSERT INTO Rol (Nombre, Estado) VALUES ('Paciente', 'Activo');
INSERT INTO Rol (Nombre, Estado) VALUES ('Profesional', 'Activo');
INSERT INTO Rol (Nombre, Estado) VALUES ('Configurador', 'Activo');

-- Ver los roles creados
SELECT * FROM Rol;

-- Crear usuario configurador
INSERT INTO dbo.Usuario (Username, PasswordHash, Estado, Mail, idRol)
VALUES ('admin.config', '1234', 'Activo', 'alearmas01@gmail.com', 3);

-- Ver el usuario creado
SELECT * FROM Usuario WHERE Username = 'admin.config';

select *
from dbo.Usuario

SELECT 
    t.TABLE_NAME,
    c.COLUMN_NAME,
    c.DATA_TYPE
FROM INFORMATION_SCHEMA.TABLES t
JOIN INFORMATION_SCHEMA.COLUMNS c ON t.TABLE_NAME = c.TABLE_NAME
WHERE t.TABLE_TYPE = 'BASE TABLE'
ORDER BY t.TABLE_NAME, c.ORDINAL_POSITION;

-- 1. Crear usuario
INSERT INTO Usuario (Username, PasswordHash, Estado, Mail)
VALUES ('admin.config', '1234', 'Activo', 'config@yosoy.com');

-- 2. Ver el IdUsuario creado
SELECT IdUsuario FROM Usuario WHERE Username = 'admin.config';

-- Asignar rol Configurador (IdRol=3) al usuario
INSERT INTO UsuarioRol (IdUsuario, IdRol) VALUES (7, 3);

select *
from Usuario

-- 1. Crear usuario
INSERT INTO Usuario (Username, PasswordHash, Estado, Mail)
VALUES ('config', '1234', 'Activo', 'alejandraempleo@gmail.com');

SELECT IdUsuario FROM Usuario WHERE Username = 'config';

INSERT INTO UsuarioRol (IdUsuario, IdRol) VALUES (9, 3);

SELECT u.IdUsuario, u.Username, u.Estado, u.IntentosFallidos, u.BloqueadoHasta, r.Nombre as Rol
FROM Usuario u
JOIN UsuarioRol ur ON u.IdUsuario = ur.IdUsuario
JOIN Rol r ON ur.IdRol = r.IdRol
WHERE u.Username = 'admin.config';

UPDATE Usuario 
SET IntentosFallidos = 0, BloqueadoHasta = NULL 
WHERE IdUsuario = 7;


SELECT UPPER(CONVERT(NVARCHAR(64), HASHBYTES('SHA2_256', '1234'), 2));

UPDATE Usuario 
SET PasswordHash = '03AC674216F3E15C761EE1A5E255F067953623C8B388B4459E13F978D7C846F4',
    IntentosFallidos = 0,
    BloqueadoHasta = NULL
WHERE IdUsuario = 7;

-- Estas consultas son para hacer el flujo completo

SELECT * FROM Especialidad;
--Solo tenemos Psicologo-empecemos por este

-- Insertar profesionales---------------------------------------------------------------------------------
INSERT INTO Profesional (Nombre, Apellido, Matricula, IdEspecialidad, Estado, Mail, Telefono)
VALUES 
('Ana', 'Martinez', 'MP-1001', 1, 'Activo', 'ana.martinez@yosoy.com', '351-111-1111'),
('Carlos', 'Lopez', 'MP-1002', 1, 'Activo', 'carlos.lopez@yosoy.com', '351-222-2222'),
('Sofia', 'Gomez', 'MP-1003', 1, 'Activo', 'sofia.gomez@yosoy.com', '351-333-3333');

----------------------------------------------------------------------------------------------------------
----------------------------------------------------------------------------------------------------------
-- Ver los IDs creados
SELECT IdProfesional, Nombre, Apellido FROM Profesional;

-- Crear usuarios para los 3 nuevos profesionales
INSERT INTO Usuario (Username, PasswordHash, Estado, Mail)
VALUES 
('ana.martinez', '03AC674216F3E15C761EE1A5E255F067953623C8B388B4459E13F978D7C846F4', 'Activo', 'ana.martinez@yosoy.com'),
('carlos.lopez', '03AC674216F3E15C761EE1A5E255F067953623C8B388B4459E13F978D7C846F4', 'Activo', 'carlos.lopez@yosoy.com'),
('sofia.gomez', '03AC674216F3E15C761EE1A5E255F067953623C8B388B4459E13F978D7C846F4', 'Activo', 'sofia.gomez@yosoy.com');

-- Ver IDs creados
SELECT IdUsuario, Username FROM Usuario WHERE Username IN ('ana.martinez','carlos.lopez','sofia.gomez');

----------------------------------------------------------------------------------------------------------
----------------------------------------------------------------------------------------------------------
-- Vincular usuarios con profesionales
INSERT INTO UsuarioProfesional (IdUsuario, IdProfesional, FechaVinculacion)
VALUES 
(10, 38, GETDATE()),
(11, 39, GETDATE()),
(12, 40, GETDATE());

-- Crear pacientes de prueba
INSERT INTO Paciente (Nombre, Dni, FechaNacimiento, Domicilio, Telefono, Mail, Estado)
VALUES 
('Pedro Gonzalez', '28111222', '1990-05-15', 'Av. Colon 123', '351-444-4444', 'pedro@mail.com', 'Activo'),
('Laura Fernandez', '31222333', '1995-08-20', 'Bv. San Juan 456', '351-555-5555', 'laura@mail.com', 'Activo'),
('Diego Ramirez', '33333444', '1988-03-10', 'Calle 9 de Julio 789', '351-666-6666', 'diego@mail.com', 'Activo');

-- Ver IDs de pacientes
SELECT IdPaciente, Nombre FROM Paciente ORDER BY IdPaciente DESC;

----------------------------------------------------------------------------------------------------------
----------------------------------------------------------------------------------------------------------
-- Atenciones de Ana Martinez (IdProfesional=38)
INSERT INTO SesionClinica (IdTurno, IdPaciente, IdProfesional, FechaHora, Transcripcion, NotasEvolucion, HistorialEmociones, TipoCierre)
VALUES 
(NULL, 4, 38, '2026-06-20 09:00:00', 'Sesion de seguimiento', 'Paciente estable', '{}', 'Seguimiento'),
(NULL, 5, 38, '2026-06-21 10:00:00', 'Primera consulta', 'Buena respuesta', '{}', 'Alta'),
(NULL, 6, 38, '2026-06-22 11:00:00', 'Derivacion necesaria', 'Se deriva', '{}', 'Derivacion');

-- Atenciones de Carlos Lopez (IdProfesional=39)
INSERT INTO SesionClinica (IdTurno, IdPaciente, IdProfesional, FechaHora, Transcripcion, NotasEvolucion, HistorialEmociones, TipoCierre)
VALUES 
(NULL, 2, 39, '2026-06-22 09:00:00', 'Consulta inicial', 'Paciente ansioso', '{}', 'Seguimiento'),
(NULL, 4, 39, '2026-06-23 10:00:00', 'Segunda sesion', 'Mejora notable', '{}', 'Alta'),
(NULL, 5, 39, '2026-06-24 11:00:00', 'Seguimiento mensual', 'Sin cambios', '{}', 'Seguimiento');

-- Atenciones de Sofia Gomez (IdProfesional=40)
INSERT INTO SesionClinica (IdTurno, IdPaciente, IdProfesional, FechaHora, Transcripcion, NotasEvolucion, HistorialEmociones, TipoCierre)
VALUES 
(NULL, 3, 40, '2026-06-23 09:00:00', 'Evaluacion inicial', 'Requiere seguimiento', '{}', 'Seguimiento'),
(NULL, 6, 40, '2026-06-24 10:00:00', 'Sesion terapeutica', 'Progreso positivo', '{}', 'Alta'),
(NULL, 4, 40, '2026-06-25 11:00:00', 'Control mensual', 'Estable', '{}', 'Seguimiento');

-- Verificar
SELECT sc.IdSesion, p.Nombre AS Paciente, prof.Nombre + ' ' + prof.Apellido AS Profesional, sc.FechaHora, sc.TipoCierre
FROM SesionClinica sc
JOIN Paciente p ON p.IdPaciente = sc.IdPaciente
JOIN Profesional prof ON prof.IdProfesional = sc.IdProfesional
ORDER BY sc.FechaHora;

----------------------------------------------------------------------------------------------------------
----------------------------------------------------------------------------------------------------------
INSERT INTO Especialidad (Nombre, Estado) VALUES
('Admision', 'Activo'),
('Admision Infanto-Juvenil', 'Activo'),
('Atencion Domiciliaria', 'Activo'),
('Centro de Rehabilitacion Sociolaboral', 'Activo'),
('Psicologia Infanto-Juvenil', 'Activo'),
('Psicomotricidad', 'Activo'),
('Psicopedagogia', 'Activo'),
('Psiquiatria', 'Activo'),
('Psiquiatria Infanto-Juvenil', 'Activo'),
('Servicio Social', 'Activo');

SELECT * FROM Especialidad;

----------------------------------------------------------------------------------------------------------
----------------------------------------------------------------------------------------------------------

CREATE TRIGGER trg_CrearHistoriaClinica
ON Paciente
AFTER INSERT
AS
BEGIN
    INSERT INTO HistoriaClinica (IdPaciente, FechaApertura)
    SELECT IdPaciente, GETDATE()
    FROM inserted;
END;



INSERT INTO Paciente (Nombre, Dni, FechaNacimiento, Domicilio, Telefono, Mail, Estado)
VALUES ('Test Trigger', '99999999', '2000-01-01', 'Calle Test 1', '000-000', 'test@test.com', 'Activo');

-- Ver si se creó la HC
SELECT p.Nombre, hc.IdHistoria, hc.FechaApertura
FROM Paciente p
JOIN HistoriaClinica hc ON hc.IdPaciente = p.IdPaciente
WHERE p.Dni = '99999999';

-- Crear HC para pacientes que no tienen
INSERT INTO HistoriaClinica (IdPaciente, FechaApertura)
SELECT p.IdPaciente, GETDATE()
FROM Paciente p
LEFT JOIN HistoriaClinica hc ON hc.IdPaciente = p.IdPaciente
WHERE hc.IdHistoria IS NULL;

-- Verificar todas las HCs
SELECT p.Nombre, hc.IdHistoria, hc.FechaApertura
FROM Paciente p
JOIN HistoriaClinica hc ON hc.IdPaciente = p.IdPaciente
ORDER BY p.IdPaciente;






