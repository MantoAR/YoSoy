-- Chat de WhatsApp para el personal: lo que hace falta para guardar completos los mensajes que entran.
-- Idempotente: se puede correr más de una vez sin error ni datos duplicados. Solo AGREGA tres columnas, una restricción y un índice: no modifica ni borra ningún
-- dato existente (los mensajes que ya hay quedan con Tipo 'texto' y sin IdExterno), no toca ninguna otra columna ni tabla y no crea tablas.
--
-- Qué agrega:
--   MensajeChat.IdExterno        NVARCHAR(64) NULL                       El id del mensaje en WhatsApp. Con el índice único filtrado de abajo, el mismo mensaje no se
--                                                                        guarda dos veces en una conversación (whatsapp-service reintenta si la API falla y WhatsApp
--                                                                        puede entregar dos veces lo mismo). NULL en los mensajes que no vienen de WhatsApp.
--   MensajeChat.Tipo             NVARCHAR(12) NOT NULL DEFAULT 'texto'   texto, audio, imagen, video, documento, sticker, ubicacion, contacto u otro. Lo que no es
--                                                                        texto se guarda como un AVISO del tipo en Texto ("[Imagen]", con la leyenda a continuación si
--                                                                        la trae): el archivo NO se guarda ni se descarga.
--   Conversacion.NombreContacto  NVARCHAR(100) NULL                      El nombre del perfil de WhatsApp de quien escribe. Solo se guarda acá: no va a los logs.
--
-- Cuándo aplicarla: ANTES de pasar la API a Mensajeria__ModoAtencion=Personal. Con el modo Asistente (el valor por defecto) la API no lee ni escribe ninguna de estas
-- columnas, así que se puede publicar la API sin haberla aplicado. Con Personal y sin ella, la API sigue guardando los mensajes, pero sin id, sin tipo y sin nombre
-- (no detecta un mensaje repetido) y lo avisa en el log. Es independiente de la migración 16.
-- Con sqlcmd se corre así:
--   sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -f 65001 -i migrations\add_chat_personal_entrantes.sql
--
-- Verificar (tiene que dar 3, 1, 1 y 1: las tres columnas, la restricción del tipo, su valor por defecto y el índice):
--   SELECT (SELECT COUNT(*) FROM sys.columns WHERE (object_id = OBJECT_ID('dbo.MensajeChat') AND name IN ('IdExterno', 'Tipo'))
--                                               OR (object_id = OBJECT_ID('dbo.Conversacion') AND name = 'NombreContacto')) AS columnas,
--          (SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_MensajeChat_Tipo' AND parent_object_id = OBJECT_ID('dbo.MensajeChat')) AS restriccion,
--          (SELECT COUNT(*) FROM sys.default_constraints WHERE name = 'DF_MensajeChat_Tipo' AND parent_object_id = OBJECT_ID('dbo.MensajeChat')) AS valor_por_defecto,
--          (SELECT COUNT(*) FROM sys.indexes WHERE name = 'UQ_MensajeChat_IdExterno' AND object_id = OBJECT_ID('dbo.MensajeChat')) AS indice;
--
-- Deshacer: no hace falta para volver a la API anterior, que no conoce las columnas y las ignora (el valor por defecto de Tipo hace que sus INSERT sigan andando). Si de
-- verdad hay que sacarlas, antes un backup de la base y después, en este orden: DROP INDEX UQ_MensajeChat_IdExterno ON dbo.MensajeChat; ALTER TABLE dbo.MensajeChat DROP
-- CONSTRAINT CK_MensajeChat_Tipo; ALTER TABLE dbo.MensajeChat DROP CONSTRAINT DF_MensajeChat_Tipo; ALTER TABLE dbo.MensajeChat DROP COLUMN IdExterno, Tipo; ALTER TABLE
-- dbo.Conversacion DROP COLUMN NombreContacto. Se pierde el tipo, el id y el nombre de lo ya guardado (el texto y el resto de cada mensaje no).
--
-- QUOTED_IDENTIFIER ON: lo pide el índice único filtrado de más abajo (error 1934 si no), igual que en add_consentimiento_whatsapp.sql. Con -I ya viene así; el SET lo
-- deja a salvo si se olvida.
SET QUOTED_IDENTIFIER ON;
GO

-- ── 1) MensajeChat.IdExterno ────────────────────────────────────────────────
IF COL_LENGTH('dbo.MensajeChat', 'IdExterno') IS NULL
    ALTER TABLE dbo.MensajeChat ADD IdExterno NVARCHAR(64) NULL;
GO

-- ── 2) MensajeChat.Tipo ─────────────────────────────────────────────────────
IF COL_LENGTH('dbo.MensajeChat', 'Tipo') IS NULL
    ALTER TABLE dbo.MensajeChat ADD Tipo NVARCHAR(12) NOT NULL CONSTRAINT DF_MensajeChat_Tipo DEFAULT 'texto';
GO

-- La lista cerrada de tipos. Se agrega en un lote aparte: el de arriba tiene que haber creado la columna antes de que este se compile.
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_MensajeChat_Tipo' AND parent_object_id = OBJECT_ID('dbo.MensajeChat'))
    ALTER TABLE dbo.MensajeChat ADD CONSTRAINT CK_MensajeChat_Tipo
        CHECK (Tipo IN ('texto', 'audio', 'imagen', 'video', 'documento', 'sticker', 'ubicacion', 'contacto', 'otro'));
GO

-- ── 3) Conversacion.NombreContacto ──────────────────────────────────────────
IF COL_LENGTH('dbo.Conversacion', 'NombreContacto') IS NULL
    ALTER TABLE dbo.Conversacion ADD NombreContacto NVARCHAR(100) NULL;
GO

-- ── 4) Un mismo mensaje de WhatsApp no se guarda dos veces en una conversación ─────
-- Filtrado: los mensajes sin id (los de siempre, los del equipo, los del asistente) no cuentan, así que puede haber todos los que haga falta.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_MensajeChat_IdExterno' AND object_id = OBJECT_ID('dbo.MensajeChat'))
    CREATE UNIQUE INDEX UQ_MensajeChat_IdExterno
        ON dbo.MensajeChat (IdConversacion, IdExterno) WHERE IdExterno IS NOT NULL;
GO
