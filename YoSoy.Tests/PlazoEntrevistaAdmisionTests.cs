// La primera entrevista de admisión se programa dentro de las 24 horas de recibida la solicitud (PlazoEntrevistaAdmision).
public class PlazoEntrevistaAdmisionTests
{
    private static readonly DateTime Recibida = new(2026, 10, 23, 9, 30, 0);

    [Fact]
    public void Dentro_de_las_24_horas_se_puede_programar() =>
        Assert.Null(PlazoEntrevistaAdmision.Validar(Recibida, Recibida.AddHours(23), Recibida.AddHours(1)));

    [Fact]
    public void Justo_a_las_24_horas_todavia_se_puede() =>
        Assert.Null(PlazoEntrevistaAdmision.Validar(Recibida, Recibida.AddHours(24), Recibida.AddHours(1)));

    [Fact]
    public void Despues_de_las_24_horas_no_y_el_mensaje_dice_hasta_cuando()
    {
        var error = PlazoEntrevistaAdmision.Validar(Recibida, Recibida.AddHours(24).AddMinutes(1), Recibida.AddHours(1));
        Assert.NotNull(error);
        Assert.Contains("24 horas", error);
        Assert.Contains("24/10/2026 a las 09:30 hs", error);
    }

    [Fact]
    public void Una_fecha_que_ya_paso_no_se_acepta() =>
        Assert.Equal("Esa fecha y hora ya pasaron.",
            PlazoEntrevistaAdmision.Validar(Recibida, Recibida.AddHours(2), Recibida.AddHours(3)));

    [Fact]
    public void Unos_minutos_de_diferencia_con_el_reloj_no_la_rechazan() =>
        Assert.Null(PlazoEntrevistaAdmision.Validar(Recibida, Recibida.AddHours(3).AddMinutes(-3), Recibida.AddHours(3)));

    [Fact]
    public void Con_el_plazo_vencido_se_programa_dentro_de_las_24_horas_siguientes()
    {
        var ahora = Recibida.AddHours(30);
        Assert.True(PlazoEntrevistaAdmision.Vencido(Recibida, ahora));
        Assert.Equal(ahora.AddHours(24), PlazoEntrevistaAdmision.Limite(Recibida, ahora));
        Assert.Null(PlazoEntrevistaAdmision.Validar(Recibida, ahora.AddHours(20), ahora));

        var error = PlazoEntrevistaAdmision.Validar(Recibida, ahora.AddHours(25), ahora);
        Assert.NotNull(error);
        Assert.Contains("Ya pasaron las 24 horas", error);
    }
}
