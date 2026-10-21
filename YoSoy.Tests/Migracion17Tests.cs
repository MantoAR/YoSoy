using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

// La migración 17 (migrations/add_chat_personal_entrantes.sql): lo que el chat de WhatsApp para el personal necesita para guardar completos los mensajes que entran
// (MensajeChat.IdExterno y MensajeChat.Tipo, Conversacion.NombreContacto, y un índice único filtrado para no guardar dos veces el mismo mensaje).
//
//  · Migracion17ScriptTests (sin base): la migración solo agrega, cada cambio está protegido por un IF (se puede correr dos veces) y define EXACTAMENTE lo mismo que
//    el esquema consolidado docs/produccion/01-produccion.sql, en el mismo orden de columnas.
//  · Migracion17BaseRealTests (contra la base de pruebas, que tiene que tener la 17: ver Scripts\crear-base-de-pruebas.ps1): las columnas, la restricción del tipo, el
//    índice único filtrado y que correr la migración otra vez no cambia nada. Se saltean contra una base SIN la 17 con -Filtro 'Migracion!=17'.
public class Migracion17ScriptTests
{
    private static string Migracion => File.ReadAllText(Path.Combine(VideoAccesoEndpointsTests.RaizDelRepositorio(), "migrations", "add_chat_personal_entrantes.sql"));
    private static string Esquema => File.ReadAllText(Path.Combine(VideoAccesoEndpointsTests.RaizDelRepositorio(), "docs", "produccion", "01-produccion.sql"));

    /// <summary>Minúsculas, sin corchetes, sin "dbo." y sin espacios ni paréntesis: dos formas de escribir lo mismo dan lo mismo.</summary>
    private static string Normalizado(string s) =>
        Regex.Replace(s.ToLowerInvariant().Replace("[", "").Replace("]", "").Replace("dbo.", ""), @"[\s()]+", "");

    /// <summary>Los renglones de la definición de una tabla (columnas y CONSTRAINT), separados por las comas del nivel de afuera.</summary>
    private static List<string> ItemsDeLaTabla(string script, string tabla)
    {
        var sinComentarios = ConsentimientoWhatsAppMigracionTests.SinComentarios(script);
        var m = Regex.Match(sinComentarios, @"CREATE\s+TABLE\s+(?:\[?dbo\]?\.)?\[?" + tabla + @"\]?\s*\(", RegexOptions.IgnoreCase);
        Assert.True(m.Success, $"no encontré el CREATE TABLE de {tabla}");

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
        throw new InvalidOperationException($"el CREATE TABLE de {tabla} no cierra");
    }

    /// <summary>Lo que la migración agrega con "ALTER TABLE x ADD ...;": la tabla y la definición (columna o restricción).</summary>
    private static List<(string Tabla, string Definicion)> Agregados() =>
        Regex.Matches(ConsentimientoWhatsAppMigracionTests.SinComentarios(Migracion), @"ALTER\s+TABLE\s+(?:\[?dbo\]?\.)?\[?(?<t>\w+)\]?\s+ADD\s+(?<d>[^;]+);",
                RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Select(m => (m.Groups["t"].Value, m.Groups["d"].Value.Trim())).ToList();

    // ───────────── La migración ─────────────

    [Fact]
    public void La_migracion_solo_agrega_y_cada_cambio_esta_protegido_por_un_IF()
    {
        var lotes = ConsentimientoWhatsAppMigracionTests.Lotes(Migracion);
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
    public void Cada_columna_nueva_y_la_restriccion_se_agregan_en_su_propio_lote_para_que_el_lote_de_arriba_ya_la_haya_creado()
    {
        // Un ALTER que nombra una columna creada en el MISMO lote no compila (la columna todavía no existe): por eso la restricción del tipo va en un lote aparte.
        foreach (var lote in ConsentimientoWhatsAppMigracionTests.Lotes(Migracion))
            Assert.True(Regex.Matches(lote, @"ALTER\s+TABLE", RegexOptions.IgnoreCase).Count <= 1, "este lote cambia más de una vez el esquema:\n" + lote);
    }

    [Fact]
    public void La_migracion_pone_QUOTED_IDENTIFIER_ON_antes_del_indice_filtrado()
    {
        var script = ConsentimientoWhatsAppMigracionTests.SinComentarios(Migracion);

        var set = Regex.Match(script, @"SET\s+QUOTED_IDENTIFIER\s+ON", RegexOptions.IgnoreCase);
        var indice = Regex.Match(script, @"CREATE\s+UNIQUE\s+INDEX\s+UQ_MensajeChat_IdExterno", RegexOptions.IgnoreCase);
        Assert.True(set.Success && indice.Success);
        Assert.True(set.Index < indice.Index, "sin QUOTED_IDENTIFIER ON, CREATE INDEX con WHERE falla (error 1934) si no se corre con -I");
    }

    [Fact]
    public void La_migracion_solo_toca_MensajeChat_y_Conversacion_y_no_crea_tablas()
    {
        var script = ConsentimientoWhatsAppMigracionTests.SinComentarios(Migracion);

        var tablas = Regex.Matches(script, @"ALTER\s+TABLE\s+(?:\[?dbo\]?\.)?\[?(\w+)\]?", RegexOptions.IgnoreCase)
            .Select(m => m.Groups[1].Value).Distinct().OrderBy(t => t, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "Conversacion", "MensajeChat" }, tablas);
        Assert.DoesNotMatch(@"(?i)CREATE\s+TABLE", script);
    }

    // ───────────── La migración y el esquema consolidado: lo mismo ─────────────

    [Fact]
    public void Todo_lo_que_agrega_la_migracion_esta_igual_en_el_esquema_consolidado()
    {
        var agregados = Agregados();
        Assert.Equal(4, agregados.Count);   // tres columnas y la restricción CHECK del tipo

        foreach (var (tabla, definicion) in agregados)
            Assert.Contains(Normalizado(definicion), ItemsDeLaTabla(Esquema, tabla).Select(Normalizado));
    }

    [Fact]
    public void En_el_esquema_consolidado_las_columnas_nuevas_son_las_ultimas_como_cuando_las_agrega_la_migracion()
    {
        static List<string> Columnas(string tabla) =>
            ItemsDeLaTabla(Esquema, tabla).Where(i => !Regex.IsMatch(i, @"^CONSTRAINT\s", RegexOptions.IgnoreCase)).Select(Normalizado).ToList();

        var deLaMigracion = Agregados().Where(a => !Regex.IsMatch(a.Definicion, @"^CONSTRAINT\s", RegexOptions.IgnoreCase));

        Assert.Equal(deLaMigracion.Where(a => a.Tabla == "MensajeChat").Select(a => Normalizado(a.Definicion)), Columnas("MensajeChat").TakeLast(2));
        Assert.Equal(deLaMigracion.Where(a => a.Tabla == "Conversacion").Select(a => Normalizado(a.Definicion)), Columnas("Conversacion").TakeLast(1));
    }

    [Fact]
    public void La_migracion_y_el_esquema_consolidado_tienen_el_mismo_indice_unico_filtrado()
    {
        const string indice = @"CREATE\s+UNIQUE\s+INDEX\s+\[?UQ_MensajeChat_IdExterno\]?[^;]*;";

        var deLaMigracion = Regex.Matches(ConsentimientoWhatsAppMigracionTests.SinComentarios(Migracion), indice, RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Select(m => Normalizado(m.Value)).ToList();
        var delEsquema = Regex.Matches(ConsentimientoWhatsAppMigracionTests.SinComentarios(Esquema), indice, RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Select(m => Normalizado(m.Value)).ToList();

        Assert.Single(deLaMigracion);
        Assert.Equal(deLaMigracion, delEsquema);
        Assert.Contains("idconversacion,idexternowhereidexternoisnotnull", deLaMigracion[0]);   // Normalizado ya sacó los paréntesis
    }

    [Fact]
    public void El_encabezado_del_esquema_cuenta_24_migraciones()
    {
        Assert.Contains("más las 24 migraciones de migrations/", Esquema);
    }
}

[Trait("Categoria", "Integracion")]
[Trait("Migracion", "17")]   // necesita las columnas de la migración 17: se saltea con -Filtro 'Migracion!=17' contra una base sin ella
public class Migracion17BaseRealTests : IDisposable
{
    private readonly DatosDePrueba _datos = new();

    public void Dispose() => _datos.Dispose();

    private string ConversacionNueva()
    {
        var telefono = "11" + Random.Shared.Next(10_000_000, 99_999_999);
        _datos.AlLimpiar("Conversacion", "Telefono", telefono);   // y en cascada sus mensajes
        DatosDePrueba.Ejecutar("INSERT INTO Conversacion (Telefono, Canal) VALUES (@t, 'Simulado')", ("@t", telefono));
        return telefono;
    }

    private static int IdDe(string telefono) =>
        Convert.ToInt32(DatosDePrueba.Escalar("SELECT IdConversacion FROM Conversacion WHERE Telefono = @t", ("@t", telefono)));

    private static void Mensaje(int idConversacion, string? idExterno = null, string? tipo = null)
    {
        // Sin Tipo ni IdExterno es el INSERT de la API anterior: tiene que seguir andando.
        if (tipo is null && idExterno is null)
            DatosDePrueba.Ejecutar("INSERT INTO MensajeChat (IdConversacion, Direccion, Origen, Texto) VALUES (@c, 'Entrante', 'Paciente', 'hola')", ("@c", idConversacion));
        else
            DatosDePrueba.Ejecutar(
                "INSERT INTO MensajeChat (IdConversacion, Direccion, Origen, Texto, IdExterno, Tipo) VALUES (@c, 'Entrante', 'Paciente', 'hola', @i, @t)",
                ("@c", idConversacion), ("@i", idExterno), ("@t", tipo ?? "texto"));
    }

    [Fact]
    public void Las_tres_columnas_existen_con_su_tipo_su_nulabilidad_y_su_valor_por_defecto()
    {
        string Columna(string tabla, string columna) =>
            (string)DatosDePrueba.Escalar(@"
                SELECT t.name + '(' + CAST(c.max_length / 2 AS varchar(10)) + ')|' + CASE WHEN c.is_nullable = 1 THEN 'NULL' ELSE 'NOT NULL' END + '|' + ISNULL(d.definition, '')
                FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
                LEFT JOIN sys.default_constraints d ON d.parent_object_id = c.object_id AND d.parent_column_id = c.column_id
                WHERE c.object_id = OBJECT_ID(@tabla) AND c.name = @columna", ("@tabla", "dbo." + tabla), ("@columna", columna))!;

        Assert.Equal("nvarchar(64)|NULL|", Columna("MensajeChat", "IdExterno"));
        Assert.Equal("nvarchar(12)|NOT NULL|('texto')", Columna("MensajeChat", "Tipo"));
        Assert.Equal("nvarchar(100)|NULL|", Columna("Conversacion", "NombreContacto"));
    }

    [Fact]
    public void Un_mensaje_que_se_inserta_como_lo_hacia_la_API_anterior_queda_como_texto_y_sin_id()
    {
        var c = IdDe(ConversacionNueva());

        Mensaje(c);

        Assert.Equal("texto", DatosDePrueba.Escalar("SELECT Tipo FROM MensajeChat WHERE IdConversacion = @c", ("@c", c)));
        Assert.Null(DatosDePrueba.Escalar("SELECT IdExterno FROM MensajeChat WHERE IdConversacion = @c", ("@c", c)));
    }

    [Theory]
    [InlineData("texto")]
    [InlineData("audio")]
    [InlineData("imagen")]
    [InlineData("video")]
    [InlineData("documento")]
    [InlineData("sticker")]
    [InlineData("ubicacion")]
    [InlineData("contacto")]
    [InlineData("otro")]
    public void La_restriccion_del_tipo_acepta_cada_uno_de_los_nueve(string tipo)
    {
        var c = IdDe(ConversacionNueva());

        Mensaje(c, tipo: tipo);

        Assert.Equal(tipo, DatosDePrueba.Escalar("SELECT Tipo FROM MensajeChat WHERE IdConversacion = @c", ("@c", c)));
    }

    [Theory]
    [InlineData("gif")]
    [InlineData("videos")]
    [InlineData("imagen2")]
    [InlineData("")]
    public void La_restriccion_del_tipo_rechaza_lo_que_no_esta_en_la_lista(string tipo)
    {
        var c = IdDe(ConversacionNueva());

        var error = Assert.Throws<SqlException>(() => Mensaje(c, tipo: tipo));

        Assert.Equal(547, error.Number);                                  // viola una restricción CHECK
        Assert.Contains("CK_MensajeChat_Tipo", error.Message);
    }

    [Fact]
    public void El_indice_unico_rechaza_el_mismo_id_en_la_misma_conversacion_pero_no_en_otra_ni_sin_id()
    {
        var a = IdDe(ConversacionNueva());
        var b = IdDe(ConversacionNueva());

        Mensaje(a, idExterno: "3EB0AAAA");

        var repetido = Assert.Throws<SqlException>(() => Mensaje(a, idExterno: "3EB0AAAA"));
        Assert.True(repetido.Number is 2601 or 2627);                     // viola el índice único
        Assert.Contains("UQ_MensajeChat_IdExterno", repetido.Message);

        Mensaje(b, idExterno: "3EB0AAAA");                                // el mismo id en OTRA conversación es otro mensaje
        Mensaje(a, idExterno: "3EB0BBBB");                                // otro id en la misma
        Mensaje(a);                                                       // y los mensajes sin id no cuentan: tantos como haga falta
        Mensaje(a);
        Assert.Equal(4, DatosDePrueba.Contar("SELECT COUNT(*) FROM MensajeChat WHERE IdConversacion = @c", ("@c", a)));
    }

    // Columnas, restricciones y índices de las dos tablas, leídos de las vistas del catálogo y juntados de este lado (sin STRING_AGG, que pide SQL Server 2017).
    private static string Huella()
    {
        var partes = new List<string>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        foreach (var consulta in new[]
        {
            @"SELECT CONCAT(OBJECT_NAME(c.object_id), '.', c.column_id, '.', c.name, '.', c.max_length, '.', c.is_nullable) FROM sys.columns c
              WHERE c.object_id IN (OBJECT_ID('dbo.MensajeChat'), OBJECT_ID('dbo.Conversacion')) ORDER BY c.object_id, c.column_id",
            @"SELECT CONCAT(name, '=', definition) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('dbo.MensajeChat') ORDER BY name",
            @"SELECT CONCAT(name, '=', ISNULL(filter_definition, '')) FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.MensajeChat') AND name IS NOT NULL ORDER BY name"
        })
        {
            using var cmd = new SqlCommand(consulta, con);
            using var r = cmd.ExecuteReader();
            while (r.Read()) partes.Add(r.GetString(0));
            partes.Add("|");
        }
        return string.Join(";", partes);
    }

    [Fact]
    public void Correr_la_migracion_sobre_una_base_que_ya_la_tiene_no_cambia_nada()
    {
        var antes = Huella();
        var lotes = ConsentimientoWhatsAppMigracionTests.Lotes(File.ReadAllText(Path.Combine(VideoAccesoEndpointsTests.RaizDelRepositorio(), "migrations", "add_chat_personal_entrantes.sql")));
        foreach (var lote in lotes) DatosDePrueba.Ejecutar(lote);       // la 2.a vez (o la 3.a): sin error...
        var despues = Huella();

        Assert.Equal(antes, despues);                                    // ...y sin cambiar ni una columna, restricción o índice
        Assert.Contains("UQ_MensajeChat_IdExterno", despues);
        Assert.Contains("CK_MensajeChat_Tipo", despues);
    }
}
