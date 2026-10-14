-- Videollamadas, PR 6: consentimiento por sesión para la lectura de emociones.
-- Idempotente: se puede correr más de una vez sin error ni datos duplicados. No modifica ni borra ningún dato
-- existente: solo agrega una tabla y tres columnas NULL.
--
-- Qué agrega:
--   1) Tabla ConsentimientoVideo: una fila por DECISIÓN de un paciente sobre una sesión de video (un turno o una
--      entrevista de admisión). La decisión vigente es la fila más nueva de esa sesión. Una fila no se edita salvo
--      RevocadoUtc (revocar); cambiar de idea es otra fila. Las fechas van en UTC (DATETIME2) a propósito: es lo que
--      se pidió para este registro; el resto del esquema usa DATETIME/GETDATE() (hora del servidor).
--   2) SesionClinica.IdConsentimientoVideo: con qué consentimiento se guardaron las emociones de esa sesión.
--   3) SesionClinica.EmocionesAnuladasUtc / EmocionesAnuladasMotivo: cuándo y por qué se anularon las emociones
--      guardadas ('revocacion': el paciente revocó; 'retencion': las purgó el plazo de retención). Anular pone
--      HistorialEmociones en NULL; las notas clínicas no se tocan nunca.
--
-- Aplicar ANTES de desplegar la API de este PR (la API lee y escribe estas columnas). Va después de
-- add_tablas_sin_migracion_previa.sql (SesionClinica) y alter_logauditoria_idusuario_nullable.sql (la purga audita
-- sin usuario). Con sqlcmd hace falta -I (QUOTED_IDENTIFIER ON, como el resto de los scripts):
--   sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -f 65001 -i migrations\add_consentimiento_video.sql

-- ── 1) ConsentimientoVideo ──────────────────────────────────────────────────
IF OBJECT_ID('dbo.ConsentimientoVideo', 'U') IS NULL
CREATE TABLE ConsentimientoVideo (
    IdConsentimientoVideo INT          IDENTITY(1,1) NOT NULL CONSTRAINT PK_ConsentimientoVideo PRIMARY KEY,
    IdUsuario             INT          NOT NULL,   -- quién decidió (el usuario del paciente)
    IdPaciente            INT          NOT NULL,
    Origen                NVARCHAR(10) NOT NULL,   -- 'turno' | 'admision'
    IdOrigen              INT          NOT NULL,   -- IdTurno o IdInforme, según Origen
    VersionTexto          NVARCHAR(40) NOT NULL,   -- versión del texto que se mostró (ej. v0-provisorio)
    HashTexto             CHAR(64)     NOT NULL,   -- SHA-256 del texto exacto que se mostró
    Decision              NVARCHAR(10) NOT NULL,   -- 'acepta' | 'rechaza'
    DecididoUtc           DATETIME2(3) NOT NULL CONSTRAINT DF_ConsentimientoVideo_DecididoUtc DEFAULT SYSUTCDATETIME(),
    RevocadoUtc           DATETIME2(3) NULL,       -- solo una aceptación se revoca; NULL = vigente
    CONSTRAINT CK_ConsentimientoVideo_Origen     CHECK (Origen IN ('turno', 'admision')),
    CONSTRAINT CK_ConsentimientoVideo_Decision   CHECK (Decision IN ('acepta', 'rechaza')),
    CONSTRAINT CK_ConsentimientoVideo_Revocacion CHECK (RevocadoUtc IS NULL OR (Decision = 'acepta' AND RevocadoUtc >= DecididoUtc))
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_ConsentimientoVideo_Usuario')
    ALTER TABLE ConsentimientoVideo ADD CONSTRAINT FK_ConsentimientoVideo_Usuario
        FOREIGN KEY (IdUsuario) REFERENCES Usuario(IdUsuario);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_ConsentimientoVideo_Paciente')
    ALTER TABLE ConsentimientoVideo ADD CONSTRAINT FK_ConsentimientoVideo_Paciente
        FOREIGN KEY (IdPaciente) REFERENCES Paciente(IdPaciente);
GO

-- La consulta de siempre es "la última decisión de esta sesión para este paciente".
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_ConsentimientoVideo_Sesion' AND object_id = OBJECT_ID('dbo.ConsentimientoVideo'))
    CREATE INDEX IX_ConsentimientoVideo_Sesion
        ON ConsentimientoVideo (Origen, IdOrigen, IdPaciente, IdConsentimientoVideo);
GO

-- ── 2) SesionClinica: vínculo con el consentimiento y registro de la anulación ──
-- Cada ALTER va en su propio lote: SQL Server no resuelve una columna recién agregada dentro del mismo lote.
IF COL_LENGTH('dbo.SesionClinica', 'IdConsentimientoVideo') IS NULL
    ALTER TABLE SesionClinica ADD IdConsentimientoVideo INT NULL;
GO

IF COL_LENGTH('dbo.SesionClinica', 'EmocionesAnuladasUtc') IS NULL
    ALTER TABLE SesionClinica ADD EmocionesAnuladasUtc DATETIME2(3) NULL;
GO

IF COL_LENGTH('dbo.SesionClinica', 'EmocionesAnuladasMotivo') IS NULL
    ALTER TABLE SesionClinica ADD EmocionesAnuladasMotivo NVARCHAR(20) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_SesionClinica_ConsentimientoVideo')
    ALTER TABLE SesionClinica ADD CONSTRAINT FK_SesionClinica_ConsentimientoVideo
        FOREIGN KEY (IdConsentimientoVideo) REFERENCES ConsentimientoVideo(IdConsentimientoVideo);
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_SesionClinica_EmocionesAnuladasMotivo')
    ALTER TABLE SesionClinica ADD CONSTRAINT CK_SesionClinica_EmocionesAnuladasMotivo
        CHECK (EmocionesAnuladasMotivo IS NULL OR EmocionesAnuladasMotivo IN ('revocacion', 'retencion'));
GO
