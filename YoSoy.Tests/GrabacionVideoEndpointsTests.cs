using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Repositorio de TranscripcionSesion en memoria: misma semántica que TranscripcionSesionDAO (incluido el índice único
// "una fila 'grabando' por sesión", simulado acá con una comprobación antes de insertar), para probar GrabacionVideoEndpoints
// y EncoladoGrabacionReglas sin SQL Server.
public class FakeTranscripcionRepo : ITranscripcionSesionRepositorio
{
    public readonly List<TranscripcionSesion> Filas = new();
    public TimeProvider? Reloj { get; set; }
    private int _siguiente = 9000;

    public TranscripcionSesion Crear(string origen, int idOrigen, int idConsentimientoVideo, long? inicioPistaProfesionalMs, long? inicioPistaPacienteMs)
    {
        if (ObtenerActiva(origen, idOrigen) is not null)
            throw new InvalidOperationException("Ya hay una grabación 'grabando' para esta sesión (índice único).");
        var ahora = Reloj?.GetUtcNow().UtcDateTime ?? DateTime.UtcNow;
        var fila = new TranscripcionSesion
        {
            IdTranscripcion = _siguiente++, Origen = origen, IdOrigen = idOrigen, IdConsentimientoVideo = idConsentimientoVideo,
            Estado = TranscripcionEstado.Grabando, InicioPistaProfesionalMs = inicioPistaProfesionalMs,
            InicioPistaPacienteMs = inicioPistaPacienteMs, CreadoUtc = ahora, ActualizadoUtc = ahora
        };
        Filas.Add(fila);
        return fila;
    }

    public TranscripcionSesion CrearOReusar(string origen, int idOrigen, int idConsentimientoVideo, long? inicioPistaProfesionalMs, long? inicioPistaPacienteMs) =>
        ObtenerActiva(origen, idOrigen) ?? Crear(origen, idOrigen, idConsentimientoVideo, inicioPistaProfesionalMs, inicioPistaPacienteMs);

    public TranscripcionSesion? ObtenerActiva(string origen, int idOrigen) =>
        Filas.Where(f => f.Origen == origen && f.IdOrigen == idOrigen && f.Estado == TranscripcionEstado.Grabando)
             .OrderByDescending(f => f.IdTranscripcion).FirstOrDefault();

    public bool ActualizarInicioPista(int idTranscripcion, string pista, long inicioMs)
    {
        var f = Filas.FirstOrDefault(x => x.IdTranscripcion == idTranscripcion);
        if (f is null) return false;
        if (pista == PistaGrabacion.Profesional)
        {
            if (f.InicioPistaProfesionalMs is not null) return false;
            f.InicioPistaProfesionalMs = inicioMs;
        }
        else
        {
            if (f.InicioPistaPacienteMs is not null) return false;
            f.InicioPistaPacienteMs = inicioMs;
        }
        return true;
    }

    public TranscripcionSesion? ObtenerPorSesion(int idSesion) =>
        Filas.Where(f => f.IdSesion == idSesion).OrderByDescending(f => f.IdTranscripcion).FirstOrDefault();

    public bool VincularSesion(int idTranscripcion, int idSesion)
    {
        var f = Filas.FirstOrDefault(x => x.IdTranscripcion == idTranscripcion && x.Estado == TranscripcionEstado.Grabando);
        if (f is null) return false;
        f.IdSesion = idSesion;
        f.Estado = TranscripcionEstado.Pendiente;
        return true;
    }

    public bool CambiarEstado(int idTranscripcion, string estado, string? error = null, int? duracionAudioSeg = null, int? duracionProcesoSeg = null, string? modelo = null)
    {
        var f = Filas.FirstOrDefault(x => x.IdTranscripcion == idTranscripcion);
        if (f is null) return false;
        f.Estado = estado; f.Error = error; f.DuracionAudioSeg = duracionAudioSeg; f.DuracionProcesoSeg = duracionProcesoSeg; f.Modelo = modelo;
        return true;
    }

    public TranscripcionSesion? TomarSiguientePendiente()
    {
        var f = Filas.Where(x => x.Estado == TranscripcionEstado.Pendiente).OrderBy(x => x.IdTranscripcion).FirstOrDefault();
        if (f is not null) f.Estado = TranscripcionEstado.Procesando;
        return f;
    }

    public int ReencolarProcesando()
    {
        var procesando = Filas.Where(x => x.Estado == TranscripcionEstado.Procesando).ToList();
        foreach (var f in procesando) f.Estado = TranscripcionEstado.Pendiente;
        return procesando.Count;
    }
}

// POST /video/grabacion/fragmento con la aplicación real y dobles en memoria para consentimiento y TranscripcionSesion.
// Mismo turno/admisión/personas de referencia que VideoApp. La carpeta de audio es un directorio temporal propio de
// cada test, que se borra al salir.
public class GrabacionVideoEndpointsTests
{
    private sealed class App : IDisposable
    {
        public readonly VideoApp Video;
        public readonly FakeConsentimientoRepo Consentimientos = new();
        public readonly FakeTranscripcionRepo Transcripciones = new();
        public readonly string CarpetaAudio = Path.Combine(Path.GetTempPath(), "yosoy-grabacion-tests", Guid.NewGuid().ToString("N"));

        public App(Dictionary<string, string?>? config = null)
        {
            Consentimientos.Pacientes[VideoApp.IdPaciente] = new DatosPacienteParaConsentimiento(new DateTime(1990, 5, 5), false);

            var ajustes = new Dictionary<string, string?>
            {
                ["Video:Emociones:ClaveCifrado"] = EscenarioEmociones.ClaveDePrueba,
                ["Video:Transcripcion:Habilitada"] = "true",
                ["Video:Transcripcion:CarpetaAudio"] = CarpetaAudio,
                ["Video:Transcripcion:MaxBytesFragmento"] = (2L * 1024 * 1024).ToString(),
                ["Video:Transcripcion:MaxBytesPista"] = (300L * 1024 * 1024).ToString(),
                ["Video:Transcripcion:MaxMinutosGrabacion"] = "120",
                ["Video:Transcripcion:LimitePorMinuto"] = "1000"
            };
            if (config is not null) foreach (var (k, v) in config) ajustes[k] = v;

            Video = new VideoApp(ajustes, servicios: s =>
            {
                // GrabacionVideoEndpoints.Subir recibe el TimeProvider global por DI (no pasa por AccesoSalaService),
                // así que hay que pisarlo también para que el tope de duración vea el mismo reloj fijo que el resto.
                s.RemoveAll<TimeProvider>();
                s.AddSingleton<TimeProvider>(_ => Video!.Reloj);
                s.RemoveAll<IConsentimientoVideoRepositorio>();
                s.AddSingleton<IConsentimientoVideoRepositorio>(Consentimientos);
                s.RemoveAll<ITranscripcionSesionRepositorio>();
                s.AddSingleton<ITranscripcionSesionRepositorio>(Transcripciones);
                s.RemoveAll<ConsentimientoVideoService>();
                s.AddSingleton(sp => new ConsentimientoVideoService(Consentimientos, sp.GetRequiredService<IOrigenSalaRepositorio>(),
                    sp.GetRequiredService<AccesoSalaService>(), sp.GetRequiredService<ITextoConsentimientoVideo>(),
                    sp.GetRequiredService<IEmocionesCifrado>(), sp.GetRequiredService<IAuditoriaEmociones>(), Video!.Reloj,
                    sp.GetRequiredService<IOptionsMonitor<VideoOptions>>(), sp.GetRequiredService<ILogger<ConsentimientoVideoService>>()));
            });
            Transcripciones.Reloj = Video.Reloj;
        }

        public void Aceptar(string origen, int id, int idPaciente) =>
            Consentimientos.Insertar(VideoApp.UsuarioPaciente, idPaciente, origen, id, "v0-provisorio-grabacion", "hash",
                ConsentimientoVideoFila.Acepta, Video.Reloj.GetUtcNow().UtcDateTime, ConsentimientoVideoFila.Grabacion);

        public void Dispose()
        {
            Video.Dispose();
            try { if (Directory.Exists(CarpetaAudio)) Directory.Delete(CarpetaAudio, recursive: true); } catch { /* best effort */ }
        }
    }

    private const string Url = "/video/grabacion/fragmento";

    private static MultipartFormDataContent Fragmento(string origen = "turno", int id = VideoApp.IdTurno, string pista = "profesional",
        int secuencia = 0, long? inicioMs = 0, byte[]? bytes = null)
    {
        var m = new MultipartFormDataContent
        {
            { new StringContent(origen), "origen" },
            { new StringContent(id.ToString()), "id" },
            { new StringContent(pista), "pista" },
            { new StringContent(secuencia.ToString()), "secuencia" }
        };
        if (inicioMs is { } ms) m.Add(new StringContent(ms.ToString()), "inicioMs");
        var contenido = new ByteArrayContent(bytes ?? Encoding.UTF8.GetBytes("audio-de-prueba"));
        contenido.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/webm");
        m.Add(contenido, "archivo", "fragmento.webm");
        return m;
    }

    private static async Task<(HttpResponseMessage R, JsonElement J)> Subir(HttpClient c, MultipartFormDataContent form)
    {
        var r = await c.PostAsync(Url, form);
        return (r, await VideoApp.Json(r));
    }

    // ───────────── Autenticación y pertenencia ─────────────

    [Fact]
    public async Task Sin_sesion_da_401_y_no_crea_nada()
    {
        using var app = new App();
        var r = await app.Video.SinSesion().PostAsync(Url, Fragmento());

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Empty(app.Transcripciones.Filas);
    }

    [Fact]
    public async Task El_paciente_no_puede_subir_audio()
    {
        using var app = new App();
        app.Aceptar("turno", VideoApp.IdTurno, VideoApp.IdPaciente);

        var (r, _) = await Subir(app.Video.Paciente(), Fragmento());

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Empty(app.Transcripciones.Filas);
    }

    [Fact]
    public async Task Un_profesional_ajeno_al_turno_recibe_el_mismo_404_que_una_sesion_inexistente()
    {
        using var app = new App();
        app.Aceptar("turno", VideoApp.IdTurno, VideoApp.IdPaciente);

        var (rAjeno, _) = await Subir(app.Video.OtroProfesional(), Fragmento());
        var (rInexistente, _) = await Subir(app.Video.Profesional(), Fragmento(id: 987));

        Assert.Equal(HttpStatusCode.NotFound, rAjeno.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, rInexistente.StatusCode);
        Assert.Empty(app.Transcripciones.Filas);
    }

    [Fact]
    public async Task Quien_hace_la_admision_puede_subir_audio_de_esa_admision()
    {
        using var app = new App();
        app.Aceptar("admision", VideoApp.IdInforme, VideoApp.IdPaciente);

        var (r, j) = await Subir(app.Video.Admision(), Fragmento(origen: "admision", id: VideoApp.IdInforme));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("grabando", j.GetProperty("estado").GetString());
    }

    // ───────────── Disponibilidad / 503 ─────────────

    [Fact]
    public async Task Con_la_grabacion_deshabilitada_da_503()
    {
        using var app = new App(new() { ["Video:Transcripcion:Habilitada"] = "false" });
        app.Aceptar("turno", VideoApp.IdTurno, VideoApp.IdPaciente);

        var (r, j) = await Subir(app.Video.Profesional(), Fragmento());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.Equal("grabacion-no-disponible", j.GetProperty("codigo").GetString());
    }

    // ───────────── Ventana de la sesión ─────────────

    [Fact]
    public async Task Fuera_de_la_ventana_de_la_sesion_da_409_con_su_codigo()
    {
        using var app = new App();
        app.Aceptar("turno", VideoApp.IdTurno, VideoApp.IdPaciente);
        app.Video.Reloj.Avanzar(TimeSpan.FromHours(3));   // la sala del turno 1 cerró a las 11:00

        var (r, j) = await Subir(app.Video.Profesional(), Fragmento());

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("ya-termino", j.GetProperty("codigo").GetString());
        Assert.Empty(app.Transcripciones.Filas);
    }

    // ───────────── Consentimiento ─────────────

    [Fact]
    public async Task Sin_consentimiento_de_grabacion_da_409_sin_consentimiento()
    {
        using var app = new App();   // nadie aceptó

        var (r, j) = await Subir(app.Video.Profesional(), Fragmento());

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("sin-consentimiento", j.GetProperty("codigo").GetString());
        Assert.Empty(app.Transcripciones.Filas);
    }

    [Fact]
    public async Task Un_consentimiento_revocado_ya_no_habilita_la_subida()
    {
        using var app = new App();
        var fila = app.Consentimientos.Insertar(VideoApp.UsuarioPaciente, VideoApp.IdPaciente, "turno", VideoApp.IdTurno,
            "v0-provisorio-grabacion", "hash", ConsentimientoVideoFila.Acepta, app.Video.Reloj.GetUtcNow().UtcDateTime, ConsentimientoVideoFila.Grabacion);
        app.Consentimientos.Revocar(fila.IdConsentimientoVideo, app.Video.Reloj.GetUtcNow().UtcDateTime);

        var (r, j) = await Subir(app.Video.Profesional(), Fragmento());

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("sin-consentimiento", j.GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task Un_paciente_menor_de_edad_no_tiene_grabacion_disponible()
    {
        using var app = new App();
        app.Consentimientos.Pacientes[VideoApp.IdPaciente] = new DatosPacienteParaConsentimiento(new DateTime(2018, 2, 2), false);

        var (r, j) = await Subir(app.Video.Profesional(), Fragmento());

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("menor-de-edad", j.GetProperty("codigo").GetString());
    }

    // ───────────── Pedido mal armado ─────────────

    [Theory]
    [InlineData("otro", VideoApp.IdTurno, "profesional", 0)]
    [InlineData("turno", 0, "profesional", 0)]
    [InlineData("turno", VideoApp.IdTurno, "otra-pista", 0)]
    public async Task Un_pedido_invalido_da_400_solicitud_invalida(string origen, int id, string pista, int secuencia)
    {
        using var app = new App();
        app.Aceptar("turno", VideoApp.IdTurno, VideoApp.IdPaciente);

        var (r, j) = await Subir(app.Video.Profesional(), Fragmento(origen: origen, id: id, pista: pista, secuencia: secuencia));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("solicitud-invalida", j.GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task La_secuencia_0_sin_inicioMs_da_400()
    {
        using var app = new App();
        app.Aceptar("turno", VideoApp.IdTurno, VideoApp.IdPaciente);

        var (r, j) = await Subir(app.Video.Profesional(), Fragmento(secuencia: 0, inicioMs: null));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("solicitud-invalida", j.GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task Una_secuencia_distinta_de_0_con_inicioMs_da_400()
    {
        using var app = new App();
        app.Aceptar("turno", VideoApp.IdTurno, VideoApp.IdPaciente);
        await Subir(app.Video.Profesional(), Fragmento(secuencia: 0, inicioMs: 0));

        var (r, j) = await Subir(app.Video.Profesional(), Fragmento(secuencia: 1, inicioMs: 500));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("solicitud-invalida", j.GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task Un_fragmento_mas_grande_que_el_tope_da_400()
    {
        using var app = new App(new() { ["Video:Transcripcion:MaxBytesFragmento"] = "10" });
        app.Aceptar("turno", VideoApp.IdTurno, VideoApp.IdPaciente);

        var (r, j) = await Subir(app.Video.Profesional(), Fragmento(bytes: new byte[1000]));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("fragmento-muy-grande", j.GetProperty("codigo").GetString());
        Assert.Empty(app.Transcripciones.Filas);
    }

    // ───────────── Subida de punta a punta: creación, concatenación, idempotencia, orden ─────────────

    [Fact]
    public async Task El_primer_fragmento_crea_la_grabacion_y_las_dos_pistas_la_reusan()
    {
        using var app = new App();
        app.Aceptar("turno", VideoApp.IdTurno, VideoApp.IdPaciente);

        var (r1, j1) = await Subir(app.Video.Profesional(), Fragmento(pista: "profesional", secuencia: 0, inicioMs: 0));
        var (r2, j2) = await Subir(app.Video.Profesional(), Fragmento(pista: "paciente", secuencia: 0, inicioMs: 120));

        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.Equal("grabando", j1.GetProperty("estado").GetString());
        var idTranscripcion = j1.GetProperty("idTranscripcion").GetInt32();
        Assert.Equal(idTranscripcion, j2.GetProperty("idTranscripcion").GetInt32());   // misma fila para las dos pistas
        Assert.Single(app.Transcripciones.Filas);

        var fila = app.Transcripciones.Filas[0];
        Assert.Equal(0, fila.InicioPistaProfesionalMs);
        Assert.Equal(120, fila.InicioPistaPacienteMs);

        var carpeta = Path.Combine(app.CarpetaAudio, idTranscripcion.ToString());
        Assert.True(File.Exists(Path.Combine(carpeta, "profesional.webm")));
        Assert.True(File.Exists(Path.Combine(carpeta, "paciente.webm")));
    }

    [Fact]
    public async Task Los_fragmentos_en_orden_se_van_concatenando()
    {
        using var app = new App();
        app.Aceptar("turno", VideoApp.IdTurno, VideoApp.IdPaciente);
        var uno = Encoding.UTF8.GetBytes("uno---");
        var dos = Encoding.UTF8.GetBytes("dos---");

        var (_, j) = await Subir(app.Video.Profesional(), Fragmento(secuencia: 0, inicioMs: 0, bytes: uno));
        await Subir(app.Video.Profesional(), Fragmento(secuencia: 1, inicioMs: null, bytes: dos));

        var idTranscripcion = j.GetProperty("idTranscripcion").GetInt32();
        var ruta = Path.Combine(app.CarpetaAudio, idTranscripcion.ToString(), "profesional.webm");
        Assert.Equal("uno---dos---", Encoding.UTF8.GetString(await File.ReadAllBytesAsync(ruta)));
    }

    [Fact]
    public async Task Repetir_la_misma_secuencia_es_idempotente_y_no_duplica_el_audio()
    {
        using var app = new App();
        app.Aceptar("turno", VideoApp.IdTurno, VideoApp.IdPaciente);
        var contenido = Encoding.UTF8.GetBytes("solo-una-vez");
        var (_, j) = await Subir(app.Video.Profesional(), Fragmento(secuencia: 0, inicioMs: 0, bytes: contenido));

        var (r, jRepetido) = await Subir(app.Video.Profesional(), Fragmento(secuencia: 0, inicioMs: 0, bytes: contenido));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(0, jRepetido.GetProperty("secuencia").GetInt32());
        var idTranscripcion = j.GetProperty("idTranscripcion").GetInt32();
        var ruta = Path.Combine(app.CarpetaAudio, idTranscripcion.ToString(), "profesional.webm");
        Assert.Equal("solo-una-vez", Encoding.UTF8.GetString(await File.ReadAllBytesAsync(ruta)));   // no se duplicó
    }

    [Fact]
    public async Task Saltear_una_secuencia_da_409_con_cual_es_la_que_falta()
    {
        using var app = new App();
        app.Aceptar("turno", VideoApp.IdTurno, VideoApp.IdPaciente);
        await Subir(app.Video.Profesional(), Fragmento(secuencia: 0, inicioMs: 0));

        var (r, j) = await Subir(app.Video.Profesional(), Fragmento(secuencia: 2, inicioMs: null));

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("secuencia-fuera-de-orden", j.GetProperty("codigo").GetString());
        Assert.Equal(1, j.GetProperty("secuenciaEsperada").GetInt32());
    }

    [Fact]
    public async Task Sin_grabacion_activa_no_se_puede_empezar_por_una_secuencia_distinta_de_0()
    {
        using var app = new App();
        app.Aceptar("turno", VideoApp.IdTurno, VideoApp.IdPaciente);

        var (r, j) = await Subir(app.Video.Profesional(), Fragmento(secuencia: 1, inicioMs: null));

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("sin-grabacion-activa", j.GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task El_tope_de_bytes_de_la_pista_corta_la_grabacion_con_409()
    {
        using var app = new App(new() { ["Video:Transcripcion:MaxBytesPista"] = "10", ["Video:Transcripcion:MaxBytesFragmento"] = "1000" });
        app.Aceptar("turno", VideoApp.IdTurno, VideoApp.IdPaciente);

        var (r, j) = await Subir(app.Video.Profesional(), Fragmento(secuencia: 0, inicioMs: 0, bytes: new byte[20]));

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("tope-bytes-pista", j.GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task El_tope_de_duracion_de_la_grabacion_corta_fragmentos_nuevos_con_409()
    {
        using var app = new App(new() { ["Video:Transcripcion:MaxMinutosGrabacion"] = "1" });
        app.Aceptar("turno", VideoApp.IdTurno, VideoApp.IdPaciente);
        await Subir(app.Video.Profesional(), Fragmento(secuencia: 0, inicioMs: 0));
        app.Video.Reloj.Avanzar(TimeSpan.FromMinutes(2));

        var (r, j) = await Subir(app.Video.Profesional(), Fragmento(secuencia: 1, inicioMs: null));

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("duracion-maxima", j.GetProperty("codigo").GetString());
    }

    // ───────────── Limitador ─────────────

    [Fact]
    public async Task Pasado_el_limite_por_minuto_se_rechaza_con_429()
    {
        using var app = new App(new() { ["Video:Transcripcion:LimitePorMinuto"] = "2" });
        app.Aceptar("turno", VideoApp.IdTurno, VideoApp.IdPaciente);
        var profesional = app.Video.Profesional();

        await Subir(profesional, Fragmento(secuencia: 0, inicioMs: 0));
        await Subir(profesional, Fragmento(secuencia: 1, inicioMs: null));
        var (r, _) = await Subir(profesional, Fragmento(secuencia: 2, inicioMs: null));

        Assert.Equal(HttpStatusCode.TooManyRequests, r.StatusCode);
    }
}
