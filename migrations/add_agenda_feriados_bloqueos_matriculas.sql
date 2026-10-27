-- Agenda y matrícula (octubre 2026, bloques 1 y 2 de nuevo-circuito2). Es la migración 33. Aditiva e idempotente: si se corta a mitad, se vuelve a correr entera.
-- Requiere la 18 (VersionEsquema), la 19 (auditoría) y la 20 (Especialidad.Tipo, roles y permisos).
--
--   sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i migrations/add_agenda_feriados_bloqueos_matriculas.sql
--
-- Es OBLIGATORIA para la API de esta versión: el cálculo de horarios libres lee Feriado, BloqueoAgenda y ProfesionalMatricula.
-- Correla ANTES de publicar la API nueva. La API anterior ignora todo esto, así que se puede correr con la vieja andando.
--
-- Deshacer: docs/produccion/deshacer-migracion-33.sql
--
-- Qué hace:
--  1. Feriado: los feriados que carga el Configurador. Ese día nadie ofrece turnos, salvo quien marcó "trabajo este feriado".
--     Dar de baja un feriado no lo borra (Activo = 0): queda quién lo cargó.
--  2. FeriadoTrabajado: el profesional que trabaja un feriado puntual.
--  3. BloqueoAgenda: licencias, vacaciones, capacitación, etc. de un profesional, entre dos fechas y horas. Anular no borra.
--  4. ProfesionalMatricula: una matrícula por especialidad. La principal (una por profesional) es obligatoria; las
--     secundarias, opcionales. Vencimiento NULL = todavía no se cargó: no corta la agenda, pero aparece en el reporte.
--     Desde la fecha de vencimiento de la PRINCIPAL la agenda deja de ofrecer turnos.
--     Se carga una fila principal por cada profesional que ya tiene matrícula y especialidad (sin vencimiento).
--  5. AvisoMatricula: un registro por matrícula y por día con aviso enviado, para no repetir el aviso del mismo día.
--  6. EstadoTurno 'Ausente' (inasistencia que registra el profesional). Se busca por descripción, no por id.
--  7. Registra la versión 26 en VersionEsquema.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.VersionEsquema', 'U') IS NULL OR COL_LENGTH('dbo.Especialidad', 'Tipo') IS NULL OR COL_LENGTH('dbo.LogAuditoria', 'Categoria') IS NULL
    RAISERROR(N'Faltan migraciones anteriores (18, 19 o 20): aplicalas antes (ver docs/CATALOGO_SCRIPTS_SQL.md).', 16, 1);
GO

-- ── 1) Feriado ────────────────────────────────────────────
IF OBJECT_ID('dbo.Feriado', 'U') IS NULL
CREATE TABLE dbo.Feriado (
    IdFeriado      INT            IDENTITY(1,1) CONSTRAINT PK_Feriado PRIMARY KEY,
    Fecha          DATE           NOT NULL,
    Nombre         NVARCHAR(120)  NOT NULL,
    Activo         BIT            NOT NULL CONSTRAINT DF_Feriado_Activo DEFAULT (1),
    IdUsuarioCreo  INT            NULL CONSTRAINT FK_Feriado_Usuario REFERENCES dbo.Usuario(IdUsuario),
    Creado         DATETIME2(0)   NOT NULL CONSTRAINT DF_Feriado_Creado DEFAULT SYSDATETIME()
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Feriado') AND name = 'UX_Feriado_Fecha_Activo')
    EXEC(N'CREATE UNIQUE INDEX UX_Feriado_Fecha_Activo ON dbo.Feriado(Fecha) WHERE Activo = 1;');

GO

-- ── 2) FeriadoTrabajado ───────────────────────────────────
IF OBJECT_ID('dbo.FeriadoTrabajado', 'U') IS NULL
CREATE TABLE dbo.FeriadoTrabajado (
    IdFeriado      INT           NOT NULL CONSTRAINT FK_FeriadoTrabajado_Feriado REFERENCES dbo.Feriado(IdFeriado),
    IdProfesional  INT           NOT NULL CONSTRAINT FK_FeriadoTrabajado_Profesional REFERENCES dbo.Profesional(IdProfesional),
    Creado         DATETIME2(0)  NOT NULL CONSTRAINT DF_FeriadoTrabajado_Creado DEFAULT SYSDATETIME(),
    CONSTRAINT PK_FeriadoTrabajado PRIMARY KEY (IdFeriado, IdProfesional)
);

GO

-- ── 3) BloqueoAgenda ──────────────────────────────────────
IF OBJECT_ID('dbo.BloqueoAgenda', 'U') IS NULL
CREATE TABLE dbo.BloqueoAgenda (
    IdBloqueo      INT            IDENTITY(1,1) CONSTRAINT PK_BloqueoAgenda PRIMARY KEY,
    IdProfesional  INT            NOT NULL CONSTRAINT FK_BloqueoAgenda_Profesional REFERENCES dbo.Profesional(IdProfesional),
    Desde          DATETIME2(0)   NOT NULL,
    Hasta          DATETIME2(0)   NOT NULL,
    Motivo         NVARCHAR(30)   NOT NULL,
    Detalle        NVARCHAR(200)  NULL,
    IdUsuarioCreo  INT            NULL CONSTRAINT FK_BloqueoAgenda_Usuario REFERENCES dbo.Usuario(IdUsuario),
    Creado         DATETIME2(0)   NOT NULL CONSTRAINT DF_BloqueoAgenda_Creado DEFAULT SYSDATETIME(),
    Anulado        DATETIME2(0)   NULL,
    CONSTRAINT CK_BloqueoAgenda_Rango  CHECK (Hasta > Desde),
    CONSTRAINT CK_BloqueoAgenda_Motivo CHECK (Motivo IN (N'Licencia', N'Vacaciones', N'Capacitación', N'Personal', N'Otro'))
);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.BloqueoAgenda') AND name = 'IX_BloqueoAgenda_Profesional')
    EXEC(N'CREATE INDEX IX_BloqueoAgenda_Profesional ON dbo.BloqueoAgenda(IdProfesional, Desde, Hasta);');

GO

-- ── 4) ProfesionalMatricula ───────────────────────────────
IF OBJECT_ID('dbo.ProfesionalMatricula', 'U') IS NULL
CREATE TABLE dbo.ProfesionalMatricula (
    IdMatricula        INT            IDENTITY(1,1) CONSTRAINT PK_ProfesionalMatricula PRIMARY KEY,
    IdProfesional      INT            NOT NULL CONSTRAINT FK_ProfesionalMatricula_Profesional REFERENCES dbo.Profesional(IdProfesional),
    IdEspecialidad     INT            NOT NULL CONSTRAINT FK_ProfesionalMatricula_Especialidad REFERENCES dbo.Especialidad(IdEspecialidad),
    Numero             NVARCHAR(50)   NOT NULL,
    Jurisdiccion       NVARCHAR(60)   NULL,
    Vencimiento        DATE           NULL,
    Principal          BIT            NOT NULL CONSTRAINT DF_ProfesionalMatricula_Principal DEFAULT (0),
    Activa             BIT            NOT NULL CONSTRAINT DF_ProfesionalMatricula_Activa DEFAULT (1),
    Creada             DATETIME2(0)   NOT NULL CONSTRAINT DF_ProfesionalMatricula_Creada DEFAULT SYSDATETIME(),
    Modificada         DATETIME2(0)   NULL,
    IdUsuarioModifico  INT            NULL CONSTRAINT FK_ProfesionalMatricula_Usuario REFERENCES dbo.Usuario(IdUsuario)
);

-- Una sola principal activa por profesional y una sola matrícula activa por especialidad.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.ProfesionalMatricula') AND name = 'UX_ProfesionalMatricula_Principal')
    EXEC(N'CREATE UNIQUE INDEX UX_ProfesionalMatricula_Principal ON dbo.ProfesionalMatricula(IdProfesional) WHERE Principal = 1 AND Activa = 1;');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.ProfesionalMatricula') AND name = 'UX_ProfesionalMatricula_Especialidad')
    EXEC(N'CREATE UNIQUE INDEX UX_ProfesionalMatricula_Especialidad ON dbo.ProfesionalMatricula(IdProfesional, IdEspecialidad) WHERE Activa = 1;');

-- Las matrículas que ya existen pasan como principales, sin vencimiento (lo carga el Configurador).
EXEC(N'
    INSERT INTO dbo.ProfesionalMatricula (IdProfesional, IdEspecialidad, Numero, Jurisdiccion, Vencimiento, Principal)
    SELECT p.IdProfesional, p.IdEspecialidad, LTRIM(RTRIM(p.Matricula)), p.JurisdiccionMatricula, NULL, 1
    FROM dbo.Profesional p
    WHERE p.Matricula IS NOT NULL AND LTRIM(RTRIM(p.Matricula)) <> N''''
      AND p.IdEspecialidad IS NOT NULL
      AND EXISTS (SELECT 1 FROM dbo.Especialidad e WHERE e.IdEspecialidad = p.IdEspecialidad)
      AND NOT EXISTS (SELECT 1 FROM dbo.ProfesionalMatricula m WHERE m.IdProfesional = p.IdProfesional AND m.Activa = 1);');

GO

-- ── 5) AvisoMatricula ─────────────────────────────────────
IF OBJECT_ID('dbo.AvisoMatricula', 'U') IS NULL
CREATE TABLE dbo.AvisoMatricula (
    IdAviso      INT            IDENTITY(1,1) CONSTRAINT PK_AvisoMatricula PRIMARY KEY,
    IdMatricula  INT            NOT NULL CONSTRAINT FK_AvisoMatricula_Matricula REFERENCES dbo.ProfesionalMatricula(IdMatricula),
    Fecha        DATE           NOT NULL,
    Tipo         NVARCHAR(20)   NOT NULL CONSTRAINT CK_AvisoMatricula_Tipo CHECK (Tipo IN (N'PorVencer', N'Vencida')),
    Creado       DATETIME2(0)   NOT NULL CONSTRAINT DF_AvisoMatricula_Creado DEFAULT SYSDATETIME(),
    CONSTRAINT UQ_AvisoMatricula_Dia UNIQUE (IdMatricula, Fecha)
);

GO

-- ── 6) Estado Ausente ─────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.EstadoTurno WHERE Descripcion = N'Ausente')
    INSERT INTO dbo.EstadoTurno (Descripcion, Estado) VALUES (N'Ausente', N'Activo');

GO

-- ── 7) Registro de versión ────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.VersionEsquema WHERE Numero = 33)
    INSERT INTO dbo.VersionEsquema (Numero, Archivo, Origen) VALUES (33, N'add_agenda_feriados_bloqueos_matriculas.sql', 'Script');
GO

PRINT N'Migración 33 aplicada.';
SELECT (SELECT COUNT(*) FROM dbo.ProfesionalMatricula WHERE Activa = 1) AS matriculas_cargadas,
   (SELECT COUNT(*) FROM dbo.ProfesionalMatricula WHERE Activa = 1 AND Vencimiento IS NULL) AS sin_vencimiento,
   (SELECT IdEstadoTurno FROM dbo.EstadoTurno WHERE Descripcion = N'Ausente') AS id_estado_ausente;
GO
