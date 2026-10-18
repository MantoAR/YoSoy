-- Alta de usuarios del equipo por el Configurador, con invitación por mail (octubre 2026).
-- Es la migración 19 de la tabla del README. Idempotente. Requiere las migraciones 16 a 18.
--
-- Qué hace:
--  1. Usuario.Telefono (teléfono de contacto del empleado). Nombre, Apellido y Cuil ya
--     existen desde la migración 16.
--  2. InvitacionCuenta: el link que le llega al empleado para confirmar su cuenta y elegir
--     la contraseña. En la base se guarda solo el hash del token (SHA-256), nunca el token:
--     quien lea la tabla no puede activar cuentas ajenas. Un uso, vence a las 72 horas, y
--     una invitación nueva anula las anteriores del mismo usuario.
--  Hasta que activa la invitación, la cuenta queda en Estado = 'Pendiente' y el ingreso la
--  rechaza (el login solo acepta 'Activo').

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- ── 1) Teléfono de contacto ───────────────────────────────────
IF COL_LENGTH('dbo.Usuario', 'Telefono') IS NULL
    ALTER TABLE Usuario ADD Telefono NVARCHAR(30) NULL;
GO

-- ── 2) Invitaciones ───────────────────────────────────────────
IF OBJECT_ID('dbo.InvitacionCuenta', 'U') IS NULL
CREATE TABLE InvitacionCuenta (
    IdInvitacion   INT          IDENTITY(1,1) CONSTRAINT PK_InvitacionCuenta PRIMARY KEY,
    IdUsuario      INT          NOT NULL CONSTRAINT FK_InvitacionCuenta_Usuario REFERENCES Usuario(IdUsuario),
    TokenHash      CHAR(64)     NOT NULL CONSTRAINT UQ_InvitacionCuenta_TokenHash UNIQUE,
    Creada         DATETIME2(0) NOT NULL CONSTRAINT DF_InvitacionCuenta_Creada DEFAULT SYSDATETIME(),
    Vence          DATETIME2(0) NOT NULL,
    Usada          DATETIME2(0) NULL,
    Anulada        DATETIME2(0) NULL,
    IdUsuarioCreo  INT          NULL CONSTRAINT FK_InvitacionCuenta_Creo REFERENCES Usuario(IdUsuario),
    CONSTRAINT CK_InvitacionCuenta_Vence CHECK (Vence > Creada)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.InvitacionCuenta') AND name = 'IX_InvitacionCuenta_Usuario')
    CREATE INDEX IX_InvitacionCuenta_Usuario ON InvitacionCuenta(IdUsuario);
GO

-- ── 3) Registro de versión ────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM VersionEsquema WHERE Numero = 19)
    INSERT INTO VersionEsquema (Numero, Archivo, Origen) VALUES (19, N'add_alta_equipo_invitaciones.sql', 'Script');
GO

PRINT N'Migración 19 aplicada.';
GO
