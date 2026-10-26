-- Primeros pasos del paciente (octubre 2026). Es la migración 31. Aditiva, idempotente y en una sola transacción.
-- Requiere la 18 (la tabla VersionEsquema) y las tablas InformeAdmision, Consentimiento y Notificacion, nada más.
--
--   sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i migrations/add_primeros_pasos.sql
--
-- La API de esta versión la NECESITA (lee y escribe estas columnas): aplicarla ANTES de publicar la API. La API anterior las ignora.
--
-- Deshacer (con la API anterior otra vez publicada):
--   docs/produccion/deshacer-migracion-31.sql
--
-- Qué hace:
--  1. InformeAdmision: la revisión de la recepcionista (AclaracionRecepcion = qué falta cuando se devuelve; IdUsuarioRevision y FechaRevision =
--     quién y cuándo aceptó, devolvió o cerró con «otro recurso») y las dos preferencias nuevas del paciente (HorarioPreferido y
--     ProfesionalesPreferidos, ambos JSON en texto; por ahora solo se guardan y se muestran, el cruce con las agendas es un trabajo siguiente).
--  2. Consentimiento: FechaRevocacion e IdUsuarioRevocacion. Revocar NO borra la fila: la deja dada de baja (Estado 'Inactivo', como siempre) y
--     con la constancia de cuándo y quién la revocó.
--  3. Notificacion.Evento (NVARCHAR(100) NULL) y un índice único filtrado (IdPaciente, Evento): un mismo evento (por ejemplo «turno 123 propuesto»)
--     no puede crear dos avisos. Los avisos que ya existen quedan con Evento en NULL y no se tocan.
--  4. Registra la versión 31 en VersionEsquema, con Origen 'Script' como las anteriores.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.VersionEsquema', 'U') IS NULL OR OBJECT_ID('dbo.InformeAdmision', 'U') IS NULL
   OR OBJECT_ID('dbo.Consentimiento', 'U') IS NULL OR OBJECT_ID('dbo.Notificacion', 'U') IS NULL
    RAISERROR(N'Falta VersionEsquema, InformeAdmision, Consentimiento o Notificacion: aplicá antes las migraciones anteriores (ver docs/CATALOGO_SCRIPTS_SQL.md).', 16, 1);
GO

BEGIN TRY
    BEGIN TRANSACTION;

    -- ── 1) Revisión del formulario y preferencias ─────────────
    IF COL_LENGTH('dbo.InformeAdmision', 'AclaracionRecepcion') IS NULL ALTER TABLE dbo.InformeAdmision ADD AclaracionRecepcion NVARCHAR(1000) NULL;
    IF COL_LENGTH('dbo.InformeAdmision', 'IdUsuarioRevision') IS NULL ALTER TABLE dbo.InformeAdmision ADD IdUsuarioRevision INT NULL;
    IF COL_LENGTH('dbo.InformeAdmision', 'FechaRevision') IS NULL ALTER TABLE dbo.InformeAdmision ADD FechaRevision DATETIME NULL;
    IF COL_LENGTH('dbo.InformeAdmision', 'HorarioPreferido') IS NULL ALTER TABLE dbo.InformeAdmision ADD HorarioPreferido NVARCHAR(1000) NULL;
    IF COL_LENGTH('dbo.InformeAdmision', 'ProfesionalesPreferidos') IS NULL ALTER TABLE dbo.InformeAdmision ADD ProfesionalesPreferidos NVARCHAR(500) NULL;

    -- ── 2) Constancia de la revocación ────────────────────────
    IF COL_LENGTH('dbo.Consentimiento', 'FechaRevocacion') IS NULL ALTER TABLE dbo.Consentimiento ADD FechaRevocacion DATETIME NULL;
    IF COL_LENGTH('dbo.Consentimiento', 'IdUsuarioRevocacion') IS NULL ALTER TABLE dbo.Consentimiento ADD IdUsuarioRevocacion INT NULL;

    -- ── 3) Avisos idempotentes por evento ─────────────────────
    IF COL_LENGTH('dbo.Notificacion', 'Evento') IS NULL ALTER TABLE dbo.Notificacion ADD Evento NVARCHAR(100) NULL;
    -- En un EXEC: la columna recién agregada no existe todavía cuando se compila este lote.
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Notificacion_Evento' AND object_id = OBJECT_ID('dbo.Notificacion'))
        EXEC(N'CREATE UNIQUE INDEX UX_Notificacion_Evento ON dbo.Notificacion (IdPaciente, Evento) WHERE Evento IS NOT NULL;');

    -- ── 4) Registro de versión ────────────────────────────────
    IF NOT EXISTS (SELECT 1 FROM dbo.VersionEsquema WHERE Numero = 31)
        INSERT INTO dbo.VersionEsquema (Numero, Archivo, Origen) VALUES (31, N'add_primeros_pasos.sql', 'Script');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO

PRINT N'Migración 31 aplicada.';
EXEC(N'SELECT COUNT(*) AS informes, SUM(CASE WHEN Estado = N''Pendiente'' THEN 1 ELSE 0 END) AS pendientes FROM dbo.InformeAdmision');
GO
