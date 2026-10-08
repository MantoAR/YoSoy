// Avisos automáticos por evento. Ningún método lanza excepciones: un fallo al
// notificar se registra en el log y nunca hace fallar al endpoint que lo llamó.
// respondidoPorChat = true cuando el evento lo generó el propio bot, que ya le
// contestó al paciente en la conversación (así no se duplica el mensaje).
public interface INotificadorTurnos
{
    void TurnoSolicitado(int idTurno, bool respondidoPorChat = false);
    void TurnoSolicitado(Turno t);
    void TurnoAgendadoPorProfesional(int idPaciente, int idProfesional, DateTime fecha, TimeSpan hora);
    void TurnoConfirmado(int idTurno);
    void TurnoCancelado(int idTurno, bool respondidoPorChat = false);
    void CambioDeEstado(int idTurno, int idEstadoAnterior);
    void AvisarUrgencia(Conversacion c, string textoPaciente);
    void AvisarDerivacion(Conversacion c);
    void AvisarNoAsistencia(int idTurno);
}
