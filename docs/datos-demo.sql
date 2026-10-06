/* ============================================================================
   Datos de demo (rama Cambios-Agus) — NO EJECUTADO, para que Agustín lo revise.
   ----------------------------------------------------------------------------
   Completa lo que hoy está vacío en la base local para que el guion de
   docs/DEMO.md tenga algo que mostrar en cada pantalla, sin inventar texto
   clínico: un paciente de prueba obviamente ficticio, un caso pendiente de
   confirmar (reproduce a mano lo que haría AsignacionService.SugerirDerivacion,
   sin correr la cascada real — el profesional sugerido es fijo, no calculado),
   y una conversación existente marcada como escalada.

   REQUIERE haber aplicado antes docs/datos-prueba-grupos-fase2.sql (con COMMIT,
   no con el ROLLBACK que trae por defecto) — este script revisa que exista el
   grupo "Psicología clínica" y frena si no.

   No se tocan: usuarios, contraseñas, Profesional.Slug/LinkPublicoActivo (eso
   se activa desde la propia pantalla, ver docs/DEMO.md), ni ningún paciente o
   turno real ya existente — excepto la conversación que se marca como
   escalada (es un UPDATE de un campo de estado, no texto nuevo).

   Cómo usarlo:
     - Tal como está, termina con ROLLBACK: se puede correr para ver qué haría
       (los SELECT finales muestran el resultado) sin cambiar nada.
     - Para aplicarlo, reemplazar el ROLLBACK del final por COMMIT.
     - sqlcmd -S "DESKTOP-76GUM00\SQLEXPRESS" -E -C -d yosoy -f 65001 -i docs\datos-demo.sql

   Para volver atrás: docs/reset-demo.sql.
   ============================================================================ */

SET QUOTED_IDENTIFIER ON;   -- lo pide el índice único filtrado de Caso.IdInforme
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @IdGrupoClinica INT = (SELECT IdGrupo FROM Grupo WHERE Nombre = N'Psicología clínica');
    IF @IdGrupoClinica IS NULL
        THROW 50001, N'No existe el grupo "Psicología clínica" — aplicar antes docs/datos-prueba-grupos-fase2.sql (con COMMIT).', 1;

    IF EXISTS (SELECT 1 FROM Paciente WHERE Nombre = N'Paciente de Prueba')
        THROW 50002, N'Ya existe un paciente "Paciente de Prueba" — revisar antes de volver a correr esto (¿ya se aplicó?).', 1;

    -- Paciente nuevo, obviamente ficticio, solo para que haya un caso en la cola.
    -- Sin Usuario asociado: no hace falta loguearse como él para el guion.
    DECLARE @IdPacienteDemo INT;
    INSERT INTO Paciente (Nombre, Dni, FechaNacimiento, Domicilio, Telefono, Mail, Estado) VALUES
        (N'Paciente de Prueba', N'00000000', '1990-01-01', N'Calle Demo 123', N'1100000000', N'paciente.demo@ejemplo.com', N'Activo');
    SET @IdPacienteDemo = SCOPE_IDENTITY();

    -- Caso + CasoDerivacion pendiente — mismos valores que deja
    -- AsignacionService.SugerirDerivacion para una sugerencia automática
    -- (Origen 'Regla', Estado 'Pendiente de confirmar', Motivo 'AutomaticaPorCarga'),
    -- escrito a mano porque este script no corre la cascada real.
    DECLARE @IdCasoDemo INT;
    INSERT INTO Caso (IdPaciente, Estado, IdGrupoActual, Origen, FechaCreacion) VALUES
        (@IdPacienteDemo, N'Derivado', @IdGrupoClinica, N'Admision', GETDATE());
    SET @IdCasoDemo = SCOPE_IDENTITY();

    INSERT INTO CasoDerivacion (IdCaso, IdGrupo, IdProfesionalSugerido, Origen, Estado, Motivo, FechaCreacion) VALUES
        (@IdCasoDemo, @IdGrupoClinica, 1, N'Regla', N'Pendiente de confirmar', N'AutomaticaPorCarga', GETDATE());  -- 1 = Santiago Manto

    -- Conversación existente de paciente1 (Juan García, la que ya tiene turnos
    -- en la agenda) marcada como escalada — solo cambia el Estado, no agrega
    -- mensajes ni texto nuevo.
    IF NOT EXISTS (SELECT 1 FROM Conversacion WHERE IdConversacion = 1)
        THROW 50003, N'No existe la conversación #1 — revisar antes de aplicar (puede que la base cambió).', 1;
    UPDATE Conversacion SET Estado = N'DerivadaHumano' WHERE IdConversacion = 1;

    SELECT 'Paciente demo' AS Que, IdPaciente, Nombre FROM Paciente WHERE IdPaciente = @IdPacienteDemo;
    SELECT 'Caso demo' AS Que, c.IdCaso, c.Estado, g.Nombre AS Grupo FROM Caso c JOIN Grupo g ON g.IdGrupo = c.IdGrupoActual WHERE c.IdCaso = @IdCasoDemo;
    SELECT 'CasoDerivacion demo' AS Que, IdCasoDerivacion, Estado, Motivo FROM CasoDerivacion WHERE IdCaso = @IdCasoDemo;
    SELECT 'Conversación escalada' AS Que, IdConversacion, IdPaciente, Estado FROM Conversacion WHERE IdConversacion = 1;

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
