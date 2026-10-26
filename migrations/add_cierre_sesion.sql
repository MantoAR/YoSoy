-- Cierre de sesion (octubre 2026). Es la migracion 26. Aditiva, idempotente y en una sola transaccion.
-- Requiere la 18 (la tabla VersionEsquema) y las tablas SesionClinica y Evolucion, nada mas.
--
--   sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i migrations/add_cierre_sesion.sql
--
-- Es OPCIONAL: la API de esta version anda sin ella para los pedidos de siempre (guardar una sesion sin los campos nuevos). Si el cliente manda claveCierre o confirmaRealizacion,
-- la API responde 409 "falta-migracion-26" en vez de fallar. Se puede aplicar antes o despues de publicar la API; para que la sala nueva funcione tiene que estar aplicada.
--
-- Deshacer (la API anterior ignora las columnas; esto solo hace falta si se quiere sacarlas):
--   docs/produccion/deshacer-migracion-26.sql
--
-- Que hace:
--  1. SesionClinica.ClaveCierre UNIQUEIDENTIFIER NULL: la clave que genera la sala para un cierre. Repetir el mismo cierre (corte de red, doble clic) con la misma clave no
--     duplica nada. Indice unico FILTRADO por (IdProfesional, ClaveCierre): la base lo garantiza aunque dos pedidos lleguen a la vez.
--  2. SesionClinica.TurnoRealizadoUtc DATETIME2(3) NULL: el momento (UTC) en que ESTE cierre paso el turno a Realizado. Queda NULL si el cierre no toco el turno
--     (no se realizo, ya estaba Realizado, o el profesional no lo confirmo). Solo trazabilidad: ninguna logica lo lee para decidir.
--  3. SesionClinica.IdEvolucion INT NULL: el vinculo (manual, hecho por el profesional) entre la sesion y una evolucion de la historia clinica. Va de este lado a proposito: asi
--     no se toca la tabla Evolucion (una evolucion firmada es inmutable). FK a Evolucion e indice unico filtrado: una evolucion se vincula a lo sumo con una sesion.
--  4. IX_SesionClinica_Turno (IdTurno) filtrado: para buscar la sesion de un turno. NO es unico: puede haber turnos con mas de una sesion de antes (la API no validaba el turno).
--  5. Registra la version 26 en VersionEsquema, con Origen 'Script' como las anteriores.
-- No toca ninguna fila existente (todo queda en NULL).

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.VersionEsquema', 'U') IS NULL OR OBJECT_ID('dbo.SesionClinica', 'U') IS NULL OR OBJECT_ID('dbo.Evolucion', 'U') IS NULL
    RAISERROR(N'Falta VersionEsquema, SesionClinica o Evolucion: aplica antes las migraciones anteriores (ver docs/CATALOGO_SCRIPTS_SQL.md).', 16, 1);
GO

BEGIN TRY
    BEGIN TRANSACTION;

    -- 1) Las columnas
    IF COL_LENGTH('dbo.SesionClinica', 'ClaveCierre') IS NULL
        ALTER TABLE dbo.SesionClinica ADD ClaveCierre UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH('dbo.SesionClinica', 'TurnoRealizadoUtc') IS NULL
        ALTER TABLE dbo.SesionClinica ADD TurnoRealizadoUtc DATETIME2(3) NULL;
    IF COL_LENGTH('dbo.SesionClinica', 'IdEvolucion') IS NULL
        ALTER TABLE dbo.SesionClinica ADD IdEvolucion INT NULL;

    -- 2) Claves foraneas e indices. Van en EXEC porque el lote se compila antes de que las columnas existan.
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_SesionClinica_Evolucion')
        EXEC(N'ALTER TABLE dbo.SesionClinica ADD CONSTRAINT FK_SesionClinica_Evolucion FOREIGN KEY (IdEvolucion) REFERENCES dbo.Evolucion(IdEvolucion)');
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_SesionClinica_Clave' AND object_id = OBJECT_ID('dbo.SesionClinica'))
        EXEC(N'CREATE UNIQUE INDEX UX_SesionClinica_Clave ON dbo.SesionClinica (IdProfesional, ClaveCierre) WHERE ClaveCierre IS NOT NULL');
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_SesionClinica_Evolucion' AND object_id = OBJECT_ID('dbo.SesionClinica'))
        EXEC(N'CREATE UNIQUE INDEX UX_SesionClinica_Evolucion ON dbo.SesionClinica (IdEvolucion) WHERE IdEvolucion IS NOT NULL');
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SesionClinica_Turno' AND object_id = OBJECT_ID('dbo.SesionClinica'))
        EXEC(N'CREATE INDEX IX_SesionClinica_Turno ON dbo.SesionClinica (IdTurno) WHERE IdTurno IS NOT NULL');

    -- 3) Registro de version
    IF NOT EXISTS (SELECT 1 FROM dbo.VersionEsquema WHERE Numero = 26)
        INSERT INTO dbo.VersionEsquema (Numero, Archivo, Origen) VALUES (26, N'add_cierre_sesion.sql', 'Script');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO

PRINT N'Migracion 26 aplicada.';
SELECT COUNT(*) AS sesiones, SUM(CASE WHEN ClaveCierre IS NULL THEN 0 ELSE 1 END) AS con_clave FROM dbo.SesionClinica;
GO
