/* ============================================================================
   add_especialidades_colegio.sql — Migración 34 (rama AAA1).

   Qué hace: completa el catálogo de especialidades para que el Configurador lo
   administre desde Configuración › Especialidades.
     1) Agrega las seis especialidades de la psicología que reconoce el Colegio de
        Psicólogos de la Provincia de Córdoba: Clínica, Educacional, Social,
        Jurídica y Forense, del Trabajo y las Organizaciones, y Sanitaria
        (Reglamento de Especialidades, Res. de Junta de Gobierno N° 083/16; las
        mismas que enumera su Código de Ética de 2016). Disciplina "Psicología",
        piden matrícula de especialista.
     2) Completa la disciplina de las especialidades que ya cargó seed_catalogos.sql
        (solo donde falta).
     3) Marca como "Servicio" las filas que no son la especialidad de una persona
        (Admisión, Atención domiciliaria, etc.), con la misma regla que ya tiene
        docs/produccion/01-produccion.sql (sección E): no se ofrecen al registrar
        un profesional. El Configurador lo puede cambiar después.

   Efectos: solo agrega filas y completa columnas vacías. No borra, no renombra y
   no cambia el estado de ninguna especialidad. Corre una sola vez: si el
   Configurador después borra o renombra alguna, volver a correrla no la revive.

   Cómo correrla:
     sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i migrations/add_especialidades_colegio.sql

   Verificación: termina con "Migración 34 aplicada." y lista el catálogo.

   Deshacer: docs/produccion/deshacer-migracion-34.sql (saca las seis del Colegio
   solo si nadie las usa).
   ============================================================================ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.VersionEsquema', N'U') IS NULL
BEGIN
    RAISERROR(N'Falta la migración 18 (VersionEsquema). Corré antes 01-produccion.sql.', 16, 1);
    RETURN;
END
IF COL_LENGTH(N'dbo.Especialidad', N'Tipo') IS NULL OR COL_LENGTH(N'dbo.Especialidad', N'Disciplina') IS NULL
BEGIN
    RAISERROR(N'Falta la migración 20 (Especialidad.Disciplina y Tipo).', 16, 1);
    RETURN;
END

IF EXISTS (SELECT 1 FROM dbo.VersionEsquema WHERE Numero = 34)
BEGIN
    PRINT N'La migración 34 ya estaba aplicada: no se tocó el catálogo.';
END
ELSE
BEGIN
    BEGIN TRANSACTION;

    -- 1) Las seis del Colegio de Psicólogos de Córdoba (se comparan sin tildes ni mayúsculas).
    INSERT INTO dbo.Especialidad (Nombre, Estado, Disciplina, Tipo, RequiereMatriculaEspecialista)
    SELECT c.Nombre, N'Activo', N'Psicología', 'Especialidad', 1
    FROM (VALUES (N'Psicología Clínica'), (N'Psicología Educacional'), (N'Psicología Social'),
                 (N'Psicología Jurídica y Forense'), (N'Psicología del Trabajo y las Organizaciones'),
                 (N'Psicología Sanitaria')) AS c(Nombre)
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Especialidad e
                      WHERE e.Nombre COLLATE Latin1_General_CI_AI = c.Nombre COLLATE Latin1_General_CI_AI);
    PRINT N'Especialidades del Colegio agregadas: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + N'.';

    -- 2) Disciplina de las que ya estaban (solo donde falta).
    UPDATE e SET e.Disciplina = d.Disciplina
    FROM dbo.Especialidad e
    JOIN (VALUES (N'Psicología', N'Psicología'), (N'Psicología Infanto-Juvenil', N'Psicología'),
                 (N'Neuropsicología', N'Psicología'), (N'Terapia de Pareja y Familia', N'Psicología'),
                 (N'Psiquiatría', N'Psiquiatría'), (N'Psiquiatría Infanto-Juvenil', N'Psiquiatría'),
                 (N'Psicopsiquiatría de Adicciones', N'Psiquiatría'),
                 (N'Trabajo Social', N'Trabajo social'), (N'Terapia Ocupacional', N'Terapia ocupacional'),
                 (N'Psicopedagogía', N'Psicopedagogía'), (N'Psicomotricidad', N'Psicomotricidad')) AS d(Nombre, Disciplina)
      ON e.Nombre COLLATE Latin1_General_CI_AI = d.Nombre COLLATE Latin1_General_CI_AI
    WHERE e.Disciplina IS NULL;

    -- 3) Servicios: la misma lista que 01-produccion.sql, sección E.
    UPDATE dbo.Especialidad SET Tipo = 'Servicio'
    WHERE Nombre COLLATE Latin1_General_CI_AI IN (N'Admision', N'Admision Infanto-Juvenil', N'Atencion Domiciliaria',
                                                  N'Centro de Rehabilitacion Sociolaboral', N'Psicologia Infanto-Juvenil')
      AND Tipo <> 'Servicio';

    INSERT INTO dbo.VersionEsquema (Numero, Archivo, Origen) VALUES (34, N'add_especialidades_colegio.sql', 'Script');
    COMMIT TRANSACTION;
    PRINT N'Migración 34 aplicada.';
END

SELECT IdEspecialidad, Nombre, Disciplina, Tipo, RequiereMatriculaEspecialista, Estado
FROM dbo.Especialidad ORDER BY Tipo, Disciplina, Nombre;
