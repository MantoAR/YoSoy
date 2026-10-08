-- LogAuditoria.IdUsuario pasa a admitir NULL: representa una acción sin sesión
-- (por ejemplo, el registro público de un profesional, antes de que exista una sesión).
-- Antes, esas acciones guardaban IdUsuario = 0, que no existe en Usuario y choca con la
-- clave foránea FK__LogAudito__IdUsu__778AC167 (devuelve 500). La FK no valida los NULL,
-- así que no hace falta tocarla: alcanza con permitir NULL en la columna.
-- Idempotente: se puede correr más de una vez.
IF EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'LogAuditoria' AND COLUMN_NAME = 'IdUsuario' AND IS_NULLABLE = 'NO'
)
    ALTER TABLE LogAuditoria ALTER COLUMN IdUsuario INT NULL;
GO
