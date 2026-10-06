/* ============================================================================
   Contraseña de demo para 3 usuarios (rama Cambios-Agus) — NO EJECUTADO.
   ----------------------------------------------------------------------------
   No hay forma de leer una contraseña ya cargada (está hasheada, SHA-256, y
   no corresponde intentar adivinarla contra la base — Claude Code lo bloqueó
   al intentarlo de forma automática, y tiene razón en bloquearlo). Si Agustín
   ya conoce las contraseñas reales de admin / paciente1 / smanto, este script
   no hace falta — se puede armar el guion con esas.

   Si no las tiene a mano, esto les pone a esos 3 usuarios (y SOLO a esos 3)
   una contraseña de prueba, a elección de quien lo corra: completar @Clave
   más abajo antes de ejecutar — el script corta solo si no se completó.

   El hash se calcula en el momento con HASHBYTES, igual que lo hace la app
   (Program.cs, SHA256.HashData + Convert.ToHexString) — la contraseña que se
   elija no queda en texto plano en ningún lado: ni en este archivo ni en
   ningún commit, solo en la variable @Clave mientras corre el script.
   También resetea IntentosFallidos/BloqueadoHasta de los 3, por si alguno
   quedó bloqueado de alguna prueba anterior.

   Cómo usarlo:
     - Completar @Clave más abajo con la contraseña elegida.
     - Tal como está, termina con ROLLBACK: muestra a quién le cambiaría la
       contraseña, sin cambiar nada.
     - Para aplicarlo, reemplazar el ROLLBACK del final por COMMIT.
     - sqlcmd -S "DESKTOP-76GUM00\SQLEXPRESS" -E -C -d yosoy -f 65001 -i docs\credenciales-demo.sql

   Esto PISA la contraseña real de esos 3 usuarios (no hay forma de
   recuperarla después) — por eso no se ejecutó solo, para que decidas.
   ============================================================================ */

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Clave NVARCHAR(100) = N'<ELEGIR-ANTES-DE-EJECUTAR>';
IF @Clave LIKE N'<%' THROW 50000, N'Elegí una contraseña antes de ejecutar', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @Hash CHAR(64) = UPPER(CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', @Clave), 2));

    UPDATE Usuario
    SET PasswordHash = @Hash, IntentosFallidos = 0, BloqueadoHasta = NULL
    WHERE Username IN (N'admin', N'paciente1', N'smanto');

    SELECT Username, IdUsuario, Estado, IntentosFallidos, BloqueadoHasta FROM Usuario WHERE Username IN (N'admin', N'paciente1', N'smanto');

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
