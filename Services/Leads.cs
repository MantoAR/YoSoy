using System.Globalization;
using System.Text;

// Vista única, pantalla de Leads: un lead es CUALQUIER conversación (con un paciente identificado o con alguien sin identificar). Es una vista de solo
// lectura sobre las mismas conversaciones que la bandeja de Chats (GET /mensajeria/leads): no se crean ni se borran leads. Búsqueda, filtros y paginación
// del lado del servidor, con la MISMA regla de pertenencia que /conversaciones (VisibilidadConversacion). Cada fila trae solo lo necesario para listar:
// nunca el texto de los mensajes, ni el paso del bot, ni datos clínicos. Como /conversaciones, leer la lista no deja registro de auditoría; abrir una
// conversación sí (lo hace la bandeja de Chats al abrirla, igual que siempre).
public record LeadFila(int IdConversacion, string? Nombre, string? NombreContacto, string Telefono, string Tipo, string Canal, string Estado,
    IReadOnlyList<int> IdsEtiquetas, int NoLeidos, DateTime FechaUltimaActividad, string? TelefonoMostrar = null);

public record FiltroLeads(IReadOnlyList<string> Palabras, string Tipo, string? Estado, int? IdEtiqueta, int Pagina, int Cantidad);

public static class Leads
{
    public const int CantidadPorDefecto = 25;
    public const int CantidadMaxima = 100;
    public const int MaxPalabras = 5;
    public const int MaxLargoPalabra = 50;

    public static readonly string[] Tipos = { "todos", "paciente", "sin-identificar" };
    public static readonly string[] Estados = { "Activa", "DerivadaHumano", "Urgente", "Cerrada" };

    public static (FiltroLeads? Filtro, string? Error) Normalizar(string? q, string? tipo, string? estado, int? idEtiqueta, int? pagina, int? cantidad)
    {
        var t = string.IsNullOrWhiteSpace(tipo) ? "todos" : tipo.Trim().ToLowerInvariant();
        if (!Tipos.Contains(t)) return (null, "El tipo tiene que ser: " + string.Join(", ", Tipos) + ".");
        string? e = null;
        if (!string.IsNullOrWhiteSpace(estado) && !string.Equals(estado.Trim(), "todos", StringComparison.OrdinalIgnoreCase))
        {
            e = Estados.FirstOrDefault(x => string.Equals(x, estado.Trim(), StringComparison.OrdinalIgnoreCase));
            if (e is null) return (null, "El estado tiene que ser: todos, " + string.Join(", ", Estados) + ".");
        }
        if (idEtiqueta is < 1) return (null, "La etiqueta no es válida.");
        if (pagina is < 1) return (null, "La página empieza en 1.");
        if (cantidad is < 1 or > CantidadMaxima) return (null, $"La cantidad por página va de 1 a {CantidadMaxima}.");

        var palabras = (q ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(MaxPalabras).Select(p => p.Length > MaxLargoPalabra ? p[..MaxLargoPalabra] : p).ToList();
        return (new FiltroLeads(palabras, t, e, idEtiqueta, pagina ?? 1, cantidad ?? CantidadPorDefecto), null);
    }

    /// <summary>Minúsculas y sin tildes, para buscar "maria" y encontrar "María".</summary>
    public static string Simplificar(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return "";
        var d = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    private static string Digitos(string? texto) => new((texto ?? "").Where(char.IsDigit).ToArray());

    /// <summary>Filtra (ya con la pertenencia aplicada) y devuelve el total y SOLO la página pedida, en el orden que traen las conversaciones.</summary>
    public static (int Total, List<LeadFila> Filas) Filtrar(IEnumerable<ConversacionResumen> conversaciones, FiltroLeads f)
    {
        bool Coincide(ConversacionResumen c)
        {
            if (f.Tipo == "paciente" && c.IdPaciente is null) return false;
            if (f.Tipo == "sin-identificar" && c.IdPaciente is not null) return false;
            if (f.IdEtiqueta is int tag && !c.IdsEtiquetas.Contains(tag)) return false;
            var texto = Simplificar(c.NombrePaciente + " " + c.NombreContacto + " " + c.Telefono);
            var digitos = Digitos(c.Telefono);
            return f.Palabras.All(p =>
            {
                var s = Simplificar(p);
                var d = Digitos(p);
                return texto.Contains(s) || (d.Length >= 1 && p.All(ch => char.IsDigit(ch) || ch is '+' or '-' or '(' or ')') && digitos.Contains(d));
            });
        }

        var todas = conversaciones.Where(Coincide).ToList();
        var pagina = todas.Skip((f.Pagina - 1) * f.Cantidad).Take(f.Cantidad)
            .Select(c => new LeadFila(c.IdConversacion, c.NombrePaciente, c.NombreContacto, c.Telefono, c.IdPaciente is null ? "sin-identificar" : "paciente",
                c.Canal, c.Estado, c.IdsEtiquetas, c.NoLeidos, c.FechaUltimaActividad, TelefonoWhatsApp.Formatear(c.Telefono)))
            .ToList();
        return (todas.Count, pagina);
    }
}
