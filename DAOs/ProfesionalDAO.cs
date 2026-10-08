using Microsoft.Data.SqlClient;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

public class ProfesionalDAO
{
    private const string SELECT_COLUMNAS =
        "IdProfesional, Nombre, Apellido, SegundoNombre, SegundoApellido, Dni, Sexo, FechaNacimiento, Matricula, IdEspecialidad, Provincia, OrigenContacto, Estado, Mail, Telefono, FechaSolicitud, Slug, LinkPublicoActivo";

    public void Insertar(Profesional p)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "INSERT INTO Profesional (Nombre, Apellido, SegundoNombre, SegundoApellido, Dni, Sexo, FechaNacimiento, Matricula, IdEspecialidad, Provincia, OrigenContacto, Estado, Mail, Telefono, FechaSolicitud) VALUES (@Nombre, @Apellido, @SegundoNombre, @SegundoApellido, @Dni, @Sexo, @FechaNacimiento, @Matricula, @IdEspecialidad, @Provincia, @OrigenContacto, @Estado, @Mail, @Telefono, @FechaSolicitud)", con);
        cmd.Parameters.AddWithValue("@Nombre",          p.Nombre);
        cmd.Parameters.AddWithValue("@Apellido",        (object?)p.Apellido        ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SegundoNombre",   (object?)p.SegundoNombre   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SegundoApellido", (object?)p.SegundoApellido ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Dni",             (object?)p.Dni             ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Sexo",            (object?)p.Sexo            ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FechaNacimiento", (object?)p.FechaNacimiento ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Matricula",       p.Matricula);
        cmd.Parameters.AddWithValue("@IdEspecialidad",  p.IdEspecialidad);
        cmd.Parameters.AddWithValue("@Provincia",       (object?)p.Provincia       ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@OrigenContacto",  (object?)p.OrigenContacto  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Estado",          p.Estado);
        cmd.Parameters.AddWithValue("@Mail",            (object?)p.Mail            ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Telefono",        (object?)p.Telefono        ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FechaSolicitud",  (object?)p.FechaSolicitud  ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Modificar(Profesional p)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Profesional SET Nombre = @Nombre, Apellido = @Apellido, SegundoNombre = @SegundoNombre, SegundoApellido = @SegundoApellido, Dni = @Dni, Sexo = @Sexo, FechaNacimiento = @FechaNacimiento, Matricula = @Matricula, IdEspecialidad = @IdEspecialidad, Provincia = @Provincia, OrigenContacto = @OrigenContacto, Estado = @Estado, Mail = @Mail, Telefono = @Telefono, FechaSolicitud = @FechaSolicitud WHERE IdProfesional = @Id", con);
        cmd.Parameters.AddWithValue("@Nombre",          p.Nombre);
        cmd.Parameters.AddWithValue("@Apellido",        (object?)p.Apellido        ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SegundoNombre",   (object?)p.SegundoNombre   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SegundoApellido", (object?)p.SegundoApellido ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Dni",             (object?)p.Dni             ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Sexo",            (object?)p.Sexo            ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FechaNacimiento", (object?)p.FechaNacimiento ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Matricula",       p.Matricula);
        cmd.Parameters.AddWithValue("@IdEspecialidad",  p.IdEspecialidad);
        cmd.Parameters.AddWithValue("@Provincia",       (object?)p.Provincia       ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@OrigenContacto",  (object?)p.OrigenContacto  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Estado",          p.Estado);
        cmd.Parameters.AddWithValue("@Mail",            (object?)p.Mail            ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Telefono",        (object?)p.Telefono        ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FechaSolicitud",  (object?)p.FechaSolicitud  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Id",              p.IdProfesional);
        cmd.ExecuteNonQuery();
    }

    public void Baja(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Profesional SET Estado = 'Inactivo' WHERE IdProfesional = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.ExecuteNonQuery();
    }

    public List<Profesional> ObtenerTodos()
    {
        var lista = new List<Profesional>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            $"SELECT {SELECT_COLUMNAS} FROM Profesional", con);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    public Profesional ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            $"SELECT {SELECT_COLUMNAS} FROM Profesional WHERE IdProfesional = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    public List<Profesional> ObtenerPorEspecialidad(int idEspecialidad)
    {
        var lista = new List<Profesional>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            $"SELECT {SELECT_COLUMNAS} FROM Profesional WHERE IdEspecialidad = @IdEsp", con);
        cmd.Parameters.AddWithValue("@IdEsp", idEspecialidad);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lista.Add(Mapear(reader));
        return lista;
    }

    private Profesional Mapear(SqlDataReader r) => new Profesional
    {
        IdProfesional   = (int)r["IdProfesional"],
        Nombre          = (string)r["Nombre"],
        Apellido        = r["Apellido"]        == DBNull.Value ? null : (string)r["Apellido"],
        SegundoNombre   = r["SegundoNombre"]   == DBNull.Value ? null : (string)r["SegundoNombre"],
        SegundoApellido = r["SegundoApellido"] == DBNull.Value ? null : (string)r["SegundoApellido"],
        Dni             = r["Dni"]             == DBNull.Value ? null : (string)r["Dni"],
        Sexo            = r["Sexo"]            == DBNull.Value ? null : (string)r["Sexo"],
        FechaNacimiento = r["FechaNacimiento"] == DBNull.Value ? null : (DateTime?)r["FechaNacimiento"],
        Matricula       = (string)r["Matricula"],
        IdEspecialidad  = (int)r["IdEspecialidad"],
        Provincia       = r["Provincia"]       == DBNull.Value ? null : (string)r["Provincia"],
        OrigenContacto  = r["OrigenContacto"]  == DBNull.Value ? null : (string)r["OrigenContacto"],
        Estado          = r["Estado"]          == DBNull.Value ? null : (string)r["Estado"],
        Mail            = r["Mail"]            == DBNull.Value ? null : (string)r["Mail"],
        Telefono        = r["Telefono"]        == DBNull.Value ? null : (string)r["Telefono"],
        FechaSolicitud  = r["FechaSolicitud"]  == DBNull.Value ? null : (DateTime?)r["FechaSolicitud"],
        Slug              = r["Slug"]              == DBNull.Value ? null : (string)r["Slug"],
        LinkPublicoActivo = (bool)r["LinkPublicoActivo"]
    };

    /// <summary>
    /// Prende o apaga el link público de reserva. Si se prende y todavía no tiene slug, genera
    /// uno (nombre normalizado + sufijo aleatorio, no adivinable en secuencia) y lo guarda junto
    /// con el flag; si ya tenía uno, lo conserva — apagar y prender de nuevo no cambia el link.
    /// Columna aparte del UPDATE genérico de Modificar() a propósito (ver Models/Profesional.cs).
    /// </summary>
    public Profesional ActivarLinkPublico(int id, bool activo)
    {
        var p = ObtenerPorId(id) ?? throw new InvalidOperationException($"No existe el profesional {id}.");
        if (activo && string.IsNullOrEmpty(p.Slug))
            p.Slug = GenerarSlugUnico(p.Nombre, p.Apellido);
        p.LinkPublicoActivo = activo;

        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE Profesional SET Slug = @Slug, LinkPublicoActivo = @Activo WHERE IdProfesional = @Id", con);
        cmd.Parameters.AddWithValue("@Slug", (object?)p.Slug ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Activo", p.LinkPublicoActivo);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.ExecuteNonQuery();
        return p;
    }

    /// <summary>
    /// Para la página pública: null tanto si el slug no existe como si existe pero el link está
    /// apagado o el profesional no está Activo — a propósito, la respuesta pública tiene que ser
    /// idéntica en los dos casos (no revelar qué profesionales existen).
    /// </summary>
    public Profesional? ObtenerPorSlugActivo(string slug)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            $"SELECT {SELECT_COLUMNAS} FROM Profesional WHERE Slug = @Slug AND LinkPublicoActivo = 1 AND Estado = 'Activo'", con);
        cmd.Parameters.AddWithValue("@Slug", slug);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Mapear(reader) : null;
    }

    private string GenerarSlugUnico(string nombre, string? apellido)
    {
        var baseTexto = QuitarAcentos($"{nombre}-{apellido}".Trim('-')).ToLowerInvariant();
        baseTexto = new string(baseTexto.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (baseTexto.Contains("--")) baseTexto = baseTexto.Replace("--", "-");
        baseTexto = baseTexto.Trim('-');
        if (baseTexto.Length > 40) baseTexto = baseTexto[..40].TrimEnd('-');
        if (string.IsNullOrEmpty(baseTexto)) baseTexto = "profesional";

        for (var intento = 0; intento < 10; intento++)
        {
            var sufijo = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
            var candidato = $"{baseTexto}-{sufijo}";
            using var con = Conexion.ObtenerConexion();
            con.Open();
            var cmd = new SqlCommand("SELECT COUNT(*) FROM Profesional WHERE Slug = @Slug", con);
            cmd.Parameters.AddWithValue("@Slug", candidato);
            if ((int)cmd.ExecuteScalar() == 0) return candidato;
        }
        throw new InvalidOperationException("No se pudo generar un slug único.");
    }

    private static string QuitarAcentos(string texto)
    {
        var normalizado = texto.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalizado)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }
}
