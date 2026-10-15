using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// El reloj de la clínica: la hora real con la zona horaria de la clínica EXPLÍCITA, no la del sistema operativo.
// Sin base ni HTTP: relojes fijos y zonas armadas a mano, así dan lo mismo en cualquier máquina.
public class RelojDeLaClinicaTests
{
    // Argentina: UTC-3 todo el año (sin horario de verano), igual que la real.
    private static readonly TimeZoneInfo Argentina =
        TimeZoneInfo.CreateCustomTimeZone("ART-prueba", TimeSpan.FromHours(-3), "ART", "ART");

    private static DateTime Utc(int a, int mes, int d, int h, int min = 0, int s = 0) => new(a, mes, d, h, min, s);

    // ---------- La hora local es la de la clínica ----------

    [Fact]
    public void La_hora_local_es_la_de_la_zona_de_la_clinica_y_no_la_del_origen()
    {
        var origen = new RelojFijo(Utc(2031, 3, 4, 13, 0));       // un servidor en UTC: LocalTimeZone = UTC
        var reloj = new RelojDeLaClinica(Argentina, origen);

        var local = reloj.GetLocalNow();

        Assert.Same(Argentina, reloj.LocalTimeZone);
        Assert.Equal(TimeSpan.FromHours(-3), local.Offset);
        Assert.Equal(new DateTime(2031, 3, 4, 10, 0, 0), local.DateTime);
    }

    // Lo que importa en la práctica: pasadas las 21 en la clínica, en UTC ya es "mañana". "Hoy" tiene que seguir
    // siendo el día de la clínica.
    [Fact]
    public void Pasadas_las_21_de_la_clinica_la_fecha_sigue_siendo_la_de_la_clinica_aunque_en_UTC_ya_sea_manana()
    {
        var origen = new RelojFijo(Utc(2031, 3, 5, 1, 30));       // 05/03 01:30 UTC = 04/03 22:30 en Argentina
        var reloj = new RelojDeLaClinica(Argentina, origen);

        Assert.Equal(new DateTime(2031, 3, 4, 22, 30, 0), reloj.GetLocalNow().DateTime);
        Assert.Equal(new DateTime(2031, 3, 4), reloj.GetLocalNow().Date);
        // El de un reloj que usa la zona del sistema (UTC en esta prueba) se corre al día siguiente: lo que había antes.
        Assert.Equal(new DateTime(2031, 3, 5), new RelojDeLaClinica(TimeZoneInfo.Utc, origen).GetLocalNow().Date);
    }

    [Fact]
    public void El_instante_UTC_no_se_toca()
    {
        var origen = new RelojFijo(Utc(2031, 3, 4, 13, 0, 7));
        var reloj = new RelojDeLaClinica(Argentina, origen);

        Assert.Equal(origen.GetUtcNow(), reloj.GetUtcNow());
        Assert.Equal(TimeSpan.Zero, reloj.GetUtcNow().Offset);
    }

    [Fact]
    public void Acompana_al_origen_cuando_este_avanza()
    {
        var origen = new RelojFijo(Utc(2031, 3, 4, 12, 59));
        var reloj = new RelojDeLaClinica(Argentina, origen);
        Assert.Equal(9, reloj.GetLocalNow().Hour);

        origen.Avanzar(TimeSpan.FromMinutes(1));

        Assert.Equal(new DateTime(2031, 3, 4, 10, 0, 0), reloj.GetLocalNow().DateTime);
    }

    // ---------- Marcas de tiempo y temporizadores: los resuelve el origen ----------

    private sealed class OrigenContable : TimeProvider
    {
        public int Marcas, Temporizadores;
        public override long GetTimestamp() { Marcas++; return 42; }
        public override long TimestampFrequency => 1000;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Temporizadores++;
            return base.CreateTimer(callback, state, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    [Fact]
    public void Las_marcas_de_tiempo_y_los_temporizadores_los_resuelve_el_origen()
    {
        var origen = new OrigenContable();
        var reloj = new RelojDeLaClinica(Argentina, origen);

        Assert.Equal(42, reloj.GetTimestamp());
        Assert.Equal(1000, reloj.TimestampFrequency);
        using (reloj.CreateTimer(_ => { }, null, TimeSpan.FromHours(1), Timeout.InfiniteTimeSpan)) { }

        Assert.Equal(1, origen.Marcas);
        Assert.Equal(1, origen.Temporizadores);
    }

    [Fact]
    public void Sin_origen_usa_el_reloj_del_sistema()
    {
        var reloj = new RelojDeLaClinica(Argentina);

        var diferencia = (reloj.GetUtcNow() - DateTimeOffset.UtcNow).Duration();

        Assert.True(diferencia < TimeSpan.FromSeconds(5));
        Assert.Same(Argentina, reloj.LocalTimeZone);
    }

    [Fact]
    public void Sin_zona_no_se_puede_crear()
    {
        Assert.Throws<ArgumentNullException>(() => new RelojDeLaClinica(null!));
    }

    // ---------- De dónde sale la zona ----------

    // Argentina no tiene horario de verano: el mismo desfase en cualquier fecha.
    private static void AssertEsArgentina(TimeZoneInfo zona)
    {
        foreach (var instante in new[] { Utc(2026, 1, 15, 12), Utc(2026, 7, 15, 12), Utc(2031, 3, 4, 12) })
            Assert.Equal(TimeSpan.FromHours(-3), zona.GetUtcOffset(new DateTimeOffset(DateTime.SpecifyKind(instante, DateTimeKind.Utc))));
    }

    [Fact]
    public void Sin_ninguna_configuracion_la_zona_es_la_de_Argentina()
    {
        AssertEsArgentina(ZonaHorariaDeLaClinica.Elegir(null, null));
        AssertEsArgentina(ZonaHorariaDeLaClinica.Elegir("", "   "));
    }

    [Fact]
    public void Turnos_ZonaHoraria_manda_sobre_todo_lo_demas()
    {
        var zona = ZonaHorariaDeLaClinica.Elegir("UTC", ZonaHorariaDeLaClinica.PorDefecto);
        Assert.Equal(TimeSpan.Zero, zona.BaseUtcOffset);
    }

    [Fact]
    public void Los_espacios_alrededor_de_la_zona_no_importan()
    {
        Assert.Equal(TimeSpan.Zero, ZonaHorariaDeLaClinica.Elegir("  UTC \t").BaseUtcOffset);
    }

    // Video:ZonaHoraria ya existía y significa lo mismo: quien la tiene configurada no tiene que tocar nada.
    [Fact]
    public void Si_no_hay_Turnos_ZonaHoraria_se_respeta_la_que_ya_estaba_en_Video()
    {
        Assert.Equal(TimeSpan.Zero, ZonaHorariaDeLaClinica.Elegir(null, "UTC").BaseUtcOffset);
        Assert.Equal(TimeSpan.Zero, ZonaHorariaDeLaClinica.Elegir("  ", "UTC").BaseUtcOffset);
    }

    // Esa clave nunca frenó el arranque (AccesoSalaService solo avisa en el log): sigue sin frenarlo.
    [Fact]
    public void Una_Video_ZonaHoraria_que_no_existe_se_ignora_y_no_frena_el_arranque()
    {
        AssertEsArgentina(ZonaHorariaDeLaClinica.Elegir(null, "Marte/Olympus_Mons"));
    }

    // La clave nueva sí frena el arranque: es preferible enterarse al desplegar que ofrecer horarios corridos.
    [Fact]
    public void Una_Turnos_ZonaHoraria_que_no_existe_hace_fallar_con_un_mensaje_claro()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ZonaHorariaDeLaClinica.Elegir("Marte/Olympus_Mons"));

        Assert.Contains("Turnos:ZonaHoraria", ex.Message);
        Assert.Contains("Marte/Olympus_Mons", ex.Message);
        Assert.Contains(ZonaHorariaDeLaClinica.PorDefecto, ex.Message);
    }

    [Fact]
    public void La_zona_se_lee_de_la_configuracion()
    {
        static IConfiguration Config(params (string, string)[] valores) => new ConfigurationBuilder()
            .AddInMemoryCollection(valores.ToDictionary(v => v.Item1, v => (string?)v.Item2)).Build();

        AssertEsArgentina(ZonaHorariaDeLaClinica.Resolver(Config()));
        Assert.Equal(TimeSpan.Zero, ZonaHorariaDeLaClinica.Resolver(Config(("Turnos:ZonaHoraria", "UTC"))).BaseUtcOffset);
        Assert.Equal(TimeSpan.Zero, ZonaHorariaDeLaClinica.Resolver(Config(("Video:ZonaHoraria", "UTC"))).BaseUtcOffset);
        Assert.Equal(TimeSpan.Zero, ZonaHorariaDeLaClinica.Resolver(
            Config(("Turnos:ZonaHoraria", "UTC"), ("Video:ZonaHoraria", ZonaHorariaDeLaClinica.PorDefecto))).BaseUtcOffset);
    }

    // ---------- El registro ----------

    private static IConfiguration Vacia() => new ConfigurationBuilder().Build();

    [Fact]
    public void AddRelojDeLaClinica_registra_el_reloj_con_la_zona_de_la_configuracion()
    {
        var servicios = new ServiceCollection();
        servicios.AddRelojDeLaClinica(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Turnos:ZonaHoraria"] = "UTC" }).Build());

        var reloj = servicios.BuildServiceProvider().GetRequiredService<TimeProvider>();

        Assert.IsType<RelojDeLaClinica>(reloj);
        Assert.Equal(TimeSpan.Zero, reloj.LocalTimeZone.BaseUtcOffset);
    }

    // El reloj de la clínica gana aunque otro registro de TimeProvider llegue antes (lo hace RemoveAll) o después
    // (AddMensajeria y AddVideo usan TryAdd): el que decide "qué hora es" es uno solo.
    [Fact]
    public void Ningun_otro_registro_de_TimeProvider_lo_pisa()
    {
        var antes = new ServiceCollection();
        antes.AddSingleton(TimeProvider.System);
        antes.AddRelojDeLaClinica(Vacia());
        antes.TryAddSingleton(TimeProvider.System);

        var despues = new ServiceCollection();
        despues.AddRelojDeLaClinica(Vacia());
        despues.TryAddSingleton(TimeProvider.System);

        foreach (var servicios in new[] { antes, despues })
        {
            var reloj = servicios.BuildServiceProvider().GetRequiredService<TimeProvider>();
            Assert.IsType<RelojDeLaClinica>(reloj);
            AssertEsArgentina(reloj.LocalTimeZone);
        }
    }

    // Program.cs pide el reloj apenas arranca: la zona mal escrita frena el arranque con este mensaje.
    [Fact]
    public void Con_una_zona_inexistente_el_reloj_no_se_puede_crear_y_el_mensaje_dice_cual_es_la_clave()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Turnos:ZonaHoraria"] = "Marte/Olympus_Mons" }).Build();
        var servicios = new ServiceCollection();
        servicios.AddRelojDeLaClinica(config);

        var ex = Assert.Throws<InvalidOperationException>(() => servicios.BuildServiceProvider().GetRequiredService<TimeProvider>());

        Assert.Contains("Turnos:ZonaHoraria", ex.Message);
    }

    // La configuración se lee al crear el reloj, no al registrarlo: lo que se agregue después se respeta.
    [Fact]
    public void La_zona_se_lee_de_la_configuracion_definitiva()
    {
        var config = new ConfigurationManager();
        var servicios = new ServiceCollection();
        servicios.AddRelojDeLaClinica(config);
        config["Turnos:ZonaHoraria"] = "UTC";                    // llega después del registro

        var reloj = servicios.BuildServiceProvider().GetRequiredService<TimeProvider>();

        Assert.Equal(TimeSpan.Zero, reloj.LocalTimeZone.BaseUtcOffset);
    }
}
