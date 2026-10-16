// Las reglas de turnos (RN-01: "ese horario ya pasó") con el reloj de la clínica. El servidor de prueba está en UTC
// (RelojFijo.LocalTimeZone = UTC): lo que cuenta es la zona EXPLÍCITA de la clínica, no la del sistema.
// Escenario base: jueves 24/09/2026; el profesional 10 atiende de lunes a viernes de 09:00 a 12:00, cada 30 minutos.
public class TurnosZonaHorariaTests
{
    private static readonly TimeZoneInfo Argentina =
        TimeZoneInfo.CreateCustomTimeZone("ART-prueba", TimeSpan.FromHours(-3), "ART", "ART");

    // Un TurnoReglasService con la base en memoria del escenario y el reloj de la clínica sobre un servidor en UTC.
    private static TurnoReglasService Reglas(Escenario e, DateTime ahoraUtc, TimeZoneInfo? zona = null) =>
        new(new FakeTurnoRepositorio(e.Db), new RelojDeLaClinica(zona ?? Argentina, new RelojFijo(ahoraUtc)),
            e.Opciones, e.Consentimiento);

    // El mismo servicio, pero con la zona del sistema (UTC): lo que pasaba con una VPS sin configurar.
    private static TurnoReglasService ReglasConLaZonaDelServidor(Escenario e, DateTime ahoraUtc) =>
        new(new FakeTurnoRepositorio(e.Db), new RelojFijo(ahoraUtc), e.Opciones, e.Consentimiento);

    private static DateTime Jueves(int h, int m = 0) => new(2026, 9, 24, h, m, 0);

    [Fact]
    public void Ahora_es_la_hora_de_pared_de_la_clinica()
    {
        var reglas = Reglas(new Escenario(), new DateTime(2026, 9, 24, 12, 30, 0));   // 12:30 UTC = 09:30 en Argentina

        Assert.Equal(Jueves(9, 30), reglas.Ahora);
    }

    // 09:30 en la clínica (12:30 UTC). Las 10:00 todavía no pasaron. Con la zona del servidor (UTC) "ahora" eran las
    // 12:30 y las 10:00 se rechazaban como pasadas: había que acordarse de fijarle la zona a la VPS.
    [Fact]
    public void Un_horario_de_hoy_que_todavia_no_paso_en_la_clinica_se_acepta_aunque_en_UTC_sea_anterior_a_la_hora_del_servidor()
    {
        var e = new Escenario();
        var ahoraUtc = new DateTime(2026, 9, 24, 12, 30, 0);

        Assert.True(Reglas(e, ahoraUtc).ValidarSolicitud(1, 10, Jueves(10)).Ok);

        var conLaZonaDelServidor = ReglasConLaZonaDelServidor(e, ahoraUtc).ValidarSolicitud(1, 10, Jueves(10));
        Assert.False(conLaZonaDelServidor.Ok);
        Assert.Equal("Ese horario ya pasó.", conLaZonaDelServidor.Mensaje);
    }

    [Fact]
    public void Un_horario_que_ya_paso_en_la_clinica_se_rechaza_con_el_mensaje_de_siempre()
    {
        var reglas = Reglas(new Escenario(), new DateTime(2026, 9, 24, 12, 30, 0));   // 09:30 en la clínica

        var r = reglas.ValidarSolicitud(1, 10, Jueves(9));

        Assert.False(r.Ok);
        Assert.Equal("RN-01", r.Codigo);
        Assert.Equal("Ese horario ya pasó.", r.Mensaje);
    }

    [Fact]
    public void El_horario_exacto_de_ahora_ya_paso_y_el_siguiente_no()
    {
        var reglas = Reglas(new Escenario(), new DateTime(2026, 9, 24, 12, 0, 0));    // 09:00 en la clínica

        Assert.False(reglas.ValidarSolicitud(1, 10, Jueves(9)).Ok);
        Assert.True(reglas.ValidarSolicitud(1, 10, Jueves(9, 30)).Ok);
    }

    [Fact]
    public void Un_minuto_antes_el_horario_todavia_se_puede_pedir()
    {
        var reglas = Reglas(new Escenario(), new DateTime(2026, 9, 24, 11, 59, 0));   // 08:59 en la clínica

        Assert.True(reglas.ValidarSolicitud(1, 10, Jueves(9)).Ok);
        Assert.Equal(Jueves(8, 59), reglas.Ahora);
    }

    // El caso que más duele: de noche. A las 22:15 de la clínica ya es el día siguiente en UTC (01:15). Con una agenda
    // vespertina, las 22:30 de HOY se pueden pedir; con la zona del servidor se rechazaban como pasadas.
    [Fact]
    public void De_noche_el_dia_es_el_de_la_clinica_aunque_en_UTC_ya_sea_el_siguiente()
    {
        var e = new Escenario();
        e.Db.Agenda.Add(new Agenda
        {
            IdProfesional = 10, Dia = "jueves", HoraInicio = new(22, 0, 0), HoraFin = new(23, 59, 59),
            Disponible = true, IntervaloMinutos = 30
        });
        var ahoraUtc = new DateTime(2026, 9, 25, 1, 15, 0);                            // jueves 22:15 en la clínica

        var reglas = Reglas(e, ahoraUtc);

        Assert.Equal(Jueves(22, 15), reglas.Ahora);
        Assert.False(reglas.ValidarSolicitud(1, 10, Jueves(22)).Ok);                   // ya pasó
        Assert.True(reglas.ValidarSolicitud(1, 10, Jueves(22, 30)).Ok);
        Assert.Equal(new[] { Jueves(22, 30), Jueves(23) }, reglas.HorariosLibres(1, 10, 1, 10));   // solo hoy

        Assert.False(ReglasConLaZonaDelServidor(e, ahoraUtc).ValidarSolicitud(1, 10, Jueves(22, 30)).Ok);
    }

    [Fact]
    public void Solicitar_crea_el_turno_en_la_hora_de_pared_de_la_clinica()
    {
        var e = new Escenario();
        var reglas = Reglas(e, new DateTime(2026, 9, 24, 12, 30, 0));

        var r = reglas.Solicitar(1, 10, Jueves(10), "Consulta");

        Assert.True(r.Regla.Ok);
        var turno = e.Db.Turnos.Single(t => t.IdTurno == r.IdTurno);
        Assert.Equal(new DateTime(2026, 9, 24), turno.Fecha);
        Assert.Equal(new TimeSpan(10, 0, 0), turno.Hora);
    }

    // La cancelación conserva su regla (RN-05) y ahora también se mide en la hora de la clínica.
    [Fact]
    public void La_cancelacion_sigue_pidiendo_24_horas_medidas_en_la_clinica()
    {
        var e = new Escenario();
        var turno = e.Db.AgregarTurno(1, 10, new DateTime(2026, 9, 25, 10, 0, 0), BaseEnMemoria.Confirmado);   // viernes 10:00

        // 12:30 UTC = jueves 09:30 en la clínica: faltan 24 h 30 min.
        Assert.True(Reglas(e, new DateTime(2026, 9, 24, 12, 30, 0)).ValidarCancelacion(turno, 1).Ok);

        // 13:30 UTC = jueves 10:30 en la clínica: faltan 23 h 30 min.
        var tarde = Reglas(e, new DateTime(2026, 9, 24, 13, 30, 0)).ValidarCancelacion(turno, 1);
        Assert.False(tarde.Ok);
        Assert.Equal("RN-05", tarde.Codigo);

        // Con la zona del servidor (UTC) a las 12:30 UTC "faltaban" 21 h 30 min y se rechazaba.
        Assert.False(ReglasConLaZonaDelServidor(e, new DateTime(2026, 9, 24, 12, 30, 0)).ValidarCancelacion(turno, 1).Ok);
    }

    // Otra zona configurada: la regla sigue la configuración, no una zona fija en el código.
    [Fact]
    public void Con_otra_zona_configurada_la_regla_sigue_a_esa_zona()
    {
        var e = new Escenario();
        var ahoraUtc = new DateTime(2026, 9, 24, 12, 30, 0);

        var enUtc = Reglas(e, ahoraUtc, TimeZoneInfo.Utc);            // la clínica en UTC: son las 12:30
        Assert.False(enUtc.ValidarSolicitud(1, 10, Jueves(10)).Ok);   // las 10:00 ya pasaron
        Assert.Equal(Jueves(12, 30), enUtc.Ahora);

        var enArgentina = Reglas(e, ahoraUtc, Argentina);             // la clínica en Argentina: son las 09:30
        Assert.True(enArgentina.ValidarSolicitud(1, 10, Jueves(10)).Ok);
    }
}
