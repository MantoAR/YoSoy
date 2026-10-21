/* ============================================================================
   deshacer-migracion-24.sql — Saca de la base lo que agregó la migración 24 (Usuario.DebeCambiarPassword).

   CUÁNDO: casi nunca. Lo normal es volver a la API anterior (ignora la columna y sigue andando con la base migrada) y dejar la base como está.
   Esto solo hace falta si se quiere dejar la base exactamente como antes de la migración, sin restaurar el backup.

   QUÉ PIERDE: la marca de "tiene que cambiar la contraseña" de quien la tenga puesta. Esas cuentas quedan con la contraseña temporal como definitiva:
   el primer SELECT las lista para que se les restablezca otra o se les avise antes de seguir. No toca ningún otro dato.

   REQUISITOS: la API de esta versión parada o ya reemplazada por la anterior (la nueva lee la columna en cada ingreso y fallaría sin ella).
     sqlcmd -S TU_SERVIDOR -d yosoy -E -C -I -b -f 65001 -i docs/produccion/deshacer-migracion-24.sql
   ============================================================================ */
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
GO

IF COL_LENGTH('dbo.Usuario', 'DebeCambiarPassword') IS NOT NULL
BEGIN
    PRINT N'Cuentas con el cambio de contraseña pendiente (se quedan con la temporal como definitiva):';
    EXEC(N'SELECT IdUsuario, Username, Estado FROM Usuario WHERE DebeCambiarPassword = 1 ORDER BY IdUsuario');
END
GO

IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_Usuario_DebeCambiarPassword')
    ALTER TABLE Usuario DROP CONSTRAINT DF_Usuario_DebeCambiarPassword;
GO
IF COL_LENGTH('dbo.Usuario', 'DebeCambiarPassword') IS NOT NULL
    ALTER TABLE Usuario DROP COLUMN DebeCambiarPassword;
GO

DELETE FROM VersionEsquema WHERE Numero = 24;
GO

PRINT N'Migración 24 deshecha en la base. Falta volver a la API anterior si todavía no se hizo.';
SELECT (SELECT COUNT(*) FROM sys.tables) AS tablas, (SELECT MAX(Numero) FROM VersionEsquema) AS ultima_version;
GO
