-- Evolucion.TipoCierre (octubre 2026). Es la migración 33. Aditiva, idempotente y en una sola transacción.
-- Requiere la 18 (la tabla VersionEsquema) y la tabla Evolucion, nada más.
--
--   sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i migrations/add_evolucion_tipocierre.sql
--
-- Por qué existe: el camino viejo de PUT /informes-admision/{id}/derivar (el que tiene entrevista) escribe la primera evolución de la historia clínica con una columna
-- Evolucion.TipoCierre que NINGUNA migración creaba ni el esquema consolidado (docs/produccion/01-produccion.sql) traía. En producción esa consulta fallaba con 500.
-- Es OPCIONAL para que la API arranque: sin ella la API escribe la evolución sin ese dato y deriva igual. Con ella, la evolución queda con el tipo de cierre 'Derivación'.
--
-- Deshacer (con la API anterior otra vez publicada):
--   docs/produccion/deshacer-migracion-33.sql
--
-- Qué hace:
--  1. Evolucion.TipoCierre NVARCHAR(50) NULL (mismo tipo que SesionClinica.TipoCierre). Las evoluciones que ya existen quedan con NULL: no se inventa ningún dato.
--  2. Registra la versión 33 en VersionEsquema, con Origen 'Script' como las anteriores.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.VersionEsquema', 'U') IS NULL OR OBJECT_ID('dbo.Evolucion', 'U') IS NULL
    RAISERROR(N'Falta VersionEsquema o Evolucion: aplicá antes las migraciones anteriores (ver docs/CATALOGO_SCRIPTS_SQL.md).', 16, 1);
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH('dbo.Evolucion', 'TipoCierre') IS NULL ALTER TABLE dbo.Evolucion ADD TipoCierre NVARCHAR(50) NULL;

    IF NOT EXISTS (SELECT 1 FROM dbo.VersionEsquema WHERE Numero = 33)
        INSERT INTO dbo.VersionEsquema (Numero, Archivo, Origen) VALUES (33, N'add_evolucion_tipocierre.sql', 'Script');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO

PRINT N'Migración 33 aplicada.';
EXEC(N'SELECT COUNT(*) AS evoluciones FROM dbo.Evolucion');
GO
