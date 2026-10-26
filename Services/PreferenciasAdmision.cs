using System.Text.Json;

// Las dos preferencias nuevas de «Sobre mí»: en qué horario le queda bien al paciente y qué profesionales prefiere. Por ahora SOLO se guardan y se muestran
// (a la recepcionista, para que elija); el cruce con las agendas de los profesionales es un trabajo siguiente. Se guardan estructuradas, en JSON, para que ese
// cruce se pueda hacer sin interpretar texto libre:
//   HorarioPreferido        {"dias":["lunes","miercoles"],"franja":"tarde"}   (franja: manana 08–12, tarde 12–18, noche 18–22, cualquiera)
//   ProfesionalesPreferidos {"cualquiera":false,"ids":[3,5]}                  («me da igual» = cualquiera true y sin ids)
public class HorarioPreferidoAdmision
{
    public List<string>? Dias { get; set; }
    public string? Franja { get; set; }
}

public class ProfesionalesPreferidosAdmision
{
    public bool Cualquiera { get; set; }
    public List<int>? Ids { get; set; }
}

public static class PreferenciasAdmision
{
    public static readonly string[] Dias = { "lunes", "martes", "miercoles", "jueves", "viernes", "sabado", "domingo" };
    public static readonly string[] Franjas = { "manana", "tarde", "noche", "cualquiera" };
    public const int MaxProfesionales = 10;

    /// <summary>Franja → (desde, hasta) en horas, para el cruce futuro con las agendas. «cualquiera» abarca todo el día.</summary>
    public static (int Desde, int Hasta) Horas(string franja) => franja switch
    {
        "manana" => (8, 12), "tarde" => (12, 18), "noche" => (18, 22), _ => (0, 24)
    };

    /// <summary>null si son válidas (o no se indicaron); si no, el mensaje claro para mostrar. «idsActivos» son los profesionales que hoy se pueden elegir.</summary>
    public static string? Validar(HorarioPreferidoAdmision? horario, ProfesionalesPreferidosAdmision? profesionales, IReadOnlySet<int> idsActivos)
    {
        if (horario is not null)
        {
            var dias = horario.Dias ?? new();
            if (dias.Any(d => !Dias.Contains(d)))
                return "Elegí los días de la semana de la lista (lunes a domingo).";
            if (dias.Distinct().Count() != dias.Count)
                return "Hay un día repetido en tus horarios preferidos.";
            if (string.IsNullOrWhiteSpace(horario.Franja) || !Franjas.Contains(horario.Franja))
                return "Elegí una franja horaria: mañana, tarde, noche o cualquiera.";
            if (dias.Count == 0)
                return "Elegí al menos un día de la semana para tu horario preferido.";
        }
        if (profesionales is not null)
        {
            var ids = profesionales.Ids ?? new();
            if (profesionales.Cualquiera && ids.Count > 0)
                return "Si elegís «me da igual» no marques profesionales puntuales.";
            if (!profesionales.Cualquiera && ids.Count == 0)
                return "Elegí uno o varios profesionales, o marcá «me da igual».";
            if (ids.Count > MaxProfesionales)
                return $"Podés elegir hasta {MaxProfesionales} profesionales.";
            if (ids.Distinct().Count() != ids.Count)
                return "Hay un profesional repetido en tu lista.";
            if (ids.Any(i => !idsActivos.Contains(i)))
                return "Alguno de los profesionales que elegiste ya no está disponible. Actualizá la lista y volvé a elegir.";
        }
        return null;
    }

    public static string? Serializar(HorarioPreferidoAdmision? h) =>
        h is null ? null : JsonSerializer.Serialize(new { dias = h.Dias ?? new(), franja = h.Franja });

    public static string? Serializar(ProfesionalesPreferidosAdmision? p) =>
        p is null ? null : JsonSerializer.Serialize(new { cualquiera = p.Cualquiera, ids = p.Ids ?? new() });

    /// <summary>Lo guardado, listo para devolver por la API (null si no hay o si el texto no se puede leer).</summary>
    public static JsonElement? Leer(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonDocument.Parse(json).RootElement.Clone(); }
        catch (JsonException) { return null; }
    }

    /// <summary>Los profesionales activos que el paciente puede elegir: los mismos que la recepcionista puede asignar (con horarios de atención).</summary>
    public static IReadOnlyList<(int IdProfesional, string Nombre, string? Especialidad)> ProfesionalesElegibles()
    {
        var lista = new List<(int, string, string?)>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
            SELECT p.IdProfesional, LTRIM(RTRIM(p.Nombre + ISNULL(' ' + p.Apellido, ''))) AS Nombre, e.Nombre AS Especialidad
            FROM Profesional p
            LEFT JOIN Especialidad e ON e.IdEspecialidad = p.IdEspecialidad
            WHERE p.Estado = 'Activo' AND ISNULL(p.Matricula, N'') <> N'ADM-001'
            ORDER BY p.Nombre, p.Apellido", con);
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(((int)r["IdProfesional"], (string)r["Nombre"], r["Especialidad"] as string));
        return lista;
    }
}
