// Chats v2, Fase 1: cómo se muestra el teléfono de una conversación. Sin base de datos. Los códigos de área salen de la lista oficial de ENACOM (AreasArgentinas).
public class AreasArgentinasTests
{
    [Fact]
    public void La_lista_tiene_los_300_codigos_de_ENACOM_sin_repetidos()
    {
        Assert.Equal(38, AreasArgentinas.CantidadDeTresDigitos);
        Assert.Equal(261, AreasArgentinas.CantidadDeCuatroDigitos);
        // Un HashSet descarta los repetidos sin avisar: si hubiera alguno, la cuenta de arriba daría menos de lo que dice la planilla (1 + 38 + 261 = 300).
    }

    [Theory]
    [InlineData("1155550001", 2)]    // Buenos Aires
    [InlineData("3512066471", 3)]    // Córdoba
    [InlineData("3415551234", 3)]    // Rosario
    [InlineData("2615551234", 3)]    // Mendoza
    [InlineData("2215551234", 3)]    // La Plata
    [InlineData("2231234567", 3)]    // Mar del Plata
    [InlineData("3814567890", 3)]    // Tucumán
    [InlineData("2995551234", 3)]    // Neuquén
    [InlineData("2966123456", 4)]    // Río Gallegos
    [InlineData("2901123456", 4)]    // Ushuaia
    [InlineData("2202345678", 4)]    // González Catán: 220 (Merlo) también existe, gana el más largo
    [InlineData("3435123456", 4)]    // 343 (Paraná) también existe, gana el de 4
    [InlineData("3434123456", 3)]    // 3434 no es un código: es 343 (Paraná)
    public void Encuentra_el_codigo_mas_largo_que_coincide(string nacional, int largo) =>
        Assert.Equal(largo, AreasArgentinas.LargoDelCodigo(nacional));

    [Theory]
    [InlineData("2125551234")]     // Nueva York: 212 no es un código argentino
    [InlineData("9115551234")]     // empieza con 9
    [InlineData("0115551234")]
    [InlineData("1234")]           // corto
    public void Lo_que_no_empieza_con_un_codigo_argentino_no_es_argentino(string nacional) =>
        Assert.Null(AreasArgentinas.LargoDelCodigo(nacional));
}

public class TelefonoWhatsAppTests
{
    [Theory]
    [InlineData("3512066471", "+54 9 351 206-6471")]        // el ejemplo del pedido: Córdoba
    [InlineData("1155550001", "+54 9 11 5555-0001")]        // Buenos Aires: 2 dígitos de área
    [InlineData("3415551234", "+54 9 341 555-1234")]        // Rosario
    [InlineData("2615551234", "+54 9 261 555-1234")]        // Mendoza
    [InlineData("2215551234", "+54 9 221 555-1234")]        // La Plata
    [InlineData("2966123456", "+54 9 2966 12-3456")]        // 4 dígitos de área: abonado de 6
    [InlineData("2202345678", "+54 9 2202 34-5678")]        // 220 y 2202 existen los dos: gana el de 4
    public void Un_numero_argentino_guardado_se_muestra_en_formato_WhatsApp(string guardado, string esperado) =>
        Assert.Equal(esperado, TelefonoWhatsApp.Formatear(guardado));

    [Theory]
    [InlineData("2125551234")]      // los últimos 10 de +1 212 555 1234: no figura como código de área argentino, NO se le inventa un "+54 9"
    [InlineData("9115551234")]
    [InlineData("12345")]           // muy corto
    public void Si_el_codigo_de_area_no_figura_se_muestra_sin_agrupar_y_sin_pais(string guardado) =>
        Assert.Equal(guardado, TelefonoWhatsApp.Formatear(guardado));

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("  ", "")]
    public void Sin_numero_no_muestra_nada(string? guardado, string esperado) => Assert.Equal(esperado, TelefonoWhatsApp.Formatear(guardado));

    [Fact]
    public void Un_numero_con_simbolos_se_lee_por_sus_digitos()
    {
        Assert.Equal("+54 9 351 206-6471", TelefonoWhatsApp.Formatear("351 206-6471"));
        Assert.Equal("+54 9 351 206-6471", TelefonoWhatsApp.Formatear("+54 9 351 206-6471"));
    }

    [Theory]
    [InlineData("5493512066471", "+54 9 351 206-6471")]     // el JID de WhatsApp sin el @
    [InlineData("543512066471", "+54 9 351 206-6471")]
    [InlineData("5491155550001", "+54 9 11 5555-0001")]
    public void Con_el_numero_original_argentino_decide_por_el(string original, string esperado) =>
        Assert.Equal(esperado, TelefonoWhatsApp.Formatear("3512066471", original));

    [Theory]
    [InlineData("12125551234", "+12125551234")]             // +1 212 555 1234: aunque lo guardado sean 10 dígitos, el original dice que no es argentino
    [InlineData("59899123456", "+59899123456")]             // Uruguay
    [InlineData("5511987654321", "+5511987654321")]         // Brasil
    public void Con_el_numero_original_de_otro_pais_nunca_dice_54_9(string original, string esperado)
    {
        var guardado = Telefono.Normalizar(original);        // lo que quedaría guardado: los últimos 10 dígitos
        var texto = TelefonoWhatsApp.Formatear(guardado, original);
        Assert.Equal(esperado, texto);
        Assert.DoesNotContain("+54 9", texto);
    }

    [Fact]
    public void Lo_que_hoy_queda_guardado_de_un_numero_de_WhatsApp_argentino_da_el_formato_esperado()
    {
        // De punta a punta: el JID de Baileys → lo que guarda la API (Telefono.Normalizar) → lo que se muestra.
        Assert.Equal("+54 9 351 206-6471", TelefonoWhatsApp.Formatear(Telefono.Normalizar("5493512066471")));
        Assert.Equal("+54 9 11 5555-0001", TelefonoWhatsApp.Formatear(Telefono.Normalizar("5491155550001")));
    }

    [Fact]
    public void Limite_conocido_un_numero_extranjero_cuyos_10_ultimos_digitos_parecen_argentinos_se_muestra_argentino_sin_el_numero_original()
    {
        // +1 351 555 1234 (Massachusetts) queda guardado como 3515551234, que empieza con el código de Córdoba. Sin el número original no hay forma de distinguirlo: este test documenta
        // el límite (lo cierra guardar el número completo; ver la propuesta en el informe de la Fase A/B).
        Assert.Equal("+54 9 351 555-1234", TelefonoWhatsApp.Formatear(Telefono.Normalizar("13515551234")));
        // Con el original sí se distingue.
        Assert.Equal("+13515551234", TelefonoWhatsApp.Formatear("3515551234", "13515551234"));
    }
}

public class PoliticaRespuestaAutomaticaTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]     // global apagado: ningún chat responde con IA, aunque la conversación la tenga prendida
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void La_IA_solo_puede_responder_con_los_dos_interruptores_prendidos(bool global, bool conversacion, bool efectiva) =>
        Assert.Equal(efectiva, PoliticaRespuestaAutomatica.IaEfectiva(global, conversacion));

    [Fact]
    public void En_esta_fase_la_IA_no_existe_y_nunca_se_informa_como_quien_responde()
    {
        Assert.False(PoliticaRespuestaAutomatica.IaImplementada);
        Assert.Equal("Bot", PoliticaRespuestaAutomatica.QuienResponde(modoPersonal: false, "Activa", iaEfectiva: true));
    }

    [Theory]
    [InlineData(true, "Activa", "Persona")]            // modo Personal: nunca hay asistente
    [InlineData(true, "DerivadaHumano", "Persona")]
    [InlineData(false, "DerivadaHumano", "Persona")]   // escalada a una persona
    [InlineData(false, "Urgente", "Persona")]
    [InlineData(false, "Activa", "Bot")]
    [InlineData(false, "Cerrada", "Bot")]              // si la persona vuelve a escribir, la atiende el asistente
    public void Quien_responde_segun_el_modo_y_el_estado(bool personal, string estado, string esperado) =>
        Assert.Equal(esperado, PoliticaRespuestaAutomatica.QuienResponde(personal, estado, iaEfectiva: false));

    [Fact]
    public void Cuando_la_IA_exista_responde_ella_salvo_que_haya_una_persona()
    {
        Assert.Equal("IA", PoliticaRespuestaAutomatica.QuienResponde(false, "Activa", iaEfectiva: true, iaImplementada: true));
        Assert.Equal("Persona", PoliticaRespuestaAutomatica.QuienResponde(false, "DerivadaHumano", iaEfectiva: true, iaImplementada: true));
        Assert.Equal("Persona", PoliticaRespuestaAutomatica.QuienResponde(true, "Activa", iaEfectiva: true, iaImplementada: true));
        Assert.Equal("Bot", PoliticaRespuestaAutomatica.QuienResponde(false, "Activa", iaEfectiva: false, iaImplementada: true));
    }
}

// La sugerencia por teléfono compara con TelefonoArgentino: los formatos reales con los que se carga un teléfono en Paciente.Telefono (texto libre) tienen que dar el mismo número que el
// de la conversación (que guarda los últimos 10 dígitos). Datos reales de producción al 21/10/2026: 2 pacientes activos con teléfono, los dos con símbolos.
public class CoincidenciaDeTelefonoTests
{
    private const string Conversacion = "3512066471";

    [Theory]
    [InlineData("351-206-6471")]
    [InlineData("(351) 15 206-6471")]
    [InlineData("+54 9 351 206 6471")]
    [InlineData("0351 15 2066471")]
    [InlineData("3512066471")]
    [InlineData("351 2066471")]
    [InlineData("(0351) 206-6471")]
    [InlineData("+543512066471")]
    [InlineData("54 9 351 15 206-6471")]
    [InlineData("  351.206.6471  ")]
    public void Los_formatos_reales_de_carga_coinciden_con_el_numero_de_la_conversacion(string cargado) =>
        Assert.True(CoincidenciaDeTelefono.Coincide(cargado, Conversacion), $"'{cargado}' debería coincidir");

    [Theory]
    [InlineData("351-206-6472")]               // un dígito distinto
    [InlineData("(351) 15 206-6472")]
    [InlineData("+54 9 351 206 6470")]
    [InlineData("0351 15 2066472")]
    [InlineData("11 5555-0001")]               // otro número
    [InlineData("(011) 15 5555-0001")]
    [InlineData("206-6471")]                   // sin código de área: no alcanza para afirmar que es el mismo
    [InlineData("llamar a la madre")]
    [InlineData("")]
    [InlineData(null)]
    public void Lo_que_no_es_el_mismo_numero_no_coincide(string? cargado) =>
        Assert.False(CoincidenciaDeTelefono.Coincide(cargado, Conversacion), $"'{cargado}' no debería coincidir");

    [Fact]
    public void Una_conversacion_con_un_numero_muy_corto_no_sugiere_a_nadie() =>
        Assert.False(CoincidenciaDeTelefono.Coincide("12345", "12345"));

    [Theory]
    [InlineData("1155550001", "(011) 15 5555-0001")]
    [InlineData("1155550001", "+54 9 11 5555-0001")]
    [InlineData("1155550001", "011 5555-0001")]
    [InlineData("2966123456", "02966 15 123456")]
    [InlineData("2966123456", "(2966) 12-3456")]
    public void Tambien_con_otros_codigos_de_area(string conversacion, string cargado) =>
        Assert.True(CoincidenciaDeTelefono.Coincide(cargado, conversacion), $"'{cargado}' debería coincidir con {conversacion}");
}
