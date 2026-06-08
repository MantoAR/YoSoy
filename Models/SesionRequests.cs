using System.Text.Json;

public record CanjearLinkRequest(string Token);
public record ProlongarRequest(int Minutos);
public record FinalizarRequest(string? Motivo);
public record DuracionTurnoRequest(int Minutos);

public class SesionEmocionesRequest
{
    public int? IdProfesional { get; set; }
    public string? Sala { get; set; }
    public int Duracion { get; set; }
    public int? Muestras { get; set; }
    public JsonElement Resumen { get; set; }
    public JsonElement Historial { get; set; }
}
