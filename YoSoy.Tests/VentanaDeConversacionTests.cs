public class VentanaDeConversacionTests
{
    private static readonly DateTime Ahora = new(2031, 3, 4, 22, 30, 0);
    private static VentanaDeConversacion Regla() => new(new RelojFijo());

    private sealed class RelojFijo : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone(
            "clinica-prueba", TimeSpan.FromHours(-3), "ART", "ART");
        public override DateTimeOffset GetUtcNow() => new(2031, 3, 5, 1, 30, 0, TimeSpan.Zero);
    }

    [Theory]
    [InlineData(86340, true, 60)]
    [InlineData(86400, false, 0)]
    [InlineData(86401, false, 0)]
    [InlineData(-3600, true, 86400)]
    public void Evalua_el_limite_con_la_hora_local_inyectada(int segundos, bool abierta, long restantes)
    {
        var estado = Regla().Evaluar(Ahora.AddSeconds(-segundos));
        Assert.Equal(abierta, estado.Abierta);
        Assert.Equal(restantes, estado.SegundosRestantes);
        Assert.Equal((long)Math.Max(0, segundos), estado.UltimoEntranteHaceSegundos);
        Assert.Equal(abierta ? null : "vencida", estado.Motivo);
        Assert.Equal(abierta ? null : VentanaDeConversacion.MensajeVencida, estado.Mensaje);
    }

    [Fact]
    public void Nunca_escribio_no_tiene_antiguedad()
    {
        var estado = Regla().Evaluar(null);
        Assert.False(estado.Abierta);
        Assert.Equal("nunca_escribio", estado.Motivo);
        Assert.Equal(0, estado.SegundosRestantes);
        Assert.Null(estado.UltimoEntranteHaceSegundos);
        Assert.Equal(VentanaDeConversacion.MensajeNuncaEscribio, estado.Mensaje);
    }
}
