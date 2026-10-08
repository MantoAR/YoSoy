-- Integración a la rama principal, Paso 3.3: InformeAdmision, Notificacion y
-- SesionClinica existen en la base de desarrollo real y el código las usa desde hace
-- rato, pero ninguna tenía una migración — se crearon a mano en algún momento, antes de
-- que estuviera el hábito de versionar cada cambio de esquema. Se encontraron recién al
-- probar la cadena completa de migraciones contra una base nueva y vacía: sin esto,
-- add_grupos_casos_derivacion.sql falla (Caso.IdInforme referencia InformeAdmision, que
-- todavía no existiría). Esquema tomado tal cual de la base real (columnas, tipos,
-- nullability, default y foreign keys, vía INFORMATION_SCHEMA/sys.foreign_keys) — no es
-- un diseño nuevo, es capturar lo que ya existe. Idempotente. Requiere solo el script
-- base de la rama DataBase (Paciente, Profesional); va antes de add_grupos_casos_derivacion.sql.

-- ── InformeAdmision ──────────────────────────────────────────
IF OBJECT_ID('dbo.InformeAdmision', 'U') IS NULL
CREATE TABLE InformeAdmision (
    IdInforme               INT           IDENTITY(1,1) PRIMARY KEY,
    IdPaciente              INT           NOT NULL REFERENCES Paciente(IdPaciente),
    IdEspecialidad          INT           NULL,
    MotivoConsulta          NVARCHAR(1000) NULL,
    TratamientoAnterior     BIT           NOT NULL CONSTRAINT DF_InformeAdmision_TratamientoAnterior DEFAULT 0,
    TratamientoAnteriorDetalle NVARCHAR(1000) NULL,
    RecibeMedicacion        BIT           NOT NULL CONSTRAINT DF_InformeAdmision_RecibeMedicacion DEFAULT 0,
    MedicacionDetalle       NVARCHAR(1000) NULL,
    GustosIntereses         NVARCHAR(1000) NULL,
    PreferenciasAtencion    NVARCHAR(1000) NULL,
    TipoEntrevista          NVARCHAR(50)  NULL,
    RedApoyo                NVARCHAR(1000) NULL,
    ObjetivosPaciente       NVARCHAR(1000) NULL,
    Estado                  NVARCHAR(50)  NOT NULL CONSTRAINT DF_InformeAdmision_Estado DEFAULT 'Pendiente',
    FechaCreacion           DATETIME      NOT NULL CONSTRAINT DF_InformeAdmision_FechaCreacion DEFAULT GETDATE(),
    FechaEntrevista         DATETIME      NULL,
    ObservacionesEntrevista NVARCHAR(1000) NULL,
    IdProfesionalDerivado   INT           NULL,
    CodigoSala              NVARCHAR(50)  NULL,
    MotivoRechazo           NVARCHAR(500) NULL
);
GO

-- ── Notificacion (distinta de NotificacionInterna, que es para el equipo) ───
IF OBJECT_ID('dbo.Notificacion', 'U') IS NULL
CREATE TABLE Notificacion (
    IdNotificacion INT      IDENTITY(1,1) PRIMARY KEY,
    IdPaciente     INT      NOT NULL REFERENCES Paciente(IdPaciente),
    Titulo         NVARCHAR(200)  NULL,
    Mensaje        NVARCHAR(1000) NULL,
    Link           NVARCHAR(200)  NULL,
    Leida          BIT      NOT NULL CONSTRAINT DF_Notificacion_Leida DEFAULT 0,
    FechaCreacion  DATETIME NOT NULL CONSTRAINT DF_Notificacion_FechaCreacion DEFAULT GETDATE()
);
GO

-- ── SesionClinica ────────────────────────────────────────────
IF OBJECT_ID('dbo.SesionClinica', 'U') IS NULL
CREATE TABLE SesionClinica (
    IdSesion           INT      IDENTITY(1,1) PRIMARY KEY,
    IdTurno            INT      NULL,
    IdPaciente         INT      NOT NULL REFERENCES Paciente(IdPaciente),
    IdProfesional      INT      NOT NULL REFERENCES Profesional(IdProfesional),
    FechaHora          DATETIME NOT NULL,
    Transcripcion      NVARCHAR(MAX) NULL,
    NotasEvolucion     NVARCHAR(MAX) NULL,
    HistorialEmociones NVARCHAR(MAX) NULL,
    TipoCierre         NVARCHAR(50)  NULL
);
GO
