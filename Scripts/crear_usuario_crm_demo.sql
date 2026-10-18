-- Usuario de demo para el rol CRM y atención al cliente (etapa 2).
-- Usuario: crm.demo   Contraseña: 1234   (solo para desarrollo y demo, nunca en producción)
-- Requiere las migraciones 16 y 17. Se puede correr más de una vez.

SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (SELECT 1 FROM Usuario WHERE Username = 'crm.demo')
    INSERT INTO Usuario (Username, PasswordHash, Estado, Mail, IntentosFallidos, Nombre, Apellido)
    VALUES ('crm.demo', UPPER(CONVERT(NVARCHAR(64), HASHBYTES('SHA2_256', '1234'), 2)), 'Activo',
            'crm.demo@yosoy.test', 0, N'Demo', N'Atención al cliente');
GO

DECLARE @u   INT = (SELECT IdUsuario FROM Usuario WHERE Username = 'crm.demo');
DECLARE @rol INT = (SELECT IdRol FROM Rol WHERE Clave = 'crm');
DECLARE @per INT = (SELECT IdPermiso FROM Permiso WHERE Clave = 'crm');

IF NOT EXISTS (SELECT 1 FROM UsuarioRol WHERE IdUsuario = @u AND IdRol = @rol)
    INSERT INTO UsuarioRol (IdUsuario, IdRol) VALUES (@u, @rol);
IF NOT EXISTS (SELECT 1 FROM UsuarioPermiso WHERE IdUsuario = @u AND IdPermiso = @per)
    INSERT INTO UsuarioPermiso (IdUsuario, IdRol, IdPermiso) VALUES (@u, @rol, @per);

-- Tiene que mostrar: Roles = CRM y atención al cliente, Permisos = CRM y atención al cliente.
SELECT Username, Roles, Permisos, Privilegios, SinPermisos FROM vw_UsuarioAccesos WHERE IdUsuario = @u;
GO
