// Candidato a recibir un caso, ya resuelto desde la base (Disponible y CasosActivos
// calculados): la cascada no sabe nada de SQL, solo de esta lista.
public record CandidatoAsignacion(int IdProfesional, bool Disponible, int CasosActivos, DateTime? UltimaAsignacion);

// A quién asignar (null = nadie, queda sin asignar) y por qué, con el mismo vocabulario
// cerrado que HistorialAsignacion.Motivo.
public record ResultadoAsignacion(int? IdProfesional, string Motivo);

/// <summary>
/// Asignación en cascada de casos a profesionales (Fase 2):
///  1. Grupo: lo decide quien llama (regla de derivación o elección manual), no este servicio.
///  2. Entre los profesionales del grupo: el disponible con menos carga.
///  3. Desempate: menor carga -> antigüedad de la última asignación (el que hace más
///     tiempo no recibe un caso nuevo) -> aleatorio con el generador inyectado.
///  4. Si no hay nadie disponible: sin asignar, motivo "SinDisponibles", visible para
///     el administrador.
/// Único lugar que decide asignaciones: lo usan la admisión y, a futuro, el bot y los
/// canales. "Disponible" y "carga" se calculan en IAsignacionRepositorio.ObtenerCandidatos
/// (Profesional.Estado = 'Activo' + al menos una franja de Agenda con Disponible = 1;
/// carga = Caso en Asignado/En atención) — acá solo se decide con la lista ya resuelta.
///
/// TODO(JWT): IdUsuario/IdCreadoPor vienen de X-Usuario-Id (sin firmar), igual que el resto
/// de la app — ver Services/AutorizacionReporte.cs.
/// </summary>
public class AsignacionService
{
    private readonly IAsignacionRepositorio _repo;
    private readonly Random _aleatorio;

    public AsignacionService(IAsignacionRepositorio repo, Random? aleatorio = null)
    {
        _repo = repo;
        _aleatorio = aleatorio ?? new Random();
    }

    /// <summary>
    /// Lógica pura de la cascada (pasos 2 y 3): sin acceso a datos, se prueba sin base.
    /// </summary>
    public static ResultadoAsignacion Elegir(IReadOnlyList<CandidatoAsignacion> candidatos, Random aleatorio)
    {
        var disponibles = candidatos.Where(c => c.Disponible).ToList();
        if (disponibles.Count == 0)
            return new ResultadoAsignacion(null, "SinDisponibles");

        int cargaMinima = disponibles.Min(c => c.CasosActivos);
        var porCarga = disponibles.Where(c => c.CasosActivos == cargaMinima).ToList();
        if (porCarga.Count == 1)
            return new ResultadoAsignacion(porCarga[0].IdProfesional, "AutomaticaPorCarga");

        // Nunca asignado (UltimaAsignacion null) cuenta como lo más antiguo posible.
        DateTime masAntigua = porCarga.Min(c => c.UltimaAsignacion ?? DateTime.MinValue);
        var porAntiguedad = porCarga.Where(c => (c.UltimaAsignacion ?? DateTime.MinValue) == masAntigua).ToList();
        if (porAntiguedad.Count == 1)
            return new ResultadoAsignacion(porAntiguedad[0].IdProfesional, "AutomaticaPorCarga");

        var elegido = porAntiguedad[aleatorio.Next(porAntiguedad.Count)];
        return new ResultadoAsignacion(elegido.IdProfesional, "AutomaticaAleatoria");
    }

    /// <summary>
    /// Deriva un caso a un grupo y sugiere profesional con la cascada. Deja una
    /// CasoDerivacion "Pendiente de confirmar" — nunca asigna en firme sola.
    /// Si nadie está disponible, igual se registra el intento (HistorialAsignacion
    /// con profesional nulo) para que la cola sin asignar se pueda explicar.
    /// </summary>
    public CasoDerivacion SugerirDerivacion(int idCaso, int idGrupo, string origen, int? idCreadoPor, string? motivoManual = null)
    {
        var resultado = Elegir(_repo.ObtenerCandidatos(idGrupo), _aleatorio);

        var derivacion = new CasoDerivacion
        {
            IdCaso                = idCaso,
            IdGrupo               = idGrupo,
            IdProfesionalSugerido = resultado.IdProfesional,
            Origen                = origen,
            Estado                = "Pendiente de confirmar",
            Motivo                = origen == "Manual" ? motivoManual : resultado.Motivo,
            IdCreadoPor           = idCreadoPor
        };
        derivacion.IdCasoDerivacion = _repo.InsertarCasoDerivacion(derivacion);

        _repo.ActualizarCaso(idCaso, "Derivado", idGrupo, null);

        if (resultado.IdProfesional is null)
            _repo.InsertarHistorial(idCaso, null, "SinDisponibles", idCreadoPor);

        return derivacion;
    }

    /// <summary>Confirma la sugerencia tal cual quedó (requiere que haya alguien sugerido).</summary>
    public void ConfirmarDerivacion(int idCasoDerivacion, int? idUsuario)
    {
        var d = _repo.ObtenerCasoDerivacion(idCasoDerivacion)
            ?? throw new InvalidOperationException("No existe esa derivación.");
        if (d.IdProfesionalSugerido is null)
            throw new InvalidOperationException("No hay profesional sugerido para confirmar; corregí o reasigná manualmente.");

        _repo.ResolverCasoDerivacion(idCasoDerivacion, "Confirmada", d.IdGrupo, d.IdProfesionalSugerido);
        _repo.ActualizarCaso(d.IdCaso, "Asignado", d.IdGrupo, d.IdProfesionalSugerido);
        _repo.InsertarHistorial(d.IdCaso, d.IdProfesionalSugerido, d.Motivo ?? "Manual", idUsuario);
        SincronizarPacienteProfesional(d.IdCaso, d.IdProfesionalSugerido.Value);
    }

    /// <summary>Confirma cambiando el grupo y/o el profesional que había sugerido la cascada.</summary>
    public void CorregirDerivacion(int idCasoDerivacion, int? idUsuario, int? nuevoGrupo = null, int? nuevoProfesional = null)
    {
        var d = _repo.ObtenerCasoDerivacion(idCasoDerivacion)
            ?? throw new InvalidOperationException("No existe esa derivación.");
        int grupo = nuevoGrupo ?? d.IdGrupo;
        int? profesional = nuevoProfesional ?? d.IdProfesionalSugerido;

        _repo.ResolverCasoDerivacion(idCasoDerivacion, "Corregida", grupo, profesional);
        _repo.ActualizarCaso(d.IdCaso, profesional is null ? "Derivado" : "Asignado", grupo, profesional);
        if (profesional is not null)
        {
            _repo.InsertarHistorial(d.IdCaso, profesional, "Manual", idUsuario);
            SincronizarPacienteProfesional(d.IdCaso, profesional.Value);
        }
    }

    /// <summary>Rechaza la sugerencia: el caso queda derivado al grupo pero sin profesional.</summary>
    public void RechazarDerivacion(int idCasoDerivacion, int? idUsuario)
    {
        var d = _repo.ObtenerCasoDerivacion(idCasoDerivacion)
            ?? throw new InvalidOperationException("No existe esa derivación.");
        _repo.ResolverCasoDerivacion(idCasoDerivacion, "Rechazada", d.IdGrupo, null);
    }

    /// <summary>
    /// Reasignación manual (administrador): no pasa por la cascada, el profesional ya
    /// viene elegido. Deja su propia CasoDerivacion (Origen Manual, ya Confirmada) para
    /// que el historial del caso quede completo, igual que una sugerida.
    /// </summary>
    public void ReasignarManual(int idCaso, int idProfesional, int? idUsuario, string? motivo = null)
    {
        var caso = _repo.ObtenerCaso(idCaso) ?? throw new InvalidOperationException("No existe ese caso.");
        int grupo = caso.IdGrupoActual ?? throw new InvalidOperationException("El caso todavía no tiene grupo; derivalo primero.");

        _repo.InsertarCasoDerivacion(new CasoDerivacion
        {
            IdCaso                = idCaso,
            IdGrupo               = grupo,
            IdProfesionalAsignado = idProfesional,
            Origen                = "Manual",
            Estado                = "Confirmada",
            Motivo                = motivo ?? "Manual",
            IdCreadoPor           = idUsuario,
            FechaResolucion       = DateTime.Now
        });

        string nuevoEstado = caso.Estado is "Nuevo" or "Derivado" ? "Asignado" : caso.Estado;
        _repo.ActualizarCaso(idCaso, nuevoEstado, grupo, idProfesional);
        _repo.InsertarHistorial(idCaso, idProfesional, "Manual", idUsuario);
        _repo.SincronizarPacienteProfesional(caso.IdPaciente, idProfesional);
    }

    // Confirmar/Corregir solo tienen el caso por id; buscan el paciente para el vínculo.
    private void SincronizarPacienteProfesional(int idCaso, int idProfesional)
    {
        var caso = _repo.ObtenerCaso(idCaso);
        if (caso is not null) _repo.SincronizarPacienteProfesional(caso.IdPaciente, idProfesional);
    }
}
