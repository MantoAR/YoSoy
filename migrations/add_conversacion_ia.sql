-- Chats v2, Fase 1: el interruptor de IA de cada conversación (octubre 2026). Es la migración 25. Aditiva, idempotente y en una sola transacción.
-- Requiere la 18 (la tabla VersionEsquema) y la tabla Conversacion, nada más.
--
--   sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i migrations/add_conversacion_ia.sql
--
-- Es OPCIONAL: la API de esta versión anda sin ella (no lee ni escribe la columna, y el interruptor por conversación responde 409 "falta la migración 25"). Se puede aplicar antes o
-- después de publicar la API. Tampoco hace que nada responda con IA: la IA todavía no existe; esto solo guarda y muestra el interruptor.
--
-- Deshacer (la API anterior ignora la columna; esto solo hace falta si se quiere sacar):
--   docs/produccion/deshacer-migracion-25.sql
--
-- Qué hace:
--  1. Conversacion.IaActiva BIT NOT NULL, por defecto 0 (apagada). Las conversaciones que ya existen quedan en 0 y las nuevas también: la IA se prende conversación por conversación,
--     y solo puede responder si además está prendido el interruptor GLOBAL (la fila 'Chats.IaActiva' de ConfiguracionOrganizacion, que no necesita esquema nuevo y arranca apagada).
--     Agregar una columna NOT NULL con valor por defecto es solo un cambio de metadatos: no recorre la tabla ni la bloquea más que un instante.
--  2. Registra la versión 25 en VersionEsquema, con Origen 'Script' como las anteriores.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.VersionEsquema', 'U') IS NULL OR OBJECT_ID('dbo.Conversacion', 'U') IS NULL
    RAISERROR(N'Falta VersionEsquema o Conversacion: aplicá antes las migraciones anteriores (ver docs/CATALOGO_SCRIPTS_SQL.md).', 16, 1);
GO

BEGIN TRY
    BEGIN TRANSACTION;

    -- ── 1) La columna ─────────────────────────────────────────
    IF COL_LENGTH('dbo.Conversacion', 'IaActiva') IS NULL
        ALTER TABLE dbo.Conversacion ADD IaActiva BIT NOT NULL
            CONSTRAINT DF_Conversacion_IaActiva DEFAULT (0);

    -- ── 2) Registro de versión ────────────────────────────────
    IF NOT EXISTS (SELECT 1 FROM dbo.VersionEsquema WHERE Numero = 25)
        INSERT INTO dbo.VersionEsquema (Numero, Archivo, Origen) VALUES (25, N'add_conversacion_ia.sql', 'Script');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO

PRINT N'Migración 25 aplicada.';
SELECT COUNT(*) AS conversaciones, SUM(CAST(IaActiva AS INT)) AS con_ia_prendida FROM dbo.Conversacion;
GO
