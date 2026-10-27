/* Ensayo de la migración 35 sobre una base DESCARTABLE (yosoy_test), nunca sobre yosoy.
     sqlcmd -S TU_SERVIDOR -d yosoy_test -E -C -I -b -f 65001 -i migrations/add_documentos_clinicos.sql
     sqlcmd -S TU_SERVIDOR -d yosoy_test -E -C -I -f 65001 -i Scripts/ensayo_migracion_35.sql
   Cada prueba imprime OK o FALLA. Todo lo que crea lo deshace con ROLLBACK: la base queda como estaba. */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO
IF DB_NAME() = N'yosoy' BEGIN RAISERROR(N'Esto es un ensayo: corrélo en yosoy_test, no en yosoy.', 16, 1); SET NOEXEC ON; END
GO

DECLARE @ok INT;
-- 1) Semilla: 10 plantillas, una versión publicada cada una, todas provisorias.
SELECT @ok = COUNT(*) FROM PlantillaDocumento p JOIN PlantillaDocumentoVersion v ON v.IdPlantilla = p.IdPlantilla
WHERE v.Estado = N'Publicada' AND v.Numero = 1 AND v.Provisoria = 1;
PRINT CASE WHEN @ok = 10 THEN N'OK   1) 10 plantillas publicadas y provisorias' ELSE N'FALLA 1) plantillas publicadas: ' + CAST(@ok AS NVARCHAR) END;

-- 2) Una versión publicada no se modifica.
BEGIN TRY
    BEGIN TRAN;
    UPDATE TOP (1) PlantillaDocumentoVersion SET CuerpoJson = CuerpoJson + N' ' WHERE Estado = N'Publicada';
    PRINT N'FALLA 2) se pudo modificar una versión publicada';
    ROLLBACK;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    PRINT CASE WHEN ERROR_NUMBER() = 50027 THEN N'OK   2) versión publicada protegida (50027)' ELSE N'FALLA 2) ' + ERROR_MESSAGE() END;
END CATCH;

-- 3) Una versión publicada sí puede pasar a Retirada (sin cambiar nada más).
BEGIN TRY
    BEGIN TRAN;
    UPDATE TOP (1) PlantillaDocumentoVersion SET Estado = N'Retirada' WHERE Estado = N'Publicada';
    PRINT N'OK   3) Publicada -> Retirada permitido';
    ROLLBACK;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    PRINT N'FALLA 3) ' + ERROR_MESSAGE();
END CATCH;

-- 4) Una sola publicada por plantilla.
BEGIN TRY
    BEGIN TRAN;
    INSERT INTO PlantillaDocumentoVersion (IdPlantilla, Numero, CamposJson, CuerpoJson, Estado)
    SELECT TOP (1) IdPlantilla, 99, N'[]', N'[]', N'Publicada' FROM PlantillaDocumento;
    PRINT N'FALLA 4) se pudieron publicar dos versiones';
    ROLLBACK;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    PRINT CASE WHEN ERROR_NUMBER() IN (2601, 2627) THEN N'OK   4) una sola publicada por plantilla' ELSE N'FALLA 4) ' + ERROR_MESSAGE() END;
END CATCH;

-- 5 y 6) Un documento emitido no se modifica ni se borra (necesita un paciente con historia clínica y un profesional).
-- Cada prueba en su propia transacción: el error del trigger deja la transacción sin poder confirmarse.
DECLARE @pac INT, @hc INT, @prof INT, @usr INT, @ver INT, @doc INT, @i INT = 1;
SELECT TOP (1) @pac = h.IdPaciente, @hc = h.IdHistoria FROM HistoriaClinica h;
SELECT TOP (1) @prof = IdProfesional FROM Profesional;
SELECT TOP (1) @usr = IdUsuario FROM Usuario;
SELECT TOP (1) @ver = IdVersion FROM PlantillaDocumentoVersion WHERE Estado = N'Publicada';
IF @pac IS NULL OR @prof IS NULL OR @usr IS NULL
    PRINT N'---  5-6) sin paciente con historia clínica o sin profesional en esta base: se saltean';
ELSE
WHILE @i <= 2
BEGIN
    BEGIN TRY
        BEGIN TRAN;
        INSERT INTO DocumentoClinico (IdPaciente, IdHistoria, IdProfesional, IdVersion, DatosJson, ContenidoJson, Pdf, HashSha256, IdUsuarioEmisor)
        VALUES (@pac, @hc, @prof, @ver, N'{}', N'[]', 0x25504446, REPLICATE('0', 64), @usr);
        SET @doc = SCOPE_IDENTITY();
        IF @i = 1 UPDATE DocumentoClinico SET DatosJson = N'{"x":1}' WHERE IdDocumento = @doc;
        ELSE DELETE FROM DocumentoClinico WHERE IdDocumento = @doc;
        PRINT N'FALLA ' + CAST(@i + 4 AS NVARCHAR) + N') se pudo ' + CASE WHEN @i = 1 THEN N'modificar' ELSE N'borrar' END + N' un documento emitido';
        ROLLBACK;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        PRINT CASE WHEN ERROR_NUMBER() = 50028
                   THEN N'OK   ' + CAST(@i + 4 AS NVARCHAR) + N') un documento emitido no se ' + CASE WHEN @i = 1 THEN N'modifica' ELSE N'borra' END + N' (50028)'
                   ELSE N'FALLA ' + CAST(@i + 4 AS NVARCHAR) + N') ' + ERROR_MESSAGE() END;
    END CATCH;
    SET @i += 1;
END
GO
PRINT N'Ensayo terminado. No quedó nada escrito (ROLLBACK).';
GO
