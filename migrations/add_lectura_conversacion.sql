-- Bandeja de chats: hasta qué mensaje leyó el equipo cada conversación.
-- Con esto la bandeja calcula los mensajes sin leer (contador del botón Chats y filtro
-- "No leídas"). Es una marca compartida por el equipo: si alguien abre la conversación,
-- deja de figurar como no leída para todos, como en una bandeja de WhatsApp Business.
-- Requiere add_mensajeria.sql. Idempotente: se puede correr más de una vez.

IF COL_LENGTH('dbo.Conversacion', 'IdUltimoMensajeLeido') IS NULL
BEGIN
    ALTER TABLE dbo.Conversacion ADD IdUltimoMensajeLeido INT NULL;

    -- Las conversaciones que ya existían arrancan leídas hasta su último mensaje; si no,
    -- todo el historial de prueba aparecería sin leer. Solo corre la vez que se crea la
    -- columna (con EXEC, porque la columna recién agregada no existe al compilar el lote).
    EXEC('UPDATE c
          SET IdUltimoMensajeLeido = (SELECT MAX(m.IdMensaje) FROM dbo.MensajeChat m
                                      WHERE m.IdConversacion = c.IdConversacion)
          FROM dbo.Conversacion c');
END
GO
