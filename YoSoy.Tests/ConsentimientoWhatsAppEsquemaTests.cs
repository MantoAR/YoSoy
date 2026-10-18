using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

// Etapa 1 del consentimiento de WhatsApp: la tabla ConsentimientoWhatsApp, su migración y el esquema consolidado.
//
//  · ConsentimientoWhatsAppMigracionTests: sin base. La migración solo agrega, cada cambio está protegido por un IF (se puede
//    correr dos veces) y define EXACTAMENTE la misma tabla que docs/produccion/01-produccion.sql.
//  · ConsentimientoWhatsAppEsquemaBaseRealTests: contra la base de pruebas (que se crea desde 01-produccion.sql). Las columnas, las
//    restricciones que sostienen el modelo (una fila por aceptación, a lo sumo una abierta por paciente, la revocación coherente) y
//    que correr la migración sobre una base que ya la tiene no cambia nada.
public class ConsentimientoWhatsAppMigracionTests
{
    private static string Migracion => File.ReadAllText(Path.Combine(VideoAccesoEndpointsTests.RaizDelRepositorio(), "migrations", "add_consentimiento_whatsapp.sql"));
    private static string Esquema => File.ReadAllText(Path.Combine(VideoAccesoEndpointsTests.RaizDelRepositorio(), "docs", "produccion", "01-produccion.sql"));

    // ───────────── Utilidades para leer los scripts ─────────────

    /// <summary>Los lotes del script (separados por líneas "GO"), sin comentarios y sin los que quedan vacíos.</summary>
    internal static List<string> Lotes(string script) =>
        Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Select(SinComentarios).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    internal static string SinComentarios(string sql) =>
        string.Join("\n", sql.Split('\n').Select(l => Regex.Replace(l, @"--.*$", "")));

    /// <summary>Minúsculas, sin corchetes, sin "dbo." y sin espacios ni paréntesis: dos formas de escribir lo mismo dan lo mismo.</summary>
    private static string Normalizado(string s) =>
        Regex.Replace(s.ToLowerInvariant().Replace("[", "").Replace("]", "").Replace("dbo.", ""), @"[\s()]+", "");

    /// <summary>Los renglones de la definición de la tabla (columnas y CONSTRAINT), separados por las comas del nivel de afuera.</summary>
    private static List<string> ItemsDeLaTabla(string script)
    {
        var sinComentarios = SinComentarios(script);
        var m = Regex.Match(sinComentarios, @"CREATE\s+TABLE\s+(?:\[?dbo\]?\.)?\[?ConsentimientoWhatsApp\]?\s*\(", RegexOptions.IgnoreCase);
        Assert.True(m.Success, "no encontré el CREATE TABLE de ConsentimientoWhatsApp");

        var items = new List<string>();
        var profundidad = 1;
        var inicio = m.Index + m.Length;
        for (var i = inicio; i < sinComentarios.Length; i++)
        {
            var c = sinComentarios[i];
            if (c == '(') profundidad++;
            else if (c == ')')
            {
                profundidad--;
                if (profundidad == 0) { items.Add(sinComentarios[inicio..i]); return items.Select(x => x.Trim()).Where(x => x.Length > 0).ToList(); }
            }
            else if (c == ',' && profundidad == 1) { items.Add(sinComentarios[inicio..i]); inicio = i + 1; }
        }
        throw new InvalidOperationException("el CREATE TABLE de ConsentimientoWhatsApp no cierra");
    }

    private static List<string> Sentencias(string script, string patron) =>
        Regex.Matches(SinComentarios(script), patron, RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Select(x => Normalizado(x.Value)).OrderBy(x => x, StringComparer.Ordinal).ToList();

    // ───────────── La migración ─────────────

    [Fact]
    public void La_migracion_solo_agrega_y_cada_cambio_esta_protegido_por_un_IF()
    {
        var lotes = Lotes(Migracion);
        Assert.NotEmpty(lotes);

        foreach (var lote in lotes)
        {
            if (Regex.IsMatch(lote, @"^SET\s+QUOTED_IDENTIFIER\s+ON\s*;?$", RegexOptions.IgnoreCase)) continue;

            // Todo lo que cambia el esquema empieza con un IF (existe / no existe), así que correrla dos veces no hace nada.
            Assert.True(Regex.IsMatch(lote, @"^IF\s", RegexOptions.IgnoreCase), "este lote no está protegido por un IF:\n" + lote);
            // Y nunca borra, pisa ni reescribe datos o columnas que ya existen.
            Assert.DoesNotMatch(@"(?i)\b(DROP|DELETE|UPDATE|TRUNCATE|INSERT|MERGE|EXEC(UTE)?)\b", lote);
            Assert.DoesNotMatch(@"(?i)ALTER\s+(TABLE\s+\S+\s+)?(ALTER|DROP)\s+COLUMN", lote);
        }
    }

    [Fact]
    public void La_migracion_pone_QUOTED_IDENTIFIER_ON_antes_del_indice_filtrado()
    {
        var script = SinComentarios(Migracion);

        var set = Regex.Match(script, @"SET\s+QUOTED_IDENTIFIER\s+ON", RegexOptions.IgnoreCase);
        var indice = Regex.Match(script, @"CREATE\s+UNIQUE\s+INDEX\s+UQ_ConsentimientoWhatsApp_Abierto", RegexOptions.IgnoreCase);
        Assert.True(set.Success && indice.Success);
        Assert.True(set.Index < indice.Index, "sin QUOTED_IDENTIFIER ON, CREATE INDEX con WHERE falla (error 1934) si no se corre con -I");
    }

    [Fact]
    public void La_migracion_no_toca_ninguna_tabla_que_ya_existe()
    {
        // La única tabla que nombra con ALTER TABLE es la nueva (para sus claves foráneas), y crea una sola tabla.
        var tablas = Regex.Matches(SinComentarios(Migracion), @"ALTER\s+TABLE\s+(?:\[?dbo\]?\.)?\[?(\w+)\]?", RegexOptions.IgnoreCase)
            .Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.Equal(new[] { "ConsentimientoWhatsApp" }, tablas);

        Assert.Single(Regex.Matches(SinComentarios(Migracion), @"CREATE\s+TABLE", RegexOptions.IgnoreCase));
    }

    // ───────────── La migración y el esquema consolidado: la misma tabla ─────────────

    [Fact]
    public void La_migracion_y_el_esquema_consolidado_definen_las_mismas_columnas_y_restricciones()
    {
        var deLaMigracion = ItemsDeLaTabla(Migracion).Select(Normalizado).ToList();
        var delEsquema = ItemsDeLaTabla(Esquema).Select(Normalizado).ToList();

        Assert.Equal(deLaMigracion, delEsquema);   // mismo orden: las columnas se crean en el mismo orden
        Assert.Equal(14, deLaMigracion.Count);     // 10 columnas + 4 restricciones CHECK
    }

    [Fact]
    public void La_migracion_y_el_esquema_consolidado_tienen_los_mismos_indices_y_claves_foraneas()
    {
        const string indices = @"CREATE\s+(UNIQUE\s+)?INDEX\s+\[?\w+\]?\s+ON\s+(?:\[?dbo\]?\.)?\[?ConsentimientoWhatsApp\]?[^;]*;";
        const string claves = @"ALTER\s+TABLE\s+(?:\[?dbo\]?\.)?\[?ConsentimientoWhatsApp\]?\s+ADD\s+CONSTRAINT\s+\[?FK_ConsentimientoWhatsApp_\w+\]?[^;]*;";

        Assert.Equal(Sentencias(Migracion, indices), Sentencias(Esquema, indices));
        Assert.Equal(2, Sentencias(Migracion, indices).Count);
        Assert.Equal(Sentencias(Migracion, claves), Sentencias(Esquema, claves));
        Assert.Equal(2, Sentencias(Migracion, claves).Count);
    }
}

[Trait("Categoria", "Integracion")]
public class ConsentimientoWhatsAppEsquemaBaseRealTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private static readonly DateTime Inicio = new(2031, 3, 4, 10, 0, 0);

    /// <summary>Un INSERT con valores válidos que cada prueba pisa de a una cosa.</summary>
    private static void Insertar(int idPaciente, int idUsuario, string telefono = "1155550001", string origen = "perfil",
        string version = "v0-provisorio", string hash = Hash, DateTime? aceptado = null, DateTime? revocado = null, string? motivo = null)
    {
        DatosDePrueba.Ejecutar(@"
            INSERT INTO ConsentimientoWhatsApp (IdPaciente, IdUsuario, Telefono, Origen, VersionTexto, HashTexto, AceptadoUtc, RevocadoUtc, MotivoRevocacion)
            VALUES (@p, @u, @t, @o, @v, @h, @a, @r, @m)",
            ("@p", idPaciente), ("@u", idUsuario), ("@t", telefono), ("@o", origen), ("@v", version), ("@h", hash),
            ("@a", aceptado ?? Inicio), ("@r", revocado), ("@m", motivo));
    }

    private static int Filas(int idPaciente) =>
        DatosDePrueba.Contar("SELECT COUNT(*) FROM ConsentimientoWhatsApp WHERE IdPaciente = @p", ("@p", idPaciente));

    private static SqlException Falla(Action accion) => Assert.Throws<SqlException>(accion);

    // 547 = restricción CHECK o clave foránea. 8152 y 2628 = el texto no entra en la columna (según la versión de SQL Server):
    // también es un rechazo.
    private static readonly int[] Rechazos = { 547, 8152, 2628 };

    // ───────────── La tabla ─────────────

    [Fact]
    public void La_tabla_tiene_las_columnas_esperadas_con_su_tipo()
    {
        var esperadas = new[]
        {
            "IdConsentimientoWhatsApp|int|NO", "IdPaciente|int|NO", "IdUsuario|int|NO", "Telefono|varchar(10)|NO",
            "Origen|nvarchar(10)|NO", "VersionTexto|nvarchar(40)|NO", "HashTexto|char(64)|NO", "AceptadoUtc|datetime2|NO",
            "RevocadoUtc|datetime2|SI", "MotivoRevocacion|nvarchar(20)|SI"
        };

        var reales = new List<string>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, DATETIME_PRECISION, IS_NULLABLE
            FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'ConsentimientoWhatsApp' ORDER BY ORDINAL_POSITION", con);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var tipo = (string)r["DATA_TYPE"];
            if (r["CHARACTER_MAXIMUM_LENGTH"] != DBNull.Value) tipo += "(" + r["CHARACTER_MAXIMUM_LENGTH"] + ")";
            reales.Add($"{r["COLUMN_NAME"]}|{tipo}|{((string)r["IS_NULLABLE"] == "YES" ? "SI" : "NO")}");
        }

        Assert.Equal(esperadas, reales);
    }

    [Fact]
    public void La_tabla_tiene_el_indice_unico_filtrado_el_de_consulta_y_las_dos_claves_foraneas()
    {
        var filtro = DatosDePrueba.Escalar(@"SELECT filter_definition FROM sys.indexes
            WHERE object_id = OBJECT_ID('dbo.ConsentimientoWhatsApp') AND name = 'UQ_ConsentimientoWhatsApp_Abierto' AND is_unique = 1") as string;
        Assert.NotNull(filtro);
        Assert.Contains("RevocadoUtc", filtro);
        Assert.Contains("IS NULL", filtro);

        Assert.Equal(1, DatosDePrueba.Contar(@"SELECT COUNT(*) FROM sys.indexes
            WHERE object_id = OBJECT_ID('dbo.ConsentimientoWhatsApp') AND name = 'IX_ConsentimientoWhatsApp_Paciente'"));

        var destinos = new List<string>();
        using (var con = Conexion.ObtenerConexion())
        {
            con.Open();
            using var cmd = new SqlCommand(@"SELECT OBJECT_NAME(referenced_object_id) FROM sys.foreign_keys
                WHERE parent_object_id = OBJECT_ID('dbo.ConsentimientoWhatsApp') ORDER BY 1", con);
            using var r = cmd.ExecuteReader();
            while (r.Read()) destinos.Add(r.GetString(0));
        }
        Assert.Equal(new[] { "Paciente", "Usuario" }, destinos);
    }

    // ───────────── Las restricciones que sostienen el modelo ─────────────

    [Fact]
    public void Una_aceptacion_valida_entra_y_la_fecha_de_aceptacion_por_defecto_es_ahora_en_UTC()
    {
        using var d = new DatosDePrueba();
        var paciente = d.Paciente();
        var usuario = d.UsuarioDePaciente(paciente);

        DatosDePrueba.Ejecutar(@"INSERT INTO ConsentimientoWhatsApp (IdPaciente, IdUsuario, Telefono, Origen, VersionTexto, HashTexto)
            VALUES (@p, @u, '1155550001', 'registro', 'v0-provisorio', @h)", ("@p", paciente), ("@u", usuario), ("@h", Hash));

        var segundos = Convert.ToInt32(DatosDePrueba.Escalar(
            "SELECT DATEDIFF(SECOND, AceptadoUtc, SYSUTCDATETIME()) FROM ConsentimientoWhatsApp WHERE IdPaciente = @p", ("@p", paciente)));
        Assert.InRange(segundos, 0, 60);
        Assert.Equal(1, Filas(paciente));
    }

    [Theory]
    [InlineData("origen", "web")]
    [InlineData("origen", "")]
    [InlineData("telefono", "115555000")]        // 9 dígitos
    [InlineData("telefono", "11555500012")]      // 11 dígitos: no entra en la columna
    [InlineData("telefono", "11 5555 01")]       // 10 caracteres, pero con espacios
    [InlineData("telefono", "11a5550001")]       // 10 caracteres, pero con una letra
    [InlineData("telefono", "")]
    [InlineData("hash", "abc")]
    public void Un_valor_invalido_se_rechaza(string campo, string valor)
    {
        using var d = new DatosDePrueba();
        var paciente = d.Paciente();
        var usuario = d.UsuarioDePaciente(paciente);

        var e = Falla(() => Insertar(paciente, usuario,
            origen: campo == "origen" ? valor : "perfil",
            telefono: campo == "telefono" ? valor : "1155550001",
            hash: campo == "hash" ? valor : Hash));

        Assert.Contains(e.Number, Rechazos);
        Assert.Equal(0, Filas(paciente));
    }

    [Fact]
    public void La_revocacion_tiene_que_traer_la_fecha_y_el_motivo_juntos()
    {
        using var d = new DatosDePrueba();
        var paciente = d.Paciente();
        var usuario = d.UsuarioDePaciente(paciente);

        Assert.Equal(547, Falla(() => Insertar(paciente, usuario, revocado: Inicio.AddMinutes(1), motivo: null)).Number);   // fecha sin motivo
        Assert.Equal(547, Falla(() => Insertar(paciente, usuario, revocado: null, motivo: "paciente")).Number);             // motivo sin fecha
        Assert.Equal(0, Filas(paciente));
    }

    [Fact]
    public void No_se_puede_revocar_antes_de_haber_aceptado_ni_con_un_motivo_inventado()
    {
        using var d = new DatosDePrueba();
        var paciente = d.Paciente();
        var usuario = d.UsuarioDePaciente(paciente);

        Assert.Equal(547, Falla(() => Insertar(paciente, usuario, revocado: Inicio.AddMinutes(-1), motivo: "paciente")).Number);
        Assert.Equal(547, Falla(() => Insertar(paciente, usuario, revocado: Inicio.AddMinutes(1), motivo: "porque-si")).Number);
        Assert.Equal(0, Filas(paciente));

        // Los cuatro motivos válidos: una revocación cerrada no cuenta como "abierta", así que pueden convivir.
        foreach (var motivo in new[] { "paciente", "baja-whatsapp", "telefono-cambiado", "texto-nuevo" })
            Insertar(paciente, usuario, revocado: Inicio.AddMinutes(1), motivo: motivo);
        Assert.Equal(4, Filas(paciente));
    }

    [Fact]
    public void Un_paciente_no_puede_tener_dos_aceptaciones_abiertas_pero_si_una_nueva_despues_de_revocar()
    {
        using var d = new DatosDePrueba();
        var paciente = d.Paciente();
        var usuario = d.UsuarioDePaciente(paciente);

        Insertar(paciente, usuario);
        var duplicada = Falla(() => Insertar(paciente, usuario, telefono: "1155550002"));
        Assert.Contains(duplicada.Number, new[] { 2601, 2627 });
        Assert.Equal(1, Filas(paciente));

        // Revocada la primera, vuelve a aceptar: otra fila (queda el historial) y otra vez UNA sola abierta.
        DatosDePrueba.Ejecutar("UPDATE ConsentimientoWhatsApp SET RevocadoUtc = @r, MotivoRevocacion = 'paciente' WHERE IdPaciente = @p",
            ("@r", Inicio.AddMinutes(5)), ("@p", paciente));
        Insertar(paciente, usuario, telefono: "1155550002", aceptado: Inicio.AddMinutes(10));
        Assert.Equal(2, Filas(paciente));
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM ConsentimientoWhatsApp WHERE IdPaciente = @p AND RevocadoUtc IS NULL", ("@p", paciente)));
    }

    [Fact]
    public void Dos_pacientes_pueden_tener_cada_uno_su_aceptacion_abierta()
    {
        using var d = new DatosDePrueba();
        var uno = d.Paciente();
        var dos = d.Paciente();

        Insertar(uno, d.UsuarioDePaciente(uno));
        Insertar(dos, d.UsuarioDePaciente(dos));

        Assert.Equal(1, Filas(uno));
        Assert.Equal(1, Filas(dos));
    }

    [Fact]
    public void La_aceptacion_tiene_que_ser_de_un_paciente_y_de_un_usuario_que_existen()
    {
        using var d = new DatosDePrueba();
        var paciente = d.Paciente();
        var usuario = d.UsuarioDePaciente(paciente);

        Assert.Equal(547, Falla(() => Insertar(idPaciente: 2_000_000_000, idUsuario: usuario)).Number);
        Assert.Equal(547, Falla(() => Insertar(idPaciente: paciente, idUsuario: 2_000_000_000)).Number);
        Assert.Equal(0, Filas(paciente));
    }

    [Fact]
    public void Borrar_al_paciente_de_una_prueba_se_lleva_sus_consentimientos_sin_dejar_basura()
    {
        // El ayudante de limpieza sigue las claves foráneas solo (sys.foreign_keys): la tabla nueva no necesita nada especial.
        int paciente;
        using (var d = new DatosDePrueba())
        {
            paciente = d.Paciente();
            Insertar(paciente, d.UsuarioDePaciente(paciente));
            Assert.Equal(1, Filas(paciente));
        }

        Assert.Equal(0, Filas(paciente));
    }

    // ───────────── La migración sobre una base que ya la tiene ─────────────

    [Fact]
    public void Correr_la_migracion_sobre_una_base_que_ya_la_tiene_no_cambia_nada()
    {
        using var d = new DatosDePrueba();
        var paciente = d.Paciente();
        Insertar(paciente, d.UsuarioDePaciente(paciente));

        // Los objetos de la TABLA (no los de toda la base: otras pruebas pueden crear cosas a la vez).
        string Foto() => string.Join("|",
            DatosDePrueba.Contar("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ConsentimientoWhatsApp')"),
            DatosDePrueba.Contar("SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.ConsentimientoWhatsApp')"),
            DatosDePrueba.Contar("SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('dbo.ConsentimientoWhatsApp')"),
            DatosDePrueba.Contar("SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('dbo.ConsentimientoWhatsApp')"),
            DatosDePrueba.Contar("SELECT COUNT(*) FROM ConsentimientoWhatsApp WHERE IdPaciente = @p AND Telefono = '1155550001'", ("@p", paciente)));
        var antes = Foto();

        var script = File.ReadAllText(Path.Combine(VideoAccesoEndpointsTests.RaizDelRepositorio(), "migrations", "add_consentimiento_whatsapp.sql"));
        for (var vuelta = 0; vuelta < 2; vuelta++)
            foreach (var lote in ConsentimientoWhatsAppMigracionTests.Lotes(script))
                DatosDePrueba.Ejecutar(lote);

        Assert.Equal(antes, Foto());
    }
}
