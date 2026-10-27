using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

// Alta de usuarios del equipo (octubre 2026, definido el 18/10).
//
// El Configurador registra a quien va a trabajar con el sistema: nombre, apellido, CUIL,
// teléfono, email, usuario y su rol. Si es Profesional, elige "Profesional" (ve solo sus
// pacientes) o "Profesional auditor" (todos los pacientes, reportes y registros), puede
// agregarle el privilegio Admisión, y se cargan matrícula y especialidad (RN-17: no hay rol
// Profesional sin ficha con matrícula). Todo se crea en una transacción: cuenta, ficha,
// permisos e invitación, o nada.
//
// La cuenta nace en estado 'Pendiente' y sin contraseña utilizable. Al empleado le llega un
// mail con un link (válido 72 horas, un solo uso) para confirmar la cuenta y elegir su
// contraseña. Hasta entonces no puede ingresar. En la base solo queda el hash del token.

public record AltaEquipoRequest(
    string? Nombre, string? Apellido, string? Cuil, string? Telefono, string? Mail, string? Username,
    List<string>? Permisos, List<PrivilegioPedido>? Privilegios,
    string? Matricula, string? JurisdiccionMatricula, int? IdEspecialidad,
    // Migración 33: vencimiento de la matrícula principal (AAAA-MM-DD). Obligatorio para el rol Profesional.
    string? VencimientoMatricula = null,
    // Otras especialidades que atiende, cada una con su matrícula (opcionales; la principal va arriba).
    List<MatriculaAdicional>? MatriculasAdicionales = null);

/// <summary>Una especialidad más del profesional, con su propia matrícula. Vencimiento en AAAA-MM-DD.</summary>
public record MatriculaAdicional(int? IdEspecialidad, string? Numero, string? Jurisdiccion, string? Vencimiento);

public record ActivarCuentaRequest(string? Token, string? Password);
public record AgregarPrivilegioRequest(string? Privilegio);

public static class AltaEquipo
{
    public static readonly TimeSpan VigenciaInvitacion = TimeSpan.FromHours(72);

    private static readonly Regex UsuarioValido = new("^[a-z0-9._-]{3,50}$");
    private static readonly Regex MailValido = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
    private static readonly Regex TelefonoValido = new(@"^[0-9+()\- ]{6,30}$");

    /// <summary>Trim, minúsculas en mail y usuario, solo dígitos en el CUIL.</summary>
    public static DateTime? VencimientoMatricula(AltaEquipoRequest r) => Fecha(r.VencimientoMatricula);

    /// <summary>Una fecha AAAA-MM-DD, o null si no es válida.</summary>
    public static DateTime? Fecha(string? texto) =>
        DateTime.TryParseExact(texto?.Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var v) ? v : null;

    /// <summary>Máximo de especialidades adicionales en el alta (además de la principal).</summary>
    public const int MaximoAdicionales = 5;

    public static AltaEquipoRequest Normalizar(AltaEquipoRequest r) => r with
    {
        Nombre = r.Nombre?.Trim(),
        Apellido = r.Apellido?.Trim(),
        Cuil = r.Cuil is null ? null : new string(r.Cuil.Where(char.IsDigit).ToArray()),
        Telefono = r.Telefono?.Trim(),
        Mail = r.Mail?.Trim().ToLowerInvariant(),
        Username = r.Username?.Trim().ToLowerInvariant(),
        Permisos = (r.Permisos ?? new()).Distinct().ToList(),
        Privilegios = (r.Privilegios ?? new()).Distinct().ToList(),
        Matricula = string.IsNullOrWhiteSpace(r.Matricula) ? null : r.Matricula.Trim(),
        JurisdiccionMatricula = string.IsNullOrWhiteSpace(r.JurisdiccionMatricula) ? null : r.JurisdiccionMatricula.Trim(),
        // Sin jurisdicción propia, una matrícula adicional toma la de la principal.
        MatriculasAdicionales = (r.MatriculasAdicionales ?? new()).Select(m => m with
        {
            Numero = string.IsNullOrWhiteSpace(m.Numero) ? null : m.Numero.Trim(),
            Jurisdiccion = string.IsNullOrWhiteSpace(m.Jurisdiccion)
                ? (string.IsNullOrWhiteSpace(r.JurisdiccionMatricula) ? null : r.JurisdiccionMatricula.Trim())
                : m.Jurisdiccion.Trim(),
            Vencimiento = m.Vencimiento?.Trim()
        }).ToList()
    };

    /// <summary>
    /// CUIL de persona: 11 dígitos, prefijo 20, 23, 24 o 27 y dígito verificador módulo 11
    /// (pesos 5-4-3-2-7-6-5-4-3-2, como lo calcula AFIP/ARCA).
    /// </summary>
    public static bool CuilValido(string? cuil)
    {
        if (cuil is null || cuil.Length != 11 || !cuil.All(char.IsDigit)) return false;
        if (cuil[..2] is not ("20" or "23" or "24" or "27")) return false;
        int[] pesos = { 5, 4, 3, 2, 7, 6, 5, 4, 3, 2 };
        var suma = 0;
        for (var i = 0; i < 10; i++) suma += (cuil[i] - '0') * pesos[i];
        var resto = 11 - suma % 11;
        var verificador = resto == 11 ? 0 : resto == 10 ? -1 : resto;
        return verificador == cuil[10] - '0';
    }

    /// <summary>El DNI va del tercer al décimo dígito del CUIL.</summary>
    public static string DniDeCuil(string cuil) => cuil.Substring(2, 8).TrimStart('0');

    public static bool EsProfesional(AltaEquipoRequest r) =>
        (r.Permisos ?? new()).Any(p => p is Roles.Profesional or Roles.Auditor);

    /// <summary>Motivo del rechazo, o null si el pedido (ya normalizado) es válido. Sin base de datos.</summary>
    public static string? Validar(AltaEquipoRequest r, IReadOnlyList<PermisoCatalogo> catalogo)
    {
        if (string.IsNullOrEmpty(r.Nombre) || r.Nombre.Length > 100) return "Completá el nombre (hasta 100 caracteres).";
        if (string.IsNullOrEmpty(r.Apellido) || r.Apellido.Length > 100) return "Completá el apellido (hasta 100 caracteres).";
        if (!CuilValido(r.Cuil)) return "El CUIL no es válido: tienen que ser 11 dígitos y el último es el verificador.";
        if (string.IsNullOrEmpty(r.Telefono) || !TelefonoValido.IsMatch(r.Telefono)) return "Completá un teléfono de contacto válido.";
        if (string.IsNullOrEmpty(r.Mail) || r.Mail.Length > 100 || !MailValido.IsMatch(r.Mail)) return "Completá un email válido: ahí le llega la invitación.";
        if (string.IsNullOrEmpty(r.Username) || !UsuarioValido.IsMatch(r.Username))
            return "El usuario va en minúsculas, de 3 a 50 caracteres, sin espacios (letras, números, punto, guion).";

        var permisos = r.Permisos ?? new();
        if (permisos.Count == 0) return "Elegí al menos un rol: sin rol, la persona no ve nada al ingresar.";
        if (permisos.Contains(Roles.Profesional) && permisos.Contains(Roles.Auditor))
            return "Elegí Profesional o Profesional auditor, no los dos (el auditor ya tiene todo lo del profesional).";

        if (EsProfesional(r))
        {
            if (string.IsNullOrEmpty(r.Matricula) || r.Matricula.Length > 50) return "Para el rol Profesional, completá la matrícula.";
            if (string.IsNullOrEmpty(r.JurisdiccionMatricula) || r.JurisdiccionMatricula.Length > 60) return "Indicá la jurisdicción de la matrícula (por ejemplo, Córdoba).";
            if (r.IdEspecialidad is not > 0) return "Para el rol Profesional, elegí la especialidad principal.";
            if (VencimientoMatricula(r) is not DateTime vence) return "Indicá la fecha de vencimiento de la matrícula.";
            if (vence.Date <= DateTime.Today) return "La matrícula ya está vencida: cargá la fecha de la renovación.";

            var adicionales = r.MatriculasAdicionales ?? new();
            if (adicionales.Count > MaximoAdicionales) return $"Se pueden agregar hasta {MaximoAdicionales} especialidades además de la principal.";
            var elegidas = new HashSet<int> { r.IdEspecialidad!.Value };
            foreach (var m in adicionales)
            {
                if (m.IdEspecialidad is not > 0) return "En cada especialidad agregada, elegí cuál es.";
                if (!elegidas.Add(m.IdEspecialidad.Value)) return "Hay una especialidad repetida: cada especialidad va una sola vez.";
                if (string.IsNullOrEmpty(m.Numero) || m.Numero.Length > 50) return "Completá la matrícula de cada especialidad agregada.";
                if (string.IsNullOrEmpty(m.Jurisdiccion) || m.Jurisdiccion.Length > 60) return "Indicá la jurisdicción de cada matrícula agregada.";
                if (Fecha(m.Vencimiento) is not DateTime v) return "Indicá el vencimiento de cada matrícula agregada.";
                if (v.Date <= DateTime.Today) return "Una de las matrículas agregadas ya está vencida: cargá la fecha de la renovación.";
            }
        }

        return AccesosEdicion.Validar(catalogo, new AccesosEdicionRequest(permisos, r.Privilegios),
            new AccesosEdicion.Situacion(EsPaciente: false, TieneFichaProfesional: EsProfesional(r), EsUnoMismo: false, TeniaConfigurador: false));
    }

    /// <summary>
    /// Contraseña al activar la cuenta. Primero la política de la aplicación (<see cref="PoliticaContrasena"/>: nunca más débil que la de
    /// los pacientes y los restablecimientos) y, para las cuentas del equipo, además 8 o más caracteres con letras y números.
    /// </summary>
    public static string? ValidarContrasena(string? password)
    {
        var general = PoliticaContrasena.Validar(password);
        if (general is not null) return general;
        if (password!.Length < 8) return "La contraseña tiene que tener al menos 8 caracteres.";
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit)) return "La contraseña tiene que tener letras y números.";
        return null;
    }

    public static string NuevoToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    // Mismo formato que ya compara POST /login (SHA-256 en hexadecimal). Pendiente para
    // producción: pasar a un hash con sal y costo (PBKDF2), para todas las cuentas a la vez.
    public static string HashContrasena(string password) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
}

public record AltaEquipoCreada(int IdUsuario, string Token, DateTime Vence);
public record InvitacionInfo(int IdInvitacion, int IdUsuario, string Username, string? Nombre, string? Mail, string EstadoCuenta,
    DateTime Vence, DateTime? Usada, DateTime? Anulada);
public record EspecialidadOpcion(int IdEspecialidad, string Nombre);

public class AltaEquipoDAO
{
    /// <summary>Especialidades que se pueden elegir como principal (activas, no las filas de servicio).</summary>
    public List<EspecialidadOpcion> Especialidades()
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            SELECT IdEspecialidad, Nombre FROM Especialidad
            WHERE Estado = 'Activo' AND ISNULL(Tipo, '') <> 'Servicio'
            ORDER BY Nombre", con);
        using var rd = cmd.ExecuteReader();
        var lista = new List<EspecialidadOpcion>();
        while (rd.Read()) lista.Add(new EspecialidadOpcion(rd.GetInt32(0), rd.GetString(1)));
        return lista;
    }

    /// <summary>Usuario, email, CUIL o matrícula ya usados; o la especialidad no se puede elegir.</summary>
    public string? Conflicto(AltaEquipoRequest r)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        bool Existe(string sql, params (string, object)[] ps)
        {
            using var cmd = new SqlCommand(sql, con);
            foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
            return cmd.ExecuteScalar() is not null;
        }
        if (Existe("SELECT 1 FROM Usuario WHERE Username = @V", ("@V", r.Username!))) return $"Ya existe el usuario \"{r.Username}\". Elegí otro.";
        if (Existe("SELECT 1 FROM Usuario WHERE Mail = @V", ("@V", r.Mail!))) return "Ya hay una cuenta con ese email.";
        if (Existe("SELECT 1 FROM Usuario WHERE Cuil = @V", ("@V", r.Cuil!))) return "Ya hay una cuenta con ese CUIL.";
        if (AltaEquipo.EsProfesional(r))
        {
            if (Existe("SELECT 1 FROM Profesional WHERE Matricula = @V", ("@V", r.Matricula!))) return "Ya hay un profesional con esa matrícula.";
            if (!Existe("SELECT 1 FROM Especialidad WHERE IdEspecialidad = @V AND Estado = 'Activo' AND ISNULL(Tipo, '') <> 'Servicio'", ("@V", r.IdEspecialidad!.Value)))
                return "Esa especialidad no está disponible.";
            foreach (var m in r.MatriculasAdicionales ?? new())
                if (!Existe("SELECT 1 FROM Especialidad WHERE IdEspecialidad = @V AND Estado = 'Activo' AND ISNULL(Tipo, '') <> 'Servicio'", ("@V", m.IdEspecialidad!.Value)))
                    return "Una de las especialidades agregadas no está disponible.";
        }
        return null;
    }

    /// <summary>Cuenta, ficha de profesional, permisos e invitación, en una sola transacción.</summary>
    public AltaEquipoCreada Crear(AltaEquipoRequest r, int idAutor)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();
        try
        {
            // Contraseña inutilizable hasta que la persona active la cuenta: no es un hash
            // SHA-256 válido, así que ninguna contraseña coincide.
            var idUsuario = Escalar(con, tx, @"
                INSERT INTO Usuario (Username, PasswordHash, Estado, Mail, IntentosFallidos, Nombre, Apellido, Cuil, Telefono)
                OUTPUT INSERTED.IdUsuario
                VALUES (@Username, @Hash, 'Pendiente', @Mail, 0, @Nombre, @Apellido, @Cuil, @Telefono);",
                ("@Username", r.Username!), ("@Hash", "PENDIENTE-" + AltaEquipo.NuevoToken()), ("@Mail", r.Mail!),
                ("@Nombre", r.Nombre!), ("@Apellido", r.Apellido!), ("@Cuil", r.Cuil!), ("@Telefono", r.Telefono!));

            if (AltaEquipo.EsProfesional(r))
            {
                var idProfesional = Escalar(con, tx, @"
                    INSERT INTO Profesional (Nombre, Apellido, Matricula, JurisdiccionMatricula, IdEspecialidad, Estado, Mail, Telefono, Dni, FechaSolicitud)
                    OUTPUT INSERTED.IdProfesional
                    VALUES (@Nombre, @Apellido, @Matricula, @Jurisdiccion, @IdEsp, 'Activo', @Mail, @Telefono, @Dni, GETDATE());",
                    ("@Nombre", r.Nombre!), ("@Apellido", r.Apellido!), ("@Matricula", r.Matricula!), ("@Jurisdiccion", r.JurisdiccionMatricula!),
                    ("@IdEsp", r.IdEspecialidad!.Value), ("@Mail", r.Mail!), ("@Telefono", r.Telefono!), ("@Dni", AltaEquipo.DniDeCuil(r.Cuil!)));
                AccesosEdicionDAO.Ejecutar(con, tx,
                    "INSERT INTO UsuarioProfesional (IdUsuario, IdProfesional, FechaVinculacion) VALUES (@U, @P, CAST(GETDATE() AS DATE));",
                    ("@U", idUsuario), ("@P", idProfesional));
                // Migración 33: la matrícula de la ficha es la principal, con su vencimiento (Validar ya exigió la fecha).
                AccesosEdicionDAO.Ejecutar(con, tx, @"
                    INSERT INTO ProfesionalMatricula (IdProfesional, IdEspecialidad, Numero, Jurisdiccion, Vencimiento, Principal, IdUsuarioModifico)
                    VALUES (@P, @E, @N, @J, @V, 1, @A);",
                    ("@P", idProfesional), ("@E", r.IdEspecialidad!.Value), ("@N", r.Matricula!), ("@J", r.JurisdiccionMatricula!),
                    ("@V", AltaEquipo.VencimientoMatricula(r)!.Value.Date), ("@A", idAutor));
                // Las otras especialidades, cada una con su matrícula (no principales).
                foreach (var m in r.MatriculasAdicionales ?? new())
                    AccesosEdicionDAO.Ejecutar(con, tx, @"
                        INSERT INTO ProfesionalMatricula (IdProfesional, IdEspecialidad, Numero, Jurisdiccion, Vencimiento, Principal, IdUsuarioModifico)
                        VALUES (@P, @E, @N, @J, @V, 0, @A);",
                        ("@P", idProfesional), ("@E", m.IdEspecialidad!.Value), ("@N", m.Numero!), ("@J", m.Jurisdiccion!),
                        ("@V", AltaEquipo.Fecha(m.Vencimiento)!.Value.Date), ("@A", idAutor));
            }

            // Roles, permisos y privilegios: mismas reglas y la misma auditoría que el editor.
            new AccesosEdicionDAO().GuardarEn(con, tx, idUsuario, new AccesosEdicionRequest(r.Permisos, r.Privilegios), idAutor);

            var (token, vence) = InsertarInvitacion(con, tx, idUsuario, idAutor);
            Auditar(con, tx, idAutor, "Alta de usuario del equipo", idUsuario, $"Usuario {r.Username} · invitación enviada, vence {vence:dd/MM/yyyy HH:mm}");

            AccesosEdicionDAO.Ejecutar(con, tx, "EXEC sp_set_session_context @key = N'IdUsuario', @value = NULL;");
            tx.Commit();
            return new AltaEquipoCreada(idUsuario, token, vence);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>Anula las invitaciones abiertas y genera una nueva. Null si la cuenta ya no está pendiente.</summary>
    public (string Token, DateTime Vence)? NuevaInvitacion(int idUsuario, int idAutor)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();
        try
        {
            var estado = Escalar<string>(con, tx, "SELECT Estado FROM Usuario WHERE IdUsuario = @U", ("@U", idUsuario));
            if (estado != "Pendiente") { tx.Rollback(); return null; }
            AccesosEdicionDAO.Ejecutar(con, tx,
                "UPDATE InvitacionCuenta SET Anulada = SYSDATETIME() WHERE IdUsuario = @U AND Usada IS NULL AND Anulada IS NULL;", ("@U", idUsuario));
            var (token, vence) = InsertarInvitacion(con, tx, idUsuario, idAutor);
            Auditar(con, tx, idAutor, "Invitación reenviada", idUsuario, $"Vence {vence:dd/MM/yyyy HH:mm}");
            tx.Commit();
            return (token, vence);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public InvitacionInfo? Buscar(string token)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            SELECT i.IdInvitacion, u.IdUsuario, u.Username, u.Nombre, u.Mail, u.Estado, i.Vence, i.Usada, i.Anulada
            FROM InvitacionCuenta i JOIN Usuario u ON u.IdUsuario = i.IdUsuario
            WHERE i.TokenHash = @H", con);
        cmd.Parameters.AddWithValue("@H", AltaEquipo.HashToken(token));
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;
        return new InvitacionInfo(rd.GetInt32(0), rd.GetInt32(1), rd.GetString(2), rd.IsDBNull(3) ? null : rd.GetString(3),
            rd.IsDBNull(4) ? null : rd.GetString(4), rd.IsDBNull(5) ? "" : rd.GetString(5), rd.GetDateTime(6),
            rd.IsDBNull(7) ? null : rd.GetDateTime(7), rd.IsDBNull(8) ? null : rd.GetDateTime(8));
    }

    /// <summary>Usa la invitación y deja la cuenta activa con la contraseña elegida. False si ya no sirve.</summary>
    public bool Activar(InvitacionInfo inv, string passwordHash)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();
        try
        {
            using (var cmd = new SqlCommand(@"
                UPDATE InvitacionCuenta SET Usada = SYSDATETIME()
                WHERE IdInvitacion = @I AND Usada IS NULL AND Anulada IS NULL AND Vence > SYSDATETIME();", con, tx))
            {
                cmd.Parameters.AddWithValue("@I", inv.IdInvitacion);
                if (cmd.ExecuteNonQuery() != 1) { tx.Rollback(); return false; }
            }
            using (var cmd = new SqlCommand(@"
                UPDATE Usuario SET PasswordHash = @H, Estado = 'Activo', IntentosFallidos = 0, BloqueadoHasta = NULL
                WHERE IdUsuario = @U AND Estado = 'Pendiente';", con, tx))
            {
                cmd.Parameters.AddWithValue("@H", passwordHash);
                cmd.Parameters.AddWithValue("@U", inv.IdUsuario);
                if (cmd.ExecuteNonQuery() != 1) { tx.Rollback(); return false; }
            }
            Auditar(con, tx, inv.IdUsuario, "Cuenta activada", inv.IdUsuario, "Confirmó la cuenta y eligió su contraseña desde la invitación");
            tx.Commit();
            return true;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public (string? Mail, string? Nombre, string Username)? DatosParaMail(int idUsuario)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand("SELECT Mail, Nombre, Username FROM Usuario WHERE IdUsuario = @U", con);
        cmd.Parameters.AddWithValue("@U", idUsuario);
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;
        return (rd.IsDBNull(0) ? null : rd.GetString(0), rd.IsDBNull(1) ? null : rd.GetString(1), rd.GetString(2));
    }

    private static (string Token, DateTime Vence) InsertarInvitacion(SqlConnection con, SqlTransaction tx, int idUsuario, int idAutor)
    {
        var token = AltaEquipo.NuevoToken();
        var vence = DateTime.Now.Add(AltaEquipo.VigenciaInvitacion);
        vence = new DateTime(vence.Year, vence.Month, vence.Day, vence.Hour, vence.Minute, vence.Second);
        AccesosEdicionDAO.Ejecutar(con, tx,
            "INSERT INTO InvitacionCuenta (IdUsuario, TokenHash, Vence, IdUsuarioCreo) VALUES (@U, @H, @V, @A);",
            ("@U", idUsuario), ("@H", AltaEquipo.HashToken(token)), ("@V", vence), ("@A", idAutor));
        return (token, vence);
    }

    // Dentro de la misma transacción: un INSERT desde otra conexión quedaría esperando los
    // bloqueos que esta ya tiene sobre LogAuditoria (los triggers de permisos escriben ahí).
    private static void Auditar(SqlConnection con, SqlTransaction tx, int? idUsuario, string accion, int idEntidad, string detalle) =>
        AccesosEdicionDAO.Ejecutar(con, tx, @"
            INSERT INTO LogAuditoria (IdUsuario, Accion, Entidad, IdEntidad, Detalle, Categoria, Espacio)
            VALUES (@U, @Accion, 'Usuario', @E, @D, 'Administrativa', @Espacio);",
            ("@U", (object?)idUsuario ?? DBNull.Value), ("@Accion", accion), ("@E", idEntidad), ("@D", detalle),
            ("@Espacio", idUsuario == idEntidad ? "cuenta" : "configurador"));

    private static int Escalar(SqlConnection con, SqlTransaction tx, string sql, params (string Nombre, object Valor)[] ps) =>
        Convert.ToInt32(Escalar<object>(con, tx, sql, ps));

    private static T? Escalar<T>(SqlConnection con, SqlTransaction tx, string sql, params (string Nombre, object Valor)[] ps)
    {
        using var cmd = new SqlCommand(sql, con, tx);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        var r = cmd.ExecuteScalar();
        return r is null or DBNull ? default : (T)r;
    }
}
