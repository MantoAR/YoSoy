-- Integración a la rama principal, Paso 3.4: mismo caso que add_creadopor_turno.sql — el
-- script base de la rama DataBase ya trae estas columnas; sin guarda, fallaba por columna
-- duplicada en el primer intento sobre una base restaurada hoy, no solo en una repetición.
IF COL_LENGTH('dbo.RecuperacionPassword', 'Token') IS NULL
    ALTER TABLE RecuperacionPassword ADD Token NVARCHAR(64) NULL;
GO
IF COL_LENGTH('dbo.RecuperacionPassword', 'TokenExpiracion') IS NULL
    ALTER TABLE RecuperacionPassword ADD TokenExpiracion DATETIME NULL;
GO
