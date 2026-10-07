-- Mejora de chats y centrado (A3): notas internas, etiquetas y respuestas rápidas con
-- dueño, para la bandeja de Chats. Idempotente: se puede correr más de una vez.
-- Requiere add_mensajeria.sql (Conversacion) y Profesional/Usuario ya existentes.

-- ── NotaInternaConversacion ─────────────────────────────────
-- Privada del equipo: nunca la ve el paciente, nunca se audita el texto (ver
-- MensajeriaEndpoints, Auditar) - solo id de conversación y quién/cuándo.
IF OBJECT_ID('dbo.NotaInternaConversacion', 'U') IS NULL
CREATE TABLE NotaInternaConversacion (
    IdNota         INT           IDENTITY(1,1) PRIMARY KEY,
    IdConversacion INT           NOT NULL REFERENCES Conversacion(IdConversacion),
    Texto          NVARCHAR(1000) NOT NULL,
    IdUsuario      INT           NULL REFERENCES Usuario(IdUsuario),
    FechaCreacion  DATETIME      NOT NULL CONSTRAINT DF_NotaInternaConversacion_Fecha DEFAULT GETDATE()
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_NotaInternaConversacion_Conversacion')
    CREATE INDEX IX_NotaInternaConversacion_Conversacion ON NotaInternaConversacion(IdConversacion);
GO

-- ── EtiquetaConversacion / ConversacionEtiqueta ─────────────
-- Catálogo chico y administrable (ABM igual que PreguntaFrecuente: sin restricción de
-- rol aparte de Profesional/Configurador del grupo /mensajeria). Tono = clase de chip ya
-- existente en tema.css (ui-chip--tono), para no inventar un sistema de color aparte.
IF OBJECT_ID('dbo.EtiquetaConversacion', 'U') IS NULL
CREATE TABLE EtiquetaConversacion (
    IdEtiqueta INT          IDENTITY(1,1) PRIMARY KEY,
    Nombre     NVARCHAR(40) NOT NULL,
    Tono       NVARCHAR(20) NOT NULL CONSTRAINT DF_EtiquetaConversacion_Tono DEFAULT 'neutral',
    Activa     BIT          NOT NULL CONSTRAINT DF_EtiquetaConversacion_Activa DEFAULT 1,
    CONSTRAINT CK_EtiquetaConversacion_Tono CHECK (Tono IN ('marca', 'warn', 'danger', 'neutral', 'ok'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_EtiquetaConversacion_Nombre')
    CREATE UNIQUE INDEX UQ_EtiquetaConversacion_Nombre ON EtiquetaConversacion(Nombre);
GO

IF OBJECT_ID('dbo.ConversacionEtiqueta', 'U') IS NULL
CREATE TABLE ConversacionEtiqueta (
    IdConversacion INT NOT NULL REFERENCES Conversacion(IdConversacion),
    IdEtiqueta     INT NOT NULL REFERENCES EtiquetaConversacion(IdEtiqueta),
    CONSTRAINT PK_ConversacionEtiqueta PRIMARY KEY (IdConversacion, IdEtiqueta)
);
GO

-- ── RespuestaRapida ──────────────────────────────────────────
-- IdProfesional NULL = global (la define el Configurador); con valor = propia de ese
-- profesional. Reemplaza Pantalla_ChatSimulado/plantillas.json (archivo estático, sin
-- ABM) como fuente de las respuestas rápidas del composer.
IF OBJECT_ID('dbo.RespuestaRapida', 'U') IS NULL
CREATE TABLE RespuestaRapida (
    IdRespuestaRapida INT           IDENTITY(1,1) PRIMARY KEY,
    Titulo            NVARCHAR(60)  NOT NULL,
    Texto             NVARCHAR(2000) NOT NULL,
    IdProfesional     INT           NULL REFERENCES Profesional(IdProfesional),
    Activa            BIT           NOT NULL CONSTRAINT DF_RespuestaRapida_Activa DEFAULT 1,
    FechaCreacion     DATETIME      NOT NULL CONSTRAINT DF_RespuestaRapida_Fecha DEFAULT GETDATE()
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RespuestaRapida_Profesional')
    CREATE INDEX IX_RespuestaRapida_Profesional ON RespuestaRapida(IdProfesional);
GO
