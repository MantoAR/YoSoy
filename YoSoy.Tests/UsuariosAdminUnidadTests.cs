using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

// Fase 2, PR 2 (pantalla Usuarios del Configurador), lo que no necesita base de datos: los filtros de la lista, la clave temporal y
// su hash, el pedido de asignación, el token de cambio de clave y los archivos de la migración 24. La parte con base real está en
// UsuariosAdminBaseRealTests y MigracionDebeCambiarPasswordTests.
public class UsuariosAdminUnidadTests
{
    private static string Raiz => VideoAccesoEndpointsTests.RaizDelRepositorio();
    private static string Leer(params string[] partes) => File.ReadAllText(Path.Combine(new[] { Raiz }.Concat(partes).ToArray()));

    // ───────────── Los filtros de la lista ─────────────

    [Fact]
    public void Sin_filtros_la_lista_son_las_cuentas_de_todos_los_tipos_de_a_25()
    {
        var (f, error) = UsuariosLista.Normalizar(null, null, null, null, null);
        Assert.Null(error);
        Assert.Equal(("todos", "todos", 1, 25), (f!.Tipo, f.Estado, f.Pagina, f.Cantidad));
        Assert.Empty(f.Palabras);
    }

    [Theory]
    [InlineData("paciente"), InlineData("PROFESIONAL"), InlineData(" equipo "), InlineData("todos")]
    public void Acepta_los_cuatro_tipos(string tipo) => Assert.Null(UsuariosLista.Normalizar(null, tipo, null, null, null).Error);

    [Theory]
    [InlineData("activo"), InlineData("inactivo"), InlineData("pendiente"), InlineData("bloqueado"), InlineData("sin-cuenta"), InlineData("todos")]
    public void Acepta_los_seis_estados(string estado) => Assert.Null(UsuariosLista.Normalizar(null, null, estado, null, null).Error);

    [Theory]
    [InlineData("admin", null, null, null)]
    [InlineData(null, "borrado", null, null)]
    [InlineData(null, null, 0, null)]
    [InlineData(null, null, -3, null)]
    [InlineData(null, null, null, 0)]
    [InlineData(null, null, null, 101)]
    public void Rechaza_lo_que_no_es_un_valor_conocido(string? tipo, string? estado, int? pagina, int? cantidad)
    {
        var (f, error) = UsuariosLista.Normalizar(null, tipo, estado, pagina, cantidad);
        Assert.Null(f);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void El_buscador_se_parte_en_palabras_sin_pasarse_de_cinco_ni_de_50_caracteres()
    {
        var (f, _) = UsuariosLista.Normalizar("  perez   juan\tcarlos a b c d e " + new string('x', 80), null, null, null, null);
        Assert.Equal(UsuariosLista.MaxPalabras, f!.Palabras.Count);
        Assert.Equal(new[] { "perez", "juan", "carlos", "a", "b" }, f.Palabras);
        var (largo, _) = UsuariosLista.Normalizar(new string('y', 80), null, null, null, null);
        Assert.Equal(UsuariosLista.MaxLargoPalabra, largo!.Palabras.Single().Length);
    }

    [Theory]
    [InlineData("100%", "%100\\%%")]
    [InlineData("a_b", "%a\\_b%")]
    [InlineData("[abc]", "%\\[abc]%")]
    [InlineData("c:\\x", "%c:\\\\x%")]
    [InlineData("perez", "%perez%")]
    public void Lo_que_se_busca_se_toma_literal_sin_comodines_de_LIKE(string palabra, string patron) =>
        Assert.Equal(patron, UsuariosLista.PatronLike(palabra));

    // ───────────── La clave temporal ─────────────

    [Fact]
    public void La_clave_temporal_cumple_la_politica_de_contrasenas_siempre()
    {
        var claves = Enumerable.Range(0, 2000).Select(_ => RestablecerPassword.NuevaTemporal()).ToList();
        Assert.All(claves, c => Assert.Null(PoliticaContrasena.Validar(c)));
        Assert.Equal(2000, claves.Distinct().Count());   // del generador criptográfico: no se repiten
        Assert.All(claves, c => Assert.Equal(CrearUsuarioProfesionalService.LargoContrasenaTemporal, c.Length));
    }

    [Fact]
    public void Se_guarda_con_el_mismo_hash_que_compara_el_ingreso()
    {
        // POST /login: SHA-256 del UTF-8, en hexadecimal mayúscula, comparado sin mirar mayúsculas.
        var clave = RestablecerPassword.NuevaTemporal();
        var delIngreso = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clave))).ToUpper();
        Assert.Equal(delIngreso, RestablecerPassword.Hash(clave).ToUpper());
        Assert.Equal(64, RestablecerPassword.Hash(clave).Length);
    }

    // ───────────── El pedido de asignación ─────────────

    [Fact]
    public void El_pedido_de_asignacion_se_valida_y_no_repite_ids()
    {
        Assert.Equal(new[] { 3, 5 }, AsignacionAdmin.Validar(new[] { 3, 5, 3 }).Ids);
        Assert.NotNull(AsignacionAdmin.Validar(null).Error);
        Assert.NotNull(AsignacionAdmin.Validar(Array.Empty<int>()).Error);
        Assert.NotNull(AsignacionAdmin.Validar(new[] { 1, 0 }).Error);
        Assert.NotNull(AsignacionAdmin.Validar(new[] { -4 }).Error);
        Assert.NotNull(AsignacionAdmin.Validar(Enumerable.Range(1, AsignacionAdmin.MaxPorPedido + 1).ToList()).Error);
        Assert.Null(AsignacionAdmin.Validar(Enumerable.Range(1, AsignacionAdmin.MaxPorPedido).ToList()).Error);
    }

    // ───────────── El token de cambio de clave ─────────────

    private static JwtEmisorService Emisor() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = "una-clave-de-prueba-bien-larga-1234567890" }).Build());

    [Fact]
    public void El_token_de_cambio_de_clave_vale_cinco_minutos_lleva_su_uso_y_ningun_rol()
    {
        var token = new JwtSecurityTokenHandler().ReadJwtToken(Emisor().EmitirCambioClave(42));
        Assert.Equal("cambio-clave", token.Claims.Single(c => c.Type == JwtEmisorService.ClaimUso).Value);
        Assert.Equal("42", token.Claims.Single(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier).Value);
        Assert.DoesNotContain(token.Claims, c => c.Type is System.Security.Claims.ClaimTypes.Role or JwtEmisorService.ClaimModulo or JwtEmisorService.ClaimPrivilegio);
        var dura = token.ValidTo - DateTime.UtcNow;
        Assert.InRange(dura.TotalMinutes, 4.5, 5.1);
        Assert.Equal(5, JwtEmisorService.MinutosCambioClave);
    }

    [Fact]
    public void El_token_de_cambio_no_se_confunde_con_el_de_seleccion()
    {
        var emisor = Emisor();
        var cambio = new JwtSecurityTokenHandler().ReadJwtToken(emisor.EmitirCambioClave(1));
        var seleccion = new JwtSecurityTokenHandler().ReadJwtToken(emisor.EmitirSeleccion(1));
        Assert.NotEqual(cambio.Claims.Single(c => c.Type == JwtEmisorService.ClaimUso).Value, seleccion.Claims.Single(c => c.Type == JwtEmisorService.ClaimUso).Value);
    }

    // ───────────── La migración 24 (el texto de los archivos) ─────────────

    [Fact]
    public void La_migracion_24_es_aditiva_idempotente_y_se_registra_con_origen_Script()
    {
        var sql = Leer("migrations", "add_debe_cambiar_password.sql");
        Assert.Contains("IF COL_LENGTH('dbo.Usuario', 'DebeCambiarPassword') IS NULL", sql);
        Assert.Contains("ADD DebeCambiarPassword BIT NOT NULL", sql);
        Assert.Contains("DEFAULT (0)", sql);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM VersionEsquema WHERE Numero = 24)", sql);
        Assert.Contains("(24, N'add_debe_cambiar_password.sql', 'Script')", sql);
        // Aditiva: nada de borrar ni de cambiar lo que ya existe.
        Assert.DoesNotMatch(@"(?im)^\s*(DROP|DELETE|TRUNCATE|UPDATE)\b", sql);
    }

    [Fact]
    public void El_esquema_consolidado_trae_la_migracion_24_y_el_verificador_la_conoce()
    {
        var esquema = Leer("docs", "produccion", "01-produccion.sql");
        Assert.Contains("DebeCambiarPassword", esquema);
        Assert.Contains("(24, N'add_debe_cambiar_password.sql', 'Script')", esquema);
        Assert.Equal(36, VerificadorEsquema.VersionRequerida);   // la 25 (Chats v2), la 26 (cierre de sesión), la 27 (alta y derivación) y la 28 (consentimientos del circuito) son opcionales; la 29 (agenda de admisión) y la 30 (vigencia de la agenda) se exigen; la 24 sigue siendo la última que el código EXIGE
        var m = VerificadorEsquema.Migraciones.Single(x => x.Numero == 24);
        Assert.Equal("add_debe_cambiar_password.sql", m.Archivo);
        Assert.Contains("DebeCambiarPassword", m.Marcador);
        Assert.False(m.Opcional);   // el ingreso lee la columna: sin ella no entra nadie
        Assert.True(File.Exists(Path.Combine(Raiz, "migrations", m.Archivo)));
    }

    [Fact]
    public void El_esquema_consolidado_trae_la_migracion_29_y_el_verificador_la_exige()
    {
        var esquema = Leer("docs", "produccion", "01-produccion.sql");
        Assert.Contains("EsAdmision", esquema);
        Assert.Contains("IdProfesionalEntrevista", esquema);
        Assert.Contains("(29, N'add_admision_agenda_entrevista.sql', 'Script')", esquema);
        var m = VerificadorEsquema.Migraciones.Single(x => x.Numero == 29);
        Assert.Equal("add_admision_agenda_entrevista.sql", m.Archivo);
        Assert.False(m.Opcional);   // la grilla de admisiones y la agenda leen las dos columnas
        Assert.True(File.Exists(Path.Combine(Raiz, "migrations", m.Archivo)));
        Assert.True(File.Exists(Path.Combine(Raiz, "docs", "produccion", "deshacer-migracion-29.sql")));
    }

    [Fact]
    public void El_esquema_consolidado_trae_la_migracion_30_y_el_verificador_la_exige()
    {
        var esquema = Leer("docs", "produccion", "01-produccion.sql");
        Assert.Contains("CK_Agenda_Vigencia", esquema);
        Assert.Contains("(30, N'add_vigencia_agenda.sql', 'Script')", esquema);
        var m = VerificadorEsquema.Migraciones.Single(x => x.Numero == 30);
        Assert.Equal("add_vigencia_agenda.sql", m.Archivo);
        Assert.False(m.Opcional);   // la agenda lee las dos columnas
        Assert.True(File.Exists(Path.Combine(Raiz, "migrations", m.Archivo)));
        Assert.True(File.Exists(Path.Combine(Raiz, "docs", "produccion", "deshacer-migracion-30.sql")));
    }

    [Fact]
    public void El_deshacer_propio_saca_la_columna_su_restriccion_y_la_version()
    {
        var sql = Leer("docs", "produccion", "deshacer-migracion-24.sql");
        Assert.Contains("DROP CONSTRAINT DF_Usuario_DebeCambiarPassword", sql);
        Assert.Contains("DROP COLUMN DebeCambiarPassword", sql);
        Assert.Contains("DELETE FROM VersionEsquema WHERE Numero = 24", sql);
    }

    [Fact]
    public void El_catalogo_de_scripts_lista_la_migracion_24()
    {
        var catalogo = Leer("docs", "CATALOGO_SCRIPTS_SQL.md");
        Assert.Matches(@"\| 24 \| `migrations/add_debe_cambiar_password\.sql` \|", catalogo);
    }
}
