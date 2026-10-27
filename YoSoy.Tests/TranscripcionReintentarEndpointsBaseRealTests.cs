using System.Net;
using System.Net.Http.Json;
using Xunit.Abstractions;

[Trait("Categoria", "Integracion")]
public class TranscripcionReintentarEndpointsBaseRealTests
{
    private readonly ITestOutputHelper _salida;
    public TranscripcionReintentarEndpointsBaseRealTests(ITestOutputHelper salida) { _salida = salida; }

    private sealed class CarpetaAudioTemporal : IDisposable
    {
        public readonly string Raiz = Path.Combine(Path.GetTempPath(), "yosoy-reintentar-tests", Guid.NewGuid().ToString("N"));

        public string ParaTranscripcion(int idTranscripcion)
        {
            var carpeta = Path.Combine(Raiz, idTranscripcion.ToString());
            Directory.CreateDirectory(carpeta);
            File.WriteAllBytes(Path.Combine(carpeta, "profesional.webm"), new byte[] { 1, 2, 3 });
            return carpeta;
        }

        public void Dispose()
        {
            try { if (Directory.Exists(Raiz)) Directory.Delete(Raiz, recursive: true); }
            catch { }
        }
    }

    private static VideoApp App(string carpetaAudio) => new(new Dictionary<string, string?>
    {
        ["Video:LimitePorMinuto"] = "1000",
        ["Video:Transcripcion:CarpetaAudio"] = carpetaAudio
    });

    private static HttpClient Profesional(VideoApp app, DatosEmociones d) => app.Cliente(d.IdUsuarioProfesional, Roles.Profesional, idProfesional: d.IdProfesional);

    private static Task<HttpResponseMessage> Reintentar(HttpClient cliente, int idSesion) =>
        cliente.PostAsync($"/sesiones-clinicas/{idSesion}/transcripcion/reintentar", null);

    private static (int IdSesion, int IdTranscripcion) CrearTranscripcionEnError(DatosEmociones d, int intentos = 1)
    {
        var consentimiento = new ConsentimientoVideoDAO().Insertar(d.IdUsuarioPaciente, d.IdPaciente, "turno", d.IdTurno,
            "v0-provisorio", new string('c', 64), ConsentimientoVideoFila.Acepta, DateTime.UtcNow, ConsentimientoVideoFila.Grabacion);
        var transcripciones = new TranscripcionSesionDAO();
        var fila = transcripciones.Crear("turno", d.IdTurno, consentimiento.IdConsentimientoVideo, inicioPistaProfesionalMs: 0, inicioPistaPacienteMs: null);
        var idSesion = d.InsertarSesion(DatosEmociones.Fecha + DatosEmociones.Hora, emociones: null, idConsentimiento: consentimiento.IdConsentimientoVideo);
        transcripciones.VincularSesion(fila.IdTranscripcion, idSesion);
        transcripciones.CambiarEstado(fila.IdTranscripcion, TranscripcionEstado.Error, error: "script-salida-1");
        DatosEmociones.Ejecutar("UPDATE TranscripcionSesion SET Intentos = @I WHERE IdTranscripcion = @T", ("@I", intentos), ("@T", fila.IdTranscripcion));
        return (idSesion, fila.IdTranscripcion);
    }

    [FactConBaseDeConsentimiento]
    public async Task El_reintento_pone_la_transcripcion_en_pendiente_y_resetea_los_intentos_y_el_error_si_el_audio_todavia_esta()
    {
        using var d = new DatosEmociones(salida: _salida);
        using var carpeta = new CarpetaAudioTemporal();
        using var app = App(carpeta.Raiz);
        var (idSesion, idTranscripcion) = CrearTranscripcionEnError(d, intentos: 2);
        carpeta.ParaTranscripcion(idTranscripcion);

        var r = await Reintentar(Profesional(app, d), idSesion);
        var j = await VideoApp.Json(r);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(idSesion, j.GetProperty("idSesion").GetInt32());
        Assert.Equal(TranscripcionEstado.Pendiente, j.GetProperty("estadoTranscripcion").GetString());
        var fila = new TranscripcionSesionDAO().ObtenerPorSesion(idSesion)!;
        Assert.Equal(TranscripcionEstado.Pendiente, fila.Estado);
        Assert.Equal(0, fila.Intentos);
        Assert.Null(fila.Error);
    }

    [FactConBaseDeConsentimiento]
    public async Task Una_sesion_que_no_es_del_profesional_o_que_no_existe_da_404()
    {
        using var d = new DatosEmociones(salida: _salida);
        using var ajeno = new DatosEmociones(salida: _salida);
        using var carpeta = new CarpetaAudioTemporal();
        using var app = App(carpeta.Raiz);
        var (idSesion, idTranscripcion) = CrearTranscripcionEnError(d);
        carpeta.ParaTranscripcion(idTranscripcion);

        var r1 = await Reintentar(Profesional(app, ajeno), idSesion);
        var r2 = await Reintentar(Profesional(app, d), 2_000_000_000);

        Assert.Equal(HttpStatusCode.NotFound, r1.StatusCode);
        Assert.Equal("sesion-inexistente", (await VideoApp.Json(r1)).GetProperty("codigo").GetString());
        Assert.Equal(HttpStatusCode.NotFound, r2.StatusCode);
        Assert.Equal("sesion-inexistente", (await VideoApp.Json(r2)).GetProperty("codigo").GetString());
        Assert.Equal(TranscripcionEstado.Error, new TranscripcionSesionDAO().ObtenerPorSesion(idSesion)!.Estado);
    }

    [FactConBaseDeConsentimiento]
    public async Task Sin_una_transcripcion_en_error_da_409()
    {
        using var d = new DatosEmociones(salida: _salida);
        using var carpeta = new CarpetaAudioTemporal();
        using var app = App(carpeta.Raiz);
        var idSesionSinTranscripcion = d.InsertarSesion(DatosEmociones.Fecha + DatosEmociones.Hora, emociones: null);
        var consentimiento = new ConsentimientoVideoDAO().Insertar(d.IdUsuarioPaciente, d.IdPaciente, "turno", d.IdTurno,
            "v0-provisorio", new string('c', 64), ConsentimientoVideoFila.Acepta, DateTime.UtcNow, ConsentimientoVideoFila.Grabacion);
        var transcripciones = new TranscripcionSesionDAO();
        var pendiente = transcripciones.Crear("turno", d.IdTurno, consentimiento.IdConsentimientoVideo, inicioPistaProfesionalMs: 0, inicioPistaPacienteMs: null);
        var idSesionPendiente = d.InsertarSesion(DatosEmociones.Fecha + DatosEmociones.Hora, emociones: null, idConsentimiento: consentimiento.IdConsentimientoVideo);
        transcripciones.VincularSesion(pendiente.IdTranscripcion, idSesionPendiente);

        var r1 = await Reintentar(Profesional(app, d), idSesionSinTranscripcion);
        var r2 = await Reintentar(Profesional(app, d), idSesionPendiente);

        Assert.Equal(HttpStatusCode.Conflict, r1.StatusCode);
        Assert.Equal("transcripcion-no-en-error", (await VideoApp.Json(r1)).GetProperty("codigo").GetString());
        Assert.Equal(HttpStatusCode.Conflict, r2.StatusCode);
        Assert.Equal("transcripcion-no-en-error", (await VideoApp.Json(r2)).GetProperty("codigo").GetString());
    }

    [FactConBaseDeConsentimiento]
    public async Task Sin_el_audio_en_disco_da_409_y_no_cambia_el_estado()
    {
        using var d = new DatosEmociones(salida: _salida);
        using var carpeta = new CarpetaAudioTemporal();
        using var app = App(carpeta.Raiz);
        var (idSesion, _) = CrearTranscripcionEnError(d);

        var r = await Reintentar(Profesional(app, d), idSesion);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("audio-no-disponible", (await VideoApp.Json(r)).GetProperty("codigo").GetString());
        Assert.Equal(TranscripcionEstado.Error, new TranscripcionSesionDAO().ObtenerPorSesion(idSesion)!.Estado);
    }

    [FactConBaseDeConsentimiento]
    public async Task El_paciente_el_configurador_y_quien_no_tiene_sesion_no_pueden_reintentar()
    {
        using var d = new DatosEmociones(salida: _salida);
        using var carpeta = new CarpetaAudioTemporal();
        using var app = App(carpeta.Raiz);
        var (idSesion, idTranscripcion) = CrearTranscripcionEnError(d);
        carpeta.ParaTranscripcion(idTranscripcion);

        var paciente = app.Cliente(d.IdUsuarioPaciente, Roles.Paciente, idPaciente: d.IdPaciente);
        var configurador = app.Cliente(d.IdUsuarioConfigurador, Roles.Configurador);

        Assert.Equal(HttpStatusCode.Forbidden, (await Reintentar(paciente, idSesion)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Reintentar(configurador, idSesion)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Reintentar(app.SinSesion(), idSesion)).StatusCode);
        Assert.Equal(TranscripcionEstado.Error, new TranscripcionSesionDAO().ObtenerPorSesion(idSesion)!.Estado);
    }
}
