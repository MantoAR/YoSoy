using Microsoft.Data.SqlClient;

// Todo lo que necesita AsignacionService, detrás de una sola interfaz para poder
// probar la cascada con un repositorio falso (sin base de datos). Por dentro
// compone CasoDAO / CasoDerivacionDAO / HistorialAsignacionDAO, salvo la consulta
// de candidatos, que es propia (igual que DatosMensajeriaDAO para el bot).
public interface IAsignacionRepositorio
{
    List<CandidatoAsignacion> ObtenerCandidatos(int idGrupo);
    Caso? ObtenerCaso(int idCaso);
    void ActualizarCaso(int idCaso, string estado, int? idGrupo, int? idProfesional);
    int InsertarCasoDerivacion(CasoDerivacion d);
    CasoDerivacion? ObtenerCasoDerivacion(int idCasoDerivacion);
    void ResolverCasoDerivacion(int idCasoDerivacion, string estado, int? idGrupo, int? idProfesionalAsignado);
    void InsertarHistorial(int idCaso, int? idProfesional, string motivo, int? idRealizadoPor);

    // PacienteProfesional sigue siendo la tabla operativa ("quién atiende a quién ahora"
    // que ya usan turnos y agenda) — esto solo agrega el vínculo, nunca saca al profesional
    // anterior: un paciente puede tener más de uno (N:M), y sacar al que ya no corresponde
    // a este caso en particular sigue siendo una decisión de "Solicitudes de cambio".
    void SincronizarPacienteProfesional(int idPaciente, int idProfesional);
}

public class AsignacionRepositorioDAO : IAsignacionRepositorio
{
    private readonly CasoDAO _casoDao = new();
    private readonly CasoDerivacionDAO _derivacionDao = new();
    private readonly HistorialAsignacionDAO _historialDao = new();

    // Disponible = Profesional.Estado = 'Activo' y al menos una franja de Agenda con
    // Disponible = 1. Carga = Caso activos (Asignado / En atención) asignados hoy,
    // sin importar por qué grupo haya llegado cada uno (la carga es por profesional).
    public List<CandidatoAsignacion> ObtenerCandidatos(int idGrupo)
    {
        var lista = new List<CandidatoAsignacion>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT p.IdProfesional,
                   CASE WHEN p.Estado = 'Activo' AND EXISTS (
                            SELECT 1 FROM Agenda a
                            WHERE a.IdProfesional = p.IdProfesional AND a.Disponible = 1
                        ) THEN 1 ELSE 0 END AS Disponible,
                   (SELECT COUNT(*) FROM Caso c
                    WHERE c.IdProfesionalAsignado = p.IdProfesional
                      AND c.Estado IN ('Asignado', 'En atención')) AS CasosActivos,
                   (SELECT MAX(h.FechaHora) FROM HistorialAsignacion h
                    WHERE h.IdProfesional = p.IdProfesional) AS UltimaAsignacion
            FROM ProfesionalGrupo pg
            JOIN Profesional p ON p.IdProfesional = pg.IdProfesional
            WHERE pg.IdGrupo = @IdGrupo", con);
        cmd.Parameters.AddWithValue("@IdGrupo", idGrupo);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            lista.Add(new CandidatoAsignacion(
                IdProfesional:    (int)r["IdProfesional"],
                Disponible:       (int)r["Disponible"] == 1,
                CasosActivos:     (int)r["CasosActivos"],
                UltimaAsignacion: r["UltimaAsignacion"] == DBNull.Value ? null : (DateTime)r["UltimaAsignacion"]));
        return lista;
    }

    public Caso? ObtenerCaso(int idCaso) => _casoDao.ObtenerPorId(idCaso);

    public void ActualizarCaso(int idCaso, string estado, int? idGrupo, int? idProfesional) =>
        _casoDao.ActualizarEstadoYAsignacion(idCaso, estado, idGrupo, idProfesional);

    public int InsertarCasoDerivacion(CasoDerivacion d) => _derivacionDao.Insertar(d);

    public CasoDerivacion? ObtenerCasoDerivacion(int idCasoDerivacion) => _derivacionDao.ObtenerPorId(idCasoDerivacion);

    public void ResolverCasoDerivacion(int idCasoDerivacion, string estado, int? idGrupo, int? idProfesionalAsignado) =>
        _derivacionDao.Resolver(idCasoDerivacion, estado, idGrupo, idProfesionalAsignado);

    public void InsertarHistorial(int idCaso, int? idProfesional, string motivo, int? idRealizadoPor) =>
        _historialDao.Insertar(new HistorialAsignacion
        {
            IdCaso         = idCaso,
            IdProfesional  = idProfesional,
            Motivo         = motivo,
            IdRealizadoPor = idRealizadoPor
        });

    public void SincronizarPacienteProfesional(int idPaciente, int idProfesional) =>
        new PacienteProfesionalDAO().Asignar(idPaciente, idProfesional);
}
