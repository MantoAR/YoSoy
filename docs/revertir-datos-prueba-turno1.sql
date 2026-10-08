/* ============================================================================
   Revertir los datos de prueba del turno 1 / paciente 1 (rama Cambios-Agus)
   ----------------------------------------------------------------------------
   Qué pasó (5/10/2026, base local de desarrollo):
     - Una prueba de interfaz confirmó por error el turno 1 (paciente 1, martes
       11/08 a las 10:00) desde el Inicio del profesional. El turno ya se volvió
       a Pendiente con la API (PUT /turnos/1), pero la confirmación había
       generado sus avisos:
         * la notificación 1010 "Turno confirmado" para el paciente 1;
         * los registros de envío 1018 (canal Chat) y 1019 (canal App);
         * el mensaje 1190 en la conversación 1 (el aviso por el chat simulado).
     - Otra prueba abrió la campana del paciente 1, que marca como leídas sus
       notificaciones: quedaron leídas las 15 (1010 incluida).
     - El recorrido final de capturas abrió la conversación 1 en la bandeja, así
       que su marca de lectura quedó en el mensaje 1190.

   Qué hace: deja esas filas como estaban antes de las pruebas. No toca nada más.
   NO se ejecutó. Es para la base LOCAL (yosoy), no para producción.

   Cómo usarlo:
     - Tal como está termina con ROLLBACK: se puede correr para ver qué haría
       (los SELECT muestran el antes y el después) sin cambiar nada.
     - Para aplicarlo, reemplazar el ROLLBACK del final por COMMIT.
     - Cada paso verifica cuántas filas toca. Si no coincide con lo esperado
       (por ejemplo, porque alguien ya borró o cambió algo), corta con un error y
       la transacción entera se deshace.
     - sqlcmd -S "DESKTOP-76GUM00\SQLEXPRESS" -E -C -d yosoy -f 65001 -i docs\revertir-datos-prueba-turno1.sql
   ============================================================================ */

SET NOCOUNT ON;
SET XACT_ABORT ON;      -- cualquier error deshace toda la transacción

BEGIN TRY
    BEGIN TRANSACTION;

    -- Estado antes ------------------------------------------------------------
    SELECT 'antes' AS momento, IdNotificacion, Titulo, Leida
    FROM dbo.Notificacion
    WHERE IdPaciente = 1
    ORDER BY IdNotificacion;

    SELECT 'antes' AS momento, IdRecordatorio, IdTurno, Tipo, Canal, Estado
    FROM dbo.Recordatorio
    WHERE IdRecordatorio IN (1018, 1019);

    SELECT 'antes' AS momento, IdMensaje, IdConversacion, Direccion, Origen, FechaHora
    FROM dbo.MensajeChat
    WHERE IdMensaje = 1190;

    -- 1. Turno 1: ya está Pendiente (IdEstadoTurno = 1) desde que se revirtió con
    --    la API. Solo se verifica; si no lo está, algo cambió y conviene mirarlo antes.
    IF NOT EXISTS (SELECT 1 FROM dbo.Turno WHERE IdTurno = 1 AND IdEstadoTurno = 1)
        THROW 50001, N'El turno 1 no está Pendiente (IdEstadoTurno = 1). Revisar antes de seguir.', 1;

    -- 2. Mensaje 1190: el aviso "Tu turno ... quedó confirmado" que el sistema mandó
    --    por el chat simulado a la conversación 1. Se identifica por id, conversación,
    --    dirección, origen y el minuto en que se creó, para no borrar otro mensaje.
    DELETE FROM dbo.MensajeChat
    WHERE IdMensaje = 1190
      AND IdConversacion = 1
      AND Direccion = N'Saliente'
      AND Origen = N'Sistema'
      AND FechaHora >= '2026-10-05T11:53:00' AND FechaHora < '2026-10-05T11:54:00';
    IF @@ROWCOUNT <> 1
        THROW 50002, N'Se esperaba borrar exactamente 1 mensaje (el 1190 de la conversación 1).', 1;

    -- 3. Marca de lectura de la conversación 1: quedó en 1190 porque se abrió la
    --    conversación en la bandeja. Se la devuelve al último mensaje que queda
    --    (sigue figurando todo leído, como antes). Solo si todavía apunta a 1190:
    --    si alguien la movió, no se toca (0 filas es válido acá).
    UPDATE dbo.Conversacion
    SET IdUltimoMensajeLeido = (SELECT MAX(m.IdMensaje) FROM dbo.MensajeChat m WHERE m.IdConversacion = 1)
    WHERE IdConversacion = 1
      AND IdUltimoMensajeLeido = 1190;

    -- 4. Registros de envío 1018 (Chat) y 1019 (App) del aviso de turno confirmado
    --    del turno 1. Mientras existan, el notificador considera que ese aviso ya se
    --    mandó; sin ellos queda como antes de la confirmación.
    DELETE FROM dbo.Recordatorio
    WHERE IdRecordatorio IN (1018, 1019)
      AND IdTurno = 1
      AND Tipo = N'TurnoConfirmado'
      AND Canal IN (N'Chat', N'App');
    IF @@ROWCOUNT <> 2
        THROW 50003, N'Se esperaba borrar exactamente 2 registros de envío (1018 y 1019).', 1;

    -- 5. Notificación 1010 "Turno confirmado" del paciente 1 (la generó la
    --    confirmación por error). No hay claves foráneas que apunten a Notificacion.
    DELETE FROM dbo.Notificacion
    WHERE IdNotificacion = 1010
      AND IdPaciente = 1
      AND Titulo = N'Turno confirmado';
    IF @@ROWCOUNT <> 1
        THROW 50004, N'Se esperaba borrar exactamente 1 notificación (la 1010 del paciente 1).', 1;

    -- 6. Las otras 14 notificaciones del paciente 1 que la prueba marcó como leídas
    --    al abrir la campana vuelven a no leídas. Son las que estaban sin leer en ese
    --    momento (la app solo marca las no leídas). Notificacion no guarda fecha de
    --    lectura, así que Leida = 0 las deja exactamente como estaban.
    UPDATE dbo.Notificacion
    SET Leida = 0
    WHERE IdPaciente = 1
      AND Leida = 1
      AND IdNotificacion IN (1, 2, 3, 5, 6, 7, 9, 10, 15, 17, 19, 21, 24, 1007);
    IF @@ROWCOUNT <> 14
        THROW 50005, N'Se esperaba marcar como no leídas exactamente 14 notificaciones del paciente 1.', 1;

    -- Estado después ----------------------------------------------------------
    SELECT N'después' AS momento, IdNotificacion, Titulo, Leida
    FROM dbo.Notificacion
    WHERE IdPaciente = 1
    ORDER BY IdNotificacion;

    SELECT N'después' AS momento, IdConversacion, IdUltimoMensajeLeido
    FROM dbo.Conversacion
    WHERE IdConversacion = 1;

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
