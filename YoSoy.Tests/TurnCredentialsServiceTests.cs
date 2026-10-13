using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

// Servidores ICE y credenciales TURN efímeras (TurnCredentialsService): coturn con use-auth-secret, "TURN REST API".
//   username   = "{vencimientoUnix}:{sub}"
//   credential = Base64( HMAC-SHA1( secreto, username ) )
public class TurnCredentialsServiceTests
{
    private const string JwtKey = "clave-de-login-de-la-api-para-las-pruebas-0123";
    private const string SecretoTurn = VideoApp.SecretoTurn;
    private static readonly DateTimeOffset Ahora = new(2031, 3, 4, 10, 0, 0, TimeSpan.Zero);

    private sealed class Entorno
    {
        public readonly OpcionesFijas<VideoOptions> Opciones = new();
        public readonly LogsCapturados Logs = new();
        public readonly TurnCredentialsService Servicio;

        public Entorno(bool conTurn = true)
        {
            var o = Opciones.CurrentValue;
            o.Ice.StunUrls.Add("stun:stun.prueba.test:3478");
            if (conTurn)
            {
                o.Ice.TurnUrls.Add("turn:turn.prueba.test:3478?transport=udp");
                o.Ice.TurnUrls.Add("turn:turn.prueba.test:3478?transport=tcp");
                o.TurnSecreto = SecretoTurn;
            }
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = JwtKey }).Build();
            Servicio = new TurnCredentialsService(Opciones, config, Logs.CreateLogger<TurnCredentialsService>());
        }

        public ConfiguracionIce Generar(string sub = "31", int minutosExpiraToken = 50, int minutosVentana = 70) =>
            Servicio.Generar(sub, Ahora, Ahora.AddMinutes(minutosExpiraToken), TimeSpan.FromMinutes(minutosVentana));
    }

    // ---------- Vectores conocidos ----------

    // RFC 2202, caso 2 de HMAC-SHA1: la primitiva está bien. Verificado aparte con OpenSSL y con Python.
    [Fact]
    public void HMAC_SHA1_coincide_con_el_vector_de_la_RFC_2202()
    {
        var credencial = TurnCredentialsService.Credencial("Jefe", "what do ya want for nothing?");

        Assert.Equal("7/zfauXrL6LSdBbV8YTfnCWafHk=", credencial);
        Assert.Equal("effcdf6ae5eb2fa2d27416d5f184df9c259a7c79", Convert.ToHexString(Convert.FromBase64String(credencial)).ToLowerInvariant());
    }

    // Credenciales del estilo TURN REST (username "vencimiento:usuario"), calculadas de forma independiente con
    // `openssl dgst -sha1 -hmac` y con hmac.new(...) de Python. El tercero tiene un secreto con acentos: el secreto
    // se toma como UTF-8, que es lo que hace coturn con la cadena de su archivo de configuración.
    [Theory]
    [InlineData("secret", "1700000000:42", "ZzfPuPkrzUOsnm5YISnTiKDnXWo=")]
    [InlineData("turn-secreto-de-prueba-ABCDEFGHIJKLMNOP-0123", "1930000000:31", "f7Sl90vLyPnNxEmkFMmNqLz74x4=")]
    [InlineData("señalización-secreta-de-prueba-0123456789", "1930000000:31", "gsn57vJKopmgJZ4ITtntHMVfsU4=")]
    public void La_credencial_TURN_coincide_con_vectores_calculados_aparte(string secreto, string username, string esperada) =>
        Assert.Equal(esperada, TurnCredentialsService.Credencial(secreto, username));

    // ---------- Lo que se entrega ----------

    [Fact]
    public void Con_TURN_configurado_se_entrega_STUN_y_TURN_con_credenciales_efimeras()
    {
        var ice = new Entorno().Generar(sub: "31");

        Assert.True(ice.TurnDisponible);
        Assert.Equal(2, ice.Servidores.Count);

        var stun = ice.Servidores[0];
        Assert.Equal(new[] { "stun:stun.prueba.test:3478" }, stun.Urls);
        Assert.Null(stun.Username);
        Assert.Null(stun.Credential);

        var turn = ice.Servidores[1];
        Assert.Equal(new[] { "turn:turn.prueba.test:3478?transport=udp", "turn:turn.prueba.test:3478?transport=tcp" }, turn.Urls);
        // TTL de 2 h (el de por defecto), más largo que la ventana (70 min) y que el token (50 min).
        var vence = Ahora.AddMinutes(120).ToUnixTimeSeconds();
        Assert.Equal($"{vence}:31", turn.Username);
        Assert.Equal(TurnCredentialsService.Credencial(SecretoTurn, turn.Username!), turn.Credential);
    }

    [Fact]
    public void El_username_tiene_el_formato_que_espera_coturn()
    {
        var turn = new Entorno().Generar(sub: "31").Servidores[1];

        Assert.Matches(new Regex(@"^\d{10}:31$"), turn.Username!);
        Assert.True(long.Parse(turn.Username!.Split(':')[0]) > Ahora.ToUnixTimeSeconds());
        Assert.Matches(new Regex(@"^[A-Za-z0-9+/]{27}=$"), turn.Credential!);   // SHA-1 = 20 bytes → 28 caracteres en Base64
    }

    [Fact]
    public void Cada_usuario_recibe_credenciales_propias_y_el_mismo_pedido_da_lo_mismo()
    {
        var e = new Entorno();
        var a = e.Generar(sub: "31").Servidores[1];
        var b = e.Generar(sub: "32").Servidores[1];
        var otraVezA = e.Generar(sub: "31").Servidores[1];

        Assert.NotEqual(a.Username, b.Username);
        Assert.NotEqual(a.Credential, b.Credential);
        Assert.Equal(a.Username, otraVezA.Username);
        Assert.Equal(a.Credential, otraVezA.Credential);
    }

    // ---------- Vencimiento ----------

    [Fact]
    public void El_vencimiento_nunca_es_menor_que_la_ventana_de_la_sesion()
    {
        var e = new Entorno();
        e.Opciones.CurrentValue.Turn.TtlMinutos = 10;      // más corto que la sesión (70 min)

        var turn = e.Generar(minutosExpiraToken: 50, minutosVentana: 70).Servidores[1];

        Assert.Equal($"{Ahora.AddMinutes(70).ToUnixTimeSeconds()}:31", turn.Username);
    }

    [Fact]
    public void El_vencimiento_nunca_es_menor_que_el_exp_del_token_de_sala()
    {
        var e = new Entorno();
        e.Opciones.CurrentValue.Turn.TtlMinutos = 10;

        var turn = e.Generar(minutosExpiraToken: 90, minutosVentana: 5).Servidores[1];

        Assert.Equal($"{Ahora.AddMinutes(90).ToUnixTimeSeconds()}:31", turn.Username);
    }

    [Theory]
    [InlineData(0, 1)]            // un TTL absurdo se acota a 1 minuto...
    [InlineData(-30, 1)]
    [InlineData(999999, 1440)]    // ...o a un día
    [InlineData(45, 45)]
    public void El_TTL_se_acota_a_un_rango_razonable(int configurado, int efectivoMinutos)
    {
        var e = new Entorno();
        e.Opciones.CurrentValue.Turn.TtlMinutos = configurado;

        var turn = e.Generar(minutosExpiraToken: 0, minutosVentana: 0).Servidores[1];

        Assert.Equal($"{Ahora.AddMinutes(efectivoMinutos).ToUnixTimeSeconds()}:31", turn.Username);
    }

    // ---------- Sin TURN / TURN mal configurado ----------

    [Fact]
    public void Sin_TURN_configurado_solo_hay_STUN_y_la_bandera_lo_dice()
    {
        var ice = new Entorno(conTurn: false).Generar();

        Assert.False(ice.TurnDisponible);
        var servidor = Assert.Single(ice.Servidores);
        Assert.Equal(new[] { "stun:stun.prueba.test:3478" }, servidor.Urls);
        Assert.Null(servidor.Username);
        Assert.Null(servidor.Credential);
    }

    [Fact]
    public void Sin_URLs_de_TURN_no_se_entregan_credenciales_aunque_haya_secreto()
    {
        var e = new Entorno();
        e.Opciones.CurrentValue.Ice.TurnUrls.Clear();

        var ice = e.Generar();

        Assert.False(ice.TurnDisponible);
        Assert.Single(ice.Servidores);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("      ")]
    [InlineData("k7Q-9zX")]
    [InlineData("1234567890123456789012345678901")]   // 31 caracteres: uno menos que el mínimo
    [InlineData(JwtKey)]                              // el mismo valor que Jwt:Key
    public void Con_el_secreto_de_TURN_ausente_flojo_o_igual_a_Jwt_Key_solo_hay_STUN_y_se_avisa_sin_el_secreto(string? secreto)
    {
        var e = new Entorno();
        e.Opciones.CurrentValue.TurnSecreto = secreto;

        var ice = e.Generar();

        Assert.False(ice.TurnDisponible);
        var servidor = Assert.Single(ice.Servidores);
        Assert.Null(servidor.Username);
        var aviso = Assert.Single(e.Logs.Lineas, l => l.StartsWith("Warning|"));
        Assert.Contains("Video__TurnSecreto", aviso);
        if (!string.IsNullOrWhiteSpace(secreto)) Assert.DoesNotContain(secreto, e.Logs.Todo);
    }

    [Fact]
    public void Un_secreto_de_TURN_de_32_caracteres_alcanza()
    {
        var e = new Entorno();
        e.Opciones.CurrentValue.TurnSecreto = new string('x', 32);

        Assert.True(e.Generar().TurnDisponible);
        Assert.Empty(e.Logs.Lineas);
    }

    // ---------- STUN ----------

    // Con coturn propio se pisa con Video__Ice__StunUrls__0; mientras tanto queda el STUN de Google que usan hoy
    // las pantallas, para que configurar TURN sea la única novedad.
    [Fact]
    public void Sin_STUN_configurado_queda_el_de_siempre()
    {
        var e = new Entorno();
        e.Opciones.CurrentValue.Ice.StunUrls.Clear();

        var stun = e.Generar().Servidores[0];

        Assert.Equal(new[] { "stun:stun.l.google.com:19302" }, stun.Urls);
    }

    [Fact]
    public void Las_URLs_vacias_se_ignoran_y_las_otras_se_recortan()
    {
        var e = new Entorno();
        e.Opciones.CurrentValue.Ice.StunUrls.Clear();
        e.Opciones.CurrentValue.Ice.StunUrls.AddRange(new[] { "", "   " });
        e.Opciones.CurrentValue.Ice.TurnUrls.Clear();
        e.Opciones.CurrentValue.Ice.TurnUrls.AddRange(new[] { "", " turn:turn.prueba.test:3478 ", "  " });

        var ice = e.Generar();

        Assert.Equal(new[] { "stun:stun.l.google.com:19302" }, ice.Servidores[0].Urls);
        Assert.Equal(new[] { "turn:turn.prueba.test:3478" }, ice.Servidores[1].Urls);
    }

    // ---------- Cómo viaja ----------

    // Un username null llegaría al navegador como "null": en las entradas de STUN esos campos no tienen que
    // existir en el JSON.
    [Fact]
    public void Las_entradas_de_STUN_no_llevan_username_ni_credential_en_el_JSON()
    {
        var ice = new Entorno().Generar();

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ice.Servidores, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var entradas = doc.RootElement.EnumerateArray().ToList();

        Assert.Equal(new[] { "urls" }, entradas[0].EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(new[] { "urls", "username", "credential" }, entradas[1].EnumerateObject().Select(p => p.Name).ToArray());
    }
}
