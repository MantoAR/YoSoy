// Reglas de AccesoSalaService: quién entra a la sala de un turno o de una admisión, con qué rol y en qué
// horario. Sin HTTP ni base: turnos en memoria y un reloj fijo. El turno de referencia empieza el
// martes 4/3/2031 a las 10:00 y dura 30 minutos, así que la sala abre 09:50 y cierra 11:00 (inicio - 10 min
// hasta fin + 30 min, el cierre excluido).
public class AccesoSalaServiceTests
{
    private static readonly DateTime Inicio = VideoApp.Inicio;

    private static readonly Identidad Paciente = new(31, Roles.Paciente, 301, null);
    private static readonly Identidad OtroPaciente = new(32, Roles.Paciente, 302, null);
    private static readonly Identidad Profesional = new(21, Roles.Profesional, null, 201);
    private static readonly Identidad OtroProfesional = new(22, Roles.Profesional, null, 202);
    private static readonly Identidad Configurador = new(11, Roles.Configurador, null, null);

    private sealed class Entorno
    {
        public readonly FakeOrigenSala Origen = new();
        public readonly RelojFijo Reloj;
        public readonly OpcionesFijas<VideoOptions> Opciones = new();
        public readonly LogsCapturados Logs = new();
        public readonly AccesoSalaService Servicio;

        public Entorno(RelojFijo? reloj = null) : this(reloj, null) { }

        // Con un reloj de la clínica: el servidor de prueba (RelojFijo, en UTC) queda adentro y se lo mueve igual.
        public Entorno(TimeProvider clinica) : this(null, clinica) { }

        private Entorno(RelojFijo? reloj, TimeProvider? clinica)
        {
            Reloj = reloj ?? new RelojFijo(Inicio);
            Origen.Turnos[1] = new TurnoParaSala(1, 301, 201, "Confirmado", Inicio.Date, Inicio.TimeOfDay, "sala-1", 30);
            Origen.Admisiones[5] = new AdmisionParaSala(5, 301, "En entrevista", Inicio, "sala-adm-5");
            Servicio = new AccesoSalaService(Origen, clinica ?? Reloj, Opciones, Logs.CreateLogger<AccesoSalaService>());
        }

        public ResultadoAcceso Turno(Identidad yo, int id = 1) => Servicio.Evaluar(yo, OrigenSala.Turno, id);
        public ResultadoAcceso Admision(Identidad yo, int id = 5) => Servicio.Evaluar(yo, OrigenSala.Admision, id);
        public void A(int segundosDesdeElInicio) => Reloj.Ahora = Inicio.AddSeconds(segundosDesdeElInicio);
        public void CambiarTurno(Func<TurnoParaSala, TurnoParaSala> cambio) => Origen.Turnos[1] = cambio(Origen.Turnos[1]);
        public void CambiarAdmision(Func<AdmisionParaSala, AdmisionParaSala> cambio) => Origen.Admisiones[5] = cambio(Origen.Admisiones[5]);
    }

    // ---------- Quién entra y con qué rol ----------

    [Fact]
    public void El_paciente_del_turno_entra_con_rol_paciente()
    {
        var r = new Entorno().Turno(Paciente);
        Assert.True(r.Ok);
        Assert.Equal(Roles.Paciente, r.Rol);
        Assert.Equal("sala-1", r.Sala);
    }

    [Fact]
    public void El_profesional_del_turno_entra_con_rol_profesional()
    {
        var r = new Entorno().Turno(Profesional);
        Assert.True(r.Ok);
        Assert.Equal(Roles.Profesional, r.Rol);
    }

    // Misma regla que PUT /turnos/{id}/generar-sala: el Configurador puede con cualquier turno. En el signaling
    // hay dos asientos, profesional y paciente, y entra en el del profesional.
    [Fact]
    public void El_configurador_entra_a_cualquier_turno_en_el_asiento_del_profesional()
    {
        var r = new Entorno().Turno(Configurador);
        Assert.True(r.Ok);
        Assert.Equal(Roles.Profesional, r.Rol);
    }

    [Fact]
    public void Otro_paciente_no_entra() => Assert.Equal(EstadoAcceso.SinAcceso, new Entorno().Turno(OtroPaciente).Estado);

    [Fact]
    public void Otro_profesional_no_entra() => Assert.Equal(EstadoAcceso.SinAcceso, new Entorno().Turno(OtroProfesional).Estado);

    // El rol que cuenta es el del usuario en el sistema: un id de paciente dentro del JWT de un profesional
    // (o al revés) no lo sienta en el otro asiento.
    [Fact]
    public void Un_profesional_con_el_id_de_paciente_del_turno_no_entra_como_paciente()
    {
        var r = new Entorno().Turno(new Identidad(41, Roles.Profesional, 301, 999));
        Assert.Equal(EstadoAcceso.SinAcceso, r.Estado);
    }

    [Fact]
    public void Un_paciente_con_el_id_de_profesional_del_turno_no_entra_como_profesional()
    {
        var r = new Entorno().Turno(new Identidad(42, Roles.Paciente, 999, 201));
        Assert.Equal(EstadoAcceso.SinAcceso, r.Estado);
    }

    [Fact]
    public void Un_paciente_sin_id_de_paciente_no_entra()
    {
        var r = new Entorno().Turno(new Identidad(43, Roles.Paciente, null, null));
        Assert.Equal(EstadoAcceso.SinAcceso, r.Estado);
    }

    [Fact]
    public void Un_rol_desconocido_no_entra()
    {
        var r = new Entorno().Turno(new Identidad(50, "otro", 301, 201));
        Assert.Equal(EstadoAcceso.SinAcceso, r.Estado);
    }

    // ---------- Existencia y orden de los chequeos ----------

    [Fact]
    public void Un_turno_que_no_existe_es_NoExiste()
    {
        var r = new Entorno().Turno(Paciente, id: 999);
        Assert.Equal(EstadoAcceso.NoExiste, r.Estado);
        Assert.Equal("No encontramos esa sesión.", r.Mensaje);
    }

    [Fact]
    public void Una_admision_que_no_existe_es_NoExiste() =>
        Assert.Equal(EstadoAcceso.NoExiste, new Entorno().Admision(Paciente, id: 999).Estado);

    [Fact]
    public void Un_origen_desconocido_es_NoExiste() =>
        Assert.Equal(EstadoAcceso.NoExiste, new Entorno().Servicio.Evaluar(Paciente, "sala", 1).Estado);

    // Turno e informe de admisión son tablas distintas: el mismo número no es la misma sesión.
    [Fact]
    public void El_numero_de_una_admision_no_abre_un_turno_con_ese_numero()
    {
        var e = new Entorno();
        Assert.Equal(EstadoAcceso.NoExiste, e.Turno(Paciente, id: 5).Estado);
        Assert.Equal(EstadoAcceso.NoExiste, e.Admision(Paciente, id: 1).Estado);
    }

    // Primero pertenencia, después todo lo demás: quien no tiene nada que ver con el turno nunca se entera de su
    // estado, su horario ni si ya tiene sala. El mensaje es el de un id que no existe, y no trae horarios.
    [Fact]
    public void Quien_no_es_parte_no_se_entera_del_estado_ni_del_horario_ni_de_la_sala()
    {
        var e = new Entorno();
        e.CambiarTurno(t => t with { Estado = "Cancelado", CodigoSala = null });
        e.A(-86400);

        var ajeno = e.Turno(OtroPaciente);

        Assert.Equal(EstadoAcceso.SinAcceso, ajeno.Estado);   // por dentro sigue siendo otro resultado: solo este se audita
        Assert.Equal("No encontramos esa sesión.", ajeno.Mensaje);
        Assert.Equal(e.Turno(Paciente, id: 999).Mensaje, ajeno.Mensaje);   // y el texto es el de un id que no existe
        Assert.Null(ajeno.Desde);
        Assert.Null(ajeno.Hasta);
    }

    // ---------- Estado del turno ----------

    [Theory]
    [InlineData("Pendiente")]
    [InlineData("Cancelado")]
    [InlineData("Completado")]
    [InlineData("Ausente")]
    [InlineData("Rechazado")]
    [InlineData("")]
    public void Un_turno_que_no_esta_confirmado_no_deja_entrar(string estado)
    {
        var e = new Entorno();
        e.CambiarTurno(t => t with { Estado = estado });
        Assert.Equal(EstadoAcceso.EstadoInvalido, e.Turno(Paciente).Estado);
        Assert.Equal(EstadoAcceso.EstadoInvalido, e.Turno(Profesional).Estado);
        Assert.Equal(EstadoAcceso.EstadoInvalido, e.Turno(Configurador).Estado);
    }

    [Theory]
    [InlineData("Pendiente", "El turno todavía no está confirmado.")]
    [InlineData("Cancelado", "El turno fue cancelado.")]
    [InlineData("Completado", "El turno ya fue realizado.")]
    [InlineData("Ausente", "El turno no está disponible para una videollamada.")]
    public void El_mensaje_dice_por_que_no_se_puede_entrar(string estado, string mensaje)
    {
        var e = new Entorno();
        e.CambiarTurno(t => t with { Estado = estado });
        Assert.Equal(mensaje, e.Turno(Paciente).Mensaje);
    }

    // La base guarda la descripción tal cual la cargaron: mayúsculas o espacios de más no cambian el estado.
    [Theory]
    [InlineData("Confirmado")]
    [InlineData("confirmado")]
    [InlineData("CONFIRMADO")]
    [InlineData("  Confirmado  ")]
    public void El_estado_confirmado_se_reconoce_sin_importar_mayusculas_ni_espacios(string estado)
    {
        var e = new Entorno();
        e.CambiarTurno(t => t with { Estado = estado });
        Assert.True(e.Turno(Paciente).Ok);
    }

    // El estado se mira antes que el horario: un turno cancelado dice "cancelado", no "todavía no".
    [Fact]
    public void El_estado_se_evalua_antes_que_el_horario()
    {
        var e = new Entorno();
        e.CambiarTurno(t => t with { Estado = "Cancelado" });
        e.A(-86400);
        Assert.Equal(EstadoAcceso.EstadoInvalido, e.Turno(Paciente).Estado);
    }

    // ---------- Ventana horaria ----------

    [Theory]
    [InlineData(-86400, EstadoAcceso.AunNoAbre)]
    [InlineData(-601, EstadoAcceso.AunNoAbre)]      // un segundo antes de abrir
    [InlineData(-600, EstadoAcceso.Concedido)]      // 10 minutos antes: abre justo ahí (inclusive)
    [InlineData(-1, EstadoAcceso.Concedido)]
    [InlineData(0, EstadoAcceso.Concedido)]         // empieza el turno
    [InlineData(1800, EstadoAcceso.Concedido)]      // termina el turno: todavía hay 30 minutos de gracia
    [InlineData(3599, EstadoAcceso.Concedido)]      // un segundo antes de cerrar
    [InlineData(3600, EstadoAcceso.YaTermino)]      // fin + 30 min: cierra justo ahí (exclusivo)
    [InlineData(3601, EstadoAcceso.YaTermino)]
    [InlineData(86400, EstadoAcceso.YaTermino)]
    public void Ventana_de_un_turno_de_30_minutos(int segundosDesdeElInicio, EstadoAcceso esperado)
    {
        var e = new Entorno();
        e.A(segundosDesdeElInicio);

        foreach (var quien in new[] { Paciente, Profesional, Configurador })
            Assert.Equal(esperado, e.Turno(quien).Estado);
    }

    [Fact]
    public void Antes_de_abrir_el_resultado_dice_cuando_abre_y_el_mensaje_explica()
    {
        var e = new Entorno();
        e.A(-601);

        var r = e.Turno(Paciente);

        Assert.Equal(VideoApp.Utc(Inicio.AddMinutes(-10)), r.Desde!.Value);
        Assert.Equal("Todavía no se puede entrar: la sala se abre 10 minutos antes del horario.", r.Mensaje);
        Assert.Equal("aun-no-abre", r.Codigo);
    }

    [Fact]
    public void Despues_de_cerrar_el_resultado_dice_cuando_cerro()
    {
        var e = new Entorno();
        e.A(3600);

        var r = e.Turno(Paciente);

        Assert.Equal(VideoApp.Utc(Inicio.AddMinutes(60)), r.Hasta!.Value);
        Assert.Equal("Esta sesión ya terminó.", r.Mensaje);
        Assert.Equal("ya-termino", r.Codigo);
    }

    [Fact]
    public void Concedido_lleva_la_ventana_y_el_mismo_instante_con_el_que_se_decidio()
    {
        var e = new Entorno();
        e.A(120);

        var r = e.Turno(Paciente);

        Assert.True(r.Ok);
        Assert.Equal(VideoApp.Utc(e.Reloj.Ahora), r.Ahora);
        Assert.Equal(VideoApp.Utc(Inicio.AddMinutes(-10)), r.Desde!.Value);
        Assert.Equal(VideoApp.Utc(Inicio.AddMinutes(60)), r.Hasta!.Value);
    }

    // Se usa la duración real del turno (la del intervalo de su franja en la agenda), no un número fijo.
    [Theory]
    [InlineData(50, 4799, EstadoAcceso.Concedido)]  // 10:00 + 50 min + 30 min = 11:20 → 4800 s
    [InlineData(50, 4800, EstadoAcceso.YaTermino)]
    [InlineData(15, 2699, EstadoAcceso.Concedido)]  // 10:00 + 15 + 30 = 10:45 → 2700 s
    [InlineData(15, 2700, EstadoAcceso.YaTermino)]
    public void La_ventana_usa_la_duracion_real_del_turno(int duracionMinutos, int segundos, EstadoAcceso esperado)
    {
        var e = new Entorno();
        e.CambiarTurno(t => t with { DuracionMinutos = duracionMinutos });
        e.A(segundos);

        Assert.Equal(esperado, e.Turno(Paciente).Estado);
    }

    // Un turno que no cae en ninguna franja de la agenda no tiene duración propia: se usa la del sistema (30 min,
    // la misma que usa el resto de la app) y es configurable.
    [Fact]
    public void Sin_duracion_en_la_agenda_se_usa_la_del_sistema_que_son_30_minutos()
    {
        var e = new Entorno();
        e.CambiarTurno(t => t with { DuracionMinutos = null });

        e.A(3599);
        Assert.True(e.Turno(Paciente).Ok);
        e.A(3600);
        Assert.Equal(EstadoAcceso.YaTermino, e.Turno(Paciente).Estado);
    }

    [Fact]
    public void La_duracion_del_sistema_es_configurable()
    {
        var e = new Entorno();
        e.CambiarTurno(t => t with { DuracionMinutos = null });
        e.Opciones.CurrentValue.DuracionPorDefectoMinutos = 45;

        e.A(4499);   // 10:00 + 45 + 30 = 11:15 → 4500 s
        Assert.True(e.Turno(Paciente).Ok);
        e.A(4500);
        Assert.Equal(EstadoAcceso.YaTermino, e.Turno(Paciente).Estado);
    }

    [Fact]
    public void Los_minutos_de_antes_y_de_despues_son_configurables()
    {
        var e = new Entorno();
        e.Opciones.CurrentValue.Ventana.MinutosAntes = 5;
        e.Opciones.CurrentValue.Ventana.MinutosDespues = 0;

        e.A(-301); Assert.Equal(EstadoAcceso.AunNoAbre, e.Turno(Paciente).Estado);
        e.A(-300); Assert.True(e.Turno(Paciente).Ok);
        e.A(1799); Assert.True(e.Turno(Paciente).Ok);
        e.A(1800); Assert.Equal(EstadoAcceso.YaTermino, e.Turno(Paciente).Estado);
        Assert.Contains("5 minutos", AvisoDeApertura(e));
    }

    private static string AvisoDeApertura(Entorno e)
    {
        e.A(-86400);
        return e.Turno(Paciente).Mensaje;
    }

    [Fact]
    public void Ventana_por_defecto_son_10_minutos_antes_y_30_despues_del_fin()
    {
        var (desde, hasta) = AccesoSalaService.Ventana(VideoApp.Utc(Inicio), 30, new VideoOptions());
        Assert.Equal(VideoApp.Utc(Inicio.AddMinutes(-10)), desde);
        Assert.Equal(VideoApp.Utc(Inicio.AddMinutes(60)), hasta);
    }

    // Valores absurdos de configuración se acotan en vez de romper el cálculo.
    [Fact]
    public void La_ventana_acota_valores_absurdos_de_configuracion()
    {
        var o = new VideoOptions();
        o.Ventana.MinutosAntes = -5;
        o.Ventana.MinutosDespues = 999999;
        var inicio = VideoApp.Utc(Inicio);

        var (desde, hasta) = AccesoSalaService.Ventana(inicio, 999999, o);

        Assert.Equal(inicio, desde);                                                 // negativo → 0
        Assert.Equal(inicio.AddMinutes(AccesoSalaService.MaxDuracionMinutos + AccesoSalaService.MaxVentanaMinutos), hasta);

        var (_, hastaMinimo) = AccesoSalaService.Ventana(inicio, 0, new VideoOptions());   // duración 0 → al menos 1 minuto
        Assert.Equal(inicio.AddMinutes(1 + 30), hastaMinimo);
    }

    // ---------- Zona horaria ----------

    // Turno.Fecha y Turno.Hora no llevan zona: son la hora de la clínica, y el servicio la toma de la zona de su reloj
    // (en producción, RelojDeLaClinica: ver más abajo). Con la clínica en Argentina (UTC-3), el turno de las 10:00 es a
    // las 13:00 UTC.
    [Fact]
    public void La_fecha_y_la_hora_del_turno_se_leen_en_la_zona_del_servidor()
    {
        var argentina = TimeZoneInfo.CreateCustomTimeZone("ART-prueba", TimeSpan.FromHours(-3), "ART", "ART");
        var reloj = new RelojConZona(new DateTime(2031, 3, 4, 12, 49, 59), argentina);   // 09:49:59 en Argentina
        var e = new Entorno(reloj);

        var antes = e.Turno(Paciente);
        Assert.Equal(EstadoAcceso.AunNoAbre, antes.Estado);
        Assert.Equal(VideoApp.Utc(new DateTime(2031, 3, 4, 12, 50, 0)), antes.Desde!.Value);

        reloj.Ahora = new DateTime(2031, 3, 4, 12, 50, 0);
        Assert.True(e.Turno(Paciente).Ok);
    }

    // El cableado de producción: el reloj es RelojDeLaClinica, con la zona EXPLÍCITA, aunque el servidor esté en UTC y
    // nadie haya completado Video:ZonaHoraria. La sala del turno de las 10:00 (hora de la clínica) abre a las 09:50 de
    // la clínica = 12:50 UTC, y no a las 09:50 UTC (que es lo que daba una VPS en UTC con la zona del sistema).
    [Fact]
    public void Sin_Video_ZonaHoraria_la_ventana_usa_la_zona_de_la_clinica_y_no_la_del_servidor()
    {
        var servidorEnUtc = new RelojFijo(new DateTime(2031, 3, 4, 12, 49, 59));
        var clinica = new RelojDeLaClinica(ZonaHorariaDeLaClinica.Elegir(null), servidorEnUtc);
        var e = new Entorno(clinica);

        var antes = e.Turno(Paciente);
        Assert.Equal(EstadoAcceso.AunNoAbre, antes.Estado);
        Assert.Equal(VideoApp.Utc(new DateTime(2031, 3, 4, 12, 50, 0)), antes.Desde!.Value);
        Assert.Equal(VideoApp.Utc(new DateTime(2031, 3, 4, 14, 0, 0)), antes.Hasta!.Value);   // 10:00 + 30 min + 30 min = 11:00 en la clínica

        servidorEnUtc.Ahora = new DateTime(2031, 3, 4, 12, 50, 0);
        Assert.True(e.Turno(Paciente).Ok);

        servidorEnUtc.Ahora = new DateTime(2031, 3, 4, 13, 59, 59);
        Assert.True(e.Turno(Paciente).Ok);
        servidorEnUtc.Ahora = new DateTime(2031, 3, 4, 14, 0, 0);
        Assert.Equal(EstadoAcceso.YaTermino, e.Turno(Paciente).Estado);
    }

    // La misma hora de pared tiene que dar el mismo instante para "la sala abre" y para "ese horario ya pasó": el
    // instante en que abre la sala, visto con el reloj que usan las reglas de turnos, es 09:50 de la clínica.
    [Fact]
    public void La_apertura_de_la_sala_cae_en_la_hora_de_pared_de_la_clinica_que_ven_las_reglas()
    {
        var servidorEnUtc = new RelojFijo(new DateTime(2031, 3, 4, 8, 0, 0));              // bien antes de abrir
        var clinica = new RelojDeLaClinica(ZonaHorariaDeLaClinica.Elegir(null), servidorEnUtc);
        var e = new Entorno(clinica);

        var apertura = e.Turno(Paciente).Desde!.Value;                                      // el instante en que abre la sala

        servidorEnUtc.Ahora = apertura.UtcDateTime;
        Assert.Equal(Inicio.AddMinutes(-10), clinica.GetLocalNow().DateTime);               // 09:50 en la clínica
        Assert.True(e.Turno(Paciente).Ok);
    }

    [Fact]
    public void Video_ZonaHoraria_manda_sobre_la_zona_del_servidor()
    {
        var e = new Entorno();   // el servidor de prueba está en UTC
        e.Opciones.CurrentValue.ZonaHoraria = ZonaArgentina();

        e.Reloj.Ahora = new DateTime(2031, 3, 4, 12, 49, 59);
        var antes = e.Turno(Paciente);
        Assert.Equal(EstadoAcceso.AunNoAbre, antes.Estado);
        Assert.Equal(VideoApp.Utc(new DateTime(2031, 3, 4, 12, 50, 0)), antes.Desde!.Value);

        e.Reloj.Ahora = new DateTime(2031, 3, 4, 12, 50, 0);
        Assert.True(e.Turno(Paciente).Ok);
    }

    [Fact]
    public void Una_zona_horaria_que_no_existe_cae_en_la_del_servidor_y_deja_un_aviso()
    {
        var e = new Entorno();
        e.Opciones.CurrentValue.ZonaHoraria = "Marte/Olympus_Mons";
        e.A(-600);

        Assert.True(e.Turno(Paciente).Ok);   // como si no hubiera zona configurada: la del reloj (acá UTC; en producción, la de la clínica)
        Assert.Contains(e.Logs.Lineas, l => l.StartsWith("Warning|") && l.Contains("Video:ZonaHoraria"));
    }

    // Dos zonas distintas para la misma hora de pared es una configuración equivocada: la sala y las reglas de turnos
    // hablarían de instantes distintos. No se rompe nada (Video:ZonaHoraria manda para la sala, como siempre) pero se
    // avisa, una sola vez.
    [Fact]
    public void Si_Video_ZonaHoraria_no_coincide_con_la_zona_de_la_clinica_se_avisa_una_sola_vez()
    {
        var servidorEnUtc = new RelojFijo(new DateTime(2031, 3, 4, 12, 40, 0));
        var clinicaEnUtc = new RelojDeLaClinica(TimeZoneInfo.Utc, servidorEnUtc);
        var e = new Entorno(clinicaEnUtc);
        e.Opciones.CurrentValue.ZonaHoraria = ZonaArgentina();            // distinta de la de la clínica (UTC)

        e.Turno(Paciente);
        e.Turno(Paciente);
        e.Turno(Profesional);

        var avisos = e.Logs.Lineas.Where(l => l.StartsWith("Warning|") && l.Contains("no coincide")).ToList();
        Assert.Single(avisos);
        Assert.Contains("Video:ZonaHoraria", avisos[0]);
    }

    [Fact]
    public void Si_Video_ZonaHoraria_coincide_con_la_zona_de_la_clinica_no_se_avisa()
    {
        var servidorEnUtc = new RelojFijo(new DateTime(2031, 3, 4, 12, 40, 0));
        var clinica = new RelojDeLaClinica(ZonaHorariaDeLaClinica.Elegir(ZonaArgentina()), servidorEnUtc);
        var e = new Entorno(clinica);
        e.Opciones.CurrentValue.ZonaHoraria = ZonaArgentina();

        e.Turno(Paciente);

        Assert.DoesNotContain(e.Logs.Lineas, l => l.StartsWith("Warning|"));
    }

    internal static string ZonaArgentina()
    {
        foreach (var id in new[] { "America/Argentina/Buenos_Aires", "Argentina Standard Time" })
        {
            try { TimeZoneInfo.FindSystemTimeZoneById(id); return id; }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        throw new InvalidOperationException("Esta máquina no tiene la zona horaria de Argentina (ni 'America/Argentina/Buenos_Aires' ni 'Argentina Standard Time').");
    }

    // ---------- La sala ----------

    // Acá nunca se crea una sala (eso es PUT /turnos/{id}/generar-sala): sin código, no hay acceso. El
    // repositorio de la interfaz solo lee, así que no hay forma de que Evaluar genere una.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sin_sala_generada_no_hay_acceso_y_no_se_crea_una(string? codigo)
    {
        var e = new Entorno();
        e.CambiarTurno(t => t with { CodigoSala = codigo });

        var r = e.Turno(Paciente);

        Assert.Equal(EstadoAcceso.SalaNoGenerada, r.Estado);
        Assert.Equal("Todavía no se generó la sala de esta sesión.", r.Mensaje);
        Assert.Equal(codigo, e.Origen.Turnos[1].CodigoSala);
    }

    // El signaling solo acepta [A-Za-z0-9_.:-]{1,100} para "sala": un código guardado con otro formato no se emite.
    [Theory]
    [InlineData("sala con espacios")]
    [InlineData("sala/ruta")]
    [InlineData("sala<script>")]
    [InlineData("salón")]
    public void Un_codigo_de_sala_que_el_signaling_no_aceptaria_no_se_emite(string codigo)
    {
        var e = new Entorno();
        e.CambiarTurno(t => t with { CodigoSala = codigo });

        var r = e.Turno(Paciente);

        Assert.Equal(EstadoAcceso.SalaInvalida, r.Estado);
        Assert.Null(r.Sala);
        Assert.Contains(e.Logs.Lineas, l => l.StartsWith("Error|"));
        Assert.DoesNotContain(codigo, e.Logs.Todo);   // el código no va al log
    }

    [Fact]
    public void El_codigo_de_sala_acepta_hasta_100_caracteres()
    {
        var e = new Entorno();
        e.CambiarTurno(t => t with { CodigoSala = new string('a', 100) });
        Assert.True(e.Turno(Paciente).Ok);

        e.CambiarTurno(t => t with { CodigoSala = new string('a', 101) });
        Assert.Equal(EstadoAcceso.SalaInvalida, e.Turno(Paciente).Estado);
    }

    // Primero la hora y después la sala: antes de tiempo se contesta "todavía no", no "no hay sala".
    [Fact]
    public void Fuera_de_horario_se_dice_el_horario_antes_que_la_falta_de_sala()
    {
        var e = new Entorno();
        e.CambiarTurno(t => t with { CodigoSala = null });
        e.A(-86400);
        Assert.Equal(EstadoAcceso.AunNoAbre, e.Turno(Paciente).Estado);

        e.A(86400);
        Assert.Equal(EstadoAcceso.YaTermino, e.Turno(Paciente).Estado);

        e.A(0);
        Assert.Equal(EstadoAcceso.SalaNoGenerada, e.Turno(Paciente).Estado);
    }

    // ---------- Admisión ----------

    [Fact]
    public void En_la_admision_entran_el_paciente_del_informe_y_el_configurador()
    {
        var e = new Entorno();

        var paciente = e.Admision(Paciente);
        Assert.True(paciente.Ok);
        Assert.Equal(Roles.Paciente, paciente.Rol);
        Assert.Equal("sala-adm-5", paciente.Sala);

        var configurador = e.Admision(Configurador);
        Assert.True(configurador.Ok);
        Assert.Equal(Roles.Profesional, configurador.Rol);
    }

    // La entrevista la hace el Configurador y después deriva: un profesional no entra a una admisión.
    [Fact]
    public void En_la_admision_no_entran_ni_un_profesional_ni_otro_paciente()
    {
        var e = new Entorno();
        Assert.Equal(EstadoAcceso.SinAcceso, e.Admision(Profesional).Estado);
        Assert.Equal(EstadoAcceso.SinAcceso, e.Admision(OtroProfesional).Estado);
        Assert.Equal(EstadoAcceso.SinAcceso, e.Admision(OtroPaciente).Estado);
    }

    [Theory]
    [InlineData("Pendiente")]
    [InlineData("Derivado")]
    [InlineData("Rechazado")]
    [InlineData(null)]
    public void Solo_una_entrevista_programada_deja_entrar(string? estado)
    {
        var e = new Entorno();
        e.CambiarAdmision(a => a with { Estado = estado });
        Assert.Equal(EstadoAcceso.EstadoInvalido, e.Admision(Paciente).Estado);
        Assert.Equal(EstadoAcceso.EstadoInvalido, e.Admision(Configurador).Estado);
    }

    [Theory]
    [InlineData("En entrevista")]
    [InlineData("en entrevista")]
    [InlineData("EN  ENTREVISTA ")]
    public void El_estado_en_entrevista_se_reconoce_sin_importar_mayusculas_ni_espacios(string estado)
    {
        var e = new Entorno();
        e.CambiarAdmision(a => a with { Estado = estado });
        Assert.True(e.Admision(Paciente).Ok);
    }

    [Fact]
    public void Una_entrevista_sin_fecha_no_tiene_horario_y_no_deja_entrar()
    {
        var e = new Entorno();
        e.CambiarAdmision(a => a with { FechaEntrevista = null });
        Assert.Equal(EstadoAcceso.EstadoInvalido, e.Admision(Paciente).Estado);
    }

    // Misma regla que el turno: desde 10 minutos antes de la entrevista hasta 30 minutos después de su fin. La
    // entrevista no tiene duración en la base: se asume la de un turno (30 minutos), configurable.
    [Theory]
    [InlineData(-601, EstadoAcceso.AunNoAbre)]
    [InlineData(-600, EstadoAcceso.Concedido)]
    [InlineData(0, EstadoAcceso.Concedido)]
    [InlineData(3599, EstadoAcceso.Concedido)]
    [InlineData(3600, EstadoAcceso.YaTermino)]
    public void Ventana_de_una_entrevista_de_admision(int segundosDesdeElInicio, EstadoAcceso esperado)
    {
        var e = new Entorno();
        e.A(segundosDesdeElInicio);

        Assert.Equal(esperado, e.Admision(Paciente).Estado);
        Assert.Equal(esperado, e.Admision(Configurador).Estado);
    }

    [Fact]
    public void La_duracion_de_la_entrevista_es_configurable()
    {
        var e = new Entorno();
        e.Opciones.CurrentValue.Admision.DuracionMinutos = 45;

        e.A(4499); Assert.True(e.Admision(Paciente).Ok);                              // 10:00 + 45 + 30 = 11:15
        e.A(4500); Assert.Equal(EstadoAcceso.YaTermino, e.Admision(Paciente).Estado);
    }

    [Fact]
    public void Una_admision_sin_sala_no_deja_entrar()
    {
        var e = new Entorno();
        e.CambiarAdmision(a => a with { CodigoSala = null });
        Assert.Equal(EstadoAcceso.SalaNoGenerada, e.Admision(Paciente).Estado);
    }

    [Fact]
    public void Acotar_respeta_los_limites()
    {
        Assert.Equal(0, AccesoSalaService.Acotar(-1, 0, 240));
        Assert.Equal(240, AccesoSalaService.Acotar(241, 0, 240));
        Assert.Equal(17, AccesoSalaService.Acotar(17, 0, 240));
    }
}
