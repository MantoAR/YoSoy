-- Consentimiento de WhatsApp: el paciente acepta (y puede revocar) recibir avisos de sus turnos por WhatsApp.
-- Idempotente: se puede correr más de una vez sin error ni datos duplicados. No modifica ni borra ningún dato
-- existente: solo agrega una tabla, dos índices y dos claves foráneas. No toca ninguna tabla que ya exista.
--
-- Qué agrega:
--   Tabla ConsentimientoWhatsApp: una fila por ACEPTACIÓN de un paciente. Una fila no se edita salvo para revocarla
--   (RevocadoUtc + MotivoRevocacion); volver a aceptar después de revocar es otra fila, así que queda todo el historial.
--   El estado de un paciente es el de su fila más nueva. Guarda lo que hace falta para poder demostrar el consentimiento:
--     - quién lo dio (IdUsuario: el usuario del propio paciente) y para qué número (Telefono);
--     - qué texto vio (VersionTexto + HashTexto, el SHA-256 del texto exacto que se mostró);
--     - cuándo lo aceptó y cuándo lo revocó (en UTC, DATETIME2: es lo que se pidió para este registro; el resto del
--       esquema usa DATETIME/GETDATE(), hora del servidor) y por qué (paciente, baja-whatsapp, telefono-cambiado, texto-nuevo).
--   Un índice único filtrado garantiza que un paciente no pueda tener dos aceptaciones abiertas (un doble clic no duplica).
--
-- Aplicar ANTES de desplegar la API de esta rama: la API lee y escribe esta tabla (sin ella, aceptar o revocar da error; los
-- avisos de turnos por WhatsApp se seguirían intentando sin esta información). Requiere Paciente y Usuario, que existen
-- desde el esquema base. Con sqlcmd se corre así:
--   sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -f 65001 -i migrations\add_consentimiento_whatsapp.sql
--
-- Verificar (tiene que dar 1, 2 y 2: la tabla, sus dos claves foráneas y sus dos índices):
--   SELECT (SELECT COUNT(*) FROM sys.tables WHERE name = 'ConsentimientoWhatsApp') AS tabla,
--          (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('dbo.ConsentimientoWhatsApp')) AS claves,
--          (SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.ConsentimientoWhatsApp')
--              AND name IN ('UQ_ConsentimientoWhatsApp_Abierto', 'IX_ConsentimientoWhatsApp_Paciente')) AS indices;
--
-- Deshacer: no hace falta para volver a la API anterior, que no conoce la tabla y la ignora. Si de verdad hay que sacarla,
-- DROP TABLE dbo.ConsentimientoWhatsApp; PERO esa tabla es el registro de quién aceptó recibir WhatsApp, con qué texto y cuándo:
-- antes de borrarla, guardá un backup de la base (o un SELECT * de la tabla). Sin ella, la API de esta rama no puede aceptar ni
-- revocar, y los avisos por WhatsApp quedan sin salir (se prefiere no mandar a mandar sin saber si hay consentimiento).
--
-- QUOTED_IDENTIFIER ON: lo pide el índice único filtrado de más abajo (error 1934 si no), igual que en
-- add_link_publico_profesional.sql y add_grupos_casos_derivacion.sql. Con -I ya viene así; el SET lo deja a salvo si se olvida.
SET QUOTED_IDENTIFIER ON;
GO

-- ── 1) ConsentimientoWhatsApp ───────────────────────────────────────────────
IF OBJECT_ID('dbo.ConsentimientoWhatsApp', 'U') IS NULL
CREATE TABLE ConsentimientoWhatsApp (
    IdConsentimientoWhatsApp INT          IDENTITY(1,1) NOT NULL CONSTRAINT PK_ConsentimientoWhatsApp PRIMARY KEY,
    IdPaciente               INT          NOT NULL,   -- de quién es el consentimiento
    IdUsuario                INT          NOT NULL,   -- quién lo dio: el usuario del propio paciente
    Telefono                 VARCHAR(10)  NOT NULL,   -- el número para el que se aceptó: nacional, 10 dígitos (sin 0, 15, 54 ni 9)
    Origen                   NVARCHAR(10) NOT NULL,   -- 'registro' | 'perfil'
    VersionTexto             NVARCHAR(40) NOT NULL,   -- versión del texto que se mostró (ej. v0-provisorio)
    HashTexto                CHAR(64)     NOT NULL,   -- SHA-256 del texto exacto que se mostró
    AceptadoUtc              DATETIME2(3) NOT NULL CONSTRAINT DF_ConsentimientoWhatsApp_AceptadoUtc DEFAULT SYSUTCDATETIME(),
    RevocadoUtc              DATETIME2(3) NULL,       -- NULL = vigente
    MotivoRevocacion         NVARCHAR(20) NULL,       -- 'paciente' | 'baja-whatsapp' | 'telefono-cambiado' | 'texto-nuevo'
    CONSTRAINT CK_ConsentimientoWhatsApp_Origen   CHECK (Origen IN ('registro', 'perfil')),
    CONSTRAINT CK_ConsentimientoWhatsApp_Telefono CHECK (LEN(Telefono) = 10 AND Telefono NOT LIKE '%[^0-9]%'),
    CONSTRAINT CK_ConsentimientoWhatsApp_Hash     CHECK (LEN(HashTexto) = 64),
    CONSTRAINT CK_ConsentimientoWhatsApp_Revocacion CHECK (
        (RevocadoUtc IS NULL AND MotivoRevocacion IS NULL)
        -- El IS NOT NULL del motivo es necesario: un CHECK se da por cumplido cuando su resultado es "desconocido", y
        -- NULL IN (...) lo es. Sin esa condición, una revocación con fecha y sin motivo entraría.
        OR (RevocadoUtc IS NOT NULL AND MotivoRevocacion IS NOT NULL AND RevocadoUtc >= AceptadoUtc
            AND MotivoRevocacion IN ('paciente', 'baja-whatsapp', 'telefono-cambiado', 'texto-nuevo')))
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_ConsentimientoWhatsApp_Paciente')
    ALTER TABLE ConsentimientoWhatsApp ADD CONSTRAINT FK_ConsentimientoWhatsApp_Paciente
        FOREIGN KEY (IdPaciente) REFERENCES Paciente(IdPaciente);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_ConsentimientoWhatsApp_Usuario')
    ALTER TABLE ConsentimientoWhatsApp ADD CONSTRAINT FK_ConsentimientoWhatsApp_Usuario
        FOREIGN KEY (IdUsuario) REFERENCES Usuario(IdUsuario);
GO

-- A lo sumo UNA aceptación abierta (sin revocar) por paciente.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UQ_ConsentimientoWhatsApp_Abierto' AND object_id = OBJECT_ID('dbo.ConsentimientoWhatsApp'))
    CREATE UNIQUE INDEX UQ_ConsentimientoWhatsApp_Abierto
        ON ConsentimientoWhatsApp (IdPaciente) WHERE RevocadoUtc IS NULL;
GO

-- La consulta de siempre es "la última fila de este paciente".
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_ConsentimientoWhatsApp_Paciente' AND object_id = OBJECT_ID('dbo.ConsentimientoWhatsApp'))
    CREATE INDEX IX_ConsentimientoWhatsApp_Paciente
        ON ConsentimientoWhatsApp (IdPaciente, IdConsentimientoWhatsApp);
GO
