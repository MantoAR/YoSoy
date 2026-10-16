using System.Text;
using Microsoft.Extensions.Configuration;

// Cifrado en reposo de las emociones guardadas (AES-256-GCM): ida y vuelta, que el valor guardado no deje ver el
// contenido, que cualquier alteración, otra fila u otra clave se detecte, y que sin una clave válida NO se cifre ni se
// descifre nada (falla cerrada) sin que el valor de la clave aparezca en ningún mensaje.
public class EmocionesCifradoServiceTests
{
    private const string Texto = "{\"v\":1,\"promedios\":{\"neutral\":60,\"sad\":40},\"muestras\":312}";
    private const string Datos = "sesion:42";

    private static EmocionesCifradoService Servicio(string? clave = null, string? idClave = null, Dictionary<string, string?>? extra = null,
        bool sinClave = false)
    {
        var opciones = new OpcionesFijas<VideoOptions>();
        opciones.CurrentValue.Emociones.ClaveCifrado = sinClave ? null : clave ?? EscenarioEmociones.ClaveDePrueba;
        if (idClave is not null) opciones.CurrentValue.Emociones.IdClave = idClave;
        var config = new ConfigurationBuilder().AddInMemoryCollection(extra ?? new()).Build();
        return new EmocionesCifradoService(opciones, config);
    }

    [Fact]
    public void Cifra_y_descifra_de_ida_y_vuelta()
    {
        var s = Servicio();

        var guardado = s.Cifrar(Texto, Datos);
        var r = s.Descifrar(guardado, Datos, out var devuelto);

        Assert.Equal(ResultadoDescifrado.Ok, r);
        Assert.Equal(Texto, devuelto);
    }

    [Fact]
    public void El_valor_guardado_tiene_el_formato_y_no_deja_ver_el_contenido()
    {
        var s = Servicio();

        var guardado = s.Cifrar(Texto, Datos);

        Assert.Matches(@"^ys1\.k1\.[A-Za-z0-9_-]+$", guardado);
        Assert.True(EmocionesCifradoService.EstaCifrado(guardado));
        // Solo palabras largas: el texto cifrado es base64 al azar, y una subcadena corta ("60", "sad", "312") aparece
        // por casualidad en algunas corridas (con "60" fallaba ~3 de cada 100): la prueba era intermitente.
        foreach (var palabra in new[] { "neutral", "promedios", "muestras" })
            Assert.DoesNotContain(palabra, guardado);
        // Ni siquiera en la parte decodificada: no es un base64 de un JSON.
        var b64 = guardado.Split('.')[2].Replace('-', '+').Replace('_', '/');
        var bytes = Convert.FromBase64String(b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '='));
        Assert.DoesNotContain("neutral", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Cada_cifrado_usa_un_nonce_nuevo_y_el_mismo_texto_da_valores_distintos()
    {
        var s = Servicio();

        var valores = Enumerable.Range(0, 50).Select(_ => s.Cifrar(Texto, Datos)).ToList();

        Assert.Equal(50, valores.Distinct().Count());
        foreach (var v in valores) Assert.Equal(ResultadoDescifrado.Ok, s.Descifrar(v, Datos, out _));
    }

    [Fact]
    public void Un_valor_de_otra_fila_no_se_puede_descifrar_aunque_la_clave_sea_la_misma()
    {
        var s = Servicio();
        var guardado = s.Cifrar(Texto, "sesion:42");

        var r = s.Descifrar(guardado, "sesion:43", out var devuelto);

        Assert.Equal(ResultadoDescifrado.Alterado, r);
        Assert.Null(devuelto);
    }

    [Fact]
    public void Cualquier_byte_alterado_se_detecta()
    {
        var s = Servicio();
        var guardado = s.Cifrar(Texto, Datos);
        var partes = guardado.Split('.');
        var b64 = partes[2].Replace('-', '+').Replace('_', '/');
        var bytes = Convert.FromBase64String(b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '='));

        // Se prueba con un byte de cada zona: nonce (0), etiqueta (12), cifrado (28) y el último.
        foreach (var posicion in new[] { 0, 12, 28, bytes.Length - 1 })
        {
            var copia = (byte[])bytes.Clone();
            copia[posicion] ^= 0x01;
            var alterado = $"{partes[0]}.{partes[1]}.{Convert.ToBase64String(copia).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";

            Assert.Equal(ResultadoDescifrado.Alterado, s.Descifrar(alterado, Datos, out var devuelto));
            Assert.Null(devuelto);
        }
    }

    [Theory]
    [InlineData("ys1.k1.")]
    [InlineData("ys1.k1")]
    [InlineData("ys1.k1.AAAA")]                  // demasiado corto para tener nonce y etiqueta
    [InlineData("ys1.k1.%%%no-es-base64%%%")]
    [InlineData("ys1.k1.AAAA.extra")]
    public void Un_valor_malformado_se_rechaza_sin_tirar_excepcion(string valor)
    {
        var s = Servicio();

        var r = s.Descifrar(valor, Datos, out var devuelto);

        Assert.Equal(ResultadoDescifrado.Alterado, r);
        Assert.Null(devuelto);
    }

    [Fact]
    public void Con_otra_clave_no_se_descifra()
    {
        var guardado = Servicio().Cifrar(Texto, Datos);

        var r = Servicio(EscenarioEmociones.OtraClaveDePrueba).Descifrar(guardado, Datos, out var devuelto);

        Assert.Equal(ResultadoDescifrado.Alterado, r);
        Assert.Null(devuelto);
    }

    [Fact]
    public void Con_otro_id_de_clave_dice_que_la_clave_es_desconocida()
    {
        var guardado = Servicio(idClave: "k1").Cifrar(Texto, Datos);

        var r = Servicio(idClave: "k2").Descifrar(guardado, Datos, out _);

        Assert.Equal(ResultadoDescifrado.ClaveDesconocida, r);
    }

    [Fact]
    public void Un_texto_en_claro_de_antes_del_cifrado_se_reconoce_como_no_cifrado()
    {
        var s = Servicio();

        Assert.False(EmocionesCifradoService.EstaCifrado(Texto));
        Assert.Equal(ResultadoDescifrado.NoCifrado, s.Descifrar(Texto, Datos, out var devuelto));
        Assert.Null(devuelto);
    }

    [Fact]
    public void Sin_clave_no_se_cifra_ni_se_descifra_y_el_motivo_no_incluye_ningun_valor()
    {
        var s = Servicio(sinClave: true);
        var guardado = Servicio().Cifrar(Texto, Datos);

        Assert.NotNull(s.ProblemaDeConfiguracion());
        Assert.Throws<InvalidOperationException>(() => s.Cifrar(Texto, Datos));
        Assert.Equal(ResultadoDescifrado.SinClave, s.Descifrar(guardado, Datos, out var devuelto));
        Assert.Null(devuelto);
    }

    [Theory]
    [InlineData("no-es-base64!!")]
    [InlineData("AAAA")]                                                      // base64 válido, de 3 bytes
    [InlineData("QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVowMTIzNDU2Nzg5")]          // 36 bytes
    public void Una_clave_mal_formada_o_de_otro_largo_se_rechaza_sin_mostrarla(string clave)
    {
        var s = Servicio(clave);

        var problema = s.ProblemaDeConfiguracion();

        Assert.NotNull(problema);
        Assert.DoesNotContain(clave, problema);
        Assert.Throws<InvalidOperationException>(() => s.Cifrar(Texto, Datos));
    }

    [Fact]
    public void La_clave_no_puede_ser_la_misma_que_otro_secreto()
    {
        var clave = EscenarioEmociones.ClaveDePrueba;
        var conJwt = Servicio(clave, extra: new() { ["Jwt:Key"] = clave });
        Assert.NotNull(conJwt.ProblemaDeConfiguracion());
        Assert.DoesNotContain(clave, conJwt.ProblemaDeConfiguracion()!);

        var opciones = new OpcionesFijas<VideoOptions>();
        opciones.CurrentValue.Emociones.ClaveCifrado = clave;
        opciones.CurrentValue.SignalingSecreto = clave;
        Assert.NotNull(new EmocionesCifradoService(opciones, new ConfigurationBuilder().Build()).ProblemaDeConfiguracion());

        opciones.CurrentValue.SignalingSecreto = null;
        opciones.CurrentValue.TurnSecreto = clave;
        Assert.NotNull(new EmocionesCifradoService(opciones, new ConfigurationBuilder().Build()).ProblemaDeConfiguracion());
    }

    [Theory]
    [InlineData("")]
    [InlineData("con.punto")]
    [InlineData("demasiado-largo-0123456789")]
    public void Un_id_de_clave_invalido_se_rechaza(string idClave)
    {
        var s = Servicio(idClave: idClave);

        Assert.NotNull(s.ProblemaDeConfiguracion());
        Assert.Throws<InvalidOperationException>(() => s.Cifrar(Texto, Datos));
    }

    [Fact]
    public void Acepta_texto_vacio_con_tildes_y_cargas_grandes()
    {
        var s = Servicio();
        var casos = new[] { "", "{}", "Tristeza: 40 % — ñandú", new string('x', 200_000) };

        foreach (var c in casos)
        {
            var guardado = s.Cifrar(c, Datos);
            Assert.Equal(ResultadoDescifrado.Ok, s.Descifrar(guardado, Datos, out var devuelto));
            Assert.Equal(c, devuelto);
        }
    }

    [Fact]
    public void Los_datos_asociados_de_una_sesion_son_estables()
    {
        // Si esto cambiara, lo ya guardado dejaría de poder leerse: es parte del formato.
        Assert.Equal("sesion:42", EmocionesCifradoService.DatosAsociadosDeSesion(42));
    }
}
