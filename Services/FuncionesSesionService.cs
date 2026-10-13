// Interruptores de la sala de video: análisis de expresiones por IA y transcripción
// (RN-10, RN-15). Un único punto de decisión, sin SQL, para poder probarlo con dobles en
// memoria (YoSoy.Tests/FuncionesSesionServiceTests.cs).
//
// Reglas:
// - Encender exige que el paciente de la sala tenga el consentimiento de esa función vigente.
// - Enciende solo el profesional asignado al paciente de la sala, o el Configurador
//   (entrevista de admisión).
// - Apagar lo puede hacer también el propio paciente, en cualquier momento: es su derecho
//   y no necesita permiso de nadie.
// - Si el paciente revoca el consentimiento con la función encendida, el estado que
//   devuelve la API pasa a apagado aunque la última fila diga "activa" (ver Estado).
public static class FuncionesSesion
{
    public const string IaExpresiones = "IaExpresiones";
    public const string Transcripcion = "Transcripcion";
    public static readonly string[] Todas = { IaExpresiones, Transcripcion };
    public static bool Existe(string? f) => f is not null && Todas.Contains(f);
}

public record SalaInfo(string CodigoSala, int IdPaciente, int? IdProfesional, string Origen, int? IdTurno, int? IdInforme);

public interface IAtencionRepositorio
{
    SalaInfo? ResolverSala(string codigoSala);
    bool ConsentimientoVigente(int idPaciente, string descripcionTipo);
    bool EsProfesionalDelPaciente(int idProfesional, int idPaciente);
    // Última fila registrada por función para esa sala (null si nunca se tocó).
    Dictionary<string, bool> UltimoEstado(string codigoSala);
    void RegistrarCambio(string codigoSala, int idPaciente, int? idUsuario, string rol, string funcion, bool activa);
}

public record EstadoFuncion(string Funcion, bool Consentida, bool Activa);

public record ResultadoFunciones(bool Ok, int Status, string? Codigo, string? Mensaje, IReadOnlyList<EstadoFuncion>? Estado)
{
    public static ResultadoFunciones Error(int status, string mensaje, string? codigo = null) => new(false, status, codigo, mensaje, null);
}

public class FuncionesSesionService
{
    private readonly IAtencionRepositorio _repo;
    public FuncionesSesionService(IAtencionRepositorio repo) => _repo = repo;

    /// <summary>¿Puede esta identidad ver el estado de la sala? Paciente de la sala, su profesional o el Configurador.</summary>
    public bool PuedeVer(Identidad yo, SalaInfo sala) =>
        yo.EsConfigurador
        || (yo.EsPaciente && yo.IdPaciente == sala.IdPaciente)
        || (yo.EsProfesional && yo.IdProfesional is int p && (sala.IdProfesional == p || _repo.EsProfesionalDelPaciente(p, sala.IdPaciente)));

    private bool PuedeEncender(Identidad yo, SalaInfo sala) =>
        yo.EsConfigurador
        || (yo.EsProfesional && yo.IdProfesional is int p && (sala.IdProfesional == p || _repo.EsProfesionalDelPaciente(p, sala.IdPaciente)));

    public IReadOnlyList<EstadoFuncion> Estado(SalaInfo sala)
    {
        var ultimo = _repo.UltimoEstado(sala.CodigoSala);
        return FuncionesSesion.Todas.Select(f =>
        {
            var tipo = ConsentimientosOpcionales.PorFuncion(f)!;
            var consentida = _repo.ConsentimientoVigente(sala.IdPaciente, tipo.Descripcion);
            // Sin consentimiento vigente, nunca figura activa (aunque la última fila lo diga).
            var activa = consentida && ultimo.TryGetValue(f, out var a) && a;
            return new EstadoFuncion(f, consentida, activa);
        }).ToList();
    }

    public ResultadoFunciones Consultar(Identidad? yo, string codigoSala)
    {
        if (yo is null) return ResultadoFunciones.Error(401, "Iniciá sesión.");
        var sala = _repo.ResolverSala(codigoSala);
        if (sala is null) return ResultadoFunciones.Error(404, "No existe esa sala.");
        if (!PuedeVer(yo, sala)) return ResultadoFunciones.Error(404, "No existe esa sala.");
        return new ResultadoFunciones(true, 200, null, null, Estado(sala));
    }

    public ResultadoFunciones Cambiar(Identidad? yo, string codigoSala, string? funcion, bool activa)
    {
        if (yo is null) return ResultadoFunciones.Error(401, "Iniciá sesión.");
        if (!FuncionesSesion.Existe(funcion)) return ResultadoFunciones.Error(400, "Función desconocida.");
        var sala = _repo.ResolverSala(codigoSala);
        if (sala is null || !PuedeVer(yo, sala)) return ResultadoFunciones.Error(404, "No existe esa sala.");

        if (activa)
        {
            if (!PuedeEncender(yo, sala))
                return ResultadoFunciones.Error(403, "Solo el profesional de la sesión puede encender esta función.");
            var tipo = ConsentimientosOpcionales.PorFuncion(funcion!)!;
            if (!_repo.ConsentimientoVigente(sala.IdPaciente, tipo.Descripcion))
                return ResultadoFunciones.Error(409,
                    $"El paciente no dio su consentimiento para \"{tipo.Titulo}\". La función queda apagada.",
                    funcion == FuncionesSesion.IaExpresiones ? "RN-10" : "RN-15");
        }
        // Apagar: cualquiera que pueda ver la sala (incluido el paciente).

        _repo.RegistrarCambio(sala.CodigoSala, sala.IdPaciente, yo.IdUsuario, yo.Rol, funcion!, activa);
        return new ResultadoFunciones(true, 200, null, null, Estado(sala));
    }
}
