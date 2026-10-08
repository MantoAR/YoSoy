-- Módulo de mensajería: chatbot de reservas, recordatorios y avisos automáticos.
-- Idempotente: se puede correr más de una vez sin error ni datos duplicados.
-- Requiere que existan Paciente, Profesional y Turno.

-- ── Conversacion ────────────────────────────────────────────
-- Una conversación por teléfono y canal. Telefono se guarda normalizado
-- (solo dígitos, los últimos 10) para comparar contra Paciente.Telefono.
IF OBJECT_ID('dbo.Conversacion', 'U') IS NULL
CREATE TABLE Conversacion (
    IdConversacion       INT IDENTITY(1,1) PRIMARY KEY,
    Telefono             NVARCHAR(20)  NOT NULL,
    IdPaciente           INT           NULL REFERENCES Paciente(IdPaciente),
    Canal                NVARCHAR(20)  NOT NULL CONSTRAINT DF_Conversacion_Canal DEFAULT 'Simulado',
    PasoActual           NVARCHAR(50)  NOT NULL CONSTRAINT DF_Conversacion_Paso DEFAULT 'Inicio',
    DatosContexto        NVARCHAR(MAX) NULL,
    Estado               NVARCHAR(20)  NOT NULL CONSTRAINT DF_Conversacion_Estado DEFAULT 'Activa',
    FechaUltimaActividad DATETIME      NOT NULL CONSTRAINT DF_Conversacion_FUA DEFAULT GETDATE(),
    FechaCreacion        DATETIME      NOT NULL CONSTRAINT DF_Conversacion_FC DEFAULT GETDATE(),
    CONSTRAINT CK_Conversacion_Estado CHECK (Estado IN ('Activa', 'DerivadaHumano', 'Urgente', 'Cerrada')),
    CONSTRAINT UQ_Conversacion_Telefono_Canal UNIQUE (Telefono, Canal)
);
GO

-- ── MensajeChat ─────────────────────────────────────────────
IF OBJECT_ID('dbo.MensajeChat', 'U') IS NULL
CREATE TABLE MensajeChat (
    IdMensaje      INT IDENTITY(1,1) PRIMARY KEY,
    IdConversacion INT            NOT NULL REFERENCES Conversacion(IdConversacion),
    Direccion      NVARCHAR(10)   NOT NULL,
    Origen         NVARCHAR(10)   NOT NULL,
    Texto          NVARCHAR(4000) NOT NULL,
    FechaHora      DATETIME       NOT NULL CONSTRAINT DF_MensajeChat_FechaHora DEFAULT GETDATE(),
    EstadoEnvio    NVARCHAR(10)   NOT NULL CONSTRAINT DF_MensajeChat_EstadoEnvio DEFAULT 'Pendiente',
    CONSTRAINT CK_MensajeChat_Direccion   CHECK (Direccion IN ('Entrante', 'Saliente')),
    CONSTRAINT CK_MensajeChat_Origen      CHECK (Origen IN ('Paciente', 'Bot', 'Humano', 'Sistema')),
    CONSTRAINT CK_MensajeChat_EstadoEnvio CHECK (EstadoEnvio IN ('Pendiente', 'Enviado', 'Error'))
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MensajeChat_Conversacion')
    CREATE INDEX IX_MensajeChat_Conversacion ON MensajeChat (IdConversacion, IdMensaje);
GO

-- ── Recordatorio ────────────────────────────────────────────
-- Registro de cada aviso automático (recordatorio 24 h y avisos por evento).
-- El índice único (IdTurno, Tipo, Canal) garantiza que el mismo aviso no se
-- mande dos veces aunque el proceso corra varias veces.
-- Tipos usados hoy: 'Turno24h', 'TurnoSolicitado', 'TurnoAgendado',
-- 'TurnoConfirmado', 'TurnoCancelado'. Reservado para una etapa futura:
-- 'PagoPendiente' (todavía no existe una entidad de pagos).
-- Respuesta / FechaRespuesta: lo que contestó el paciente al recordatorio
-- ('AsistenciaConfirmada' o 'NoAsistira').
IF OBJECT_ID('dbo.Recordatorio', 'U') IS NULL
CREATE TABLE Recordatorio (
    IdRecordatorio  INT IDENTITY(1,1) PRIMARY KEY,
    IdTurno         INT            NOT NULL REFERENCES Turno(IdTurno),
    Tipo            NVARCHAR(30)   NOT NULL,
    Canal           NVARCHAR(10)   NOT NULL,
    FechaProgramada DATETIME       NOT NULL,
    FechaEnvio      DATETIME       NULL,
    Estado          NVARCHAR(10)   NOT NULL CONSTRAINT DF_Recordatorio_Estado DEFAULT 'Pendiente',
    Intentos        INT            NOT NULL CONSTRAINT DF_Recordatorio_Intentos DEFAULT 0,
    Error           NVARCHAR(1000) NULL,
    Respuesta       NVARCHAR(30)   NULL,
    FechaRespuesta  DATETIME       NULL,
    CONSTRAINT CK_Recordatorio_Canal  CHECK (Canal IN ('Chat', 'Email', 'App')),
    CONSTRAINT CK_Recordatorio_Estado CHECK (Estado IN ('Pendiente', 'Enviado', 'Error', 'Omitido'))
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Recordatorio_Turno_Tipo_Canal')
    CREATE UNIQUE INDEX UX_Recordatorio_Turno_Tipo_Canal ON Recordatorio (IdTurno, Tipo, Canal);
GO

-- ── PreguntaFrecuente ───────────────────────────────────────
IF OBJECT_ID('dbo.PreguntaFrecuente', 'U') IS NULL
CREATE TABLE PreguntaFrecuente (
    IdPregunta INT IDENTITY(1,1) PRIMARY KEY,
    Pregunta   NVARCHAR(300)  NOT NULL,
    Respuesta  NVARCHAR(2000) NOT NULL,
    Orden      INT            NOT NULL CONSTRAINT DF_PreguntaFrecuente_Orden DEFAULT 0,
    Activa     BIT            NOT NULL CONSTRAINT DF_PreguntaFrecuente_Activa DEFAULT 1
);
GO

-- Filas de ejemplo genéricas: solo se cargan si la tabla está vacía.
IF NOT EXISTS (SELECT 1 FROM PreguntaFrecuente)
INSERT INTO PreguntaFrecuente (Pregunta, Respuesta, Orden) VALUES
(N'¿Cómo es la modalidad de atención?',
 N'La atención es virtual, por videollamada desde la plataforma YoSoy. Solo necesitás un dispositivo con cámara, micrófono y conexión a internet.', 1),
(N'¿Cómo pido un turno?',
 N'Podés pedirlo por este chat eligiendo la opción 1 del menú, o desde la web ingresando con tu usuario. El turno queda pendiente hasta que tu profesional lo confirme.', 2),
(N'¿Cómo cancelo un turno?',
 N'Podés cancelarlo por este chat con la opción 3 del menú o desde la web. Las cancelaciones se aceptan hasta 24 horas antes del turno.', 3),
(N'¿Cómo ingreso a la videollamada?',
 N'El día del turno ingresá a la web de YoSoy con tu usuario. Cuando tu profesional habilite la sala vas a ver el acceso en tu panel y un aviso en la campanita de notificaciones.', 4);
GO

-- ── NotificacionInterna ─────────────────────────────────────
-- Avisos para el equipo (administrador o profesional). La tabla Notificacion
-- existente es solo para pacientes y no se modifica.
IF OBJECT_ID('dbo.NotificacionInterna', 'U') IS NULL
CREATE TABLE NotificacionInterna (
    IdNotificacionInterna INT IDENTITY(1,1) PRIMARY KEY,
    Destinatario          NVARCHAR(20)   NOT NULL,
    IdProfesional         INT            NULL REFERENCES Profesional(IdProfesional),
    IdTurno               INT            NULL REFERENCES Turno(IdTurno),
    IdConversacion        INT            NULL REFERENCES Conversacion(IdConversacion),
    Tipo                  NVARCHAR(40)   NOT NULL,
    Titulo                NVARCHAR(200)  NOT NULL,
    Mensaje               NVARCHAR(1000) NOT NULL,
    Leida                 BIT            NOT NULL CONSTRAINT DF_NotificacionInterna_Leida DEFAULT 0,
    FechaCreacion         DATETIME       NOT NULL CONSTRAINT DF_NotificacionInterna_FC DEFAULT GETDATE(),
    CONSTRAINT CK_NotificacionInterna_Destinatario CHECK (Destinatario IN ('Administrador', 'Profesional'))
);
GO
