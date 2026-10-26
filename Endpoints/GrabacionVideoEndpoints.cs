using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

// Subida de audio de la grabación de la sesión, para la transcripción local (etapa 2 de 5, solo backend: el
// worker que transcribe y la pantalla que grava con MediaRecorder son etapas aparte). Se registra con
// app.MapGrabacionVideo().
//
//   POST /video/grabacion/fragmento   multipart/form-data: origen, id (misma pareja que /video/acceso), pista
//                                      ("profesional" | "paciente"), secuencia (entero >= 0, uno por fragmento de
//                                      esa pista, en orden), inicioMs (milisegundos desde el arranque de la
//                                      sesión; va SOLO en la secuencia 0 de cada pista) y archivo (audio/webm).
//
// Solo quien atiende la sesión (SesionDeVideoReglas.AtiendeLaSesion: el profesional del turno o quien hace la
// admisión) puede subir audio, nunca el paciente ni el Configurador (igual que GET /video/grabacion/permiso). El
// primer fragmento (secuencia 0, con inicioMs) de la primera pista en llegar crea la fila TranscripcionSesion en
// 'grabando' (o reusa la que ya creó la otra pista: índice único UX_TranscripcionSesion_Grabando); los fragmentos
// de cada pista se van concatenando en orden en un archivo propio dentro de Video:Transcripcion:CarpetaAudio.
// Repetir la misma secuencia (reintento de red) es "ya la tengo" y no duplica nada; saltear una es 409 con cuál
// es la que falta, para que el cliente la reenvíe.
//
// Respuestas: 200 { idTranscripcion, pista, secuencia, estado } · 400 pedido mal armado o fragmento más grande que
// Video:Transcripcion:MaxBytesFragmento · 401 sin JWT · 404 no existe o no sos quien atiende la sesión (mismo
// cuerpo que /video/acceso) · 409 ventana cerrada / sin consentimiento vigente / sin grabación activa / secuencia
// fuera de orden / tope de bytes de la pista / tope de duración (con "codigo" y, según el caso, más datos) ·
// 429 demasiados pedidos · 503 grabación no disponible (Video:Transcripcion:Habilitada en false).
public static class GrabacionVideoEndpoints
{
    public static IEndpointRouteBuilder MapGrabacionVideo(this IEndpointRouteBuilder app)
    {
        app.MapPost("/video/grabacion/fragmento", Subir).RequireAuthorization().RequireRateLimiting("video-grabacion");
        return app;
    }

    private static async Task<IResult> Subir(HttpContext ctx, IdentidadActualService identidad, IOrigenSalaRepositorio origenRepo,
        AccesoSalaService acceso, ConsentimientoVideoService consentimiento, IConsentimientoVideoRepositorio repoConsentimiento,
        ITranscripcionSesionRepositorio repoTranscripcion, ITextoConsentimientoVideo textos,
        IOptionsMonitor<VideoOptions> opcionesMonitor, TimeProvider reloj)
    {
        ctx.Response.Headers.CacheControl = "no-store";
        var o = opcionesMonitor.CurrentValue.Transcripcion;

        if (!o.Habilitada)
            return Error(503, "grabacion-no-disponible", "La grabación de la sesión no está disponible por el momento.");
        if (!ctx.Request.HasFormContentType)
            return Error(400, "solicitud-invalida", "El pedido tiene que ser multipart/form-data.");

        // Se fija ANTES de leer el form: así un fragmento más grande que el tope lo corta Kestrel, no queda en memoria.
        var limite = ctx.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (limite is { IsReadOnly: false }) limite.MaxRequestBodySize = o.MaxBytesFragmento + 64 * 1024;

        IFormCollection form;
        try { form = await ctx.Request.ReadFormAsync(); }
        catch (BadHttpRequestException)
        {
            return Error(400, "fragmento-muy-grande", "El fragmento de audio supera el tamaño máximo permitido.");
        }

        var origen = TextoNormalizado.Normalizar(form["origen"]);
        if ((origen != OrigenSala.Turno && origen != OrigenSala.Admision)
            || !int.TryParse(form["id"], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            return Invalida();

        var pista = TextoNormalizado.Normalizar(form["pista"]);
        if (pista != PistaGrabacion.Profesional && pista != PistaGrabacion.Paciente) return Invalida();

        if (!int.TryParse(form["secuencia"], NumberStyles.None, CultureInfo.InvariantCulture, out var secuencia) || secuencia < 0)
            return Invalida();

        long? inicioMs = null;
        var inicioMsTexto = (string?)form["inicioMs"];
        if (!string.IsNullOrEmpty(inicioMsTexto))
        {
            if (!long.TryParse(inicioMsTexto, NumberStyles.None, CultureInfo.InvariantCulture, out var ms) || ms < 0) return Invalida();
            inicioMs = ms;
        }
        if (secuencia == 0 && inicioMs is null)
            return Error(400, "solicitud-invalida", "La secuencia 0 de cada pista tiene que incluir inicioMs.");
        if (secuencia != 0 && inicioMs is not null)
            return Error(400, "solicitud-invalida", "inicioMs solo va en la secuencia 0 de cada pista.");

        var archivo = form.Files.GetFile("archivo");
        if (archivo is null || archivo.Length <= 0) return Invalida();
        if (archivo.Length > o.MaxBytesFragmento)
            return Error(400, "fragmento-muy-grande", "El fragmento de audio supera el tamaño máximo permitido.");

        var yo = identidad.Resolver(ctx)!;
        var ses = SesionDeVideoReglas.Resolver(origenRepo, origen, id);
        if (ses is null || !SesionDeVideoReglas.AtiendeLaSesion(yo, ses)) return NoEncontrada();

        var ventana = acceso.EvaluarVentanaDeSesion(origen, id);
        if (!ventana.Ok) return Rechazo(ventana);

        var (disponible, motivoNoDisponible) = consentimiento.Disponibilidad(ses.IdPaciente, textos.Actual(ConsentimientoVideoFila.Grabacion),
            ConsentimientoVideoFila.Grabacion);
        if (!disponible)
            return Error(409, motivoNoDisponible!, ConsentimientoVideoService.MensajeNoDisponible(motivoNoDisponible!, ConsentimientoVideoFila.Grabacion));
        var ultimoConsentimiento = repoConsentimiento.Ultimo(origen, id, ses.IdPaciente, ConsentimientoVideoFila.Grabacion);
        if (ultimoConsentimiento is not { EsAceptacionVigente: true })
            return Error(409, "sin-consentimiento", "El paciente no dio su consentimiento para grabar esta sesión.");

        var sesionTranscripcion = repoTranscripcion.ObtenerActiva(origen, id);
        if (sesionTranscripcion is null)
        {
            if (secuencia != 0)
                return Error(409, "sin-grabacion-activa", "No hay una grabación en curso para esta sesión: empezá por la secuencia 0.");
            sesionTranscripcion = repoTranscripcion.CrearOReusar(origen, id, ultimoConsentimiento.IdConsentimientoVideo,
                pista == PistaGrabacion.Profesional ? inicioMs : null,
                pista == PistaGrabacion.Paciente ? inicioMs : null);
        }

        if (reloj.GetUtcNow().UtcDateTime - sesionTranscripcion.CreadoUtc > TimeSpan.FromMinutes(Math.Max(1, o.MaxMinutosGrabacion)))
            return Error(409, "duracion-maxima", $"La grabación alcanzó el máximo de {o.MaxMinutosGrabacion} minutos.");

        if (secuencia == 0 && inicioMs is { } ms0)
            repoTranscripcion.ActualizarInicioPista(sesionTranscripcion.IdTranscripcion, pista, ms0);

        var carpeta = Path.Combine(o.CarpetaAudio, sesionTranscripcion.IdTranscripcion.ToString(CultureInfo.InvariantCulture));
        var (resultado, esperada) = await EstadoPista.Agregar(carpeta, pista, secuencia, archivo, o.MaxBytesPista);
        return resultado switch
        {
            EstadoPista.Resultado.TopeBytes => Error(409, "tope-bytes-pista", "Esta pista alcanzó el tamaño máximo de grabación."),
            EstadoPista.Resultado.Gap => Error(409, "secuencia-fuera-de-orden", $"Falta la secuencia {esperada} antes de esta.", esperada),
            _ => Results.Ok(new { idTranscripcion = sesionTranscripcion.IdTranscripcion, pista, secuencia, estado = sesionTranscripcion.Estado })
        };
    }

    private static IResult Rechazo(ResultadoAcceso r) => r.Estado switch
    {
        EstadoAcceso.NoExiste or EstadoAcceso.SinAcceso => NoEncontrada(),
        EstadoAcceso.AunNoAbre => Results.Json(new { mensaje = r.Mensaje, codigo = r.Codigo, abreUtc = r.Desde?.UtcDateTime }, statusCode: 409),
        EstadoAcceso.YaTermino => Results.Json(new { mensaje = r.Mensaje, codigo = r.Codigo, cerroUtc = r.Hasta?.UtcDateTime }, statusCode: 409),
        _ => Error(409, r.Codigo, r.Mensaje)
    };

    private static IResult NoEncontrada() => Error(404, AccesoSalaService.CodigoNoEncontrada, AccesoSalaService.MensajeNoEncontrada);

    private static IResult Invalida() =>
        Error(400, "solicitud-invalida", "Indicá origen (turno o admision), id, pista (profesional o paciente), secuencia y el audio.");

    private static IResult Error(int status, string codigo, string mensaje, int? esperada = null) =>
        esperada is { } e
            ? Results.Json(new { mensaje, codigo, secuenciaEsperada = e }, statusCode: status)
            : Results.Json(new { mensaje, codigo }, statusCode: status);

    // Un fragmento por vez, por pista de cada grabación: evita que dos subidas de la misma pista se pisen al leer y
    // escribir el archivo de estado. Alcanza con esto porque el despliegue es una sola instancia (misma base que el
    // resto de los limitadores de este archivo de video, que también son en memoria).
    private static class EstadoPista
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> Candados = new();

        public enum Resultado { Nuevo, YaEstaba, Gap, TopeBytes }

        public static async Task<(Resultado Resultado, int Esperada)> Agregar(string carpeta, string pista, int secuencia,
            IFormFile archivo, long maxBytesPista)
        {
            var candado = Candados.GetOrAdd(carpeta + "|" + pista, _ => new SemaphoreSlim(1, 1));
            await candado.WaitAsync();
            try
            {
                Directory.CreateDirectory(carpeta);
                RestringirPermisos(carpeta, esCarpeta: true);
                var rutaEstado = Path.Combine(carpeta, pista + ".estado");
                var (ultimaSecuencia, bytesTotales) = LeerEstado(rutaEstado);
                var esperada = ultimaSecuencia + 1;

                if (secuencia < esperada) return (Resultado.YaEstaba, esperada);
                if (secuencia > esperada) return (Resultado.Gap, esperada);
                if (bytesTotales + archivo.Length > maxBytesPista) return (Resultado.TopeBytes, esperada);

                var rutaAudio = Path.Combine(carpeta, pista + ".webm");
                await using (var destino = new FileStream(rutaAudio, FileMode.Append, FileAccess.Write))
                await using (var origenStream = archivo.OpenReadStream())
                    await origenStream.CopyToAsync(destino);
                RestringirPermisos(rutaAudio, esCarpeta: false);

                bytesTotales += archivo.Length;
                await File.WriteAllTextAsync(rutaEstado, secuencia.ToString(CultureInfo.InvariantCulture) + ":" + bytesTotales.ToString(CultureInfo.InvariantCulture));
                RestringirPermisos(rutaEstado, esCarpeta: false);
                return (Resultado.Nuevo, secuencia);
            }
            finally { candado.Release(); }
        }

        private static (int UltimaSecuencia, long BytesTotales) LeerEstado(string ruta)
        {
            if (!File.Exists(ruta)) return (-1, 0);
            var partes = File.ReadAllText(ruta).Split(':');
            return partes.Length == 2 && int.TryParse(partes[0], NumberStyles.None, CultureInfo.InvariantCulture, out var s)
                && long.TryParse(partes[1], NumberStyles.None, CultureInfo.InvariantCulture, out var b) ? (s, b) : (-1, 0);
        }

        // Mejor esfuerzo: si falla (sistema de archivos sin soporte, permisos del proceso), el audio ya quedó
        // guardado igual; no tiene sentido tirar abajo la subida por esto.
        private static void RestringirPermisos(string ruta, bool esCarpeta)
        {
            if (OperatingSystem.IsWindows()) return;
            try
            {
                var modo = esCarpeta
                    ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    : UnixFileMode.UserRead | UnixFileMode.UserWrite;
                File.SetUnixFileMode(ruta, modo);
            }
            catch (Exception) { /* no puede tapar que el audio (o la carpeta) ya se guardó */ }
        }
    }
}
