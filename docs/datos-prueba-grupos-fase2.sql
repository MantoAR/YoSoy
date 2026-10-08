/* ============================================================================
   Datos de prueba de Grupos (rama Cambios-Agus, Fase 2)
   ----------------------------------------------------------------------------
   Unos pocos grupos de ejemplo y a qué profesionales de prueba ya existentes
   asigna cada uno, más una regla de derivación por especialidad. Para ver la
   Fase 2 funcionando sin tener que cargar nada a mano desde la interfaz.

   Se basa solo en lo que ya existe en la base local:
     - Especialidades (tabla Especialidad): 1 Psicología, 3 Trabajo Social,
       4 Terapia Ocupacional. NO se usa la 2 (Psiquiatría) — fuera de alcance
       del proyecto (ver CAMBIOS_AGUS.md, bug 6 de la Fase 1: Martín Sosa la
       tiene cargada y no se corrige sin confirmar la especialidad real).
     - Profesionales de prueba ya existentes: 1 Santiago Manto y 2 Laura
       Giménez (Psicología), 4 Ángeles Muñoz (Psicología), 5 Sebastián
       O'Brien (Trabajo Social), 6 Carolina Ferreyra (Terapia Ocupacional).
       No se crea ningún profesional nuevo.

   Grupos:
     - "Psicología clínica": Santiago Manto + Laura Giménez.
     - "Psicología educativa": Ángeles Muñoz. Sin regla propia a propósito:
       hoy la admisión solo pide "especialidad" (Psicología), sin un campo
       más fino para elegir clínica vs. educativa — así que la derivación
       automática por regla va a "Psicología clínica" (el grupo más grande)
       y "Psicología educativa" queda como ejemplo de que un caso se puede
       derivar ahí a mano (corrigiendo la sugerencia o reasignando).
     - "Trabajo Social": Sebastián O'Brien.
     - "Terapia Ocupacional": Carolina Ferreyra.

   Reglas de derivación: Psicología → Psicología clínica, Trabajo Social →
   Trabajo Social, Terapia Ocupacional → Terapia Ocupacional. Nada para
   Psiquiatría.

   NO se ejecutó. Es para la base LOCAL (yosoy), no para producción.

   Cómo usarlo:
     - Tal como está termina con ROLLBACK: se puede correr para ver qué haría
       (el SELECT final muestra lo que quedaría) sin cambiar nada.
     - Para aplicarlo, reemplazar el ROLLBACK del final por COMMIT.
     - sqlcmd -S "DESKTOP-76GUM00\SQLEXPRESS" -E -C -d yosoy -f 65001 -i docs\datos-prueba-grupos-fase2.sql
   ============================================================================ */

SET QUOTED_IDENTIFIER ON;   -- lo pide el índice único filtrado de Caso.IdInforme
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS (SELECT 1 FROM Grupo WHERE Nombre IN (N'Psicología clínica', N'Psicología educativa', N'Trabajo Social', N'Terapia Ocupacional'))
        THROW 50001, N'Ya existe alguno de estos grupos — revisar antes de volver a correr esto.', 1;

    DECLARE @IdClinica INT, @IdEducativa INT, @IdTrabajoSocial INT, @IdTerapiaOcupacional INT;

    INSERT INTO Grupo (Nombre, Descripcion, Activo) VALUES
        (N'Psicología clínica', N'Atención psicológica general. Grupo por defecto para las admisiones de Psicología.', 1);
    SET @IdClinica = SCOPE_IDENTITY();

    INSERT INTO Grupo (Nombre, Descripcion, Activo) VALUES
        (N'Psicología educativa', N'Psicología orientada al ámbito educativo. Se deriva a mano por ahora (sin regla automática).', 1);
    SET @IdEducativa = SCOPE_IDENTITY();

    INSERT INTO Grupo (Nombre, Descripcion, Activo) VALUES
        (N'Trabajo Social', NULL, 1);
    SET @IdTrabajoSocial = SCOPE_IDENTITY();

    INSERT INTO Grupo (Nombre, Descripcion, Activo) VALUES
        (N'Terapia Ocupacional', NULL, 1);
    SET @IdTerapiaOcupacional = SCOPE_IDENTITY();

    INSERT INTO ProfesionalGrupo (IdProfesional, IdGrupo) VALUES
        (1, @IdClinica),               -- Santiago Manto
        (2, @IdClinica),               -- Laura Giménez
        (4, @IdEducativa),             -- Ángeles Muñoz
        (5, @IdTrabajoSocial),         -- Sebastián O'Brien
        (6, @IdTerapiaOcupacional);    -- Carolina Ferreyra

    INSERT INTO ReglaDerivacion (IdEspecialidad, IdGrupoDestino, Prioridad, Activa) VALUES
        (1, @IdClinica, 10, 1),              -- Psicología -> Psicología clínica
        (3, @IdTrabajoSocial, 10, 1),        -- Trabajo Social -> Trabajo Social
        (4, @IdTerapiaOcupacional, 10, 1);   -- Terapia Ocupacional -> Terapia Ocupacional

    SELECT g.IdGrupo, g.Nombre, g.Descripcion,
           STUFF((SELECT N', ' + p.Nombre + ISNULL(N' ' + p.Apellido, N'')
                  FROM ProfesionalGrupo pg JOIN Profesional p ON p.IdProfesional = pg.IdProfesional
                  WHERE pg.IdGrupo = g.IdGrupo FOR XML PATH('')), 1, 2, N'') AS Miembros
    FROM Grupo g
    WHERE g.IdGrupo IN (@IdClinica, @IdEducativa, @IdTrabajoSocial, @IdTerapiaOcupacional);

    SELECT r.IdRegla, e.Nombre AS Especialidad, g.Nombre AS GrupoDestino, r.Prioridad
    FROM ReglaDerivacion r
    JOIN Especialidad e ON e.IdEspecialidad = r.IdEspecialidad
    JOIN Grupo g ON g.IdGrupo = r.IdGrupoDestino
    WHERE r.IdGrupoDestino IN (@IdClinica, @IdEducativa, @IdTrabajoSocial, @IdTerapiaOcupacional);

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
