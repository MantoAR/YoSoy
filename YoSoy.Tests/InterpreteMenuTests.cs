public class InterpreteMenuTests
{
    private readonly InterpreteMenu _interprete = new();

    [Theory]
    [InlineData("1", 1)]
    [InlineData(" 3 ", 3)]
    [InlineData("2)", 2)]
    [InlineData("5.", 5)]
    [InlineData("12", 12)]
    public void Un_numero_corto_es_una_opcion(string texto, int esperado)
    {
        var i = _interprete.Interpretar(texto);
        Assert.Equal(TipoIntencion.OpcionNumerica, i.Tipo);
        Assert.Equal(esperado, i.Numero);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("menu")]
    [InlineData("Menú")]
    [InlineData("quiero volver al inicio")]
    public void Cero_o_menu_vuelven_al_menu(string texto) =>
        Assert.Equal(TipoIntencion.VolverAlMenu, _interprete.Interpretar(texto).Tipo);

    [Fact]
    public void Un_DNI_no_se_confunde_con_una_opcion() =>
        Assert.Equal(TipoIntencion.Desconocido, _interprete.Interpretar("30123456").Tipo);

    [Theory]
    [InlineData("Hola, quiero pedir un turno", TipoIntencion.PedirTurno)]
    [InlineData("necesito sacar turno", TipoIntencion.PedirTurno)]
    [InlineData("quiero ver mis turnos", TipoIntencion.VerTurnos)]
    [InlineData("¿cuándo es mi próximo turno?", TipoIntencion.VerTurnos)]
    [InlineData("quiero CANCELAR el turno", TipoIntencion.CancelarTurno)]
    [InlineData("preguntas frecuentes", TipoIntencion.Faq)]
    [InlineData("quiero hablar con una persona", TipoIntencion.HablarConPersona)]
    [InlineData("me pasás con un operador?", TipoIntencion.HablarConPersona)]
    [InlineData("Buenas tardes", TipoIntencion.Saludo)]
    [InlineData("hola", TipoIntencion.Saludo)]
    [InlineData("sí", TipoIntencion.Afirmacion)]
    [InlineData("Dale", TipoIntencion.Afirmacion)]
    [InlineData("no", TipoIntencion.Negacion)]
    [InlineData("el perro come galletitas", TipoIntencion.Desconocido)]
    [InlineData("", TipoIntencion.Desconocido)]
    public void Palabras_clave(string texto, TipoIntencion esperado) =>
        Assert.Equal(esperado, _interprete.Interpretar(texto).Tipo);

    [Fact]
    public void Pedir_una_persona_gana_sobre_cualquier_otra_intencion() =>
        Assert.Equal(TipoIntencion.HablarConPersona,
            _interprete.Interpretar("quiero cancelar un turno pero hablando con una persona").Tipo);
}
