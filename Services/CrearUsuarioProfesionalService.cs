using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>Qué puede pasar al crear y vincular el usuario de un profesional (lo decide la base, en la transacción).</summary>
public enum ResultadoCreacionUsuario
{
    Creado,
    ProfesionalInexistente,
    ProfesionalNoActivo,
    YaTieneUsuario,
    UsernameEnUso,
    MailEnUso
}

/// <summary>Acceso a datos de "crear el usuario de un profesional" (lo implementa UsuarioProfesionalDAO).</summary>
public interface IUsuarioProfesionalAdminRepositorio
{
    Profesional? ObtenerProfesional(int idProfesional);

    /// <summary>
    /// En UNA transacción: vuelve a comprobar que el profesional existe, está Activo y no tiene usuario, que el
    /// usuario y el mail están libres, y recién entonces crea el Usuario y su vínculo. O quedan los dos o ninguno.
    /// </summary>
    (ResultadoCreacionUsuario Resultado, int IdUsuario) CrearUsuarioYVincular(
        int idProfesional, string username, string passwordHash, string mail, DateTime fechaVinculacion);
}

public sealed record SolicitudCrearUsuarioProfesional(string? Username, string? Mail, string? Contrasena, bool GenerarContrasena);

public sealed record ResultadoCrearUsuarioProfesional(
    int StatusCode, string? Mensaje = null, int IdUsuario = 0, string? Username = null, string? Mail = null, string? ContrasenaTemporal = null)
{
    public bool Ok => StatusCode == 201;
}

/// <summary>
/// Crear el usuario de un profesional ya aprobado y vincularlo (opción A de docs/PROGRESO_NOCTURNO.md, Tarea 3).
/// Lo usa el Configurador desde "Pacientes y profesionales" / "Solicitudes de registro".
///
/// Va en UN solo paso en el servidor, y no como POST /usuarios + POST /usuarioprofesionales desde el navegador,
/// porque el rol sale de los vínculos: un usuario sin vínculo a paciente ni a profesional entra como
/// CONFIGURADOR (POST /login). Entre las dos llamadas la cuenta nueva sería un administrador, y si la segunda
/// no llega queda un administrador con clave temporal.
///
/// Valida: el profesional existe, está Activo y no tiene ya un usuario; usuario de 3 a 50 caracteres (letras,
/// números, punto, guion y guion bajo) y no usado; contraseña de al menos 8 (o se genera una temporal); mail
/// válido y no usado (por defecto, el del profesional). La contraseña, escrita o generada, nunca se devuelve ni
/// se audita salvo la temporal, una sola vez, en esta respuesta.
/// </summary>
public class CrearUsuarioProfesionalService
{
    public const int LargoMinimoContrasena = 8;
    public const int LargoMaximoContrasena = 100;
    public const int LargoContrasenaTemporal = 14;

    private static readonly Regex PatronUsername = new("^[a-zA-Z0-9_.-]{3,50}$", RegexOptions.Compiled);

    private readonly IUsuarioProfesionalAdminRepositorio _repo;
    private readonly TimeProvider _reloj;

    public CrearUsuarioProfesionalService(IUsuarioProfesionalAdminRepositorio repo, TimeProvider reloj)
    {
        _repo = repo;
        _reloj = reloj;
    }

    public ResultadoCrearUsuarioProfesional Crear(int idProfesional, SolicitudCrearUsuarioProfesional req)
    {
        var profesional = _repo.ObtenerProfesional(idProfesional);
        if (profesional is null) return Falla(404, "No existe ese profesional.");
        if (profesional.Estado != "Activo")
            return Falla(409, "El profesional tiene que estar activo (aprobado) para crearle un usuario.");

        var username = req.Username?.Trim() ?? "";
        if (!PatronUsername.IsMatch(username))
            return Falla(400, "El usuario tiene que tener entre 3 y 50 caracteres (letras, números, puntos, guiones o guion bajo).");

        var mail = (string.IsNullOrWhiteSpace(req.Mail) ? profesional.Mail : req.Mail)?.Trim() ?? "";
        if (mail.Length == 0) return Falla(400, "Falta el mail: el profesional no tiene uno cargado, escribilo en el formulario.");
        if (mail.Length > 100 || !EsMailValido(mail)) return Falla(400, "El mail no tiene un formato válido.");

        string? temporal = null;
        string contrasena;
        if (req.GenerarContrasena)
        {
            contrasena = temporal = GenerarContrasenaTemporal();
        }
        else
        {
            contrasena = req.Contrasena ?? "";
            if (contrasena.Length < LargoMinimoContrasena)
                return Falla(400, $"La contraseña tiene que tener al menos {LargoMinimoContrasena} caracteres.");
            if (contrasena.Length > LargoMaximoContrasena)
                return Falla(400, $"La contraseña no puede tener más de {LargoMaximoContrasena} caracteres.");
        }

        var (resultado, idUsuario) = _repo.CrearUsuarioYVincular(
            idProfesional, username, HashearContrasena(contrasena), mail, _reloj.GetLocalNow().Date);

        return resultado switch
        {
            ResultadoCreacionUsuario.Creado => new ResultadoCrearUsuarioProfesional(201, null, idUsuario, username, mail, temporal),
            ResultadoCreacionUsuario.ProfesionalInexistente => Falla(404, "No existe ese profesional."),
            ResultadoCreacionUsuario.ProfesionalNoActivo => Falla(409, "El profesional tiene que estar activo (aprobado) para crearle un usuario."),
            ResultadoCreacionUsuario.YaTieneUsuario => Falla(409, "Ese profesional ya tiene un usuario."),
            ResultadoCreacionUsuario.UsernameEnUso => Falla(409, "Ese nombre de usuario ya está en uso."),
            ResultadoCreacionUsuario.MailEnUso => Falla(409, "Ese mail ya está asociado a otra cuenta."),
            _ => Falla(500, "No se pudo crear el usuario.")
        };
    }

    /// <summary>
    /// Contraseña temporal legible: sin caracteres que se confunden (0/O, 1/l/I) y con al menos una mayúscula,
    /// una minúscula y un número. Del generador criptográfico del sistema.
    /// </summary>
    public static string GenerarContrasenaTemporal(int largo = LargoContrasenaTemporal)
    {
        const string mayusculas = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string minusculas = "abcdefghijkmnopqrstuvwxyz";
        const string numeros    = "23456789";
        const string todos      = mayusculas + minusculas + numeros;
        if (largo < 8) throw new ArgumentOutOfRangeException(nameof(largo));

        var caracteres = new char[largo];
        caracteres[0] = mayusculas[RandomNumberGenerator.GetInt32(mayusculas.Length)];
        caracteres[1] = minusculas[RandomNumberGenerator.GetInt32(minusculas.Length)];
        caracteres[2] = numeros[RandomNumberGenerator.GetInt32(numeros.Length)];
        for (var i = 3; i < largo; i++) caracteres[i] = todos[RandomNumberGenerator.GetInt32(todos.Length)];

        // Fisher-Yates con el mismo generador, para que las tres obligatorias no queden siempre al principio.
        for (var i = largo - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (caracteres[i], caracteres[j]) = (caracteres[j], caracteres[i]);
        }
        return new string(caracteres);
    }

    // El mismo hash que usan /login y POST /usuarios: SHA-256 en hexadecimal.
    internal static string HashearContrasena(string contrasena) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contrasena)));

    private static bool EsMailValido(string mail)
    {
        try { return new System.Net.Mail.MailAddress(mail).Address == mail; }
        catch { return false; }
    }

    private static ResultadoCrearUsuarioProfesional Falla(int status, string mensaje) => new(status, mensaje);
}
