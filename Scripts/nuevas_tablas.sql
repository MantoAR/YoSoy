ALTER TABLE Usuario
    ADD IntentosFallidos INT NOT NULL DEFAULT 0,
        BloqueadoHasta   DATETIME NULL;

CREATE TABLE LogAuditoria (
    IdLog      INT IDENTITY(1,1) PRIMARY KEY,
    IdUsuario  INT NOT NULL,
    Accion     NVARCHAR(100) NOT NULL,
    Entidad    NVARCHAR(50)  NOT NULL,
    IdEntidad  INT NULL,
    Detalle    NVARCHAR(500) NULL,
    FechaHora  DATETIME NOT NULL DEFAULT GETDATE(),
    FOREIGN KEY (IdUsuario) REFERENCES Usuario(IdUsuario)
);

CREATE TABLE RecuperacionPassword (
    IdRecuperacion   INT IDENTITY(1,1) PRIMARY KEY,
    IdUsuario        INT NOT NULL,
    FechaSolicitud   DATETIME NOT NULL DEFAULT GETDATE(),
    Estado           NVARCHAR(20) NOT NULL DEFAULT 'Pendiente',
    PasswordTemporal NVARCHAR(255) NULL,
    FechaResolucion  DATETIME NULL,
    FOREIGN KEY (IdUsuario) REFERENCES Usuario(IdUsuario)
);
