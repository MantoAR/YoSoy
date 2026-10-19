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
    // seRespondioAlPaciente: false cuando al paciente NO se le mandó el mensaje de emergencia (modo Personal, donde viene apagado): el aviso se lo dice al equipo.
    void AvisarUrgencia(Conversacion c, string textoPaciente, bool seRespondioAlPaciente = true);
    void AvisarDerivacion(Conversacion c);
    // Modo Personal: una persona escribió y espera que el equipo le conteste (una vez por tanda de mensajes: ver AtencionPersonalService).
    void AvisarChatNuevo(Conversacion c);
    void AvisarNoAsistencia(int idTurno);
}
