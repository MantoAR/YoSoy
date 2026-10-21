using System.Threading.RateLimiting;
using Microsoft.Data.SqlClient;

// Fase 2, PR 2: la pantalla Usuarios del Configurador (lista única, restablecer contraseña, asignar profesionales a un
// paciente) y el cambio obligatorio de contraseña al primer ingreso (migración 24). Los endpoints están en
// Endpoints/UsuariosAdminEndpoints.cs (más POST /sesion/cambiar-password, en Program.cs porque entrega la sesión por el mismo
// camino que /login). Todo es del Configurador, nada lleva datos clínicos y cada cambio queda en la auditoría administrativa.

public static class UsuariosAdminServiceCollectionExtensions
{
    public const string PoliticaRestablecer = "usuarios-restablecer";
    public const string PoliticaCambiarClave = "usuarios-cambiar-clave";
    public const string PoliticaAsignar = "usuarios-asignar";

    // Los límites son por IP (el limitador corre antes de saber quién es el usuario), con ventana fija, como las demás políticas.
    // Restablecer es la acción delicada: 10 cada 10 minutos por defecto (UsuariosAdmin:RestablecerPorDiezMinutos).
    public static IServiceCollection AddUsuariosAdmin(this IServiceCollection services, IConfiguration config)
    {
        var restablecer = Math.Max(1, config.GetValue("UsuariosAdmin:RestablecerPorDiezMinutos", 10));
        services.AddRateLimiter(opt =>
        {
            opt.AddPolicy(PoliticaRestablecer, ctx => Ventana(ctx, TimeSpan.FromMinutes(10), restablecer));
            // El token de cambio ya limita quién puede llamar (5 minutos, lo emite un ingreso con la clave correcta).
            opt.AddPolicy(PoliticaCambiarClave, ctx => Ventana(ctx, TimeSpan.FromMinutes(10), 20));
            opt.AddPolicy(PoliticaAsignar, ctx => Ventana(ctx, TimeSpan.FromMinutes(1), 60));
        });
        return services;
    }

    private static RateLimitPartition<string> Ventana(HttpContext ctx, TimeSpan ventana, int permisos) =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
            _ => new FixedWindowRateLimiterOptions { Window = ventana, PermitLimit = permisos, QueueLimit = 0 });
}

// ── La lista única ─────────────────────────────────────────────────────────────────────────

public record FiltroUsuarios(IReadOnlyList<string> Palabras, string Tipo, string Estado, int Pagina, int Cantidad);

public record FilaUsuario(int? IdUsuario, string Tipo, string? Username, string? Nombre, string? Dni, string? Matricula,
    string? Especialidad, string? Mail, string? Estado, bool Bloqueado, DateTime? BloqueadoHasta, bool DebeCambiarPassword,
    int? IdPaciente, int? IdProfesional, bool? LinkPublicoActivo);

public record PaginaUsuarios(int Total, IReadOnlyList<FilaUsuario> Filas);

public static class UsuariosLista
{
    public const int CantidadPorDefecto = 25;
    public const int CantidadMaxima = 100;
    public const int MaxPalabras = 5;
    public const int MaxLargoPalabra = 50;

    // Tipo: lo que dice el login (ficha de paciente, ficha de profesional o, si no tiene ninguna, equipo).
    public static readonly string[] Tipos = { "todos", "paciente", "profesional", "equipo" };
    // Estado: "todos" son las cuentas (con usuario); "sin-cuenta" son los pacientes y profesionales que todavía no tienen usuario.
    public static readonly string[] Estados = { "todos", "activo", "inactivo", "pendiente", "bloqueado", "sin-cuenta" };

    public static (FiltroUsuarios? Filtro, string? Error) Normalizar(string? q, string? tipo, string? estado, int? pagina, int? cantidad)
    {
        var t = string.IsNullOrWhiteSpace(tipo) ? "todos" : tipo.Trim().ToLowerInvariant();
        if (!Tipos.Contains(t)) return (null, "El tipo tiene que ser: " + string.Join(", ", Tipos) + ".");
        var e = string.IsNullOrWhiteSpace(estado) ? "todos" : estado.Trim().ToLowerInvariant();
        if (!Estados.Contains(e)) return (null, "El estado tiene que ser: " + string.Join(", ", Estados) + ".");
        if (pagina is < 1) return (null, "La página empieza en 1.");
        if (cantidad is < 1 or > CantidadMaxima) return (null, $"La cantidad por página va de 1 a {CantidadMaxima}.");

        var palabras = (q ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(MaxPalabras).Select(p => p.Length > MaxLargoPalabra ? p[..MaxLargoPalabra] : p).ToList();
        return (new FiltroUsuarios(palabras, t, e, pagina ?? 1, cantidad ?? CantidadPorDefecto), null);
    }

    /// <summary>El texto como patrón de LIKE (con ESCAPE '\'): lo que se busca se toma literal, sin comodines.</summary>
    public static string PatronLike(string palabra) =>
        "%" + palabra.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[") + "%";
}

public class UsuariosListaDAO
{
    // Cada fila es una persona o cuenta: la cuenta con su ficha (nombre, DNI, matrícula) o, con estado "sin-cuenta", la persona sin usuario.
    // Nada clínico: ni historias, ni turnos, ni asignaciones. "Bloqueado" usa la hora de la aplicación (UTC), igual que el ingreso.
    public PaginaUsuarios Buscar(FiltroUsuarios f, DateTime ahoraUtc)
    {
        var sinCuenta = f.Estado == "sin-cuenta";
        var origen = sinCuenta ? @"
            SELECT CAST(NULL AS INT) AS IdUsuario, CAST(NULL AS NVARCHAR(50)) AS Username, pa.Estado, pa.Mail,
                   CAST(NULL AS DATETIME) AS BloqueadoHasta, CAST(0 AS BIT) AS DebeCambiarPassword, CAST('paciente' AS VARCHAR(12)) AS Tipo,
                   pa.IdPaciente, CAST(NULL AS INT) AS IdProfesional, pa.Nombre AS NombreCompleto, pa.Dni,
                   CAST(NULL AS NVARCHAR(50)) AS Matricula, CAST(NULL AS NVARCHAR(100)) AS Especialidad, CAST(NULL AS BIT) AS LinkPublicoActivo
            FROM Paciente pa
            WHERE NOT EXISTS (SELECT 1 FROM UsuarioPaciente x WHERE x.IdPaciente = pa.IdPaciente)
            UNION ALL
            SELECT NULL, NULL, pr.Estado, pr.Mail, NULL, CAST(0 AS BIT), 'profesional', NULL, pr.IdProfesional,
                   LTRIM(RTRIM(pr.Nombre + ISNULL(' ' + pr.Apellido, N''))), pr.Dni, pr.Matricula, e.Nombre, pr.LinkPublicoActivo
            FROM Profesional pr
            LEFT JOIN Especialidad e ON e.IdEspecialidad = pr.IdEspecialidad
            WHERE pr.Estado IN ('Activo', 'Inactivo')
              AND NOT EXISTS (SELECT 1 FROM UsuarioProfesional x WHERE x.IdProfesional = pr.IdProfesional)" : @"
            SELECT u.IdUsuario, u.Username,
                   -- Dar de baja a un paciente o a un profesional baja su ficha, no su usuario: para la lista, cualquiera de las dos cosas es Inactivo.
                   CASE WHEN u.Estado = 'Inactivo' OR pa.Estado = 'Inactivo' OR pr.Estado = 'Inactivo' THEN 'Inactivo' ELSE u.Estado END AS Estado,
                   u.Mail, u.BloqueadoHasta, u.DebeCambiarPassword,
                   CAST(CASE WHEN upa.IdUsuario IS NOT NULL THEN 'paciente' WHEN upr.IdUsuario IS NOT NULL THEN 'profesional' ELSE 'equipo' END AS VARCHAR(12)) AS Tipo,
                   upa.IdPaciente, upr.IdProfesional,
                   COALESCE(NULLIF(LTRIM(RTRIM(pa.Nombre)), N''),
                            NULLIF(LTRIM(RTRIM(pr.Nombre + ISNULL(' ' + pr.Apellido, N''))), N''),
                            NULLIF(LTRIM(RTRIM(ISNULL(u.Nombre, N'') + ISNULL(' ' + u.Apellido, N''))), N'')) AS NombreCompleto,
                   COALESCE(pa.Dni, pr.Dni) AS Dni, pr.Matricula, e.Nombre AS Especialidad, pr.LinkPublicoActivo
            FROM Usuario u
            LEFT JOIN UsuarioPaciente upa ON upa.IdUsuario = u.IdUsuario
            LEFT JOIN Paciente pa ON pa.IdPaciente = upa.IdPaciente
            LEFT JOIN UsuarioProfesional upr ON upr.IdUsuario = u.IdUsuario
            LEFT JOIN Profesional pr ON pr.IdProfesional = upr.IdProfesional
            LEFT JOIN Especialidad e ON e.IdEspecialidad = pr.IdEspecialidad";

        var donde = new List<string>();
        if (f.Tipo != "todos") donde.Add("Tipo = @Tipo");
        switch (f.Estado)
        {
            case "activo": donde.Add("Estado = 'Activo'"); break;
            case "inactivo": donde.Add("Estado = 'Inactivo'"); break;
            case "pendiente": donde.Add("Estado = 'Pendiente'"); break;
            case "bloqueado": donde.Add("BloqueadoHasta IS NOT NULL AND BloqueadoHasta > @Ahora"); break;
        }
        for (var i = 0; i < f.Palabras.Count; i++)
            donde.Add($@"(Username LIKE @P{i} ESCAPE '\' OR NombreCompleto LIKE @P{i} ESCAPE '\' OR Dni LIKE @P{i} ESCAPE '\'
                          OR Matricula LIKE @P{i} ESCAPE '\' OR Mail LIKE @P{i} ESCAPE '\')");

        var sql = $@"
            WITH Todos AS ({origen})
            SELECT *, COUNT(*) OVER () AS Total
            FROM Todos
            {(donde.Count == 0 ? "" : "WHERE " + string.Join(" AND ", donde))}
            ORDER BY COALESCE(NombreCompleto, Username, N''), Tipo, IdUsuario, IdPaciente, IdProfesional
            OFFSET @Desde ROWS FETCH NEXT @Cantidad ROWS ONLY";

        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.AddWithValue("@Ahora", ahoraUtc);
        cmd.Parameters.AddWithValue("@Tipo", f.Tipo);
        cmd.Parameters.AddWithValue("@Desde", (f.Pagina - 1) * f.Cantidad);
        cmd.Parameters.AddWithValue("@Cantidad", f.Cantidad);
        for (var i = 0; i < f.Palabras.Count; i++) cmd.Parameters.AddWithValue($"@P{i}", UsuariosLista.PatronLike(f.Palabras[i]));

        var filas = new List<FilaUsuario>();
        var total = 0;
        using var rd = cmd.ExecuteReader();
        string? Texto(string c) => rd[c] is DBNull ? null : (string)rd[c];
        int? Entero(string c) => rd[c] is DBNull ? null : (int)rd[c];
        while (rd.Read())
        {
            total = (int)rd["Total"];
            var hasta = rd["BloqueadoHasta"] is DBNull ? (DateTime?)null : DateTime.SpecifyKind((DateTime)rd["BloqueadoHasta"], DateTimeKind.Utc);
            filas.Add(new FilaUsuario(Entero("IdUsuario"), (string)rd["Tipo"], Texto("Username"), Texto("NombreCompleto"), Texto("Dni"),
                Texto("Matricula"), Texto("Especialidad"), Texto("Mail"), Texto("Estado"),
                hasta is not null && hasta > ahoraUtc, hasta, (bool)rd["DebeCambiarPassword"], Entero("IdPaciente"), Entero("IdProfesional"),
                rd["LinkPublicoActivo"] is DBNull ? null : (bool)rd["LinkPublicoActivo"]));
        }
        return new PaginaUsuarios(total, filas);
    }
}

// ── Restablecer la contraseña ──────────────────────────────────────────────────────────────

public enum ResultadoRestablecer { Hecho, NoExiste, NoActiva }

public static class RestablecerPassword
{
    /// <summary>
    /// Una clave temporal que cumple la política de contraseñas (la de CrearUsuarioProfesionalService, 14 caracteres con
    /// mayúscula, minúscula y número, sin caracteres que se confunden). Se vuelve a generar si por alguna razón no la cumple.
    /// </summary>
    public static string NuevaTemporal()
    {
        for (var i = 0; i < 5; i++)
        {
            var clave = CrearUsuarioProfesionalService.GenerarContrasenaTemporal();
            if (PoliticaContrasena.Validar(clave) is null) return clave;
        }
        throw new InvalidOperationException("No se pudo generar una contraseña temporal que cumpla la política.");
    }

    /// <summary>El hash que guarda el sistema (el mismo del alta, del ingreso y de la recuperación): SHA-256 del UTF-8, en hexadecimal.</summary>
    public static string Hash(string contrasena) => CrearUsuarioProfesionalService.HashearContrasena(contrasena);
}

public class UsuariosAdminDAO
{
    // Guarda la clave temporal (el mismo hash que usa el ingreso), marca que tiene que cambiarla, desbloquea la cuenta y deja el registro
    // en la auditoría administrativa, TODO en una transacción: si la auditoría falla no queda ninguna clave cambiada ni se devuelve nada.
    // El registro dice a quién y quién; nunca la clave (ni su hash). No cierra las sesiones abiertas de esa cuenta: no hay revocación.
    public (ResultadoRestablecer Resultado, string? Username) Restablecer(int idUsuario, int idAutor, string hash, string? espacio)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();
        string? username;
        using (var cmd = new SqlCommand(@"
            UPDATE Usuario
            SET PasswordHash = @Hash, DebeCambiarPassword = 1, IntentosFallidos = 0, BloqueadoHasta = NULL
            OUTPUT inserted.Username
            WHERE IdUsuario = @Id AND Estado = 'Activo'", con, tx))
        {
            cmd.Parameters.AddWithValue("@Hash", hash);
            cmd.Parameters.AddWithValue("@Id", idUsuario);
            username = cmd.ExecuteScalar() as string;
        }
        if (username is null)
        {
            using var existe = new SqlCommand("SELECT COUNT(*) FROM Usuario WHERE IdUsuario = @Id", con, tx);
            existe.Parameters.AddWithValue("@Id", idUsuario);
            var hay = (int)existe.ExecuteScalar()! > 0;
            tx.Rollback();
            return (hay ? ResultadoRestablecer.NoActiva : ResultadoRestablecer.NoExiste, null);
        }
        AuditoriaAdmin.Insertar(con, tx, idAutor, "Restablecimiento de contraseña", "Usuario", idUsuario,
            $"Usuario afectado: {username} (#{idUsuario}). Clave temporal: tiene que cambiarla al ingresar.", espacio);
        tx.Commit();
        return (ResultadoRestablecer.Hecho, username);
    }
}

// Registro de la auditoría administrativa dentro de una transacción ya abierta (LogAuditoriaDAO abre su propia conexión).
public static class AuditoriaAdmin
{
    public static void Insertar(SqlConnection con, SqlTransaction tx, int? idAutor, string accion, string entidad, int idEntidad,
        string detalle, string? espacio)
    {
        using var cmd = new SqlCommand(@"
            INSERT INTO LogAuditoria (IdUsuario, Accion, Entidad, IdEntidad, Detalle, Categoria, Espacio)
            VALUES (@IdUsuario, @Accion, @Entidad, @IdEntidad, @Detalle, @Categoria, @Espacio)", con, tx);
        cmd.Parameters.AddWithValue("@IdUsuario", (object?)idAutor ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Accion", accion);
        cmd.Parameters.AddWithValue("@Entidad", entidad);
        cmd.Parameters.AddWithValue("@IdEntidad", idEntidad);
        cmd.Parameters.AddWithValue("@Detalle", detalle.Length <= 500 ? detalle : detalle[..500]);
        cmd.Parameters.AddWithValue("@Categoria", CategoriaAuditoria.Administrativa);
        cmd.Parameters.AddWithValue("@Espacio", (object?)espacio ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}

// ── Cambio obligatorio de contraseña (primer ingreso con la clave temporal) ──────────────────

public record ResultadoCambioClave(int Estado, string? Codigo, string? Mensaje, Usuario? Usuario);

public static class CambioPasswordObligatorio
{
    public static ResultadoCambioClave Aplicar(int idUsuario, string? claveNueva)
    {
        var dao = new UsuarioDAO();
        var usuario = dao.ObtenerPorId(idUsuario);
        if (usuario is null || usuario.Estado != "Activo")
            return new(403, "CUENTA_NO_ACTIVA", "Tu cuenta no está activa.", null);
        // El token vale 5 minutos, pero la marca se baja con el primer cambio: el mismo token no sirve dos veces.
        if (!usuario.DebeCambiarPassword)
            return new(409, "CAMBIO_NO_REQUERIDO", "Ya elegiste tu clave nueva. Ingresá de nuevo con ella.", null);
        var motivo = PoliticaContrasena.Validar(claveNueva);
        if (motivo is not null) return new(400, "CLAVE_INVALIDA", motivo, null);

        var hash = RestablecerPassword.Hash(claveNueva!);
        if (string.Equals(hash, usuario.PasswordHash, StringComparison.OrdinalIgnoreCase))
            return new(400, "CLAVE_IGUAL_A_LA_TEMPORAL", "La clave nueva tiene que ser distinta de la temporal.", null);

        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();
        using (var cmd = new SqlCommand(@"
            UPDATE Usuario SET PasswordHash = @Hash, DebeCambiarPassword = 0
            WHERE IdUsuario = @Id AND DebeCambiarPassword = 1 AND PasswordHash = @Vieja AND Estado = 'Activo'", con, tx))
        {
            cmd.Parameters.AddWithValue("@Hash", hash);
            cmd.Parameters.AddWithValue("@Id", idUsuario);
            cmd.Parameters.AddWithValue("@Vieja", usuario.PasswordHash);
            if (cmd.ExecuteNonQuery() != 1)
            {
                tx.Rollback();
                return new(409, "CAMBIO_NO_REQUERIDO", "Ya elegiste tu clave nueva. Ingresá de nuevo con ella.", null);
            }
        }
        AuditoriaAdmin.Insertar(con, tx, idUsuario, "Cambio de contraseña", "Usuario", idUsuario,
            "Eligió su clave nueva en el primer ingreso con la clave temporal.", null);
        tx.Commit();
        return new(200, null, null, usuario);
    }
}

// ── Asignar profesionales a un paciente (desde la lista de Usuarios) ─────────────────────────

public record ProfesionalAsignable(int IdProfesional, string Nombre, string? Matricula, string? Especialidad);

public record ResultadoAsignacionAdmin(int Estado, string? Codigo, string? Mensaje, int Cambiados = 0, int SinCambio = 0);

public static class AsignacionAdmin
{
    public const int MaxPorPedido = 50;

    /// <summary>Los ids del pedido, sin repetir, o el mensaje de por qué no sirve.</summary>
    public static (List<int>? Ids, string? Error) Validar(IReadOnlyList<int>? idProfesionales)
    {
        if (idProfesionales is null || idProfesionales.Count == 0) return (null, "Elegí al menos un profesional.");
        if (idProfesionales.Count > MaxPorPedido) return (null, $"Se pueden elegir hasta {MaxPorPedido} profesionales por vez.");
        if (idProfesionales.Any(i => i <= 0)) return (null, "Hay un profesional que no es válido.");
        return (idProfesionales.Distinct().ToList(), null);
    }
}

public class AsignacionAdminDAO
{
    public (string Nombre, string? Estado)? LeerPaciente(int idPaciente)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand("SELECT Nombre, Estado FROM Paciente WHERE IdPaciente = @Id", con);
        cmd.Parameters.AddWithValue("@Id", idPaciente);
        using var rd = cmd.ExecuteReader();
        return rd.Read() ? ((string)rd["Nombre"], rd["Estado"] is DBNull ? null : (string)rd["Estado"]) : null;
    }

    private const string ProfesionalSql = @"
        SELECT p.IdProfesional, LTRIM(RTRIM(p.Nombre + ISNULL(' ' + p.Apellido, N''))) AS Nombre, p.Matricula, e.Nombre AS Especialidad";

    // Quién atiende hoy al paciente: solo nombre, matrícula y especialidad (nada clínico, ni contacto).
    public List<ProfesionalAsignable> Asignados(int idPaciente) => Leer(ProfesionalSql + @"
        FROM PacienteProfesional pp
        JOIN Profesional p ON p.IdProfesional = pp.IdProfesional
        LEFT JOIN Especialidad e ON e.IdEspecialidad = p.IdEspecialidad
        WHERE pp.IdPaciente = @Id
        ORDER BY Nombre, p.IdProfesional", idPaciente);

    // Los que se pueden elegir: profesionales activos.
    public List<ProfesionalAsignable> Activos() => Leer(ProfesionalSql + @"
        FROM Profesional p
        LEFT JOIN Especialidad e ON e.IdEspecialidad = p.IdEspecialidad
        WHERE p.Estado = 'Activo'
        ORDER BY Nombre, p.IdProfesional", null);

    private static List<ProfesionalAsignable> Leer(string sql, int? idPaciente)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(sql, con);
        if (idPaciente is not null) cmd.Parameters.AddWithValue("@Id", idPaciente.Value);
        var lista = new List<ProfesionalAsignable>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            lista.Add(new ProfesionalAsignable((int)rd["IdProfesional"], (string)rd["Nombre"],
                rd["Matricula"] is DBNull ? null : (string)rd["Matricula"], rd["Especialidad"] is DBNull ? null : (string)rd["Especialidad"]));
        return lista;
    }

    // Asigna todos o ninguno: el paciente y cada profesional tienen que existir y estar activos. Respeta UNIQUE(IdPaciente, IdProfesional):
    // quien ya estaba asignado no se repite ni es error. Mismo INSERT que el endpoint de Admisión (sin tocar EstadoAsignacion).
    public ResultadoAsignacionAdmin Asignar(int idPaciente, IReadOnlyList<int> idProfesionales, int idAutor, string? espacio)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();

        string? estadoPaciente;
        using (var cmd = new SqlCommand("SELECT Estado FROM Paciente WHERE IdPaciente = @Id", con, tx))
        {
            cmd.Parameters.AddWithValue("@Id", idPaciente);
            var r = cmd.ExecuteScalar();
            if (r is null) { tx.Rollback(); return new(404, "PACIENTE_INEXISTENTE", "No existe ese paciente."); }
            estadoPaciente = r as string;
        }
        if (estadoPaciente != "Activo") { tx.Rollback(); return new(409, "PACIENTE_NO_ACTIVO", "El paciente tiene que estar activo."); }

        var invalidos = new List<int>();
        foreach (var id in idProfesionales)
        {
            using var cmd = new SqlCommand("SELECT Estado FROM Profesional WHERE IdProfesional = @Id", con, tx);
            cmd.Parameters.AddWithValue("@Id", id);
            if (cmd.ExecuteScalar() as string != "Activo") invalidos.Add(id);
        }
        if (invalidos.Count > 0)
        {
            tx.Rollback();
            return new(409, "PROFESIONAL_NO_ACTIVO", "Hay profesionales que no existen o no están activos: " + string.Join(", ", invalidos.Select(i => "#" + i)) + ".");
        }

        int agregados = 0;
        foreach (var id in idProfesionales)
        {
            using var cmd = new SqlCommand(@"
                IF NOT EXISTS (SELECT 1 FROM PacienteProfesional WHERE IdPaciente = @Pac AND IdProfesional = @Prof)
                BEGIN
                    INSERT INTO PacienteProfesional (IdPaciente, IdProfesional) VALUES (@Pac, @Prof);
                    SELECT 1;
                END ELSE SELECT 0;", con, tx);
            cmd.Parameters.AddWithValue("@Pac", idPaciente);
            cmd.Parameters.AddWithValue("@Prof", id);
            if ((int)cmd.ExecuteScalar()! == 1)
            {
                agregados++;
                AuditoriaAdmin.Insertar(con, tx, idAutor, "Asignación de profesional", "Paciente", idPaciente,
                    $"Profesional #{id} asignado al paciente #{idPaciente} desde Usuarios.", espacio);
            }
        }
        tx.Commit();
        return new(200, null, null, agregados, idProfesionales.Count - agregados);
    }

    // Quita las asignaciones pedidas (las que no existían no son error). El profesional puede estar dado de baja.
    public ResultadoAsignacionAdmin Quitar(int idPaciente, IReadOnlyList<int> idProfesionales, int idAutor, string? espacio)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var tx = con.BeginTransaction();
        using (var cmd = new SqlCommand("SELECT COUNT(*) FROM Paciente WHERE IdPaciente = @Id", con, tx))
        {
            cmd.Parameters.AddWithValue("@Id", idPaciente);
            if ((int)cmd.ExecuteScalar()! == 0) { tx.Rollback(); return new(404, "PACIENTE_INEXISTENTE", "No existe ese paciente."); }
        }
        int quitados = 0;
        foreach (var id in idProfesionales)
        {
            using var cmd = new SqlCommand("DELETE FROM PacienteProfesional WHERE IdPaciente = @Pac AND IdProfesional = @Prof", con, tx);
            cmd.Parameters.AddWithValue("@Pac", idPaciente);
            cmd.Parameters.AddWithValue("@Prof", id);
            if (cmd.ExecuteNonQuery() > 0)
            {
                quitados++;
                AuditoriaAdmin.Insertar(con, tx, idAutor, "Desasignación de profesional", "Paciente", idPaciente,
                    $"Profesional #{id} quitado del paciente #{idPaciente} desde Usuarios.", espacio);
            }
        }
        tx.Commit();
        return new(200, null, null, quitados, idProfesionales.Count - quitados);
    }
}
