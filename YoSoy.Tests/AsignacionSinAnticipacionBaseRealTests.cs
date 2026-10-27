using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// La propuesta automática ya NO exige 24 h de anticipación (decisión del dueño, octubre 2026): propone el primer hueco libre a partir de «ahora + colchón de 15 min».
// Con datos propios en yosoy_test. El «ahora» de los horarios se pasa a mano (HorariosLibres lo recibe), así que no hace falta cambiar el reloj de la aplicación: un reloj falso
// haría que el servicio de fondo venciera las propuestas de otras pruebas que corren a la vez.
[Trait("Categoria", "Integracion")]
public class AsignacionSinAnticipacionBaseRealTests : IDisposable
{
    private readonly DatosDePrueba _d = new();
    private readonly List<WebApplicationFactory<Program>> _fabricas = new();

    public void Dispose() { foreach (var f in _fabricas) f.Dispose(); _d.Dispose(); }

    private WebApplicationFactory<Program> Fabrica(int? anticipacionMinutos = null)
    {
        var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.ConfigureServices(s => { s.RemoveAll<EmailService>(); s.AddSingleton<EmailService>(new EmailServiceEspia()); });
            if (anticipacionMinutos is int m) b.UseSetting(AsignacionAutomaticaReglas.ClaveAnticipacionMinimaMinutos, m.ToString());
        });
        _fabricas.Add(f);
        return f;
    }

    // Un miércoles lejano: nada de la base real coincide. Agenda todos los días de 09:00 a 18:00 (último horario 17:30, grilla de 30 min).
    private static readonly DateTime Miercoles = new(2031, 3, 5);

    private int ProfesionalConAgenda(string inicio = "09:00", string fin = "18:00")
    {
        var p = _d.Profesional(_d.Especialidad(), nombre: "Prof " + _d.Marca);
        foreach (var dia in Enum.GetValues<DayOfWeek>()) _d.Agenda(p, DatosDePrueba.NombreDelDia(new DateTime(2031, 3, 2).AddDays((int)dia)), inicio, fin);
        return p;
    }

    private static AsignacionAutomaticaService Servicio(WebApplicationFactory<Program> f) => f.Services.GetRequiredService<AsignacionAutomaticaService>();

    [FactConBaseDeAsignacion]
    public void Con_hueco_libre_hoy_mismo_propone_hoy_y_no_manana()
    {
        var p = ProfesionalConAgenda();
        var ahora = Miercoles.AddHours(10).AddMinutes(7);   // 10:07
        var primero = Servicio(Fabrica()).HorariosLibres(p, ahora).First();
        Assert.Equal(Miercoles.AddHours(10).AddMinutes(30), primero);   // hoy 10:30, 23 min después de «ahora»
        Assert.True(primero > ahora);
        // Y el vencimiento de ese turno cercano es válido: después de la creación, antes del inicio.
        var v = AsignacionAutomaticaReglas.Vencimiento(ahora, primero);
        Assert.True(v > ahora && v < primero, $"vencimiento {v}");
        Assert.Equal(ahora.AddMinutes(15), v);   // inicio − 15 min (10:15) quedaría a 8 min de la creación: vale el mínimo de 15 min
    }

    [FactConBaseDeAsignacion]
    public void Un_horario_que_empieza_en_menos_del_colchon_se_pasa_al_siguiente_hueco()
    {
        var p = ProfesionalConAgenda();
        var servicio = Servicio(Fabrica());
        Assert.Equal(Miercoles.AddHours(11), servicio.HorariosLibres(p, Miercoles.AddHours(10).AddMinutes(20)).First());      // 10:30 está a 10 min: se salta
        Assert.Equal(Miercoles.AddHours(10).AddMinutes(30), servicio.HorariosLibres(p, Miercoles.AddHours(10).AddMinutes(15)).First());   // a justo 15 min: se ofrece
        Assert.Equal(Miercoles.AddHours(11), servicio.HorariosLibres(p, Miercoles.AddHours(10).AddMinutes(30)).First());       // el que empieza ahora mismo nunca
        Assert.DoesNotContain(Miercoles.AddHours(10).AddMinutes(30), servicio.HorariosLibres(p, Miercoles.AddHours(10).AddMinutes(31)));  // ni uno que ya empezó
    }

    [FactConBaseDeAsignacion]
    public void Al_final_de_la_agenda_del_dia_propone_el_primer_hueco_de_manana()
    {
        var p = ProfesionalConAgenda();
        var servicio = Servicio(Fabrica());
        // 17:20: el de las 17:30 está a 10 min (menos del colchón) y es el último del día.
        Assert.Equal(Miercoles.AddDays(1).AddHours(9), servicio.HorariosLibres(p, Miercoles.AddHours(17).AddMinutes(20)).First());
        // 17:10: el de las 17:30 está a 20 min: todavía entra.
        Assert.Equal(Miercoles.AddHours(17).AddMinutes(30), servicio.HorariosLibres(p, Miercoles.AddHours(17).AddMinutes(10)).First());
        // Pasada la agenda (19:00): mañana a primera hora.
        Assert.Equal(Miercoles.AddDays(1).AddHours(9), servicio.HorariosLibres(p, Miercoles.AddHours(19)).First());
    }

    private sealed class RelojConZona : TimeProvider
    {
        private readonly DateTimeOffset _utc;
        public RelojConZona(DateTimeOffset utc) => _utc = utc;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("ART-prueba", TimeSpan.FromHours(-3), "ART", "ART");
        public override DateTimeOffset GetUtcNow() => _utc;
    }

    [FactConBaseDeAsignacion]
    public void El_ahora_es_el_de_la_zona_horaria_de_la_clinica_aunque_en_UTC_ya_sea_el_dia_siguiente()
    {
        var p = ProfesionalConAgenda("20:00", "23:59");
        // Martes 4/3/2031 22:15 en Argentina = miércoles 01:15 UTC. Con la hora de UTC se saltaría todo el martes de noche.
        var reloj = new RelojConZona(new DateTimeOffset(2031, 3, 5, 1, 15, 0, TimeSpan.Zero));
        var ahora = reloj.GetLocalNow().DateTime;
        Assert.Equal(new DateTime(2031, 3, 4, 22, 15, 0), ahora);
        var libres = Servicio(Fabrica()).HorariosLibres(p, ahora);
        Assert.Equal(new DateTime(2031, 3, 4, 22, 30, 0), libres.First());   // martes 22:30 (a 15 min), no el miércoles
        Assert.All(libres, h => Assert.True(h >= ahora.AddMinutes(15)));
    }

    [FactConBaseDeAsignacion]
    public void La_anticipacion_configurada_se_suma_sin_redesplegar_y_vale_para_la_propuesta()
    {
        var p = ProfesionalConAgenda();
        var servicio = Servicio(Fabrica(anticipacionMinutos: 120));
        Assert.Equal(120, servicio.AnticipacionMinimaMinutos);
        var ahora = Miercoles.AddHours(10);
        Assert.Equal(Miercoles.AddHours(12), servicio.HorariosLibres(p, ahora).First());   // ahora + 2 h
        Assert.Equal(0, Servicio(Fabrica()).AnticipacionMinimaMinutos);                      // sin la clave: 0
    }

    [FactConBaseDeAsignacion]
    public void El_calendario_del_paciente_ofrece_los_mismos_horarios_que_la_propuesta()
    {
        var p = ProfesionalConAgenda();
        var f = Fabrica();
        var servicio = Servicio(f);
        var ahora = f.Services.GetRequiredService<TimeProvider>().GetLocalNow().DateTime;
        var pac = _d.Paciente("Ana " + _d.Marca, mail: _d.Marca + "@example.test");
        var propuesta = servicio.HorariosLibres(p, ahora, null, null, null, 3).Where(h => h > ahora).ToList();
        var calendario = servicio.HorariosParaPaciente(pac, p, 3);
        var delCalendario = calendario.Dias.SelectMany(d => d.Horas.Select(h => DateTime.Parse(d.Fecha).Add(TimeSpan.Parse(h)))).ToList();
        Assert.NotEmpty(delCalendario);
        Assert.Equal(propuesta.First(), delCalendario.First());   // el mismo primer hueco
        Assert.True(delCalendario.First() >= ahora.AddMinutes(AsignacionAutomaticaReglas.MinutosColchon - 1), "el calendario no ofrece un horario a menos de 15 min");
        Assert.All(delCalendario, h => Assert.Contains(h, servicio.HorariosLibres(p, ahora, null, null, null, 4)));
    }

    // ───────────── El servicio de fondo vence las propuestas de plazo corto ─────────────

    private (int Informe, int Propuesta) PropuestaPendiente(int prof, DateTime fechaHora, DateTime creacion, DateTime expiraEn, int nro)
    {
        var pac = _d.Paciente("Bea " + _d.Marca + nro, mail: $"{_d.Marca}{nro}@example.test");
        var informe = _d.InformeAdmision(pac, "Aprobado", null, null);
        DatosDePrueba.Ejecutar("UPDATE InformeAdmision SET AsignacionEstado = N'Propuesta' WHERE IdInforme = @i", ("@i", informe));
        var id = Convert.ToInt32(DatosDePrueba.Escalar(@"INSERT INTO PropuestaAsignacion (IdInformeAdmision, IdPaciente, IdProfesional, FechaHora, Estado, Motivo, NroPropuesta, FechaCreacion, ExpiraEn)
            VALUES (@i, @p, @r, @f, N'Pendiente', N'[TEST] plazo corto', 1, @c, @e); SELECT CAST(SCOPE_IDENTITY() AS INT);",
            ("@i", informe), ("@p", pac), ("@r", prof), ("@f", fechaHora), ("@c", creacion), ("@e", expiraEn)));
        return (informe, id);
    }

    private static string? EstadoPropuesta(int id) => DatosDePrueba.Escalar("SELECT Estado FROM PropuestaAsignacion WHERE IdPropuesta = @i", ("@i", id)) as string;
    private static string? EstadoAsignacion(int informe) => DatosDePrueba.Escalar("SELECT AsignacionEstado FROM InformeAdmision WHERE IdInforme = @i", ("@i", informe)) as string;

    [FactConBaseDeAsignacion]
    public void El_servicio_de_fondo_vence_la_propuesta_de_plazo_corto_y_deja_la_que_sigue_vigente()
    {
        var f = Fabrica();
        var prof = ProfesionalConAgenda();
        var ahora = f.Services.GetRequiredService<TimeProvider>().GetLocalNow().DateTime;
        // Turno en 10 min, propuesta creada hace 16: su plazo (creación + 15 min) ya pasó, un minuto antes de ahora. Otro, en 3 h, sigue vigente.
        var creada = ahora.AddMinutes(-16);
        var turnoCercano = ahora.AddMinutes(10);
        var vencimiento = AsignacionAutomaticaReglas.Vencimiento(creada, turnoCercano);
        Assert.True(vencimiento < turnoCercano && vencimiento <= ahora, "el plazo corto ya pasó y vence antes de empezar el turno");
        var vencida = PropuestaPendiente(prof, turnoCercano, creada, vencimiento, 1);
        var vigente = PropuestaPendiente(prof, ahora.AddHours(3), ahora, AsignacionAutomaticaReglas.Vencimiento(ahora, ahora.AddHours(3)), 2);

        f.Services.GetRequiredService<RecordatorioBackgroundService>().EjecutarUnaVez();

        Assert.Equal(PropuestaEstado.Vencida, EstadoPropuesta(vencida.Propuesta));
        Assert.Equal(AsignacionInforme.PorResolver, EstadoAsignacion(vencida.Informe));   // la recepción se entera sin que nadie abra la lista
        Assert.Equal(PropuestaEstado.Pendiente, EstadoPropuesta(vigente.Propuesta));
        Assert.Equal("Propuesta", EstadoAsignacion(vigente.Informe));
    }
}
