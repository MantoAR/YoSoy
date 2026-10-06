/* ============================================================================
   Revertir datos de demo (rama Cambios-Agus) — NO EJECUTADO, para revisar.
   ----------------------------------------------------------------------------
   Deshace exactamente lo que agregó docs/datos-demo.sql si se aplicó con
   COMMIT: borra el paciente/caso/derivación de prueba y devuelve la
   conversación #1 a "Activa".

   A propósito NO toca los grupos/reglas de docs/datos-prueba-grupos-fase2.sql
   (son infraestructura, no solo datos de demo) ni ningún turno, usuario o
   profesional real. Si en algún momento también se quiere deshacer eso, es
   un DELETE aparte sobre Grupo/ProfesionalGrupo/ReglaDerivacion a mano,
   revisando primero que no haya Casos reales ya enganchados a esos grupos.

   Cómo usarlo:
     - Tal como está, termina con ROLLBACK: muestra qué borraría sin borrar.
     - Para aplicarlo, reemplazar el ROLLBACK del final por COMMIT.
     - sqlcmd -S "DESKTOP-76GUM00\SQLEXPRESS" -E -C -d yosoy -f 65001 -i docs\reset-demo.sql
   ============================================================================ */

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @IdPacienteDemo INT = (SELECT IdPaciente FROM Paciente WHERE Nombre = N'Paciente de Prueba');

    IF @IdPacienteDemo IS NOT NULL
    BEGIN
        DELETE FROM CasoDerivacion WHERE IdCaso IN (SELECT IdCaso FROM Caso WHERE IdPaciente = @IdPacienteDemo);
        DELETE FROM HistorialAsignacion WHERE IdCaso IN (SELECT IdCaso FROM Caso WHERE IdPaciente = @IdPacienteDemo);
        DELETE FROM Caso WHERE IdPaciente = @IdPacienteDemo;
        DELETE FROM Paciente WHERE IdPaciente = @IdPacienteDemo;
    END

    UPDATE Conversacion SET Estado = N'Activa' WHERE IdConversacion = 1 AND Estado = N'DerivadaHumano';

    SELECT 'Paciente demo queda' AS Que, COUNT(*) AS Filas FROM Paciente WHERE Nombre = N'Paciente de Prueba';
    SELECT 'Conversación #1' AS Que, Estado FROM Conversacion WHERE IdConversacion = 1;

    -- Por defecto NO se aplica nada. Para aplicar, cambiar esta línea por:
    --     COMMIT TRANSACTION;
    ROLLBACK TRANSACTION;
    PRINT N'Se hizo ROLLBACK: no se cambió nada. Para aplicar, cambiar ROLLBACK por COMMIT al final del script.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT N'No se aplicó nada: ' + ERROR_MESSAGE();
    THROW;
END CATCH;
