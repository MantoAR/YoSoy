using System.Text.Json;

// Pasos de la máquina de estados (Conversacion.PasoActual).
public static class PasoBot
{
    public const string Inicio                    = "Inicio";
    public const string IdentificarDni            = "IdentificarDni";
    public const string Menu                      = "Menu";
    public const string ElegirEspecialidad        = "ElegirEspecialidad";
    public const string ElegirProfesional         = "ElegirProfesional";
    public const string ElegirHorario             = "ElegirHorario";
    public const string ConfirmarTurno            = "ConfirmarTurno";
    public const string ElegirTurnoCancelar       = "ElegirTurnoCancelar";
    public const string ConfirmarCancelacion      = "ConfirmarCancelacion";
    public const string ConfirmarAvisoNoAsistencia = "ConfirmarAvisoNoAsistencia";
    public const string Faq                       = "Faq";
    public const string RespuestaRecordatorio     = "RespuestaRecordatorio";
}

public static class EstadoConversacion
{
    public const string Activa         = "Activa";
    public const string DerivadaHumano = "DerivadaHumano";
    public const string Urgente        = "Urgente";
    public const string Cerrada        = "Cerrada";
}

// Datos del flujo en curso, guardados como JSON en Conversacion.DatosContexto.
public class ContextoBot
{
    // Ids de lo que se le ofreció al paciente como lista numerada (1 = primer elemento).
    public List<int> Opciones { get; set; } = new();
    public List<string> NombresOpciones { get; set; } = new();
    public List<DateTime> Horarios { get; set; } = new();
    public int? IdProfesional { get; set; }
    public string? NombreProfesional { get; set; }
    public DateTime? HorarioElegido { get; set; }
    public int? IdTurno { get; set; }
    public int IntentosDni { get; set; }

    public static ContextoBot Leer(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ContextoBot();
        try { return JsonSerializer.Deserialize<ContextoBot>(json) ?? new ContextoBot(); }
        catch (JsonException) { return new ContextoBot(); }
    }

    public string Serializar() => JsonSerializer.Serialize(this);

    public void LimpiarFlujo()
    {
        Opciones.Clear();
        NombresOpciones.Clear();
        Horarios.Clear();
        IdProfesional     = null;
        NombreProfesional = null;
        HorarioElegido    = null;
        IdTurno           = null;
    }
}
