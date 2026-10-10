/* ============================================================================
   04-acceso-paciente.sql — Crea el acceso (Usuario + UsuarioPaciente) para un
   paciente que YA existe en la tabla Paciente.

   Por qué hace falta: ningún alta de paciente, ni la del panel de
   Configurador (que no tiene esa función) ni la del registro público, crea
   credenciales de acceso — solo la fila en Paciente. Hoy no hay ninguna
   forma, desde la interfaz, de darle a un paciente un usuario y contraseña
   para loguearse. Este script cubre eso a mano, paciente por paciente.

   Esquema real, confirmado contra docs/produccion/01-produccion.sql y
   DAOs/UsuarioDAO.cs antes de escribir esto (no se tocó código ni la base
   para esta revisión):

     Usuario: IdUsuario int IDENTITY PK · Username nvarchar(50) NOT NULL,
       UNIQUE · PasswordHash nvarchar(255) NOT NULL · Estado nvarchar(50)
       NULL a nivel de esquema, pero el login exige exactamente 'Activo'
       (comparación de string, sensible a mayúsculas) · Mail nvarchar(100)
       NULL, con restricción UNIQUE (UQ_Usuario_Mail) · IntentosFallidos
       int NOT NULL DEFAULT 0 · BloqueadoHasta datetime NULL. Sin FK propias.

     UsuarioPaciente: IdUsuario int NOT NULL, PK (1 a 1 con Usuario; FK
       FK_UPa_Usuario → Usuario.IdUsuario) · IdPaciente int NOT NULL (FK
       FK_UPa_Paciente → Paciente.IdPaciente, sin UNIQUE propia — a nivel
       de esquema nada impide que un paciente tenga más de un usuario
       vinculado; la validación 3 de abajo es la que lo evita en la
       práctica) · FechaVinculacion date NOT NULL DEFAULT getdate().

   Sobre Mail: Usuario.Mail tiene una UNIQUE constraint (UQ_Usuario_Mail),
   así que además de obligatorio, tiene que ser distinto del de cualquier
   otro Usuario — la validación 4 de abajo lo confirma antes de insertar,
   con el mismo criterio que ya usa la validación 2 para Username.

   Confirmado antes de este cambio: el flujo de recuperación de contraseña
   (POST /recuperacion-password y PUT /recuperacion-password/{id}/aceptar,
   Program.cs) no lee Mail de ninguna tabla — ni de Usuario ni de Paciente.
   Busca al usuario solo por Username, y la clave temporal se la devuelve
   en la respuesta al Configurador que aprueba el pedido (para que se la
   pase al paciente por fuera de la app); no manda ningún mail real
   (EmailService.EnviarResetPassword existe pero no lo llama nadie). Por
   eso no hace falta tocar Paciente.Mail para que la recuperación funcione
   — este script solo actualiza Usuario.Mail, que es donde está la UNIQUE
   que hay que respetar. Si Paciente.Mail ya tiene un valor distinto al que
   se cargue acá, van a quedar desincronizados; no lo sincronicé porque no
   se pidió y toca otra tabla — avisar si se quiere que este script lo
   actualice también.

   Hash de la contraseña: igual que Program.cs (POST /usuarios) y
   02-usuario-app.sql/03-usuario-admin.sql — CONVERT(NVARCHAR(64),
   HASHBYTES('SHA2_256', ...), 2) sobre un literal SIN el prefijo N (un
   byte por carácter, para que coincida con Encoding.UTF8.GetBytes en C#).
   Ya lo verifiqué a mano en una vuelta anterior: para una contraseña ASCII
   da exactamente el mismo hex en SQL Server y en C#. *** La contraseña
   tiene que ser ASCII puro (sin tildes, sin ñ, sin emojis) y sin comilla
   simple — mismas razones que 03-usuario-admin.sql. ***

   Variables SQLCMD — las cuatro SIEMPRE por -v, nunca con :setvar adentro
   (:setvar pisa sin condición lo que venga de -v):
     IdPaciente:     el paciente (ya existente) al que se le da acceso.
     Username:       el que va a usar para loguearse. No es secreto, pero
                      tampoco va fijo: cambia en cada paciente.
     Mail:           obligatorio, tiene que ser distinto del de cualquier
                      otro Usuario (ver "Sobre Mail" arriba).
     AccesoPassword: la contraseña en texto plano, SOLO para calcular el
                      hash en este mismo script — nunca queda guardada así,
                      ni en ningún archivo.

   Validaciones, todas antes de escribir nada, dentro de una sola
   transacción (SET XACT_ABORT ON + BEGIN TRANSACTION/ROLLBACK, mismo
   patrón que ya usan ConsentimientoDAO.FirmarSiNoExiste y
   TurnoRepositorioDAO.InsertarSiLibre en el código real):
     1) El paciente existe.
     2) El Username no está tomado.
     3) Ese paciente no tiene ya un usuario vinculado.
     4) El Mail no está tomado por otro Usuario.
   Cualquiera de las cuatro que falle hace ROLLBACK y corta ahí (RAISERROR
   + RETURN) — lo probé a mano, una por una, contra una tabla de prueba:
   ninguna deja nada a medio insertar.

   Cómo correrlo:
     sqlcmd -S TU_SERVIDOR -d yosoy -C -f 65001 -v IdPaciente=123 -v Username="elusuario" -v Mail="elmail@dominio.com" -v AccesoPassword="LA_CONTRASEÑA_REAL" -i 04-acceso-paciente.sql
   ============================================================================ */

SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

USE [yosoy];
GO

BEGIN TRANSACTION;

IF NOT EXISTS (SELECT 1 FROM Paciente WHERE IdPaciente = $(IdPaciente))
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR('No existe ningún paciente con IdPaciente = $(IdPaciente).', 16, 1);
    RETURN;
END

IF EXISTS (SELECT 1 FROM Usuario WHERE Username = N'$(Username)')
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR('Ya existe un usuario con ese Username. Elegí otro.', 16, 1);
    RETURN;
END

IF EXISTS (SELECT 1 FROM UsuarioPaciente WHERE IdPaciente = $(IdPaciente))
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR('Ese paciente ya tiene un usuario vinculado.', 16, 1);
    RETURN;
END

IF EXISTS (SELECT 1 FROM Usuario WHERE Mail = N'$(Mail)')
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR('Ya existe un usuario con ese Mail. Elegí otro.', 16, 1);
    RETURN;
END

DECLARE @IdUsuarioNuevo INT;

INSERT INTO Usuario (Username, PasswordHash, Estado, Mail)
VALUES (
    N'$(Username)',
    CONVERT(NVARCHAR(64), HASHBYTES('SHA2_256', '$(AccesoPassword)'), 2),
    'Activo',
    N'$(Mail)'
);
SET @IdUsuarioNuevo = CAST(SCOPE_IDENTITY() AS INT);

INSERT INTO UsuarioPaciente (IdUsuario, IdPaciente)
VALUES (@IdUsuarioNuevo, $(IdPaciente));

COMMIT TRANSACTION;
PRINT 'Acceso creado: IdUsuario ' + CAST(@IdUsuarioNuevo AS NVARCHAR(10)) + ' vinculado al paciente $(IdPaciente).';
GO
