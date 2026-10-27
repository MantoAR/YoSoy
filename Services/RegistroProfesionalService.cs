using Microsoft.Data.SqlClient;

/// <summary>
/// Lo que necesita el alta y la edición de un profesional de la base. Lo implementa ProfesionalDAO;
/// las pruebas usan uno en memoria.
/// </summary>
public interface IProfesionalRegistroRepositorio
{
    bool ExisteEspecialidad(int idEspecialidad);

    /// <summary>¿Hay un profesional con esa matrícula (distinto de <paramref name="excluirIdProfesional"/>)?</summary>
    bool ExisteMatricula(string matricula, int? excluirIdProfesional = null);

    /// <summary>
    /// ¿Hay un profesional con ese DNI (distinto de <paramref name="excluirIdProfesional"/>)? Se compara sin
    /// puntos, espacios ni guiones: ver <see cref="RegistroProfesionalService.NormalizarDni"/>.
    /// </summary>
    bool ExisteDni(string dniNormalizado, int? excluirIdProfesional = null);

    Profesional? ObtenerPorId(int idProfesional);
    int Insertar(Profesional p);
    void Modificar(Profesional p);
}

public enum EstadoRegistroProfesional { Ok, DatosInvalidos, EspecialidadInexistente, Duplicado }

public sealed record ResultadoRegistroProfesional(EstadoRegistroProfesional Estado, string? Mensaje = null, int IdProfesional = 0)
{
    public bool Ok => Estado == EstadoRegistroProfesional.Ok;

    /// <summary>409 si ya existe; 400 si los datos no sirven (faltan obligatorios o la especialidad no existe).</summary>
    public int StatusCode => Estado switch
    {
        EstadoRegistroProfesional.Ok        => 200,
        EstadoRegistroProfesional.Duplicado => 409,
        _                                   => 400
    };
}

/// <summary>
/// Alta pública (POST /profesionales) y edición (PUT /profesionales/{id}) de un profesional con los
/// errores previsibles resueltos como 4xx en vez de dejarlos caer en 500:
///  - matrícula o DNI ya registrados → 409 con un mensaje neutral (no dice cuál de los dos coincidió ni
///    de quién es, y es el mismo para los dos);
///  - especialidad que no existe → 400.
/// Primero se chequea contra la base (el caso normal) y, si dos pedidos se cruzan, el índice UNIQUE de
/// Matricula tira SqlException 2627/2601 y se responde igual (409); 547 es la clave foránea a Especialidad.
/// Ojo: Profesional.Dni NO tiene UNIQUE en el esquema (la columna admite NULL y hay datos viejos), así que
/// el DNI solo se controla acá, y una carrera entre dos altas con el mismo DNI no la frena la base.
/// </summary>
public class RegistroProfesionalService
{
    public const string MensajeDuplicadoAlta =
        "No se pudo registrar la solicitud: ya existe un registro con esa matrícula o ese DNI. " +
        "Si ya enviaste una solicitud, el equipo la está revisando; si creés que es un error, comunicate con nosotros.";

    public const string MensajeDuplicadoEdicion =
        "No se pudieron guardar los cambios: ya existe otro registro con esa matrícula o ese DNI. " +
        "Si creés que es un error, comunicate con el equipo de YoSoy.";

    public const string MensajeEspecialidad = "La especialidad elegida no existe.";
    public const string MensajeFaltanDatos  = "Faltan datos obligatorios: el nombre y la matrícula.";

    private readonly IProfesionalRegistroRepositorio _repo;

    public RegistroProfesionalService(IProfesionalRegistroRepositorio repo) => _repo = repo;

    /// <summary>El DNI sin puntos, espacios ni guiones ("12.345.678" y "12345678" son el mismo).</summary>
    public static string NormalizarDni(string dni) =>
        dni.Replace(".", "").Replace(" ", "").Replace("-", "").Trim();

    public ResultadoRegistroProfesional Registrar(Profesional p)
    {
        var invalido = Normalizar(p);
        if (invalido != null) return invalido;

        // Registros NUEVOS: la provincia, si viene, tiene que ser una de la lista (y se guarda con su nombre canónico). La edición (Modificar) no la exige: no se rompen los datos viejos.
        if (!string.IsNullOrWhiteSpace(p.Provincia))
        {
            var provincia = Provincias.Canonica(p.Provincia);
            if (provincia is null) return Resultado(EstadoRegistroProfesional.DatosInvalidos, Provincias.MensajeInvalida);
            p.Provincia = provincia;
        }

        if (!_repo.ExisteEspecialidad(p.IdEspecialidad))
            return Resultado(EstadoRegistroProfesional.EspecialidadInexistente, MensajeEspecialidad);

        if (_repo.ExisteMatricula(p.Matricula) || (p.Dni != null && _repo.ExisteDni(NormalizarDni(p.Dni))))
            return Resultado(EstadoRegistroProfesional.Duplicado, MensajeDuplicadoAlta);

        try
        {
            return new ResultadoRegistroProfesional(EstadoRegistroProfesional.Ok, null, _repo.Insertar(p));
        }
        catch (SqlException ex) when (ClasificarSql(ex.Number, MensajeDuplicadoAlta) is not null)
        {
            return ClasificarSql(ex.Number, MensajeDuplicadoAlta)!;
        }
    }

    /// <summary>
    /// Edición. La matrícula y el DNI solo se controlan si CAMBIARON respecto de lo guardado: una ficha vieja
    /// con un DNI repetido (la base lo permite) tiene que poder seguir guardando su perfil sin tocarlo.
    /// </summary>
    public ResultadoRegistroProfesional Modificar(int idProfesional, Profesional p)
    {
        var invalido = Normalizar(p);
        if (invalido != null) return invalido;
        p.IdProfesional = idProfesional;

        if (!_repo.ExisteEspecialidad(p.IdEspecialidad))
            return Resultado(EstadoRegistroProfesional.EspecialidadInexistente, MensajeEspecialidad);

        var actual = _repo.ObtenerPorId(idProfesional);

        var cambioMatricula = actual is null
            || !string.Equals(actual.Matricula?.Trim(), p.Matricula, StringComparison.OrdinalIgnoreCase);
        var cambioDni = p.Dni != null && (actual?.Dni is null
            || !string.Equals(NormalizarDni(actual.Dni), NormalizarDni(p.Dni), StringComparison.OrdinalIgnoreCase));

        if ((cambioMatricula && _repo.ExisteMatricula(p.Matricula, idProfesional))
            || (cambioDni && _repo.ExisteDni(NormalizarDni(p.Dni!), idProfesional)))
            return Resultado(EstadoRegistroProfesional.Duplicado, MensajeDuplicadoEdicion);

        try
        {
            _repo.Modificar(p);
            return new ResultadoRegistroProfesional(EstadoRegistroProfesional.Ok, null, idProfesional);
        }
        catch (SqlException ex) when (ClasificarSql(ex.Number, MensajeDuplicadoEdicion) is not null)
        {
            return ClasificarSql(ex.Number, MensajeDuplicadoEdicion)!;
        }
    }

    // Recorta los textos y exige lo que la tabla pide NOT NULL (Nombre y Matricula): antes, un cuerpo sin
    // alguno de los dos llegaba al INSERT y volvía como 500.
    private static ResultadoRegistroProfesional? Normalizar(Profesional p)
    {
        p.Nombre    = p.Nombre?.Trim()!;
        p.Matricula = p.Matricula?.Trim()!;
        p.Dni       = string.IsNullOrWhiteSpace(p.Dni) ? null : p.Dni.Trim();
        if (string.IsNullOrWhiteSpace(p.Nombre) || string.IsNullOrWhiteSpace(p.Matricula))
            return Resultado(EstadoRegistroProfesional.DatosInvalidos, MensajeFaltanDatos);
        return null;
    }

    /// <summary>2627/2601 (clave o índice único) → duplicado; 547 (clave foránea) → especialidad inexistente.</summary>
    internal static ResultadoRegistroProfesional? ClasificarSql(int numeroError, string mensajeDuplicado) => numeroError switch
    {
        2627 or 2601 => Resultado(EstadoRegistroProfesional.Duplicado, mensajeDuplicado),
        547          => Resultado(EstadoRegistroProfesional.EspecialidadInexistente, MensajeEspecialidad),
        _            => null
    };

    private static ResultadoRegistroProfesional Resultado(EstadoRegistroProfesional estado, string mensaje) => new(estado, mensaje);
}
