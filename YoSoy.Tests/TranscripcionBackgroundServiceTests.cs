using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

public class TranscripcionBackgroundServiceTests
{
    private const string Origen = "turno";

    private sealed class FakeTranscriptorLocal : ITranscriptorLocal
    {
        public Func<SolicitudTranscripcion, ResultadoTranscripcion> Resultado { get; set; } =
            _ => new ResultadoTranscripcion(10, new List<SegmentoTranscripcion>());
        public int Llamadas { get; private set; }

        public Task<ResultadoTranscripcion> Transcribir(SolicitudTranscripcion solicitud, CancellationToken ct)
        {
            Llamadas++;
            return Task.FromResult(Resultado(solicitud));
        }
    }

    private sealed class Escenario : IDisposable
    {
        public readonly FakeTranscripcionRepo Transcripciones = new();
        public readonly FakeConsentimientoRepo Consentimientos = new();
        public readonly FakeTranscriptorLocal Transcriptor = new();
        public readonly OpcionesFijas<VideoOptions> Opciones = new();
        public readonly RelojFijo Reloj = new(new DateTime(2031, 3, 4, 10, 0, 0, DateTimeKind.Utc));
        public readonly string CarpetaAudio = Path.Combine(Path.GetTempPath(), "yosoy-transcripcion-tests", Guid.NewGuid().ToString("N"));

        public Escenario(int maxIntentos = 3)
        {
            Opciones.CurrentValue.Emociones.ClaveCifrado = EscenarioEmociones.ClaveDePrueba;
            Opciones.CurrentValue.Transcripcion.Habilitada = true;
            Opciones.CurrentValue.Transcripcion.CarpetaAudio = CarpetaAudio;
            Opciones.CurrentValue.Transcripcion.MaxIntentos = maxIntentos;
            Opciones.CurrentValue.Transcripcion.HorasGrabacionHuerfana = 24;
            Opciones.CurrentValue.Transcripcion.DiasConservarAudioConError = 7;
            Transcripciones.Reloj = Reloj;
        }

        public EmocionesCifradoService Cifrado()
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
            return new EmocionesCifradoService(Opciones, config);
        }

        public TranscripcionBackgroundService Servicio() =>
            new(Transcripciones, Consentimientos, Cifrado(), Transcriptor, Opciones, Reloj, NullLogger<TranscripcionBackgroundService>.Instance);

        public int CrearPendiente(int idSesion, out int idConsentimiento)
        {
            var consentimiento = Consentimientos.Insertar(1, 301, Origen, idSesion, "v1", "hash", ConsentimientoVideoFila.Acepta,
                Reloj.Ahora, ConsentimientoVideoFila.Grabacion);
            idConsentimiento = consentimiento.IdConsentimientoVideo;
            var fila = Transcripciones.Crear(Origen, idSesion, idConsentimiento, inicioPistaProfesionalMs: 0, inicioPistaPacienteMs: null);
            Transcripciones.VincularSesion(fila.IdTranscripcion, idSesion);
            return fila.IdTranscripcion;
        }

        public string CrearCarpetaAudio(int idTranscripcion)
        {
            var carpeta = Path.Combine(CarpetaAudio, idTranscripcion.ToString());
            Directory.CreateDirectory(carpeta);
            File.WriteAllBytes(Path.Combine(carpeta, "profesional.webm"), new byte[] { 1, 2, 3 });
            return carpeta;
        }

        public void Dispose()
        {
            try { if (Directory.Exists(CarpetaAudio)) Directory.Delete(CarpetaAudio, recursive: true); }
            catch { }
        }
    }

    [Fact]
    public void Una_transcripcion_pendiente_se_procesa_se_cifra_y_se_borra_el_audio()
    {
        using var e = new Escenario();
        var idTranscripcion = e.CrearPendiente(idSesion: 1, out _);
        var carpeta = e.CrearCarpetaAudio(idTranscripcion);
        e.Transcriptor.Resultado = _ => new ResultadoTranscripcion(42, new List<SegmentoTranscripcion>
        {
            new(PistaGrabacion.Profesional, 0, 2, "hola")
        });

        var resultado = e.Servicio().EjecutarUnaVez();

        Assert.Equal(ResultadoCicloTranscripcion.Procesada, resultado);
        var fila = e.Transcripciones.Filas.Single(f => f.IdTranscripcion == idTranscripcion);
        Assert.Equal(TranscripcionEstado.Lista, fila.Estado);
        Assert.False(Directory.Exists(carpeta));

        var guardado = e.Transcripciones.TranscripcionPorSesion[1];
        var ok = e.Cifrado().Descifrar(guardado, EmocionesCifradoService.DatosAsociadosDeTranscripcion(1), out var texto);
        Assert.Equal(ResultadoDescifrado.Ok, ok);
        Assert.Equal("[00:00] Profesional: hola", texto);
    }

    [Fact]
    public void Deshabilitada_no_procesa_nada()
    {
        using var e = new Escenario();
        e.Opciones.CurrentValue.Transcripcion.Habilitada = false;
        var idTranscripcion = e.CrearPendiente(idSesion: 1, out _);

        var resultado = e.Servicio().EjecutarUnaVez();

        Assert.Equal(ResultadoCicloTranscripcion.Deshabilitado, resultado);
        Assert.Equal(TranscripcionEstado.Pendiente, e.Transcripciones.Filas.Single(f => f.IdTranscripcion == idTranscripcion).Estado);
        Assert.Equal(0, e.Transcriptor.Llamadas);
    }

    [Fact]
    public void Sin_pendientes_no_hay_nada_para_procesar()
    {
        using var e = new Escenario();

        var resultado = e.Servicio().EjecutarUnaVez();

        Assert.Equal(ResultadoCicloTranscripcion.SinPendientes, resultado);
        Assert.Equal(0, e.Transcriptor.Llamadas);
    }

    [Fact]
    public void Consentimiento_revocado_antes_de_procesar_anula_sin_llamar_al_transcriptor()
    {
        using var e = new Escenario();
        var idTranscripcion = e.CrearPendiente(idSesion: 1, out var idConsentimiento);
        var carpeta = e.CrearCarpetaAudio(idTranscripcion);
        e.Consentimientos.Revocar(idConsentimiento, e.Reloj.Ahora);

        var resultado = e.Servicio().EjecutarUnaVez();

        Assert.Equal(ResultadoCicloTranscripcion.Procesada, resultado);
        Assert.Equal(TranscripcionEstado.Anulada, e.Transcripciones.Filas.Single(f => f.IdTranscripcion == idTranscripcion).Estado);
        Assert.Equal(0, e.Transcriptor.Llamadas);
        Assert.False(Directory.Exists(carpeta));
    }

    [Fact]
    public void Consentimiento_revocado_justo_al_guardar_anula_sin_romper()
    {
        using var e = new Escenario();
        var idTranscripcion = e.CrearPendiente(idSesion: 1, out var idConsentimiento);
        e.CrearCarpetaAudio(idTranscripcion);
        e.Transcripciones.ConsentimientoVigente = id => id != idConsentimiento;

        var resultado = e.Servicio().EjecutarUnaVez();

        Assert.Equal(ResultadoCicloTranscripcion.Procesada, resultado);
        Assert.Equal(TranscripcionEstado.Anulada, e.Transcripciones.Filas.Single(f => f.IdTranscripcion == idTranscripcion).Estado);
    }

    [Fact]
    public void Una_falla_que_todavia_tiene_intentos_disponibles_vuelve_a_pendiente()
    {
        using var e = new Escenario(maxIntentos: 3);
        var idTranscripcion = e.CrearPendiente(idSesion: 1, out _);
        e.CrearCarpetaAudio(idTranscripcion);
        e.Transcriptor.Resultado = _ => throw new TranscripcionFallidaException("script-salida-1");

        e.Servicio().EjecutarUnaVez();

        var fila = e.Transcripciones.Filas.Single(f => f.IdTranscripcion == idTranscripcion);
        Assert.Equal(TranscripcionEstado.Pendiente, fila.Estado);
        Assert.Equal("script-salida-1", fila.Error);
    }

    [Fact]
    public void Una_falla_que_agota_los_intentos_queda_en_error()
    {
        using var e = new Escenario(maxIntentos: 1);
        var idTranscripcion = e.CrearPendiente(idSesion: 1, out _);
        e.CrearCarpetaAudio(idTranscripcion);
        e.Transcriptor.Resultado = _ => throw new TranscripcionFallidaException("script-salida-1");

        e.Servicio().EjecutarUnaVez();

        var fila = e.Transcripciones.Filas.Single(f => f.IdTranscripcion == idTranscripcion);
        Assert.Equal(TranscripcionEstado.Error, fila.Estado);
        Assert.Equal("script-salida-1", fila.Error);
    }

    [Fact]
    public void Un_timeout_se_guarda_como_motivo_corto_sin_detalle()
    {
        using var e = new Escenario(maxIntentos: 1);
        var idTranscripcion = e.CrearPendiente(idSesion: 1, out _);
        e.CrearCarpetaAudio(idTranscripcion);
        e.Transcriptor.Resultado = _ => throw new TimeoutException("detalle interno que no debe guardarse");

        e.Servicio().EjecutarUnaVez();

        var fila = e.Transcripciones.Filas.Single(f => f.IdTranscripcion == idTranscripcion);
        Assert.Equal(TranscripcionEstado.Error, fila.Estado);
        Assert.Equal("timeout", fila.Error);
    }

    [Fact]
    public void El_saneamiento_anula_grabaciones_huerfanas_viejas_y_borra_su_audio()
    {
        using var e = new Escenario();
        var fila = e.Transcripciones.Crear(Origen, 1, idConsentimientoVideo: 1, inicioPistaProfesionalMs: 0, inicioPistaPacienteMs: null);
        var carpeta = e.CrearCarpetaAudio(fila.IdTranscripcion);
        e.Reloj.Avanzar(TimeSpan.FromHours(25));

        e.Servicio().EjecutarUnaVez();

        Assert.Equal(TranscripcionEstado.Anulada, e.Transcripciones.Filas.Single(f => f.IdTranscripcion == fila.IdTranscripcion).Estado);
        Assert.False(Directory.Exists(carpeta));
    }

    [Fact]
    public void El_saneamiento_borra_el_audio_de_una_transcripcion_lista()
    {
        using var e = new Escenario();
        var idTranscripcion = e.CrearPendiente(idSesion: 1, out _);
        var carpeta = e.CrearCarpetaAudio(idTranscripcion);
        e.Transcripciones.CambiarEstado(idTranscripcion, TranscripcionEstado.Lista);

        e.Servicio().EjecutarUnaVez();

        Assert.False(Directory.Exists(carpeta));
    }
}
