-- Integración a la rama principal, Paso 3.4: el script base de la rama DataBase ya trae
-- esta columna desde hace rato (se exportó de una base que ya la tenía) — en una base
-- restaurada hoy desde cero, este ALTER sin guarda fallaba por columna duplicada en el
-- primer intento, no solo en una repetición. Idempotente ahora, mismo criterio que el
-- resto de las migraciones.
IF COL_LENGTH('dbo.Turno', 'CreadoPor') IS NULL
    ALTER TABLE Turno
    ADD CreadoPor NVARCHAR(20) NULL;
GO
