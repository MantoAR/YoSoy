// Cómo se leen la fecha y la hora de un turno que manda un cliente: el día TAL COMO ESTÁ ESCRITO, sin convertirlo a
// la zona del servidor (DateTime.Parse lo hacía y el día resultante dependía de dónde corriera la API).
public class MomentoDeTurnoTests
{
    private static DateTime Leer(string fecha, string hora)
    {
        Assert.True(MomentoDeTurno.TryParsear(fecha, hora, out var momento), $"No se pudo leer \"{fecha}\" \"{hora}\"");
        return momento;
    }

    [Theory]
    // Lo que manda el navegador: el mediodía local pasado a UTC.
    [InlineData("2026-10-15T15:00:00.000Z", "10:30:00", 2026, 10, 15, 10, 30)]
    [InlineData("2026-10-15", "10:30:00", 2026, 10, 15, 10, 30)]
    [InlineData("2026-10-15", "10:30", 2026, 10, 15, 10, 30)]
    [InlineData("2026-10-15T00:00:00", "00:00:00", 2026, 10, 15, 0, 0)]
    // Con la zona de un servidor en Argentina (UTC-3) "2026-10-15T01:00:00Z" caía en el 14; en UTC, en el 15.
    // El día escrito es el 15 en los dos.
    [InlineData("2026-10-15T01:00:00Z", "09:00:00", 2026, 10, 15, 9, 0)]
    // Con la zona de un servidor en UTC, "23:30-03:00" caía en el 16 (02:30 UTC); en Argentina, en el 15.
    [InlineData("2026-10-15T23:30:00-03:00", "09:00:00", 2026, 10, 15, 9, 0)]
    [InlineData("2026-10-15T00:30:00+09:00", "09:00:00", 2026, 10, 15, 9, 0)]
    [InlineData("  2026-10-15  ", "  18:45:30  ", 2026, 10, 15, 18, 45)]
    public void El_dia_es_el_que_esta_escrito_sea_cual_sea_la_zona_del_servidor(
        string fecha, string hora, int a, int mes, int d, int h, int min)
    {
        var momento = Leer(fecha, hora);

        Assert.Equal(new DateTime(a, mes, d), momento.Date);
        Assert.Equal(new TimeSpan(h, min, momento.Second), momento.TimeOfDay);
    }

    [Fact]
    public void Los_segundos_de_la_hora_se_conservan()
    {
        Assert.Equal(new DateTime(2026, 10, 15, 14, 33, 21), Leer("2026-10-15", "14:33:21"));
    }

    [Theory]
    [InlineData(null, "09:00")]
    [InlineData("", "09:00")]
    [InlineData("   ", "09:00")]
    [InlineData("no es una fecha", "09:00")]
    [InlineData("2026-13-45", "09:00")]
    [InlineData("2026-10-15", null)]
    [InlineData("2026-10-15", "")]
    [InlineData("2026-10-15", "no es una hora")]
    [InlineData("2026-10-15", "25:00")]
    [InlineData("2026-10-15", "24:00:00")]
    [InlineData("2026-10-15", "-01:00")]
    [InlineData("2026-10-15", "1.02:00:00")]      // un día y dos horas: antes se sumaba y el turno caía al día siguiente
    public void Lo_que_no_se_puede_leer_no_explota_se_rechaza(string? fecha, string? hora)
    {
        Assert.False(MomentoDeTurno.TryParsear(fecha, hora, out var momento));
        Assert.Equal(default, momento);
    }

    // ---------- Cita urgente ----------

    [Fact]
    public void La_cita_urgente_es_ahora_truncado_a_los_segundos()
    {
        var ahora = new DateTime(2026, 10, 15, 14, 33, 21).AddTicks(1234567);

        Assert.Equal(new DateTime(2026, 10, 15, 14, 33, 21), MomentoDeTurno.DeCitaUrgente(ahora));
    }

    [Fact]
    public void La_cita_urgente_no_cambia_si_ya_esta_en_segundos_exactos()
    {
        var ahora = new DateTime(2026, 10, 15, 23, 59, 59);

        Assert.Equal(ahora, MomentoDeTurno.DeCitaUrgente(ahora));
    }
}
