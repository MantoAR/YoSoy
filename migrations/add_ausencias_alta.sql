-- Ausencias con umbrales y alta estructurada con plan de control (octubre 2026). Es la migración 35. Aditiva, idempotente y en una sola transacción.
-- Requiere la 18 (la tabla VersionEsquema), la 27 (AltaClinica) y las tablas Turno, EstadoTurno, Caso, Paciente, Profesional y Usuario.
--
--   sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i migrations/add_ausencias_alta.sql
--
-- Es OPCIONAL para que la API arranque: sin ella no hay avisos de ausencia, el botón «Marcar como ausente» responde que falta aplicarla, y el alta clínica anda como siempre
-- (informe en texto libre y plan de control numérico, sin los controles de 3 y 6 meses).
-- Aplicarla ANTES de publicar la API que la usa.
--
-- Deshacer (con la API anterior otra vez publicada):
--   docs/produccion/deshacer-migracion-35.sql
--
-- Qué hace:
--  1. EstadoTurno: agrega la fila 'Ausente' (el catálogo no la tenía). Se resuelve siempre por descripción, nunca por id.
--  2. Caso.Estado: el CHECK admite 'Abandono' (el paciente que se perdió, confirmado por el profesional). Es el mínimo: el sistema no tenía ningún estado para esto.
--  3. TurnoConexion: la primera vez que cada rol (Paciente / Profesional) pidió entrar a la sala de un turno. Es lo que permite saber «el paciente nunca se conectó».
--  4. AvisoSeguimiento: una clave única por aviso (turno + tipo, paciente + ausencia…). Insertar la clave ANTES de avisar es lo que impide duplicar una campanita.
--  5. PropuestaAbandono: la tarea «Proponer abandono» del profesional (Pendiente → Confirmada / Descartada), con motivo y quién resolvió. Una sola pendiente por caso.
--  6. AltaClinica.InformeEstructurado (JSON con las secciones del informe de alta) e IdEvolucion (la entrada de la historia clínica donde quedó el informe).
--  7. ControlSeguimiento: los controles del plan de alta (por defecto a los 3 y a los 6 meses): recordatorios para el profesional, NO turnos. Pendiente → Avisado → Resuelto.
--  8. Registra la versión 35 en VersionEsquema, con Origen 'Script' como las anteriores.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.VersionEsquema', 'U') IS NULL OR OBJECT_ID('dbo.Turno', 'U') IS NULL OR OBJECT_ID('dbo.EstadoTurno', 'U') IS NULL OR OBJECT_ID('dbo.Caso', 'U') IS NULL
   OR OBJECT_ID('dbo.AltaClinica', 'U') IS NULL OR OBJECT_ID('dbo.Paciente', 'U') IS NULL OR OBJECT_ID('dbo.Profesional', 'U') IS NULL OR OBJECT_ID('dbo.Usuario', 'U') IS NULL
    RAISERROR(N'Falta VersionEsquema, Turno, EstadoTurno, Caso, AltaClinica, Paciente, Profesional o Usuario: aplicá antes las migraciones anteriores (ver docs/CATALOGO_SCRIPTS_SQL.md).', 16, 1);
GO

BEGIN TRY
    BEGIN TRANSACTION;

    -- 1) El estado de turno «Ausente»
    IF NOT EXISTS (SELECT 1 FROM dbo.EstadoTurno WHERE Descripcion = N'Ausente')
        INSERT INTO dbo.EstadoTurno (Descripcion, Estado) VALUES (N'Ausente', N'Activo');

    -- 2) Caso.Estado admite 'Abandono'
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Caso_Estado' AND parent_object_id = OBJECT_ID('dbo.Caso') AND definition NOT LIKE N'%Abandono%')
        ALTER TABLE dbo.Caso DROP CONSTRAINT CK_Caso_Estado;
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Caso_Estado' AND parent_object_id = OBJECT_ID('dbo.Caso'))
        ALTER TABLE dbo.Caso ADD CONSTRAINT CK_Caso_Estado CHECK (Estado IN (N'Nuevo', N'Derivado', N'Asignado', N'En atención', N'En alta', N'Abandono', N'Cerrado'));

    -- 3) Quién entró a la sala de cada turno (la primera vez)
    IF OBJECT_ID('dbo.TurnoConexion', 'U') IS NULL
        CREATE TABLE dbo.TurnoConexion (
            IdTurno   INT          NOT NULL CONSTRAINT FK_TurnoConexion_Turno REFERENCES dbo.Turno(IdTurno),
            Rol       NVARCHAR(20) NOT NULL,
            PrimeraUtc DATETIME2(3) NOT NULL CONSTRAINT DF_TurnoConexion_Primera DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT PK_TurnoConexion PRIMARY KEY (IdTurno, Rol),
            CONSTRAINT CK_TurnoConexion_Rol CHECK (Rol IN (N'Paciente', N'Profesional'))
        );

    -- 4) Un aviso por clave
    IF OBJECT_ID('dbo.AvisoSeguimiento', 'U') IS NULL
        CREATE TABLE dbo.AvisoSeguimiento (
            Clave    NVARCHAR(120) NOT NULL CONSTRAINT PK_AvisoSeguimiento PRIMARY KEY,
            CreadoUtc DATETIME2(3) NOT NULL CONSTRAINT DF_AvisoSeguimiento_Creado DEFAULT (SYSUTCDATETIME())
        );

    -- 5) La propuesta de abandono
    IF OBJECT_ID('dbo.PropuestaAbandono', 'U') IS NULL
        CREATE TABLE dbo.PropuestaAbandono (
            IdPropuestaAbandono INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PropuestaAbandono PRIMARY KEY,
            IdCaso              INT NOT NULL CONSTRAINT FK_PropuestaAbandono_Caso REFERENCES dbo.Caso(IdCaso),
            IdPaciente          INT NOT NULL CONSTRAINT FK_PropuestaAbandono_Paciente REFERENCES dbo.Paciente(IdPaciente),
            IdProfesional       INT NOT NULL CONSTRAINT FK_PropuestaAbandono_Profesional REFERENCES dbo.Profesional(IdProfesional),
            IdTurnoDisparador   INT NOT NULL CONSTRAINT FK_PropuestaAbandono_Turno REFERENCES dbo.Turno(IdTurno),
            Ausencias           INT NOT NULL,
            Estado              NVARCHAR(20) NOT NULL CONSTRAINT DF_PropuestaAbandono_Estado DEFAULT (N'Pendiente'),
            Motivo              NVARCHAR(500) NULL,
            CreadaUtc           DATETIME2(3) NOT NULL CONSTRAINT DF_PropuestaAbandono_Creada DEFAULT (SYSUTCDATETIME()),
            ResueltaUtc         DATETIME2(3) NULL,
            IdUsuarioResolucion INT NULL CONSTRAINT FK_PropuestaAbandono_Usuario REFERENCES dbo.Usuario(IdUsuario),
            EstadoCasoAnterior  NVARCHAR(20) NULL,
            CONSTRAINT CK_PropuestaAbandono_Estado CHECK (Estado IN (N'Pendiente', N'Confirmada', N'Descartada'))
        );
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PropuestaAbandono_Turno' AND object_id = OBJECT_ID('dbo.PropuestaAbandono'))
        EXEC(N'CREATE UNIQUE INDEX UX_PropuestaAbandono_Turno ON dbo.PropuestaAbandono (IdTurnoDisparador)');
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PropuestaAbandono_Pendiente' AND object_id = OBJECT_ID('dbo.PropuestaAbandono'))
        EXEC(N'CREATE UNIQUE INDEX UX_PropuestaAbandono_Pendiente ON dbo.PropuestaAbandono (IdCaso) WHERE Estado = N''Pendiente''');

    -- 6) El informe de alta estructurado y su entrada en la historia clínica (en EXEC: el lote se compila antes de que existan las columnas)
    IF COL_LENGTH('dbo.AltaClinica', 'InformeEstructurado') IS NULL ALTER TABLE dbo.AltaClinica ADD InformeEstructurado NVARCHAR(MAX) NULL;
    IF COL_LENGTH('dbo.AltaClinica', 'IdEvolucion') IS NULL ALTER TABLE dbo.AltaClinica ADD IdEvolucion INT NULL;

    -- 7) Los controles del plan de alta
    IF OBJECT_ID('dbo.ControlSeguimiento', 'U') IS NULL
        CREATE TABLE dbo.ControlSeguimiento (
            IdControlSeguimiento INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ControlSeguimiento PRIMARY KEY,
            IdAltaClinica        INT NOT NULL CONSTRAINT FK_ControlSeguimiento_Alta REFERENCES dbo.AltaClinica(IdAltaClinica),
            IdCaso               INT NOT NULL CONSTRAINT FK_ControlSeguimiento_Caso REFERENCES dbo.Caso(IdCaso),
            IdPaciente           INT NOT NULL CONSTRAINT FK_ControlSeguimiento_Paciente REFERENCES dbo.Paciente(IdPaciente),
            IdProfesional        INT NOT NULL CONSTRAINT FK_ControlSeguimiento_Profesional REFERENCES dbo.Profesional(IdProfesional),
            Orden                INT NOT NULL,
            FechaControl         DATE NOT NULL,
            Estado               NVARCHAR(20) NOT NULL CONSTRAINT DF_ControlSeguimiento_Estado DEFAULT (N'Pendiente'),
            AvisadoUtc           DATETIME2(3) NULL,
            ResueltoUtc          DATETIME2(3) NULL,
            IdUsuarioResolucion  INT NULL CONSTRAINT FK_ControlSeguimiento_Usuario REFERENCES dbo.Usuario(IdUsuario),
            Nota                 NVARCHAR(500) NULL,
            CONSTRAINT CK_ControlSeguimiento_Estado CHECK (Estado IN (N'Pendiente', N'Avisado', N'Resuelto'))
        );
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ControlSeguimiento_Alta_Orden' AND object_id = OBJECT_ID('dbo.ControlSeguimiento'))
        EXEC(N'CREATE UNIQUE INDEX UX_ControlSeguimiento_Alta_Orden ON dbo.ControlSeguimiento (IdAltaClinica, Orden)');
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ControlSeguimiento_Profesional' AND object_id = OBJECT_ID('dbo.ControlSeguimiento'))
        EXEC(N'CREATE INDEX IX_ControlSeguimiento_Profesional ON dbo.ControlSeguimiento (IdProfesional, Estado, FechaControl)');

    -- 8) Registro de versión
    IF NOT EXISTS (SELECT 1 FROM dbo.VersionEsquema WHERE Numero = 35)
        INSERT INTO dbo.VersionEsquema (Numero, Archivo, Origen) VALUES (35, N'add_ausencias_alta.sql', 'Script');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO

PRINT N'Migración 35 aplicada.';
EXEC(N'SELECT (SELECT COUNT(*) FROM dbo.PropuestaAbandono) AS propuestas_abandono, (SELECT COUNT(*) FROM dbo.ControlSeguimiento) AS controles');
GO
