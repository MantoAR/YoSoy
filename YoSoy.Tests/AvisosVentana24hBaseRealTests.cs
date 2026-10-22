using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

// Usa solo columnas anteriores al consentimiento y al chat Personal: también corre
// en la base de validación sin las migraciones 16 y 17. Nunca usa un canal de red.
[Trait("Categoria", "Integracion")]
public class AvisosVentana24hBaseRealTests : IDisposable
{
    private readonly DatosDePrueba _datos = new();
    private readonly MensajeChatDAO _historial = new();
    private readonly CanalEspia _canal = new();
    private readonly OpcionesFijas _opciones = new();
    private readonly RelojLocal _reloj = new();
    private readonly string _telefono = "11" + Random.Shared.Next(10_000_000, 99_999_999);
    private readonly Conversacion _conversacion;
    private readonly ServicioMensajes _servicio;

    private sealed class RelojLocal : TimeProvider
    {
        public DateTime Ahora = new(2031, 3, 4, 23, 59, 30);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("ART-avisos", TimeSpan.FromHours(-3), "ART", "ART");
        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(Ahora, TimeSpan.FromHours(-3)).ToUniversalTime();
    }

    public AvisosVentana24hBaseRealTests()
    {
        _conversacion = new ConversacionDAO().ObtenerOCrear(_telefono, "Prueba");
        _datos.AlLimpiar("Conversacion", "Telefono", _telefono);
        _servicio = new ServicioMensajes(_historial, _canal, _reloj, NullLogger<ServicioMensajes>.Instance, _opciones);
    }

    public void Dispose() => _datos.Dispose();

    private MensajeChat Mensaje(DateTime fecha, string estado = "Enviado", string origen = "Sistema", string direccion = "Saliente", int? conversacion = null) => new()
    {
        IdConversacion = conversacion ?? _conversacion.IdConversacion, Texto = "Aviso de prueba",
        FechaHora = fecha, EstadoEnvio = estado, Origen = origen, Direccion = direccion
    };

    private Task<MensajeChat> Avisar() => _servicio.EnviarAsync(_conversacion, "Aviso", "Sistema", new OpcionesEnvio(OptIn: true, IniciaElNegocio: true));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task El_tope_configurado_omite_el_exceso_sin_fila_ni_envio(int tope)
    {
        Assert.Equal(3, new MensajeriaOptions().TopeAvisosPorNumeroPorDia);
        _opciones.CurrentValue.TopeAvisosPorNumeroPorDia = tope;
        for (var i = 0; i < tope; i++) Assert.Equal("Enviado", (await Avisar()).EstadoEnvio);
        var exceso = await Avisar();
        Assert.Equal("Omitido", exceso.EstadoEnvio);
        Assert.Equal(ServicioMensajes.CodigoTopeAvisos, exceso.CodigoDeError);
        Assert.Equal(0, exceso.IdMensaje);
        Assert.Equal(tope, _canal.Envios.Count);
        Assert.Equal(tope, _historial.ObtenerPorConversacion(_conversacion.IdConversacion, 0).Count);
    }

    [Fact]
    public async Task El_tope_cuenta_solo_salientes_Sistema_Enviado_o_Pendiente_del_dia_local()
    {
        var dia = _reloj.Ahora.Date;
        foreach (var mensaje in new[]
        {
            Mensaje(dia, "Pendiente"), Mensaje(_reloj.Ahora.AddMinutes(-1)),
            Mensaje(dia.AddSeconds(-1)), Mensaje(dia, "Error"),
            Mensaje(dia, origen: "Humano"), Mensaje(dia, origen: "Bot"),
            Mensaje(dia, origen: "Paciente", direccion: "Entrante"), Mensaje(dia.AddDays(1))
        }) _historial.Insertar(mensaje);
        Assert.Equal("Enviado", (await Avisar()).EstadoEnvio);
        Assert.Equal("Omitido", (await Avisar()).EstadoEnvio);
        Assert.Single(_canal.Envios);
    }

    [Fact]
    public async Task El_cupo_se_renueva_a_las_cero_locales_aunque_en_UTC_ya_sea_otro_dia()
    {
        for (var i = 0; i < 3; i++) Assert.Equal("Enviado", (await Avisar()).EstadoEnvio);
        Assert.Equal("Omitido", (await Avisar()).EstadoEnvio);
        _reloj.Ahora = _reloj.Ahora.Date.AddDays(1);
        Assert.Equal("Enviado", (await Avisar()).EstadoEnvio);
        Assert.Equal(4, _canal.Envios.Count);
    }

    [Fact]
    public async Task El_tope_es_por_numero_aunque_tenga_dos_conversaciones()
    {
        var otra = new ConversacionDAO().ObtenerOCrear(_telefono, "Otro");
        for (var i = 0; i < 3; i++) _historial.Insertar(Mensaje(_reloj.Ahora, conversacion: otra.IdConversacion));
        Assert.Equal("Omitido", (await Avisar()).EstadoEnvio);
        Assert.Empty(_canal.Envios);
    }

    [Fact]
    public async Task Las_reservas_simultaneas_no_superan_el_tope_y_los_Pendiente_consumen_cupo()
    {
        var reservas = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() =>
            _historial.IntentarInsertarAviso(Mensaje(_reloj.Ahora, "Pendiente"), 3))));
        Assert.Equal(3, reservas.Count(r => r));
        Assert.Equal(3, _historial.ObtenerPorConversacion(_conversacion.IdConversacion, 0).Count);
        Assert.Equal("Omitido", (await Avisar()).EstadoEnvio);
        Assert.Empty(_canal.Envios);
    }

    private sealed class Politica(PermisoEnvioWhatsApp permiso) : IPoliticaEnvioWhatsApp
    {
        public PermisoEnvioWhatsApp Evaluar(int idPaciente, string? telefonoActual) => permiso;
    }

    [Theory]
    [InlineData(PermisoEnvioWhatsApp.ConConsentimiento)]
    [InlineData(PermisoEnvioWhatsApp.SinDecision)]
    public void El_aviso_omitido_no_se_reintenta_y_no_necesita_columnas_de_consentimiento_en_el_historial(PermisoEnvioWhatsApp permiso)
    {
        var paciente = _datos.Paciente();
        DatosDePrueba.Ejecutar("UPDATE Paciente SET Telefono = @t WHERE IdPaciente = @p", ("@t", _telefono), ("@p", paciente));
        var turno = _datos.Turno(paciente, _datos.Profesional(), "Confirmado", _reloj.Ahora.AddDays(1), TimeSpan.FromHours(10));
        _opciones.CurrentValue.AvisosPorWhatsApp = true;
        _opciones.CurrentValue.TopeAvisosPorNumeroPorDia = 0;
        using var servicios = new ServiceCollection().BuildServiceProvider();
        var notificador = new NotificadorTurnos(new DatosMensajeriaDAO(), new RecordatorioDAO(), new NotificacionInternaDAO(),
            new ConversacionDAO(), _servicio, _canal, new Politica(permiso), new TurnoRepositorioDAO(), servicios,
            new ConfigurationBuilder().Build(), _opciones, _reloj, NullLogger<NotificadorTurnos>.Instance);
        notificador.TurnoConfirmado(turno);
        notificador.TurnoConfirmado(turno);
        Assert.Empty(_canal.Envios);
        Assert.Equal("Omitido", DatosDePrueba.Escalar("SELECT Estado FROM Recordatorio WHERE IdTurno = @t AND Canal = 'Chat'", ("@t", turno)));
        Assert.Equal(1, DatosDePrueba.Contar("SELECT Intentos FROM Recordatorio WHERE IdTurno = @t AND Canal = 'Chat'", ("@t", turno)));
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM Notificacion WHERE IdPaciente = @p", ("@p", paciente)));
    }
}
