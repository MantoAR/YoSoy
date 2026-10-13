-- Circuito de atención (octubre 2026): consentimientos opcionales, interruptores de IA y
-- transcripción por sesión, preferencias del profesional y evolución por enfoque con firma
-- inmutable (RN-10, RN-11, RN-15, RN-16).
-- Idempotente: se puede correr más de una vez. Requiere add_consentimiento_telemedicina.sql
-- (6) y add_tablas_sin_migracion_previa.sql (9). Es la migración 15 de la tabla del README.

-- ── 1) Tipos de consentimiento opcionales ─────────────────────
-- El código los busca por Descripcion (Services/ConsentimientosOpcionales.cs), nunca por id.
IF NOT EXISTS (SELECT 1 FROM TipoConsentimiento WHERE Descripcion = N'Consentimiento Opcional - Análisis de expresiones faciales por IA')
    INSERT INTO TipoConsentimiento (Descripcion, Estado) VALUES (N'Consentimiento Opcional - Análisis de expresiones faciales por IA', 'Activo');
IF NOT EXISTS (SELECT 1 FROM TipoConsentimiento WHERE Descripcion = N'Consentimiento Opcional - Transcripción de la sesión')
    INSERT INTO TipoConsentimiento (Descripcion, Estado) VALUES (N'Consentimiento Opcional - Transcripción de la sesión', 'Activo');
IF NOT EXISTS (SELECT 1 FROM TipoConsentimiento WHERE Descripcion = N'Consentimiento Opcional - Avisos por WhatsApp')
    INSERT INTO TipoConsentimiento (Descripcion, Estado) VALUES (N'Consentimiento Opcional - Avisos por WhatsApp', 'Activo');
GO

-- ── 2) Revocación de consentimientos ──────────────────────────
-- Revocar no borra la fila (queda la prueba de que estuvo firmado y desde cuándo dejó de
-- estarlo): Estado pasa a 'Revocado' y se guarda la fecha.
IF COL_LENGTH('dbo.Consentimiento', 'FechaRevocacion') IS NULL
    ALTER TABLE Consentimiento ADD FechaRevocacion DATETIME NULL;
GO

-- ── 3) Preferencias de sesión del profesional ─────────────────
IF OBJECT_ID('dbo.ConfiguracionProfesional', 'U') IS NULL
CREATE TABLE ConfiguracionProfesional (
    IdProfesional            INT          NOT NULL PRIMARY KEY REFERENCES Profesional(IdProfesional),
    Enfoque                  NVARCHAR(30) NOT NULL CONSTRAINT DF_ConfigProf_Enfoque DEFAULT 'General',
    IaExpresionesPorDefecto  BIT          NOT NULL CONSTRAINT DF_ConfigProf_Ia DEFAULT 0,
    TranscripcionPorDefecto  BIT          NOT NULL CONSTRAINT DF_ConfigProf_Transc DEFAULT 0,
    ModoEvolucionPorDefecto  NVARCHAR(30) NOT NULL CONSTRAINT DF_ConfigProf_Modo DEFAULT 'Manual',
    FechaActualizacion       DATETIME     NOT NULL CONSTRAINT DF_ConfigProf_Fecha DEFAULT GETDATE()
);
GO

-- ── 4) Salas de video registradas ─────────────────────────────
-- Una fila por código de sala: a qué paciente y a qué profesional corresponde. Permite que
-- la API sepa, con solo el código, qué consentimientos mirar y quién puede prender o
-- apagar funciones. Las salas creadas antes de esta migración se siguen resolviendo por
-- Turno.CodigoSala o InformeAdmision.CodigoSala (ver SalaSesionDAO.Resolver).
IF OBJECT_ID('dbo.SalaSesion', 'U') IS NULL
CREATE TABLE SalaSesion (
    CodigoSala       NVARCHAR(60) NOT NULL PRIMARY KEY,
    IdPaciente       INT          NOT NULL REFERENCES Paciente(IdPaciente),
    IdProfesional    INT          NULL REFERENCES Profesional(IdProfesional),
    Origen           NVARCHAR(20) NOT NULL,      -- 'Turno', 'Admision', 'Directa'
    IdTurno          INT          NULL,
    IdInforme        INT          NULL,
    IdUsuarioCreador INT          NULL,
    FechaCreacion    DATETIME     NOT NULL CONSTRAINT DF_SalaSesion_Fecha DEFAULT GETDATE()
);
GO

-- ── 5) Registro de cada encendido y apagado de funciones ──────
-- El estado actual de una función en una sala es la última fila (por IdSesionFuncion).
IF OBJECT_ID('dbo.SesionFuncion', 'U') IS NULL
CREATE TABLE SesionFuncion (
    IdSesionFuncion INT          IDENTITY(1,1) PRIMARY KEY,
    CodigoSala      NVARCHAR(60) NOT NULL,
    IdPaciente      INT          NOT NULL REFERENCES Paciente(IdPaciente),
    IdUsuario       INT          NULL,
    RolUsuario      NVARCHAR(20) NULL,
    Funcion         NVARCHAR(30) NOT NULL,      -- 'IaExpresiones', 'Transcripcion'
    Activa          BIT          NOT NULL,
    Fecha           DATETIME     NOT NULL CONSTRAINT DF_SesionFuncion_Fecha DEFAULT GETDATE()
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SesionFuncion_Sala')
    CREATE INDEX IX_SesionFuncion_Sala ON SesionFuncion(CodigoSala, Funcion, IdSesionFuncion);
GO

-- ── 6) Evolución por enfoque, con borrador y firma ────────────
IF COL_LENGTH('dbo.Evolucion', 'TipoCierre') IS NULL
    ALTER TABLE Evolucion ADD TipoCierre NVARCHAR(50) NULL;
IF COL_LENGTH('dbo.Evolucion', 'Enfoque') IS NULL
    ALTER TABLE Evolucion ADD Enfoque NVARCHAR(30) NULL;
IF COL_LENGTH('dbo.Evolucion', 'ModoRegistro') IS NULL
    ALTER TABLE Evolucion ADD ModoRegistro NVARCHAR(30) NULL;
IF COL_LENGTH('dbo.Evolucion', 'ContenidoJson') IS NULL
    ALTER TABLE Evolucion ADD ContenidoJson NVARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.Evolucion', 'CodigoSala') IS NULL
    ALTER TABLE Evolucion ADD CodigoSala NVARCHAR(60) NULL;
IF COL_LENGTH('dbo.Evolucion', 'FechaFirma') IS NULL
    ALTER TABLE Evolucion ADD FechaFirma DATETIME NULL;
IF COL_LENGTH('dbo.Evolucion', 'HashContenido') IS NULL
    ALTER TABLE Evolucion ADD HashContenido CHAR(64) NULL;
-- Las evoluciones que ya existían quedan como firmadas: se cargaron como definitivas.
IF COL_LENGTH('dbo.Evolucion', 'Estado') IS NULL
    ALTER TABLE Evolucion ADD Estado NVARCHAR(20) NOT NULL CONSTRAINT DF_Evolucion_Estado DEFAULT 'Firmada';
GO

-- ── 7) Correcciones de una evolución firmada (addenda) ────────
IF OBJECT_ID('dbo.EvolucionAddendum', 'U') IS NULL
CREATE TABLE EvolucionAddendum (
    IdAddendum    INT           IDENTITY(1,1) PRIMARY KEY,
    IdEvolucion   INT           NOT NULL REFERENCES Evolucion(IdEvolucion),
    IdProfesional INT           NULL REFERENCES Profesional(IdProfesional),
    IdUsuario     INT           NOT NULL,
    Texto         NVARCHAR(MAX) NOT NULL,
    Fecha         DATETIME      NOT NULL CONSTRAINT DF_EvolucionAddendum_Fecha DEFAULT GETDATE()
);
GO

-- ── 8) RN-11 en la base: una evolución firmada no se modifica ni se borra ─
-- Defensa en profundidad: aunque algún endpoint se olvide de chequearlo, la base lo
-- rechaza. Solo se permite pasar de 'Borrador' a 'Firmada'.
CREATE OR ALTER TRIGGER TR_Evolucion_Inmutable ON Evolucion AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.IdEvolucion = d.IdEvolucion
        WHERE d.Estado = 'Firmada' AND (
               i.Estado <> 'Firmada'
            OR i.IdHistoria <> d.IdHistoria
            OR i.IdProfesional <> d.IdProfesional
            OR ISNULL(i.IdTurno, -1) <> ISNULL(d.IdTurno, -1)
            OR i.Fecha <> d.Fecha
            OR ISNULL(i.Diagnostico, N'') <> ISNULL(d.Diagnostico, N'')
            OR ISNULL(i.Observaciones, N'') <> ISNULL(d.Observaciones, N'')
            OR ISNULL(i.PlanTratamiento, N'') <> ISNULL(d.PlanTratamiento, N'')
            OR ISNULL(i.TipoCierre, N'') <> ISNULL(d.TipoCierre, N'')
            OR ISNULL(i.Enfoque, N'') <> ISNULL(d.Enfoque, N'')
            OR ISNULL(i.ModoRegistro, N'') <> ISNULL(d.ModoRegistro, N'')
            OR ISNULL(i.ContenidoJson, N'') <> ISNULL(d.ContenidoJson, N'')
            OR ISNULL(i.HashContenido, '') <> ISNULL(d.HashContenido, '')
            OR ISNULL(i.FechaFirma, '19000101') <> ISNULL(d.FechaFirma, '19000101')))
        THROW 50011, N'RN-11: una evolución firmada no se puede modificar. Agregá un addendum.', 1;
END;
GO

CREATE OR ALTER TRIGGER TR_Evolucion_NoBorrarFirmada ON Evolucion AFTER DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted WHERE Estado = 'Firmada')
        THROW 50012, N'RN-11: una evolución firmada no se puede borrar.', 1;
END;
GO
