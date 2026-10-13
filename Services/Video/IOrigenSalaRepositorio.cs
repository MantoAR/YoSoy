// Lo que AccesoSalaService necesita saber de un turno o de un informe de admisión para decidir si
// alguien puede entrar a su sala. Es una interfaz para poder probar las reglas (pertenencia, estado y
// ventana horaria) con datos en memoria; la implementación real, con SQL, es OrigenSalaDAO.
public interface IOrigenSalaRepositorio
{
    TurnoParaSala? ObtenerTurno(int idTurno);
    AdmisionParaSala? ObtenerAdmision(int idInforme);
}

// Estado es la Descripcion del catálogo EstadoTurno ("Confirmado", "Cancelado"...), no el id: los ids
// son datos de cada base. DuracionMinutos sale de la franja de la agenda que contiene la hora del turno
// (Agenda.IntervaloMinutos); null si el turno no cae en ninguna franja.
public record TurnoParaSala(int IdTurno, int IdPaciente, int IdProfesional, string Estado,
    DateTime Fecha, TimeSpan Hora, string? CodigoSala, int? DuracionMinutos);

public record AdmisionParaSala(int IdInforme, int IdPaciente, string? Estado, DateTime? FechaEntrevista, string? CodigoSala);
