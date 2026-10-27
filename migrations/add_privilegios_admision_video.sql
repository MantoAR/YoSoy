/* ============================================================================
   add_privilegios_admision_video.sql — Migración 36 (rama AAA1).

   Qué hace (pedido de la psicóloga, reunión del 26/10):
     1) El privilegio Admisión se puede dar también a la Recepcionista (permiso crm): la
        admisionista revisa los formularios «Sobre mí», propone y asigna profesional. Sin el
        privilegio, la recepcionista no los ve (tienen datos sensibles: motivo de consulta,
        tratamientos, medicación). Nunca ve la historia clínica.
     2) Privilegio nuevo "Entrevista de admisión por video" (entrevista_video), solo para los
        permisos Profesional y Profesional auditor. La entrevista por video es OPCIONAL y la
        ofrece únicamente un profesional con este privilegio, que asigna el Configurador.
     3) Para que nada deje de funcionar el día que se publique: las recepcionistas que YA
        existen reciben el privilegio Admisión (hoy todas revisan formularios). El Configurador
        se lo puede sacar después desde Usuarios › Permisos. Queda en la auditoría por el
        trigger TR_UsuarioPrivilegio_Auditar (migración 21).

   Efectos: solo agrega filas. No toca usuarios profesionales ni otros privilegios.

   Cómo correrla:
     sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i migrations/add_privilegios_admision_video.sql
   Verificación: termina con "Migración 36 aplicada." y la tabla de qué privilegio admite cada permiso.
   Deshacer: docs/produccion/deshacer-migracion-36.sql
   ============================================================================ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.PermisoPrivilegio', N'U') IS NULL OR OBJECT_ID(N'dbo.VersionEsquema', N'U') IS NULL
BEGIN
    RAISERROR(N'Faltan las migraciones 18 a 21 (VersionEsquema, permisos y privilegios).', 16, 1);
    RETURN;
END

BEGIN TRANSACTION;

-- 1) Admisión también para la Recepcionista.
INSERT INTO dbo.PermisoPrivilegio (IdPermiso, IdPrivilegio)
SELECT p.IdPermiso, pr.IdPrivilegio
FROM dbo.Permiso p JOIN dbo.Privilegio pr ON pr.Clave = 'admision'
WHERE p.Clave = 'crm'
  AND NOT EXISTS (SELECT 1 FROM dbo.PermisoPrivilegio x WHERE x.IdPermiso = p.IdPermiso AND x.IdPrivilegio = pr.IdPrivilegio);

-- 2) Entrevista de admisión por video, solo para Profesional y Profesional auditor.
IF NOT EXISTS (SELECT 1 FROM dbo.Privilegio WHERE Clave = 'entrevista_video')
    INSERT INTO dbo.Privilegio (Clave, Nombre, Descripcion)
    VALUES ('entrevista_video', N'Entrevista de admisión por video',
            N'Ofrecer al paciente una entrevista de admisión por video. Es opcional: la admisión se puede resolver sin entrevista.');
INSERT INTO dbo.PermisoPrivilegio (IdPermiso, IdPrivilegio)
SELECT p.IdPermiso, pr.IdPrivilegio
FROM dbo.Permiso p JOIN dbo.Privilegio pr ON pr.Clave = 'entrevista_video'
WHERE p.Clave IN ('profesional', 'auditor')
  AND NOT EXISTS (SELECT 1 FROM dbo.PermisoPrivilegio x WHERE x.IdPermiso = p.IdPermiso AND x.IdPrivilegio = pr.IdPrivilegio);

-- 3) Las recepcionistas que ya existen conservan lo que hacen hoy (solo la primera vez que corre).
IF NOT EXISTS (SELECT 1 FROM dbo.VersionEsquema WHERE Numero = 36)
    INSERT INTO dbo.UsuarioPrivilegio (IdUsuario, IdPermiso, IdPrivilegio)
    SELECT up.IdUsuario, up.IdPermiso, pr.IdPrivilegio
    FROM dbo.UsuarioPermiso up
    JOIN dbo.Permiso p ON p.IdPermiso = up.IdPermiso AND p.Clave = 'crm'
    JOIN dbo.Privilegio pr ON pr.Clave = 'admision'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.UsuarioPrivilegio x
                      WHERE x.IdUsuario = up.IdUsuario AND x.IdPermiso = up.IdPermiso AND x.IdPrivilegio = pr.IdPrivilegio);
PRINT N'Recepcionistas que conservan Admisión: ' + CAST(@@ROWCOUNT AS NVARCHAR(10)) + N'.';

IF NOT EXISTS (SELECT 1 FROM dbo.VersionEsquema WHERE Numero = 36)
    INSERT INTO dbo.VersionEsquema (Numero, Archivo, Origen) VALUES (36, N'add_privilegios_admision_video.sql', 'Script');

COMMIT TRANSACTION;
PRINT N'Migración 36 aplicada.';

SELECT pe.Clave AS permiso, pr.Clave AS privilegio, pr.Nombre
FROM dbo.PermisoPrivilegio pp
JOIN dbo.Permiso pe ON pe.IdPermiso = pp.IdPermiso
JOIN dbo.Privilegio pr ON pr.IdPrivilegio = pp.IdPrivilegio
ORDER BY pe.Clave, pr.Clave;
