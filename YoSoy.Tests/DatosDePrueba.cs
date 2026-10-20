using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Filas PROPIAS para las pruebas que usan una base de datos (las de categoría Integracion): cada prueba crea lo
/// que necesita (paciente, profesional, usuarios, vínculos, agenda...) y todo se borra al terminar.
///
/// Antes, esas pruebas suponían filas de la base de desarrollo (el paciente 1, los profesionales 1 y 2, los usuarios
/// 1 a 4, la historia clínica 1, la obra social 1...) y fallaban en cualquier otra base. Con esto no necesitan
/// ninguna fila previa: corren igual contra una base vacía con el esquema de docs/produccion/01-produccion.sql.
///
/// Limpieza: en orden INVERSO al de creación, y en cascada por las claves foráneas (se leen de sys.foreign_keys, no
/// de los mensajes de error: no dependen del idioma del servidor). Solo se borran filas que apuntan a las filas
/// creadas acá, que son nuevas: nunca algo ajeno.
///
/// Uso:  using var d = new DatosDePrueba();  var idPaciente = d.Paciente();  ...
/// </summary>
public sealed class DatosDePrueba : IDisposable
{
    private static int _contador;
    private readonly List<Action> _limpieza = new();

    /// <summary>Prefijo único de esta instancia: va en todo lo que se crea (usuarios, DNI, matrículas, teléfonos).</summary>
    public string Marca { get; } = "xtest" + Guid.NewGuid().ToString("N")[..10];

    private string Unico(string sufijo = "") => $"{Marca}{sufijo}{Interlocked.Increment(ref _contador)}";

    // ── Acceso a la base ─────────────────────────────────────────────────────────────────────

    public static void Ejecutar(string sql, params (string Nombre, object? Valor)[] parametros)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = Armar(con, sql, parametros);
        cmd.ExecuteNonQuery();
    }

    /// <summary>La primera columna de la primera fila, o null si no hay filas o es NULL.</summary>
    public static object? Escalar(string sql, params (string Nombre, object? Valor)[] parametros)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = Armar(con, sql, parametros);
        var r = cmd.ExecuteScalar();
        return r is DBNull ? null : r;
    }

    public static int Contar(string sql, params (string Nombre, object? Valor)[] parametros) =>
        Convert.ToInt32(Escalar(sql, parametros) ?? 0);

    private static SqlCommand Armar(SqlConnection con, string sql, (string Nombre, object? Valor)[] parametros)
    {
        var cmd = new SqlCommand(sql, con);
        foreach (var (nombre, valor) in parametros) cmd.Parameters.AddWithValue(nombre, valor ?? DBNull.Value);
        return cmd;
    }

    private int Insertar(string tabla, string columnaId, string sql, params (string, object?)[] parametros)
    {
        var id = Convert.ToInt32(Escalar(sql, parametros));
        _limpieza.Add(() => BorrarEnCascada(tabla, $"{columnaId} = @v", id));
        return id;
    }

    // ── Limpieza ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Para lo que crea el código bajo prueba (un turno pedido por la API, una nota, una auditoría...): borra,
    /// al limpiar y ANTES que lo creado con los métodos de abajo, las filas de <paramref name="tabla"/> donde
    /// <paramref name="columna"/> vale <paramref name="valor"/> (y lo que dependa de ellas).
    /// </summary>
    public void AlLimpiar(string tabla, string columna, object valor) =>
        _limpieza.Add(() => BorrarEnCascada(tabla, $"{columna} = @v", valor));

    private static void BorrarEnCascada(string tabla, string condicion, object valor, int profundidad = 0)
    {
        if (profundidad > 6) throw new InvalidOperationException($"Cascada de borrado demasiado profunda en {tabla}.");

        var hijas = HijasDe(tabla);

        // La auditoría es de solo agregar (migración 19: el trigger rechaza el DELETE, error 50018): no se puede borrar lo que dejó
        // la aplicación en LogAuditoria. Si alguna de sus filas apunta a lo que se iba a borrar (un usuario que actuó), eso queda
        // como está: no se toca nada de esta rama (ni sus hijas) y la fila retenida es una más de la base de pruebas, con marca.
        foreach (var (hija, columnaHija, columnaPadre) in hijas)
            if (EsDeSoloAgregar(hija) &&
                Contar($"SELECT COUNT(*) FROM {hija} WHERE {columnaHija} IN (SELECT {columnaPadre} FROM {tabla} WHERE {condicion})", ("@v", valor)) > 0)
                return;

        // Primero lo que apunta a estas filas (las claves foráneas que referencian a la tabla), de forma recursiva.
        foreach (var (hija, columnaHija, columnaPadre) in hijas)
        {
            if (string.Equals(hija, tabla, StringComparison.OrdinalIgnoreCase)) continue;   // auto-referencia
            if (EsDeSoloAgregar(hija)) continue;   // ya se comprobó arriba que no tiene filas que apunten acá
            BorrarEnCascada(hija, $"{columnaHija} IN (SELECT {columnaPadre} FROM {tabla} WHERE {condicion})", valor, profundidad + 1);
        }
        Ejecutar($"DELETE FROM {tabla} WHERE {condicion}", ("@v", valor));
    }

    private static bool EsDeSoloAgregar(string tablaCalificada) =>
        tablaCalificada.EndsWith("[LogAuditoria]", StringComparison.OrdinalIgnoreCase) || tablaCalificada.EndsWith("[LogError]", StringComparison.OrdinalIgnoreCase);

    private static List<(string Hija, string ColumnaHija, string ColumnaPadre)> HijasDe(string tabla)
    {
        var lista = new List<(string, string, string)>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            SELECT QUOTENAME(OBJECT_SCHEMA_NAME(fk.parent_object_id)) + '.' + QUOTENAME(OBJECT_NAME(fk.parent_object_id)),
                   QUOTENAME(COL_NAME(fkc.parent_object_id, fkc.parent_column_id)),
                   QUOTENAME(COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id))
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            WHERE fk.referenced_object_id = OBJECT_ID(@t)", con);
        cmd.Parameters.AddWithValue("@t", tabla);
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add((r.GetString(0), r.GetString(1), r.GetString(2)));
        return lista;
    }

    public void Dispose()
    {
        // En orden inverso al de creación. Si algo falla (una fila que apunta a las nuestras y que la cascada no
        // pudo resolver) no se interrumpe el resto: se sigue, y los fallos se informan por la salida de error.
        var fallos = new List<Exception>();
        for (var i = _limpieza.Count - 1; i >= 0; i--)
        {
            try { _limpieza[i](); }
            catch (Exception ex) { fallos.Add(ex); }
        }
        _limpieza.Clear();
        foreach (var f in fallos)
            Console.Error.WriteLine($"[DatosDePrueba {Marca}] no se pudo limpiar: {f.GetType().Name}: {f.Message}");
    }

    // ── Catálogos ────────────────────────────────────────────────────────────────────────────

    public int Especialidad() => Insertar("Especialidad", "IdEspecialidad",
        "INSERT INTO Especialidad (Nombre, Estado) VALUES (@n, N'Activo'); SELECT CAST(SCOPE_IDENTITY() AS INT);",
        ("@n", Unico("esp")));

    /// <summary>
    /// El id del estado con esa descripción. Es un catálogo COMPARTIDO (lo usan todas las pruebas, también las que
    /// corren en paralelo) y una base recién creada lo tiene vacío: si no está, se crea. Al limpiar, el estado se
    /// SUELTA y lo borra la última prueba que lo usa: mientras otra lo esté usando no se borra (ver
    /// <see cref="EstadosCompartidos"/>). Si la base ya lo tenía, nunca se borra. Si al borrarlo lo está usando un turno
    /// que no es de esta prueba, simplemente queda, como en cualquier base real, donde ese catálogo existe.
    /// </summary>
    public int EstadoTurno(string descripcion)
    {
        var id = EstadosCompartidos.Tomar(descripcion);
        _limpieza.Add(() => EstadosCompartidos.Soltar(descripcion));
        return id;
    }

    public int ObraSocial() => Insertar("ObraSocial", "IdObraSocial",
        "INSERT INTO ObraSocial (Nombre) VALUES (@n); SELECT CAST(SCOPE_IDENTITY() AS INT);", ("@n", Unico("os")));

    public int TipoConsentimiento() => Insertar("TipoConsentimiento", "IdTipoConsentimiento",
        "INSERT INTO TipoConsentimiento (Descripcion, Estado) VALUES (@d, N'Activo'); SELECT CAST(SCOPE_IDENTITY() AS INT);",
        ("@d", Unico("tc")));

    // ── Personas y cuentas ───────────────────────────────────────────────────────────────────

    public int Paciente(string? nombre = null, string estado = "Activo", string? mail = null) =>
        Insertar("Paciente", "IdPaciente",
            "INSERT INTO Paciente (Nombre, Dni, Estado, Mail) VALUES (@n, @d, @e, @m); SELECT CAST(SCOPE_IDENTITY() AS INT);",
            ("@n", nombre ?? "Paciente " + Marca), ("@d", Unico()), ("@e", estado), ("@m", mail));

    public int Profesional(int? idEspecialidad = null, string estado = "Activo", string? nombre = null) =>
        Insertar("Profesional", "IdProfesional",
            @"INSERT INTO Profesional (Nombre, Apellido, Matricula, IdEspecialidad, Estado, Mail)
              VALUES (@n, @a, @m, @e, @s, @mail); SELECT CAST(SCOPE_IDENTITY() AS INT);",
            ("@n", nombre ?? "Profesional"), ("@a", Marca), ("@m", Unico("mp")),
            ("@e", idEspecialidad ?? Especialidad()), ("@s", estado), ("@mail", Unico("p") + "@example.com"));

    /// <summary>Un usuario activo (sin vínculo: entra como configurador). La contraseña es "ClaveDePrueba123".</summary>
    public int Usuario(string estado = "Activo", string? hash = null) =>
        Insertar("Usuario", "IdUsuario",
            "INSERT INTO Usuario (Username, PasswordHash, Estado, Mail) VALUES (@u, @h, @e, @m); SELECT CAST(SCOPE_IDENTITY() AS INT);",
            ("@u", Unico("u")), ("@h", hash ?? HashDeLaClaveDePrueba), ("@e", estado), ("@m", Unico("m") + "@example.com"));

    public const string ClaveDePrueba = "ClaveDePrueba123";
    public static readonly string HashDeLaClaveDePrueba =
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ClaveDePrueba)));

    public string Username(int idUsuario) =>
        (string)Escalar("SELECT Username FROM Usuario WHERE IdUsuario = @i", ("@i", idUsuario))!;

    /// <summary>Un usuario vinculado a un paciente: devuelve el IdUsuario.</summary>
    public int UsuarioDePaciente(int idPaciente)
    {
        var idUsuario = Usuario();
        Insertar("UsuarioPaciente", "IdUsuario",
            "INSERT INTO UsuarioPaciente (IdUsuario, IdPaciente, FechaVinculacion) VALUES (@u, @p, @f); SELECT @u;",
            ("@u", idUsuario), ("@p", idPaciente), ("@f", DateTime.Today));
        return idUsuario;
    }

    /// <summary>Un usuario vinculado a un profesional: devuelve el IdUsuario.</summary>
    public int UsuarioDeProfesional(int idProfesional)
    {
        var idUsuario = Usuario();
        Insertar("UsuarioProfesional", "IdUsuario",
            "INSERT INTO UsuarioProfesional (IdUsuario, IdProfesional, FechaVinculacion) VALUES (@u, @p, @f); SELECT @u;",
            ("@u", idUsuario), ("@p", idProfesional), ("@f", DateTime.Today));
        return idUsuario;
    }

    public void PacienteProfesional(int idPaciente, int idProfesional) => Insertar("PacienteProfesional", "IdPacienteProfesional",
        "INSERT INTO PacienteProfesional (IdPaciente, IdProfesional, EstadoAsignacion) VALUES (@p, @r, N'Activo'); SELECT CAST(SCOPE_IDENTITY() AS INT);",
        ("@p", idPaciente), ("@r", idProfesional));

    // ── Clínica y agenda ─────────────────────────────────────────────────────────────────────

    public int HistoriaClinica(int idPaciente) => Insertar("HistoriaClinica", "IdHistoria",
        "INSERT INTO HistoriaClinica (IdPaciente, FechaApertura) VALUES (@p, GETDATE()); SELECT CAST(SCOPE_IDENTITY() AS INT);",
        ("@p", idPaciente));

    /// <param name="dia">"Lunes", "Martes"... (como lo guarda la aplicación).</param>
    public int Agenda(int idProfesional, string dia, string inicio = "09:00", string fin = "18:00", int intervaloMinutos = 30) =>
        Insertar("Agenda", "IdAgenda",
            @"INSERT INTO Agenda (IdProfesional, Dia, HoraInicio, HoraFin, Disponible, IntervaloMinutos)
              VALUES (@p, @d, @i, @f, 1, @m); SELECT CAST(SCOPE_IDENTITY() AS INT);",
            ("@p", idProfesional), ("@d", dia), ("@i", TimeSpan.Parse(inicio)), ("@f", TimeSpan.Parse(fin)), ("@m", intervaloMinutos));

    public static string NombreDelDia(DateTime fecha) => fecha.DayOfWeek switch
    {
        DayOfWeek.Monday => "Lunes", DayOfWeek.Tuesday => "Martes", DayOfWeek.Wednesday => "Miércoles",
        DayOfWeek.Thursday => "Jueves", DayOfWeek.Friday => "Viernes", DayOfWeek.Saturday => "Sábado", _ => "Domingo"
    };

    public int Turno(int idPaciente, int idProfesional, string estado, DateTime fecha, TimeSpan hora, string? codigoSala = null) =>
        Insertar("Turno", "IdTurno",
            @"INSERT INTO Turno (IdPaciente, IdProfesional, IdEstadoTurno, Fecha, Hora, Modalidad, Motivo, CreadoPor, CodigoSala)
              VALUES (@p, @r, @e, @f, @h, N'Virtual', N'[TEST] datos de prueba', N'paciente', @s); SELECT CAST(SCOPE_IDENTITY() AS INT);",
            ("@p", idPaciente), ("@r", idProfesional), ("@e", EstadoTurno(estado)), ("@f", fecha.Date), ("@h", hora), ("@s", codigoSala));

    public int InformeAdmision(int idPaciente, string estado, DateTime? fechaEntrevista, string? codigoSala) =>
        Insertar("InformeAdmision", "IdInforme",
            @"INSERT INTO InformeAdmision (IdPaciente, Estado, FechaEntrevista, CodigoSala, MotivoConsulta)
              VALUES (@p, @e, @f, @s, N'[TEST] datos de prueba'); SELECT CAST(SCOPE_IDENTITY() AS INT);",
            ("@p", idPaciente), ("@e", estado), ("@f", fechaEntrevista), ("@s", codigoSala));

    // ── Clientes HTTP con sesión ─────────────────────────────────────────────────────────────

    /// <summary>Un HttpClient con un JWT real emitido por la aplicación para esa identidad.</summary>
    public static HttpClient Cliente(WebApplicationFactory<Program> fabrica, int idUsuario, string rol,
        int? idPaciente = null, int? idProfesional = null) =>
        Cliente(fabrica, fabrica.Services.GetRequiredService<JwtEmisorService>()
            .Emitir(idUsuario, rol, idPaciente: idPaciente, idProfesional: idProfesional));

    /// <summary>Un HttpClient con un token ya emitido.</summary>
    public static HttpClient Cliente(WebApplicationFactory<Program> fabrica, string token)
    {
        var c = fabrica.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }
}
