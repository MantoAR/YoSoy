/* ============================================================================
   Corregir la especialidad de Martín Sosa (rama Cambios-Agus, Fase 1, Paso 5,
   bug 6)
   ----------------------------------------------------------------------------
   Qué pasó: en la base local, el profesional 3 (Martín Sosa, MP-20003) figura
   con IdEspecialidad = 2 ("Psiquiatría"), que el pedido de la Fase 1 marcó
   como "fuera de alcance" para esta plataforma. Es un dato de prueba mal
   cargado, no algo que dependa de código: no se toca sin que alguien del
   equipo confirme cuál es la especialidad real de Martín Sosa.

   Especialidades que existen hoy (tabla Especialidad):
     1 = Psicología            (Santiago Manto, Laura Giménez, Ángeles Muñoz)
     2 = Psiquiatría           (Martín Sosa — el que se corrige acá)
     3 = Trabajo Social        (Sebastián O'Brien)
     4 = Terapia Ocupacional   (Carolina Ferreyra)

   Este script NO decide el valor correcto: la variable @IdEspecialidadNueva
   de abajo arranca en NULL a propósito, así que el script corta antes de
   tocar nada hasta que alguien la complete a mano. Como referencia, el caso
   más probable es 1 (Psicología) — es la especialidad de casi todo el
   equipo y la que usa el texto de consentimiento ("atención psicológica por
   telemedicina", Pantalla_Paciente/consentimiento.html) — pero es una
   conjetura, no una confirmación: hay que verificarlo con el equipo antes de
   completar la variable.

   Martín Sosa (profesional 3) no tiene turnos cargados (Turno.IdProfesional
   = 3 da 0 filas), así que el cambio queda aislado a esa única fila de
   Profesional.

   NO se ejecutó. Es para la base LOCAL (yosoy), no para producción.

   Cómo usarlo:
     - Tal como está, @IdEspecialidadNueva es NULL: el script corta con un
       error (RAISERROR) antes de abrir la transacción, no cambia nada.
     - Para aplicarlo: completar @IdEspecialidadNueva con el id correcto de
       la tabla Especialidad de arriba, y cambiar el ROLLBACK del final por
       COMMIT.
     - sqlcmd -S "DESKTOP-76GUM00\SQLEXPRESS" -E -C -d yosoy -f 65001 -i docs\corregir-especialidad-martin-sosa.sql
   ============================================================================ */

SET NOCOUNT ON;
SET XACT_ABORT ON;      -- cualquier error deshace toda la transacción

DECLARE @IdEspecialidadNueva INT = NULL;   -- completar a mano (ver arriba) antes de aplicar

IF @IdEspecialidadNueva IS NULL
BEGIN
    RAISERROR(N'Falta completar @IdEspecialidadNueva con la especialidad correcta de Martín Sosa (ver comentario arriba). No se tocó nada.', 16, 1);
    RETURN;
END

BEGIN TRY
    BEGIN TRANSACTION;

    -- Estado antes --------------------------------------------------------
    SELECT N'antes' AS momento, p.IdProfesional, p.Nombre, p.Apellido, p.IdEspecialidad, e.Nombre AS Especialidad
    FROM dbo.Profesional p
    JOIN dbo.Especialidad e ON e.IdEspecialidad = p.IdEspecialidad
    WHERE p.IdProfesional = 3;

    -- Solo toca a Martín Sosa, y solo si sigue en Psiquiatría (2): si alguien
    -- ya lo corrigió o el dato cambió, no se asume nada y se corta.
    UPDATE dbo.Profesional
    SET IdEspecialidad = @IdEspecialidadNueva
    WHERE IdProfesional = 3
      AND Nombre = N'Martín'
      AND Apellido = N'Sosa'
      AND IdEspecialidad = 2;
    IF @@ROWCOUNT <> 1
        THROW 50001, N'Se esperaba corregir exactamente 1 fila (profesional 3, Martín Sosa, antes en Psiquiatría).', 1;

    -- Estado después --------------------------------------------------------
    SELECT N'después' AS momento, p.IdProfesional, p.Nombre, p.Apellido, p.IdEspecialidad, e.Nombre AS Especialidad
    FROM dbo.Profesional p
    JOIN dbo.Especialidad e ON e.IdEspecialidad = p.IdEspecialidad
    WHERE p.IdProfesional = 3;

    -- Por defecto NO se aplica nada. Para aplicar, cambiar esta línea por:
    --     COMMIT TRANSACTION;
    ROLLBACK TRANSACTION;
    PRINT N'Se hizo ROLLBACK: no se cambió nada. Para aplicar, completar @IdEspecialidadNueva y cambiar ROLLBACK por COMMIT al final del script.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT N'No se aplicó nada: ' + ERROR_MESSAGE();
    THROW;
END CATCH;
