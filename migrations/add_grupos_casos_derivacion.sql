-- Fase 2 (Cambios-Agus): Grupos, Casos y derivación en cascada.
-- Idempotente: se puede correr más de una vez sin error ni datos duplicados.
-- Requiere que existan Paciente, Profesional, Especialidad, Usuario e InformeAdmision.
--
-- QUOTED_IDENTIFIER ON: lo pide el índice único filtrado de más abajo (UQ_Caso_IdInforme);
-- sin esto, CREATE INDEX con WHERE falla según la sesión que corra el script.
SET QUOTED_IDENTIFIER ON;
GO
--
-- "Derivación" ya es, en este proyecto, el cambio de profesional de un paciente que ya
-- está en tratamiento (tabla Derivacion, endpoints /derivaciones/*, "Solicitudes de
-- cambio" en la interfaz) — nada que ver con lo de acá. Por eso la tabla nueva se llama
-- CasoDerivacion, no Derivacion: evita el choque de nombre y no mezcla los dos procesos
-- de negocio. Ver CAMBIOS_AGUS.md, sección "Fase 2", para el detalle completo.

-- ── Grupo ───────────────────────────────────────────────────
-- Clasificación nueva y más fina que Especialidad (p. ej. "Psicología por emociones",
-- "clínica", "educativa", "deportiva"); no la reemplaza, convive al lado.
IF OBJECT_ID('dbo.Grupo', 'U') IS NULL
CREATE TABLE Grupo (
    IdGrupo     INT IDENTITY(1,1) PRIMARY KEY,
    Nombre      NVARCHAR(100) NOT NULL,
    Descripcion NVARCHAR(300) NULL,
    Activo      BIT           NOT NULL CONSTRAINT DF_Grupo_Activo DEFAULT 1
);
GO

-- ── ProfesionalGrupo ────────────────────────────────────────
-- N:M: un profesional puede estar en más de un grupo. Profesional.IdEspecialidad
-- (una sola FK, obligatoria) no se toca.
IF OBJECT_ID('dbo.ProfesionalGrupo', 'U') IS NULL
CREATE TABLE ProfesionalGrupo (
    IdProfesional INT NOT NULL REFERENCES Profesional(IdProfesional),
    IdGrupo       INT NOT NULL REFERENCES Grupo(IdGrupo),
    CONSTRAINT PK_ProfesionalGrupo PRIMARY KEY (IdProfesional, IdGrupo)
);
GO

-- ── Caso ────────────────────────────────────────────────────
-- Un caso por admisión como máximo. OJO: a diferencia de otros motores, SQL Server NO
-- permite más de un NULL en una UNIQUE CONSTRAINT/índice único normal (lo probó el test
-- de integración: el segundo Caso sin admisión ya rompía). La unicidad de IdInforme va
-- aparte, como índice único FILTRADO (WHERE IdInforme IS NOT NULL) — ahí sí, cualquier
-- cantidad de NULL conviven sin chocar, y los casos que no nazcan de una admisión
-- (Origen 'Canal'/'Bot', reservado para más adelante) no se ven afectados.
IF OBJECT_ID('dbo.Caso', 'U') IS NULL
CREATE TABLE Caso (
    IdCaso                INT IDENTITY(1,1) PRIMARY KEY,
    IdPaciente            INT           NOT NULL REFERENCES Paciente(IdPaciente),
    Estado                NVARCHAR(20)  NOT NULL CONSTRAINT DF_Caso_Estado DEFAULT 'Nuevo',
    IdGrupoActual         INT           NULL REFERENCES Grupo(IdGrupo),
    IdProfesionalAsignado INT           NULL REFERENCES Profesional(IdProfesional),
    Origen                NVARCHAR(20)  NOT NULL CONSTRAINT DF_Caso_Origen DEFAULT 'Admision',
    FechaCreacion         DATETIME      NOT NULL CONSTRAINT DF_Caso_FechaCreacion DEFAULT GETDATE(),
    IdInforme             INT           NULL REFERENCES InformeAdmision(IdInforme),
    CONSTRAINT CK_Caso_Estado CHECK (Estado IN ('Nuevo', 'Derivado', 'Asignado', 'En atención', 'Cerrado')),
    CONSTRAINT CK_Caso_Origen CHECK (Origen IN ('Admision', 'Canal', 'Bot'))
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_Caso_IdInforme')
    CREATE UNIQUE INDEX UQ_Caso_IdInforme ON Caso(IdInforme) WHERE IdInforme IS NOT NULL;
GO

-- ── CasoDerivacion ──────────────────────────────────────────
-- La sugerencia/decisión de a qué grupo (y profesional) va un caso. IdProfesionalSugerido
-- es lo que propuso la cascada; IdProfesionalAsignado se completa al confirmar/corregir
-- (puede diferir del sugerido). Motivo es corto a propósito — vocabulario cerrado o texto
-- breve, nunca el detalle clínico (MotivoConsulta de InformeAdmision no se referencia acá).
IF OBJECT_ID('dbo.CasoDerivacion', 'U') IS NULL
CREATE TABLE CasoDerivacion (
    IdCasoDerivacion      INT IDENTITY(1,1) PRIMARY KEY,
    IdCaso                INT           NOT NULL REFERENCES Caso(IdCaso),
    IdGrupo               INT           NOT NULL REFERENCES Grupo(IdGrupo),
    IdProfesionalSugerido INT           NULL REFERENCES Profesional(IdProfesional),
    IdProfesionalAsignado INT           NULL REFERENCES Profesional(IdProfesional),
    Origen                NVARCHAR(10)  NOT NULL CONSTRAINT DF_CasoDerivacion_Origen DEFAULT 'Regla',
    Estado                NVARCHAR(25)  NOT NULL CONSTRAINT DF_CasoDerivacion_Estado DEFAULT 'Pendiente de confirmar',
    Motivo                NVARCHAR(200) NULL,
    IdCreadoPor           INT           NULL REFERENCES Usuario(IdUsuario),
    FechaCreacion         DATETIME      NOT NULL CONSTRAINT DF_CasoDerivacion_FechaCreacion DEFAULT GETDATE(),
    FechaResolucion       DATETIME      NULL,
    CONSTRAINT CK_CasoDerivacion_Origen CHECK (Origen IN ('Regla', 'Manual', 'Bot')),
    CONSTRAINT CK_CasoDerivacion_Estado CHECK (Estado IN ('Pendiente de confirmar', 'Confirmada', 'Corregida', 'Rechazada'))
);
GO

-- ── ReglaDerivacion ─────────────────────────────────────────
-- Criterio mínimo viable: especialidad solicitada en la admisión → grupo destino.
-- Nada de palabras clave ni IA en esta fase.
IF OBJECT_ID('dbo.ReglaDerivacion', 'U') IS NULL
CREATE TABLE ReglaDerivacion (
    IdRegla        INT IDENTITY(1,1) PRIMARY KEY,
    IdEspecialidad INT NOT NULL REFERENCES Especialidad(IdEspecialidad),
    IdGrupoDestino INT NOT NULL REFERENCES Grupo(IdGrupo),
    Prioridad      INT NOT NULL CONSTRAINT DF_ReglaDerivacion_Prioridad DEFAULT 0,
    Activa         BIT NOT NULL CONSTRAINT DF_ReglaDerivacion_Activa DEFAULT 1
);
GO

-- ── HistorialAsignacion ─────────────────────────────────────
-- Quién fue asignado a qué caso, cuándo y por qué (vocabulario cerrado), para poder
-- explicar cada asignación. IdProfesional NULL = quedó sin asignar.
IF OBJECT_ID('dbo.HistorialAsignacion', 'U') IS NULL
CREATE TABLE HistorialAsignacion (
    IdHistorial    INT          IDENTITY(1,1) PRIMARY KEY,
    IdCaso         INT          NOT NULL REFERENCES Caso(IdCaso),
    IdProfesional  INT          NULL REFERENCES Profesional(IdProfesional),
    Motivo         NVARCHAR(25) NOT NULL,
    IdRealizadoPor INT          NULL REFERENCES Usuario(IdUsuario),
    FechaHora      DATETIME     NOT NULL CONSTRAINT DF_HistorialAsignacion_FechaHora DEFAULT GETDATE(),
    CONSTRAINT CK_HistorialAsignacion_Motivo CHECK (Motivo IN ('AutomaticaPorCarga', 'AutomaticaAleatoria', 'Manual', 'SolicitudCambio', 'SinDisponibles'))
);
GO

-- ── Índices (columnas de búsqueda: grupo, profesional, estado) ─
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Caso_Estado')
    CREATE INDEX IX_Caso_Estado ON Caso(Estado);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Caso_Grupo')
    CREATE INDEX IX_Caso_Grupo ON Caso(IdGrupoActual);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Caso_Profesional')
    CREATE INDEX IX_Caso_Profesional ON Caso(IdProfesionalAsignado);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CasoDerivacion_Estado')
    CREATE INDEX IX_CasoDerivacion_Estado ON CasoDerivacion(Estado);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CasoDerivacion_Caso')
    CREATE INDEX IX_CasoDerivacion_Caso ON CasoDerivacion(IdCaso);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_HistorialAsignacion_Caso')
    CREATE INDEX IX_HistorialAsignacion_Caso ON HistorialAsignacion(IdCaso);
GO
