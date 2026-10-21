-- Cambio obligatorio de contraseña al primer ingreso (octubre 2026). Es la migración 24. Aditiva e idempotente.
-- Requiere la 18 (la tabla VersionEsquema) y la columna Usuario, nada más.
--
--   sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i migrations/add_debe_cambiar_password.sql
--
-- Deshacer (la API anterior ignora la columna y sigue andando con la base migrada; esto solo hace falta si se quiere sacar):
--   docs/produccion/deshacer-migracion-24.sql
--
-- Qué hace:
--  1. Usuario.DebeCambiarPassword BIT NOT NULL, por defecto 0. Las cuentas que ya existen quedan en 0 (no se les pide nada).
--     La pone en 1 el Configurador al restablecer la contraseña de alguien (POST /usuarios/{id}/restablecer-password) y la
--     pone en 0 el propio usuario al elegir su clave nueva (POST /sesion/cambiar-password). Mientras vale 1, el ingreso no
--     entrega sesión: devuelve un token corto que solo sirve para elegir la clave nueva.
--  2. Registra la versión 24 en VersionEsquema, con Origen 'Script' como las anteriores.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.VersionEsquema', 'U') IS NULL OR OBJECT_ID('dbo.Usuario', 'U') IS NULL
    RAISERROR(N'Falta VersionEsquema o Usuario: aplicá antes las migraciones anteriores (ver docs/CATALOGO_SCRIPTS_SQL.md).', 16, 1);
GO

-- ── 1) La columna ─────────────────────────────────────────────
IF COL_LENGTH('dbo.Usuario', 'DebeCambiarPassword') IS NULL
    ALTER TABLE Usuario ADD DebeCambiarPassword BIT NOT NULL
        CONSTRAINT DF_Usuario_DebeCambiarPassword DEFAULT (0);
GO

-- ── 2) Registro de versión ────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM VersionEsquema WHERE Numero = 24)
    INSERT INTO VersionEsquema (Numero, Archivo, Origen) VALUES (24, N'add_debe_cambiar_password.sql', 'Script');
GO

PRINT N'Migración 24 aplicada.';
SELECT COUNT(*) AS cuentas, SUM(CAST(DebeCambiarPassword AS INT)) AS con_cambio_pendiente FROM Usuario;
GO
