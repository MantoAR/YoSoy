/* ============================================================================
   deshacer-migracion-25.sql — Saca de la base lo que agregó la migración 25 (Conversacion.IaActiva).

   CUÁNDO: casi nunca. Lo normal es dejar la columna (la API anterior la ignora) o volver a la API anterior sin tocar la base.
   Esto solo hace falta si se quiere dejar la base exactamente como antes de la migración, sin restaurar el backup.

   QUÉ PIERDE: el interruptor de IA de cada conversación. Como la IA todavía no responde en ninguna (Fase 1), perder la marca no cambia lo que ve ni recibe nadie: el primer SELECT
   cuenta cuántas conversaciones la tenían prendida, por si querés anotarlo. No toca ningún otro dato (ni el interruptor GLOBAL, que es una fila de ConfiguracionOrganizacion).

   REQUISITOS: la API de esta versión parada o ya reemplazada por la anterior (la nueva consulta la columna por conversación; sin ella sigue andando, pero el interruptor responde 409).
   Todo en una sola transacción: si algo falla, no cambia nada.
     sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i docs/produccion/deshacer-migracion-25.sql
   ============================================================================ */
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH('dbo.Conversacion', 'IaActiva') IS NOT NULL
BEGIN
    PRINT N'Conversaciones con la IA prendida (se pierde la marca):';
    EXEC(N'SELECT COUNT(*) AS con_ia_prendida FROM dbo.Conversacion WHERE IaActiva = 1');
END
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_Conversacion_IaActiva')
        ALTER TABLE dbo.Conversacion DROP CONSTRAINT DF_Conversacion_IaActiva;
    IF COL_LENGTH('dbo.Conversacion', 'IaActiva') IS NOT NULL
        ALTER TABLE dbo.Conversacion DROP COLUMN IaActiva;

    DELETE FROM dbo.VersionEsquema WHERE Numero = 25;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO

PRINT N'Migración 25 deshecha en la base. Falta volver a la API anterior si todavía no se hizo.';
SELECT (SELECT COUNT(*) FROM sys.tables) AS tablas, (SELECT MAX(Numero) FROM VersionEsquema) AS ultima_version;
GO
