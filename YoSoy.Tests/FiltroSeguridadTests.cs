public class FiltroSeguridadTests
{
    private static FiltroSeguridad Crear()
    {
        var op = new OpcionesFijas();
        op.CurrentValue.PalabrasRiesgo   = new() { "suicid", "no quiero vivir", "hacerme daño" };
        op.CurrentValue.PalabrasClinicas = new() { "ansiedad", "medicación" };
        op.CurrentValue.LineasEmergencia = new()
        {
            new LineaEmergencia { Nombre = "Emergencias", Telefono = "[COMPLETAR Y VALIDAR]" },
            new LineaEmergencia { Nombre = "Salud mental", Telefono = "[COMPLETAR Y VALIDAR]" }
        };
        return new FiltroSeguridad(op);
    }

    [Theory]
    [InlineData("Pienso en el SUICIDIO")]
    [InlineData("a veces pienso en suicidarme")]
    [InlineData("ya no quiero vivir más")]
    [InlineData("NO QUIERO VIVIR")]
    [InlineData("tengo ganas de hacerme dano")]   // sin eñe también coincide
    public void Detecta_frases_de_riesgo_sin_importar_mayusculas_ni_tildes(string texto) =>
        Assert.True(Crear().EsRiesgo(texto));

    [Theory]
    [InlineData("quiero pedir un turno")]
    [InlineData("quiero vivir tranquilo")]
    [InlineData("")]
    public void No_marca_mensajes_comunes(string texto) =>
        Assert.False(Crear().EsRiesgo(texto));

    [Fact]
    public void Detecta_consultas_clinicas() =>
        Assert.True(Crear().EsConsultaClinica("¿Qué hago con la ANSIEDAD a la noche?"));

    [Fact]
    public void El_mensaje_de_emergencia_incluye_las_lineas_configuradas()
    {
        var m = Crear().MensajeEmergencia();
        Assert.Contains("• Emergencias: [COMPLETAR Y VALIDAR]", m);
        Assert.Contains("• Salud mental: [COMPLETAR Y VALIDAR]", m);
        Assert.Contains("guardia", m);
    }

    [Theory]
    [InlineData("+54 9 11 5555-0001", "1155550001")]
    [InlineData("11 5555-0001", "1155550001")]
    [InlineData("5491155550001", "1155550001")]
    [InlineData("(0351) 444-4444", "3514444444")]
    [InlineData(null, "")]
    public void Telefono_se_normaliza_a_los_ultimos_10_digitos(string? entrada, string esperado) =>
        Assert.Equal(esperado, Telefono.Normalizar(entrada));
}
