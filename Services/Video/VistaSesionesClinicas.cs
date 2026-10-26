using System.Text.Json;

// Cómo se le muestra una sesión clínica a quien la pide (GET /pacientes/{id}/sesiones-clinicas y
// GET /profesionales/{id}/sesiones-clinicas).
//
//  · El Configurador NO lee emociones: para ese rol la API no devuelve HistorialEmociones (ni nada que diga qué
//    contenía). Es una decisión de producto del PR 6: el panel del Configurador nunca las mostró, pero la API se las
//    mandaba igual.
//  · Para el profesional se descifra acá, así que las pantallas no cambian: siguen recibiendo historialEmociones como un
//    JSON { "Neutral": 60, "Felicidad": 10, ... }. Lo guardado antes del cifrado (JSON en claro) se devuelve tal cual.
//  · Si lo guardado no se puede descifrar (falta la clave, o está alterado) se devuelve null y queda un aviso en el log,
//    sin el contenido. Nunca se devuelve el texto cifrado.
public record SesionClinicaVista(int IdSesion, int? IdTurno, int IdPaciente, int IdProfesional, DateTime FechaHora,
    string? Transcripcion, string? NotasEvolucion, string? HistorialEmociones, string? TipoCierre, EmocionesInfo? Emociones, int? IdEvolucion = null);

/// <summary>Datos de la lectura de emociones de una sesión: ventana, muestras y, si se anularon, cuándo y por qué.</summary>
public record EmocionesInfo(int? Muestras, DateTime? DesdeUtc, DateTime? HastaUtc, string? TextoVersion,
    DateTime? AnuladasUtc, string? AnuladasMotivo);

public class VistaSesionesClinicas
{
    private readonly IEmocionesCifrado _cifrado;
    private readonly ILogger<VistaSesionesClinicas> _log;

    public VistaSesionesClinicas(IEmocionesCifrado cifrado, ILogger<VistaSesionesClinicas> log)
    {
        _cifrado = cifrado; _log = log;
    }

    public List<SesionClinicaVista> Armar(IEnumerable<SesionClinica> sesiones, bool puedeVerEmociones) =>
        sesiones.Select(s => Armar(s, puedeVerEmociones)).ToList();

    public SesionClinicaVista Armar(SesionClinica s, bool puedeVerEmociones)
    {
        string? historial = null;
        EmocionesInfo? info = null;
        if (puedeVerEmociones) (historial, info) = Leer(s);

        return new SesionClinicaVista(s.IdSesion, s.IdTurno, s.IdPaciente, s.IdProfesional, s.FechaHora,
            s.Transcripcion, s.NotasEvolucion, historial, s.TipoCierre, info, s.IdEvolucion);
    }

    private (string? Json, EmocionesInfo? Info) Leer(SesionClinica s)
    {
        var anuladas = s.EmocionesAnuladasUtc is null
            ? null
            : new EmocionesInfo(null, null, null, null, s.EmocionesAnuladasUtc, s.EmocionesAnuladasMotivo);

        var guardado = s.HistorialEmociones;
        if (string.IsNullOrEmpty(guardado)) return (null, anuladas);

        string plano;
        if (EmocionesCifradoService.EstaCifrado(guardado))
        {
            var r = _cifrado.Descifrar(guardado, EmocionesCifradoService.DatosAsociadosDeSesion(s.IdSesion), out var texto);
            if (r != ResultadoDescifrado.Ok)
            {
                _log.LogWarning("Video: no se pudieron descifrar las emociones de una sesión clínica ({Motivo}).", r);
                return (null, null);
            }
            plano = texto!;
        }
        else plano = guardado;   // de antes del cifrado: JSON en claro, tal cual

        try
        {
            using var doc = JsonDocument.Parse(plano);
            var raiz = doc.RootElement;
            if (raiz.ValueKind == JsonValueKind.Object && raiz.TryGetProperty("promedios", out var promedios)
                && promedios.ValueKind == JsonValueKind.Object)
            {
                // Formato del PR 6: claves en inglés + ventana y muestras. Se devuelve con las etiquetas de siempre.
                var conEtiquetas = new Dictionary<string, int>();
                foreach (var (clave, etiqueta) in EmocionesCatalogo.Etiquetas)
                    if (promedios.TryGetProperty(clave, out var v) && v.TryGetInt32(out var n)) conEtiquetas[etiqueta] = n;
                var info = new EmocionesInfo(
                    raiz.TryGetProperty("muestras", out var m) && m.TryGetInt32(out var nm) ? nm : null,
                    LeerFecha(raiz, "desdeUtc"), LeerFecha(raiz, "hastaUtc"),
                    raiz.TryGetProperty("textoVersion", out var tv) ? tv.GetString() : null, null, null);
                return (JsonSerializer.Serialize(conEtiquetas), info);
            }
        }
        catch (JsonException) { /* no es JSON: se devuelve tal cual, como siempre */ }
        return (plano, null);
    }

    private static DateTime? LeerFecha(JsonElement raiz, string nombre) =>
        raiz.TryGetProperty(nombre, out var p) && p.TryGetDateTime(out var d) ? d.ToUniversalTime() : null;
}
