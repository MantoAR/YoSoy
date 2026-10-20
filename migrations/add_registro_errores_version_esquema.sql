-- Migración 18: versión del esquema y registro de errores con código.
-- Idempotente: se puede correr más de una vez sin error ni datos duplicados. Solo AGREGA dos tablas, dos índices y filas
-- en la tabla nueva: no modifica ni borra ningún dato existente ni toca ninguna otra tabla.
-- Origen: rama nuevo-circuito1 de Alearmas01 (su migración "16", partes A y B), separada y renumerada; el resto de aquella
-- migración (roles, permisos, especialidades) NO está en esta.
--
-- Qué agrega:
--   VersionEsquema  Una fila por migración aplicada. Las 1 a 17 no se registraban: se detectan al final de este script por
--                   un objeto que cada una crea (los mismos marcadores que usa Services/VerificadorEsquema.cs; si se cambia
--                   uno, cambiar el otro).
--   LogError        Cada error de la API queda acá con un código corto que el usuario ve en pantalla ("Código: 7F3A-91C2"):
--                       SELECT * FROM LogError WHERE Codigo = '7F3A-91C2';
--                   Nunca se guarda el cuerpo del pedido ni texto clínico; los números largos (DNI, CUIL, teléfonos) y los
--                   correos se enmascaran antes de guardar (Services/RegistroErrores.cs). Sin clave foránea a Usuario a
--                   propósito: registrar un error nunca puede fallar por otro.
--
-- Cuándo aplicarla: ANTES de publicar la API de la etapa 1. La API vieja no conoce estas tablas y sigue andando; la nueva
-- guarda los errores en LogError y, si la tabla no existe, solo en el archivo logs/ y en el log del servicio (no se cae).
-- Con sqlcmd se corre así:
--   sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i migrations\add_registro_errores_version_esquema.sql
--
-- Verificar (tiene que dar 2 y 1):
--   SELECT (SELECT COUNT(*) FROM sys.tables WHERE name IN ('VersionEsquema', 'LogError')) AS tablas,
--          (SELECT COUNT(*) FROM VersionEsquema WHERE Numero = 18) AS registrada;
--
-- Deshacer: primero la migración 19 si está aplicada (sus triggers protegen LogError), después
--   DROP TABLE dbo.LogError; DROP TABLE dbo.VersionEsquema;
-- Se pierde lo registrado ahí; ningún dato de negocio.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- ── 1) Versión de esquema ───────────────────────────────────────────────────
IF OBJECT_ID('dbo.VersionEsquema', 'U') IS NULL
CREATE TABLE VersionEsquema (
    Numero          INT           NOT NULL PRIMARY KEY,
    Archivo         NVARCHAR(200) NOT NULL,
    FechaAplicacion DATETIME2(0)  NOT NULL CONSTRAINT DF_VersionEsquema_Fecha DEFAULT SYSDATETIME(),
    Origen          NVARCHAR(20)  NOT NULL CONSTRAINT DF_VersionEsquema_Origen DEFAULT 'Script'
        CONSTRAINT CK_VersionEsquema_Origen CHECK (Origen IN ('Script', 'Detectada'))
);
GO

-- ── 2) Registro de errores ──────────────────────────────────────────────────
IF OBJECT_ID('dbo.LogError', 'U') IS NULL
CREATE TABLE LogError (
    IdLogError  BIGINT         IDENTITY(1,1) PRIMARY KEY,
    Codigo      CHAR(9)        NOT NULL,
    FechaHora   DATETIME2(3)   NOT NULL CONSTRAINT DF_LogError_Fecha DEFAULT SYSDATETIME(),
    Metodo      NVARCHAR(10)   NULL,
    Ruta        NVARCHAR(300)  NULL,
    StatusCode  INT            NULL,
    IdUsuario   INT            NULL,
    Rol         NVARCHAR(30)   NULL,
    TipoError   NVARCHAR(200)  NULL,
    Mensaje     NVARCHAR(1000) NULL,
    Detalle     NVARCHAR(MAX)  NULL,
    VersionApi  NVARCHAR(50)   NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.LogError') AND name = 'UX_LogError_Codigo')
    CREATE UNIQUE INDEX UX_LogError_Codigo ON LogError(Codigo);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.LogError') AND name = 'IX_LogError_Fecha')
    CREATE INDEX IX_LogError_Fecha ON LogError(FechaHora DESC);
GO

-- ── 3) Registro de las versiones anteriores y de esta ───────────────────────
-- Las 1 a 17 se registran como 'Detectada' si su marcador está presente; si alguna no aparece, este script no la inventa:
-- queda sin registrar. La 16 (consentimiento de WhatsApp, dormido) y la 17 (chat del personal, solo la lee el modo Personal)
-- son opcionales para la API: se registran si están, y su ausencia no se informa como falta.
DECLARE @Detectadas TABLE (Numero INT, Archivo NVARCHAR(200), Presente BIT);
INSERT INTO @Detectadas (Numero, Archivo, Presente)
SELECT 1, N'add_columnas_faltantes.sql',
       CASE WHEN COL_LENGTH('dbo.Turno', 'CodigoSala') IS NOT NULL AND COL_LENGTH('dbo.PacienteProfesional', 'EstadoAsignacion') IS NOT NULL THEN 1 ELSE 0 END
UNION ALL
SELECT 2, N'add_creadopor_turno.sql',
       CASE WHEN COL_LENGTH('dbo.Turno', 'CreadoPor') IS NOT NULL THEN 1 ELSE 0 END
UNION ALL
SELECT 3, N'add_token_profesional.sql',
       CASE WHEN COL_LENGTH('dbo.Profesional', 'Token') IS NOT NULL THEN 1 ELSE 0 END
UNION ALL
SELECT 4, N'add_token_recuperacion.sql',
       CASE WHEN COL_LENGTH('dbo.RecuperacionPassword', 'TokenExpiracion') IS NOT NULL THEN 1 ELSE 0 END
UNION ALL
SELECT 5, N'add_mensajeria.sql',
       CASE WHEN OBJECT_ID('dbo.Conversacion', 'U') IS NOT NULL AND OBJECT_ID('dbo.PreguntaFrecuente', 'U') IS NOT NULL THEN 1 ELSE 0 END
UNION ALL
SELECT 6, N'add_consentimiento_telemedicina.sql',
       CASE WHEN EXISTS (SELECT 1 FROM TipoConsentimiento WHERE Descripcion = N'Consentimiento Informado - Atención por Telemedicina') THEN 1 ELSE 0 END
UNION ALL
SELECT 7, N'add_lectura_conversacion.sql',
       CASE WHEN COL_LENGTH('dbo.Conversacion', 'IdUltimoMensajeLeido') IS NOT NULL THEN 1 ELSE 0 END
UNION ALL
SELECT 8, N'alter_logauditoria_idusuario_nullable.sql',
       CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.LogAuditoria') AND name = 'IdUsuario' AND is_nullable = 1) THEN 1 ELSE 0 END
UNION ALL
SELECT 9, N'add_tablas_sin_migracion_previa.sql',
       CASE WHEN OBJECT_ID('dbo.InformeAdmision', 'U') IS NOT NULL AND OBJECT_ID('dbo.Notificacion', 'U') IS NOT NULL AND OBJECT_ID('dbo.SesionClinica', 'U') IS NOT NULL THEN 1 ELSE 0 END
UNION ALL
SELECT 10, N'add_grupos_casos_derivacion.sql',
       CASE WHEN OBJECT_ID('dbo.Caso', 'U') IS NOT NULL AND OBJECT_ID('dbo.ReglaDerivacion', 'U') IS NOT NULL THEN 1 ELSE 0 END
UNION ALL
SELECT 11, N'add_link_publico_profesional.sql',
       CASE WHEN COL_LENGTH('dbo.Profesional', 'Slug') IS NOT NULL THEN 1 ELSE 0 END
UNION ALL
SELECT 12, N'alter_casoderivacion_motivo_cerrado.sql',
       CASE WHEN COL_LENGTH('dbo.CasoDerivacion', 'Nota') IS NOT NULL THEN 1 ELSE 0 END
UNION ALL
SELECT 13, N'add_chats_notas_etiquetas_respuestas.sql',
       CASE WHEN OBJECT_ID('dbo.RespuestaRapida', 'U') IS NOT NULL THEN 1 ELSE 0 END
UNION ALL
SELECT 14, N'add_conversacion_asignacion.sql',
       CASE WHEN COL_LENGTH('dbo.Conversacion', 'IdProfesionalAsignado') IS NOT NULL THEN 1 ELSE 0 END
UNION ALL
SELECT 15, N'add_consentimiento_video.sql',
       CASE WHEN OBJECT_ID('dbo.ConsentimientoVideo', 'U') IS NOT NULL AND COL_LENGTH('dbo.SesionClinica', 'IdConsentimientoVideo') IS NOT NULL THEN 1 ELSE 0 END
UNION ALL
SELECT 16, N'add_consentimiento_whatsapp.sql',
       CASE WHEN OBJECT_ID('dbo.ConsentimientoWhatsApp', 'U') IS NOT NULL THEN 1 ELSE 0 END
UNION ALL
SELECT 17, N'add_chat_personal_entrantes.sql',
       CASE WHEN COL_LENGTH('dbo.MensajeChat', 'IdExterno') IS NOT NULL AND COL_LENGTH('dbo.MensajeChat', 'Tipo') IS NOT NULL AND COL_LENGTH('dbo.Conversacion', 'NombreContacto') IS NOT NULL THEN 1 ELSE 0 END;

INSERT INTO VersionEsquema (Numero, Archivo, Origen)
SELECT d.Numero, d.Archivo, 'Detectada'
FROM @Detectadas d
WHERE d.Presente = 1 AND NOT EXISTS (SELECT 1 FROM VersionEsquema v WHERE v.Numero = d.Numero);

IF NOT EXISTS (SELECT 1 FROM VersionEsquema WHERE Numero = 18)
    INSERT INTO VersionEsquema (Numero, Archivo, Origen) VALUES (18, N'add_registro_errores_version_esquema.sql', 'Script');
GO

-- ── Resultado (para leer en la salida de sqlcmd o en SSMS) ──────────────────
PRINT N'Migración 18 aplicada.';

-- Migraciones obligatorias anteriores que no se detectaron: hay que correrlas. Tiene que salir vacío.
SELECT d.Numero, d.Archivo AS MigracionFaltante
FROM (VALUES (1, N'add_columnas_faltantes.sql'),
             (2, N'add_creadopor_turno.sql'),
             (3, N'add_token_profesional.sql'),
             (4, N'add_token_recuperacion.sql'),
             (5, N'add_mensajeria.sql'),
             (6, N'add_consentimiento_telemedicina.sql'),
             (7, N'add_lectura_conversacion.sql'),
             (8, N'alter_logauditoria_idusuario_nullable.sql'),
             (9, N'add_tablas_sin_migracion_previa.sql'),
             (10, N'add_grupos_casos_derivacion.sql'),
             (11, N'add_link_publico_profesional.sql'),
             (12, N'alter_casoderivacion_motivo_cerrado.sql'),
             (13, N'add_chats_notas_etiquetas_respuestas.sql'),
             (14, N'add_conversacion_asignacion.sql'),
             (15, N'add_consentimiento_video.sql')) AS d(Numero, Archivo)
WHERE NOT EXISTS (SELECT 1 FROM VersionEsquema v WHERE v.Numero = d.Numero);

SELECT Numero, Archivo, Origen FROM VersionEsquema ORDER BY Numero;
GO
