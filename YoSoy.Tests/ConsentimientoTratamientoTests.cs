using Xunit;

// La decisión única de «consentimiento de tratamiento vigente» (ConsentimientoTratamiento.Decidir), sin base de datos: sin firma, versión anterior, firmas viejas sin versión,
// y la regla del tutor para las personas menores de edad.
public class ConsentimientoTratamientoTests
{
    private static readonly DateTime Hoy = new(2031, 3, 4);
    private static FirmaTratamiento Firma(string? version, int? tutor = null) => new(Hoy.AddDays(-3), version, tutor);

    [Fact]
    public void Sin_firmas_no_esta_vigente()
    {
        var e = ConsentimientoTratamiento.Decidir(Array.Empty<FirmaTratamiento>(), "v2", esMenor: false, reglaDelTutor: true);
        Assert.False(e.Vigente);
        Assert.Equal(ConsentimientoTratamiento.SinFirma, e.Motivo);
    }

    [Fact]
    public void Firma_de_la_version_vigente_vale()
    {
        var e = ConsentimientoTratamiento.Decidir(new[] { Firma("v2") }, "v2", false, true);
        Assert.True(e.Vigente);
        Assert.Equal("v2", e.Version);
    }

    [Fact]
    public void Si_el_texto_cambio_de_version_hay_que_volver_a_firmar()
    {
        var e = ConsentimientoTratamiento.Decidir(new[] { Firma("v1") }, "v2", false, true);
        Assert.False(e.Vigente);
        Assert.Equal(ConsentimientoTratamiento.VersionAnterior, e.Motivo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Las_firmas_anteriores_sin_version_siguen_valiendo(string? version)
    {
        Assert.True(ConsentimientoTratamiento.Decidir(new[] { Firma(version) }, "v2", false, true).Vigente);
    }

    [Fact]
    public void Una_firma_de_la_version_nueva_alcanza_aunque_haya_una_vieja()
    {
        Assert.True(ConsentimientoTratamiento.Decidir(new[] { Firma("v2"), Firma("v1") }, "v2", false, true).Vigente);
    }

    [Fact]
    public void Un_menor_sin_firma_del_tutor_no_esta_vigente()
    {
        var e = ConsentimientoTratamiento.Decidir(new[] { Firma("v2") }, "v2", esMenor: true, reglaDelTutor: true);
        Assert.False(e.Vigente);
        Assert.Equal(ConsentimientoTratamiento.FaltaTutor, e.Motivo);
        Assert.True(e.EsMenor);
    }

    [Fact]
    public void Un_menor_con_la_firma_del_tutor_esta_vigente()
    {
        var e = ConsentimientoTratamiento.Decidir(new[] { Firma("v2"), Firma("v2", tutor: 7) }, "v2", true, true);
        Assert.True(e.Vigente);
        Assert.Equal(7, e.IdTutorFirmante);
    }

    [Fact]
    public void Sin_la_migracion_28_no_se_aplica_la_regla_del_tutor()
    {
        Assert.True(ConsentimientoTratamiento.Decidir(new[] { Firma("v2") }, "v2", esMenor: true, reglaDelTutor: false).Vigente);
    }

    [Fact]
    public void Un_adulto_no_necesita_tutor()
    {
        Assert.True(ConsentimientoTratamiento.Decidir(new[] { Firma("v2") }, "v2", esMenor: false, reglaDelTutor: true).Vigente);
    }

    [Theory]
    [InlineData(ConsentimientoTratamiento.SinFirma, "todavía no firmó el consentimiento de tratamiento. Pedile que lo firme y volvé a intentar.")]
    [InlineData(ConsentimientoTratamiento.VersionAnterior, "tiene que volver a firmarlo")]
    [InlineData(ConsentimientoTratamiento.FaltaTutor, "firmado por su tutor")]
    public void El_mensaje_dice_el_motivo_y_que_hacer(string motivo, string fragmento)
    {
        var m = ConsentimientoTratamiento.MensajeParaAsignar("Ana Prueba", new EstadoTratamiento(false, motivo, true, null, null, null));
        Assert.StartsWith("No se puede asignar profesional: ", m);
        Assert.Contains("Ana Prueba", m);
        Assert.Contains(fragmento, m);
    }
}
