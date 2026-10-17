// TelefonoArgentino: de lo que una persona escribe en un campo de teléfono al número nacional de 10 dígitos, o null si no es un teléfono
// argentino posible. Es lo que sostiene "este consentimiento es para ESTE número".
public class TelefonoArgentinoTests
{
    // ───────────── Las formas habituales de escribir el mismo número ─────────────

    [Theory]
    // Buenos Aires (11), celular
    [InlineData("1155550001")]
    [InlineData("11 5555-0001")]
    [InlineData("11.5555.0001")]
    [InlineData("(011) 5555-0001")]
    [InlineData("011 5555-0001")]
    [InlineData("011 15 5555-0001")]               // con el 15 de los celulares
    [InlineData("011-15-5555-0001")]
    [InlineData("(011) 15-5555-0001")]
    [InlineData("11 15 5555 0001")]
    [InlineData("+54 11 5555-0001")]
    [InlineData("+54 9 11 5555-0001")]             // el formato de WhatsApp
    [InlineData("+5491155550001")]
    [InlineData("5491155550001")]
    [InlineData("541155550001")]
    [InlineData("0054 9 11 5555 0001")]            // 00 es el prefijo internacional
    [InlineData("+54 011 5555-0001")]              // el 0 que sobra
    [InlineData("+54 9 11 15 5555 0001")]          // los dos: 9 y 15
    [InlineData("9 11 5555 0001")]                 // el 9 solo
    [InlineData("  11 5555-0001  ")]
    [InlineData("011/5555/0001")]
    public void Buenos_Aires_en_cualquiera_de_sus_formas_da_el_mismo_numero(string entrada)
    {
        Assert.Equal("1155550001", TelefonoArgentino.Normalizar(entrada));
    }

    [Theory]
    [InlineData("0351 4123456", "3514123456")]            // Córdoba, código de 3 dígitos
    [InlineData("0351 15 412-3456", "3514123456")]
    [InlineData("(0351) 412-3456", "3514123456")]
    [InlineData("351 4123456", "3514123456")]
    [InlineData("+54 9 351 412 3456", "3514123456")]
    [InlineData("+54 351 412 3456", "3514123456")]
    [InlineData("02966 15 123456", "2966123456")]         // Río Gallegos, código de 4 dígitos
    [InlineData("02966 123456", "2966123456")]
    [InlineData("+54 9 2966 12-3456", "2966123456")]
    [InlineData("0341 15 555-1234", "3415551234")]        // Rosario
    [InlineData("0261 4251234", "2614251234")]            // Mendoza
    [InlineData("381 4123456", "3814123456")]             // Tucumán, empieza con 3
    public void El_resto_del_pais_tambien(string entrada, string esperado)
    {
        Assert.Equal(esperado, TelefonoArgentino.Normalizar(entrada));
    }

    // ───────────── Lo que no es un teléfono argentino ─────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("11 5555-000a")]                    // una letra
    [InlineData("11 5555-0001 int 4")]              // texto de más
    [InlineData("1155550")]                          // corto
    [InlineData("155550001")]                        // 9 dígitos
    [InlineData("11555500012")]                      // 11 dígitos
    [InlineData("115555000123456")]                  // largo
    [InlineData("15 5555-0001")]                     // celular sin código de área
    [InlineData("5555-0001")]                        // sin código de área
    [InlineData("0000000000")]
    [InlineData("1111111111")]                       // todos iguales
    [InlineData("2222222222")]
    [InlineData("1234567890")]                       // no empieza con 11, 2 ni 3
    [InlineData("5555550001")]
    [InlineData("4155550001")]
    [InlineData("911155550001")]                     // 9 + 11 + 9 dígitos: sobran
    [InlineData("+1 415 555 2671")]                  // Estados Unidos
    [InlineData("+34 612 345 678")]                  // España
    [InlineData("+598 99 123 456")]                  // Uruguay
    [InlineData("+56 9 1234 5678")]                  // Chile
    [InlineData("+55 11 91234 5678")]                // Brasil, que también tiene un 11
    [InlineData("+52 55 1234 5678")]                 // México
    [InlineData("+44 20 7123 4567")]                 // Reino Unido
    [InlineData("0034 612 345 678")]                 // España con 00
    [InlineData("<script>alert(1)</script>")]
    [InlineData("11'; DROP TABLE Paciente;--")]
    public void Lo_que_no_es_un_telefono_argentino_con_codigo_de_area_da_null(string? entrada)
    {
        Assert.Null(TelefonoArgentino.Normalizar(entrada));
    }

    [Fact]
    public void Una_entrada_enorme_no_se_cuelga_ni_da_un_numero()
    {
        Assert.Null(TelefonoArgentino.Normalizar(new string('1', 100_000)));
        Assert.Null(TelefonoArgentino.Normalizar("11 " + string.Concat(Enumerable.Repeat("5555 ", 20_000))));
    }

    // ───────────── Propiedades ─────────────

    [Fact]
    public void El_resultado_siempre_son_diez_digitos_que_empiezan_con_11_2_o_3_y_es_estable()
    {
        var entradas = new[]
        {
            "1155550001", "011 15 5555-0001", "+54 9 351 412 3456", "02966 15 123456", "(0341) 555-1234", "54 9 261 425 1234",
            "+54 9 11 15 5555 0001", "9 381 412 3456"
        };
        foreach (var e in entradas)
        {
            var n = TelefonoArgentino.Normalizar(e);
            Assert.NotNull(n);
            Assert.Matches(@"^(11\d{8}|[23]\d{9})$", n);
            Assert.Equal(n, TelefonoArgentino.Normalizar(n));   // normalizar lo normalizado no cambia nada
        }
    }

    [Fact]
    public void Coincide_con_los_ultimos_10_digitos_que_usa_el_resto_de_la_app_cuando_no_hay_un_15()
    {
        // Telefono.Normalizar (para emparejar un mensaje que llega con un paciente) y este dan lo mismo en los formatos sin 15: así lo que
        // llega por WhatsApp (549 + 10 dígitos) encuentra al paciente que aceptó.
        foreach (var e in new[] { "11 5555-0001", "+54 9 11 5555-0001", "5491155550001", "0351 4123456", "+54 9 2966 12-3456" })
            Assert.Equal(Telefono.Normalizar(e), TelefonoArgentino.Normalizar(e));
    }

    [Fact]
    public void Con_el_15_la_normalizacion_de_siempre_se_equivoca_y_esta_no()
    {
        // El motivo de existir de TelefonoArgentino: con "011 15 5555-0001" los últimos 10 dígitos son OTRO número.
        Assert.Equal("1555550001", Telefono.Normalizar("011 15 5555-0001"));
        Assert.Equal("1155550001", TelefonoArgentino.Normalizar("011 15 5555-0001"));
    }
}
