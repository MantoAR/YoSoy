-- Borra un usuario del equipo creado de prueba, para volver a mostrar el alta desde cero
-- (instructivos, demos). Libera usuario, email, CUIL y matrícula para poder reusarlos.
--
-- Uso (Git Bash, desde D:\YoSoy):
--   sqlcmd -S 'DESKTOP-Q065FTM\SQLEXPRESS' -d yosoy -E -C -f 65001 -i Scripts/borrar_usuario_prueba.sql
-- Cambiá el usuario en la línea de @Username si no es juan.pintos.
--
-- Qué hace:
--  - Si la cuenta nunca hizo nada (no ingresó ni quedó como autor en la auditoría), la borra
--    del todo: invitaciones, privilegios, permisos, roles, ficha de profesional y usuario.
--  - Si ya tiene historial en la auditoría, la base no deja borrarla (la auditoría no se
--    toca). Entonces la da de baja y le cambia usuario, email, CUIL y matrícula, así quedan
--    libres para el próximo alta.
--  - La auditoría queda como está: el alta de prueba y este borrado se siguen viendo
--    ("hecho directo en la base"). Es a propósito: nadie puede borrar la auditoría.
--  - No toca cuentas con permiso de Configurador.

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Username NVARCHAR(100) = N'juan.pintos';

DECLARE @u INT = (SELECT IdUsuario FROM Usuario WHERE Username = @Username);
IF @u IS NULL
BEGIN
    PRINT N'No existe el usuario ' + @Username + N'. No se hizo nada.';
    RETURN;
END;

IF EXISTS (SELECT 1 FROM UsuarioRol ur JOIN Rol r ON r.IdRol = ur.IdRol
           WHERE ur.IdUsuario = @u AND r.Clave = 'configurador')
BEGIN
    PRINT N'El usuario tiene permiso de Configurador. Por seguridad, este script no lo borra.';
    RETURN;
END;

DECLARE @p INT = (SELECT TOP 1 IdProfesional FROM UsuarioProfesional WHERE IdUsuario = @u);

BEGIN TRY
    BEGIN TRAN;
    DELETE FROM InvitacionCuenta   WHERE IdUsuario = @u OR IdUsuarioCreo = @u;
    DELETE FROM UsuarioPrivilegio  WHERE IdUsuario = @u;
    DELETE FROM UsuarioPermiso     WHERE IdUsuario = @u;
    DELETE FROM UsuarioRol         WHERE IdUsuario = @u;
    DELETE FROM UsuarioProfesional WHERE IdUsuario = @u;
    IF @p IS NOT NULL DELETE FROM Profesional WHERE IdProfesional = @p;
    DELETE FROM Usuario            WHERE IdUsuario = @u;
    COMMIT;
    PRINT N'Usuario ' + @Username + N' borrado. Ya podés volver a darlo de alta con los mismos datos.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;

    -- Tiene historial: baja y datos liberados.
    BEGIN TRAN;
    DELETE FROM InvitacionCuenta  WHERE IdUsuario = @u;
    DELETE FROM UsuarioPrivilegio WHERE IdUsuario = @u;
    DELETE FROM UsuarioPermiso    WHERE IdUsuario = @u;
    DELETE FROM UsuarioRol        WHERE IdUsuario = @u;

    DECLARE @sufijo NVARCHAR(20) = N'.baja' + CAST(@u AS NVARCHAR(10));
    UPDATE Usuario
       SET Username = LEFT(Username, 30) + @sufijo,
           Mail     = N'baja' + CAST(@u AS NVARCHAR(10)) + N'@yosoy.invalid',
           Cuil     = NULL,
           Estado   = 'Inactivo'
     WHERE IdUsuario = @u;

    IF @p IS NOT NULL
        UPDATE Profesional
           SET Matricula = N'BAJA-' + CAST(@p AS NVARCHAR(10)),
               Mail      = N'baja' + CAST(@u AS NVARCHAR(10)) + N'@yosoy.invalid',
               Estado    = 'Inactivo'
         WHERE IdProfesional = @p;
    COMMIT;

    PRINT N'El usuario ya tenía historial en la auditoría, así que no se puede borrar del todo.';
    PRINT N'Quedó dado de baja y se liberaron usuario, email, CUIL y matrícula: ya podés volver a darlo de alta con los mismos datos.';
    PRINT N'(Motivo técnico: ' + ERROR_MESSAGE() + N')';
END CATCH;
