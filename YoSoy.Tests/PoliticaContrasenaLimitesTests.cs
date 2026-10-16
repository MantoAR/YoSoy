// PoliticaContrasena tal como está HOY: solo de largo, entre 6 y 100 caracteres, sin ninguna exigencia de composición.
// Estas pruebas NO cambian la regla: la cubren. PoliticaContrasenaTests (PoliticaRegistroTests.cs) tiene lo básico; acá van
// los bordes exactos, el orden de los chequeos, lo que la regla NO pide (mayúsculas, minúsculas, números, símbolos) y las
// rarezas de contar con string.Length (unidades UTF-16). Si se decide cambiar la política (subir el mínimo, pedir
// composición, rechazar las contraseñas comunes), las que fallen son las que hay que revisar A PROPÓSITO. Sin base de datos.
public class PoliticaContrasenaLimitesTests
{
    private const string MensajeMinimo = "La contraseña debe tener al menos 6 caracteres.";
    private const string MensajeMaximo = "La contraseña no puede tener más de 100 caracteres.";

    // ---------- Los valores de la regla y de los mensajes ----------

    [Fact]
    public void Los_limites_son_6_y_100()
    {
        Assert.Equal(6, PoliticaContrasena.Minimo);
        Assert.Equal(100, PoliticaContrasena.Maximo);
    }

    [Fact]
    public void Los_mensajes_son_exactamente_los_que_ven_las_pantallas_y_dicen_el_limite()
    {
        Assert.Equal(MensajeMinimo, PoliticaContrasena.Validar("abc"));
        Assert.Equal(MensajeMaximo, PoliticaContrasena.Validar(new string('x', 101)));
        Assert.NotEqual(MensajeMinimo, MensajeMaximo);
    }

    // ---------- Los bordes de largo ----------

    [Theory]
    [InlineData(0, MensajeMinimo)]
    [InlineData(1, MensajeMinimo)]
    [InlineData(5, MensajeMinimo)]      // justo debajo del mínimo
    [InlineData(6, null)]               // justo en el mínimo
    [InlineData(7, null)]
    [InlineData(99, null)]
    [InlineData(100, null)]             // justo en el máximo
    [InlineData(101, MensajeMaximo)]    // justo encima del máximo
    [InlineData(102, MensajeMaximo)]
    [InlineData(1_000_000, MensajeMaximo)]
    public void El_largo_decide_en_los_bordes_exactos(int largo, string? esperado)
    {
        Assert.Equal(esperado, PoliticaContrasena.Validar(new string('a', largo)));
    }

    [Fact]
    public void Con_cualquier_largo_de_0_a_150_pasa_si_y_solo_si_esta_entre_6_y_100()
    {
        for (var largo = 0; largo <= 150; largo++)
        {
            var esperado = largo < 6 ? MensajeMinimo : largo > 100 ? MensajeMaximo : null;
            Assert.True(esperado == PoliticaContrasena.Validar(new string('a', largo)), $"largo {largo}");
        }
    }

    // ---------- null, vacío y solo espacios ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("      ")]                            // seis espacios: largo suficiente, pero no es una contraseña
    [InlineData("\t\t\t\t\t\t")]
    [InlineData("\n\n\n\n\n\n")]
    [InlineData("      ")]   // espacios de no separación
    [InlineData("\r\n\r\n\r\n")]
    public void Nada_o_solo_espacios_en_blanco_dice_el_minimo(string? contrasena)
    {
        Assert.Equal(MensajeMinimo, PoliticaContrasena.Validar(contrasena));
    }

    // El orden de los chequeos: lo que es solo espacios se rechaza primero por "no hay nada", aunque además sea larguísimo.
    [Fact]
    public void Solo_espacios_de_mas_de_100_caracteres_dice_el_minimo_y_no_el_maximo()
    {
        Assert.Equal(MensajeMinimo, PoliticaContrasena.Validar(new string(' ', 101)));
        Assert.Equal(MensajeMinimo, PoliticaContrasena.Validar(new string(' ', 5_000)));
    }

    // ---------- Lo que la regla NO exige ----------

    [Theory]
    [InlineData("abcdef")]                    // solo minúsculas
    [InlineData("ABCDEF")]                    // solo mayúsculas
    [InlineData("123456")]                    // solo números
    [InlineData("!!!!!!")]                    // solo símbolos
    [InlineData("aaaaaa")]                    // un solo carácter repetido
    [InlineData("password")]                  // de las más comunes
    [InlineData("qwerty")]
    [InlineData("12345678")]
    [InlineData("Contraseña")]
    public void No_exige_mayusculas_minusculas_numeros_ni_simbolos_y_acepta_las_comunes(string contrasena)
    {
        Assert.Null(PoliticaContrasena.Validar(contrasena));
    }

    // ---------- Espacios: cuentan para el largo y no se recortan ----------

    [Theory]
    [InlineData("  abcd  ", null)]                // 8 caracteres contando los espacios
    [InlineData("abcd  ", null)]                  // 6
    [InlineData("     a", null)]                  // 6: cinco espacios y una letra
    [InlineData("abcde ", null)]                  // el sexto es un espacio: pasa (con 5 letras)
    [InlineData("abcde", MensajeMinimo)]          // sin el espacio, 5
    [InlineData("a b c", MensajeMinimo)]          // 5, con espacios en el medio
    [InlineData("a b c d", null)]                 // 7
    [InlineData("abc\ndef", null)]                // 7, con un salto de línea
    [InlineData("a\tb\tc\t", null)]               // 6, con tabulaciones
    public void Los_espacios_cuentan_para_el_largo_y_no_se_recortan(string contrasena, string? esperado)
    {
        Assert.Equal(esperado, PoliticaContrasena.Validar(contrasena));
    }

    // ---------- Unicode: el largo son unidades UTF-16 (string.Length) ----------

    [Theory]
    [InlineData("ñandúñ", null)]                   // 6 letras con tilde y eñe
    [InlineData("ñandú", MensajeMinimo)]           // 5
    [InlineData("пароль1", null)]                  // 7, cirílico
    [InlineData("密码密码密码", null)]                // 6 ideogramas
    public void Las_letras_con_acento_o_de_otros_alfabetos_cuentan_de_a_una(string contrasena, string? esperado)
    {
        Assert.Equal(esperado, PoliticaContrasena.Validar(contrasena));
    }

    // Rareza conocida: un emoji ocupa DOS unidades UTF-16, así que 3 emojis ya alcanzan el mínimo y 51 pasan el máximo.
    [Fact]
    public void Un_emoji_cuenta_como_dos_caracteres()
    {
        Assert.Equal(2, "😀".Length);
        Assert.Equal(MensajeMinimo, PoliticaContrasena.Validar("😀😀"));                       // 4
        Assert.Null(PoliticaContrasena.Validar("😀😀😀"));                                      // 6
        Assert.Null(PoliticaContrasena.Validar(string.Concat(Enumerable.Repeat("😀", 50))));   // 100
        Assert.Equal(MensajeMaximo, PoliticaContrasena.Validar(string.Concat(Enumerable.Repeat("😀", 51))));   // 102
    }

    // Otra: una letra con un acento combinado (e + U+0301) se ve como un carácter y ocupa dos.
    [Fact]
    public void Un_acento_combinado_cuenta_como_un_caracter_mas()
    {
        var cincoQueSeVenComoCinco = "abcde";
        var cincoQueSeVenComoCincoPeroSonSeis = "abcd" + "é";

        Assert.Equal(MensajeMinimo, PoliticaContrasena.Validar(cincoQueSeVenComoCinco));
        Assert.Null(PoliticaContrasena.Validar(cincoQueSeVenComoCincoPeroSonSeis));
    }

    // ---------- Robustez ----------

    [Fact]
    public void Nunca_tira_y_da_siempre_el_mismo_resultado_para_la_misma_entrada()
    {
        var entradas = new[] { null, "", " ", "a", "abcdef", new string('x', 100), new string('x', 101), "\0\0\0\0\0\0", "\uD83D" };

        foreach (var entrada in entradas)
        {
            var primera = PoliticaContrasena.Validar(entrada);
            var segunda = PoliticaContrasena.Validar(entrada);
            Assert.Equal(primera, segunda);
        }
    }

    [Fact]
    public void Una_contrasena_de_un_millon_de_caracteres_se_rechaza_sin_problemas()
    {
        Assert.Equal(MensajeMaximo, PoliticaContrasena.Validar(new string('z', 1_000_000)));
    }

    [Fact]
    public void Un_valor_valido_devuelve_null_y_uno_invalido_devuelve_un_texto_no_vacio()
    {
        Assert.Null(PoliticaContrasena.Validar("una-contrasena-valida-123"));
        foreach (var invalida in new[] { null, "", "abc", new string('x', 101) })
            Assert.False(string.IsNullOrWhiteSpace(PoliticaContrasena.Validar(invalida)));
    }
}
