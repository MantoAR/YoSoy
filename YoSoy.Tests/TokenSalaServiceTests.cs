using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

// El token de sala que emite la API para el signaling-server, contra las reglas de signaling-server/README.md
// ("Token de sala"): JWT HS256 con el secreto como UTF-8 y los claims sala, rol, sub y exp. La verificación
// de estos tests es propia (TokenDeSala.Verificar): no usa la librería que emitió el token. El mismo token
// verificado con el código real del signaling está en VideoInteropNodeTests.
public class TokenSalaServiceTests
{
    private const string JwtKey = "clave-de-login-de-la-api-para-las-pruebas-0123";
    private static readonly DateTimeOffset Ahora = new(2031, 3, 4, 10, 0, 0, TimeSpan.Zero);

    private static (TokenSalaService Servicio, OpcionesFijas<VideoOptions> Opciones) Crear(string? secreto = VideoApp.SecretoSenal)
    {
        var opciones = new OpcionesFijas<VideoOptions>();
        opciones.CurrentValue.SignalingSecreto = secreto;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = JwtKey }).Build();
        return (new TokenSalaService(opciones, config), opciones);
    }

    // ---------- Contrato ----------

    [Fact]
    public void El_token_cumple_el_contrato_del_signaling()
    {
        var (servicio, _) = Crear();

        var token = servicio.Emitir("turno-1-sala-de-prueba", Roles.Paciente, "31", Ahora.AddMinutes(60));

        var claims = TokenDeSala.Verificar(token, VideoApp.SecretoSenal);
        // Solo estos cuatro claims: nada de nombre, mail ni ids de la historia clínica dentro de un token que
        // viaja por la señalización.
        Assert.Equal(new[] { "exp", "rol", "sala", "sub" }, claims.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.Equal("turno-1-sala-de-prueba", claims.GetProperty("sala").GetString());
        Assert.Equal("paciente", claims.GetProperty("rol").GetString());
        Assert.Equal(JsonValueKind.String, claims.GetProperty("sub").ValueKind);
        Assert.Equal("31", claims.GetProperty("sub").GetString());
        Assert.Equal(JsonValueKind.Number, claims.GetProperty("exp").ValueKind);   // jsonwebtoken exige un número
        Assert.Equal(Ahora.AddMinutes(60), TokenDeSala.Exp(claims));
        Assert.Matches(TokenDeSala.ReSala, claims.GetProperty("sala").GetString()!);
        Assert.Matches(TokenDeSala.ReSub, claims.GetProperty("sub").GetString()!);
    }

    [Theory]
    [InlineData(Roles.Paciente)]
    [InlineData(Roles.Profesional)]
    public void Los_dos_roles_del_signaling_viajan_tal_cual(string rol)
    {
        var (servicio, _) = Crear();
        var claims = TokenDeSala.Verificar(servicio.Emitir("sala-1", rol, "7", Ahora.AddMinutes(5)), VideoApp.SecretoSenal);
        Assert.Equal(rol, claims.GetProperty("rol").GetString());
    }

    [Fact]
    public void El_encabezado_del_token_dice_HS256_y_JWT()
    {
        var (servicio, _) = Crear();
        var token = servicio.Emitir("sala-1", Roles.Paciente, "7", Ahora.AddMinutes(5));

        using var cabecera = JsonDocument.Parse(TokenDeSala.DecodificarBase64Url(token.Split('.')[0]));

        Assert.Equal("HS256", cabecera.RootElement.GetProperty("alg").GetString());
        Assert.Equal("JWT", cabecera.RootElement.GetProperty("typ").GetString());
    }

    // El signaling usa el secreto tal cual es, como UTF-8. Un secreto con acentos firmado como Latin-1 (o con
    // cualquier otra codificación) daría una firma distinta y el signaling rechazaría todos los tokens.
    [Fact]
    public void El_secreto_se_usa_como_UTF8_no_como_Latin1()
    {
        var (servicio, _) = Crear();
        var token = servicio.Emitir("sala-1", Roles.Paciente, "7", Ahora.AddMinutes(5));
        var partes = token.Split('.');

        TokenDeSala.Verificar(token, VideoApp.SecretoSenal);   // UTF-8: verifica

        using var comoLatin1 = new HMACSHA256(Encoding.Latin1.GetBytes(VideoApp.SecretoSenal));
        var firmaLatin1 = comoLatin1.ComputeHash(Encoding.ASCII.GetBytes(partes[0] + "." + partes[1]));
        Assert.False(CryptographicOperations.FixedTimeEquals(firmaLatin1, TokenDeSala.DecodificarBase64Url(partes[2])));
        Assert.NotEqual(VideoApp.SecretoSenal.Length, Encoding.UTF8.GetByteCount(VideoApp.SecretoSenal));   // el secreto de prueba sí tiene bytes de más
    }

    [Fact]
    public void Con_otro_secreto_el_token_no_verifica()
    {
        var (servicio, _) = Crear();
        var token = servicio.Emitir("sala-1", Roles.Paciente, "7", Ahora.AddMinutes(5));

        Assert.ThrowsAny<Exception>(() => TokenDeSala.Verificar(token, VideoApp.SecretoSenal + "x"));
        Assert.ThrowsAny<Exception>(() => TokenDeSala.Verificar(token, JwtKey));
    }

    // ---------- Vencimiento ----------

    [Fact]
    public void El_exp_es_el_fin_de_la_ventana_si_llega_antes_que_el_tope()
    {
        var (servicio, _) = Crear();   // tope por defecto: 120 minutos

        var exp = servicio.CalcularExp(Ahora, Ahora.AddMinutes(50));

        Assert.Equal(Ahora.AddMinutes(50), exp);
    }

    [Fact]
    public void El_exp_se_acota_a_ahora_mas_el_maximo_si_la_ventana_es_mas_larga()
    {
        var (servicio, opciones) = Crear();

        Assert.Equal(Ahora.AddMinutes(120), servicio.CalcularExp(Ahora, Ahora.AddHours(8)));

        opciones.CurrentValue.Token.MaxMinutos = 15;
        Assert.Equal(Ahora.AddMinutes(15), servicio.CalcularExp(Ahora, Ahora.AddHours(8)));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(100000, 720)]
    public void El_maximo_configurado_se_acota_a_un_rango_razonable(int configurado, int efectivoMinutos)
    {
        var (servicio, opciones) = Crear();
        opciones.CurrentValue.Token.MaxMinutos = configurado;

        Assert.Equal(Ahora.AddMinutes(efectivoMinutos), servicio.CalcularExp(Ahora, Ahora.AddDays(3)));
    }

    // El exp del JWT son segundos enteros: una fracción no puede alargar la vida del token.
    [Fact]
    public void El_exp_son_segundos_enteros_y_nunca_pasa_del_fin_de_la_ventana()
    {
        var (servicio, _) = Crear();
        var fin = Ahora.AddMinutes(50).AddMilliseconds(900);

        var exp = servicio.CalcularExp(Ahora.AddMilliseconds(750), fin);

        Assert.Equal(Ahora.AddMinutes(50), exp);
        Assert.True(exp <= fin);
    }

    // ---------- Configuración ----------

    [Fact]
    public void Con_un_secreto_valido_no_hay_problema_de_configuracion()
    {
        Assert.Null(Crear().Servicio.ProblemaDeConfiguracion());
        Assert.Null(Crear(new string('x', 32)).Servicio.ProblemaDeConfiguracion());   // 32 es el mínimo
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("      ")]
    [InlineData("k7Q-9zX")]
    [InlineData("1234567890123456789012345678901")]   // 31 caracteres
    [InlineData(JwtKey)]
    public void Un_secreto_ausente_corto_o_igual_a_Jwt_Key_es_un_problema_y_el_motivo_no_incluye_el_secreto(string? secreto)
    {
        var problema = Crear(secreto).Servicio.ProblemaDeConfiguracion();

        Assert.NotNull(problema);
        Assert.Contains("Video__SignalingSecreto", problema);
        if (!string.IsNullOrWhiteSpace(secreto)) Assert.DoesNotContain(secreto, problema);
    }

    [Fact]
    public void Emitir_sin_configuracion_valida_falla_sin_revelar_el_secreto()
    {
        var (servicio, _) = Crear("corto-y-secreto");

        var error = Assert.Throws<InvalidOperationException>(() => servicio.Emitir("sala-1", Roles.Paciente, "7", Ahora.AddMinutes(5)));

        Assert.DoesNotContain("corto-y-secreto", error.Message);
    }

    // ---------- Reglas de los secretos ----------

    [Fact]
    public void Los_secretos_de_video_necesitan_32_caracteres_y_ser_distintos_de_Jwt_Key()
    {
        Assert.Null(VideoSecretos.Problema("X", new string('a', 32), JwtKey));
        Assert.Null(VideoSecretos.Problema("X", new string('a', 32), null));            // sin Jwt:Key no hay con qué comparar
        Assert.Null(VideoSecretos.Problema("X", JwtKey + "!", JwtKey));                   // distinto en un carácter: alcanza
        Assert.Contains("demasiado corto", VideoSecretos.Problema("X", new string('a', 31), JwtKey));
        Assert.Contains("no está configurado", VideoSecretos.Problema("X", null, JwtKey));
        Assert.Contains("Jwt:Key", VideoSecretos.Problema("X", JwtKey, JwtKey));
    }
}
