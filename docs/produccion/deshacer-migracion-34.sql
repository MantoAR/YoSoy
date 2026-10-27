/* ============================================================================
   deshacer-migracion-34.sql — Saca lo que agregó la migración 34 (especialidades del Colegio).

   Solo borra las seis especialidades del Colegio de Psicólogos que NADIE usa (ningún
   profesional, matrícula, informe de admisión ni regla de derivación). Si alguna está en uso,
   la deja y lo avisa: en ese caso se da de baja desde Configuración › Especialidades.
   La disciplina completada y el tipo "Servicio" quedan como están (son datos, no estorban
   a la API anterior).

   Cómo correrlo:
     sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i docs/produccion/deshacer-migracion-34.sql
   ============================================================================ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Colegio TABLE (IdEspecialidad INT);
INSERT INTO @Colegio
SELECT e.IdEspecialidad FROM dbo.Especialidad e
WHERE e.Nombre COLLATE Latin1_General_CI_AI IN (N'Psicologia Clinica', N'Psicologia Educacional', N'Psicologia Social',
      N'Psicologia Juridica y Forense', N'Psicologia del Trabajo y las Organizaciones', N'Psicologia Sanitaria');

DELETE e FROM dbo.Especialidad e
JOIN @Colegio c ON c.IdEspecialidad = e.IdEspecialidad
WHERE NOT EXISTS (SELECT 1 FROM dbo.Profesional p WHERE p.IdEspecialidad = e.IdEspecialidad)
  AND NOT EXISTS (SELECT 1 FROM dbo.ReglaDerivacion r WHERE r.IdEspecialidad = e.IdEspecialidad)
  AND NOT EXISTS (SELECT 1 FROM dbo.InformeAdmision i WHERE i.IdEspecialidad = e.IdEspecialidad)
  AND (OBJECT_ID(N'dbo.ProfesionalMatricula', N'U') IS NULL
       OR NOT EXISTS (SELECT 1 FROM dbo.ProfesionalMatricula m WHERE m.IdEspecialidad = e.IdEspecialidad));
PRINT N'Especialidades del Colegio borradas: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + N'.';

SELECT e.IdEspecialidad, e.Nombre AS en_uso_no_se_borro
FROM dbo.Especialidad e JOIN @Colegio c ON c.IdEspecialidad = e.IdEspecialidad;

DELETE FROM dbo.VersionEsquema WHERE Numero = 34;
COMMIT TRANSACTION;
PRINT N'Migración 34 deshecha.';
