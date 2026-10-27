/* ============================================================================
   deshacer-migracion-36.sql — Saca lo que agregó la migración 36 (privilegios de admisión y video).

   Quita el privilegio Admisión a las recepcionistas y el privilegio "Entrevista de admisión por
   video" a quien lo tenga, y los saca del catálogo. Con la API anterior, cualquier recepcionista
   vuelve a revisar formularios (era el comportamiento previo). Los cambios quedan en la auditoría
   por los triggers de la migración 21.

   Cómo correrlo:
     sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i docs/produccion/deshacer-migracion-36.sql
   ============================================================================ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DELETE up FROM dbo.UsuarioPrivilegio up
JOIN dbo.Permiso p ON p.IdPermiso = up.IdPermiso
JOIN dbo.Privilegio pr ON pr.IdPrivilegio = up.IdPrivilegio
WHERE (p.Clave = 'crm' AND pr.Clave = 'admision') OR pr.Clave = 'entrevista_video';

DELETE pp FROM dbo.PermisoPrivilegio pp
JOIN dbo.Permiso p ON p.IdPermiso = pp.IdPermiso
JOIN dbo.Privilegio pr ON pr.IdPrivilegio = pp.IdPrivilegio
WHERE (p.Clave = 'crm' AND pr.Clave = 'admision') OR pr.Clave = 'entrevista_video';

DELETE FROM dbo.Privilegio WHERE Clave = 'entrevista_video';
DELETE FROM dbo.VersionEsquema WHERE Numero = 36;
COMMIT TRANSACTION;
PRINT N'Migración 36 deshecha.';
