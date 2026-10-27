// Lo que AccesoSalaService necesita saber de un turno o de un informe de admisión para decidir si
// alguien puede entrar a su sala. Es una interfaz para poder probar las reglas (pertenencia, estado y
// ventana horaria) con datos en memoria; la implementación real, con SQL, es OrigenSalaDAO.
public interface IOrigenSalaRepositorio
{
    TurnoParaSala? ObtenerTurno(int idTurno);
    AdmisionParaSala? ObtenerAdmision(int idInforme);

    /// <summary>Los turnos Confirmados de la persona (paciente o profesional) desde el día <paramref name="desde"/> (inclusive), del más próximo al más lejano, con el nombre de las dos partes. Alimenta GET /salas/entrable.</summary>
    IReadOnlyList<TurnoConNombres> TurnosConfirmadosDe(int? idPaciente, int? idProfesional, DateTime desde);
}

public record TurnoConNombres(TurnoParaSala Turno, string Paciente, string Profesional);

// Estado es la Descripcion del catálogo EstadoTurno ("Confirmado", "Cancelado"...), no el id: los ids
// son datos de cada base. DuracionMinutos sale de la franja de la agenda que contiene la hora del turno
// (Agenda.IntervaloMinutos); null si el turno no cae en ninguna franja.
public record TurnoParaSala(int IdTurno, int IdPaciente, int IdProfesional, string Estado,
    DateTime Fecha, TimeSpan Hora, string? CodigoSala, int? DuracionMinutos);

public record AdmisionParaSala(int IdInforme, int IdPaciente, string? Estado, DateTime? FechaEntrevista, string? CodigoSala);
