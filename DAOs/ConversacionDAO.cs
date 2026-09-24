using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public interface IConversacionDAO
{
    Conversacion? ObtenerPorId(int id);
    Conversacion? ObtenerPorTelefono(string telefono, string canal);
    Conversacion ObtenerOCrear(string telefono, string canal);
    void Guardar(Conversacion c);
    List<ConversacionResumen> ListarResumen(string? estado);
}

public class ConversacionDAO : IConversacionDAO
{
    private const string SELECT_BASE = @"
        SELECT IdConversacion, Telefono, IdPaciente, Canal, PasoActual, DatosContexto,
               Estado, FechaUltimaActividad, FechaCreacion
        FROM Conversacion";

    public Conversacion? ObtenerPorId(int id)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(SELECT_BASE + " WHERE IdConversacion = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Mapear(r) : null;
    }

    public Conversacion? ObtenerPorTelefono(string telefono, string canal)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(SELECT_BASE + " WHERE Telefono = @Tel AND Canal = @Canal", con);
        cmd.Parameters.AddWithValue("@Tel", telefono);
        cmd.Parameters.AddWithValue("@Canal", canal);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Mapear(r) : null;
    }

    // Si dos mensajes del mismo teléfono llegan a la vez, el índice único
    // (Telefono, Canal) hace fallar el segundo INSERT y se relee la fila.
    public Conversacion ObtenerOCrear(string telefono, string canal)
    {
        var existente = ObtenerPorTelefono(telefono, canal);
        if (existente != null) return existente;
        try
        {
            using var con = Conexion.ObtenerConexion();
            con.Open();
            var cmd = new SqlCommand(@"
                INSERT INTO Conversacion (Telefono, Canal) VALUES (@Tel, @Canal);", con);
            cmd.Parameters.AddWithValue("@Tel", telefono);
            cmd.Parameters.AddWithValue("@Canal", canal);
            cmd.ExecuteNonQuery();
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { }
        return ObtenerPorTelefono(telefono, canal)!;
    }

    public void Guardar(Conversacion c)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            UPDATE Conversacion
            SET IdPaciente = @IdPaciente, PasoActual = @Paso, DatosContexto = @Ctx,
                Estado = @Estado, FechaUltimaActividad = @FUA
            WHERE IdConversacion = @Id", con);
        cmd.Parameters.AddWithValue("@IdPaciente", (object?)c.IdPaciente    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Paso",       c.PasoActual);
        cmd.Parameters.AddWithValue("@Ctx",        (object?)c.DatosContexto ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Estado",     c.Estado);
        cmd.Parameters.AddWithValue("@FUA",        c.FechaUltimaActividad);
        cmd.Parameters.AddWithValue("@Id",         c.IdConversacion);
        cmd.ExecuteNonQuery();
    }

    // Para el modo administrador: urgentes primero, después derivadas, después el resto.
    public List<ConversacionResumen> ListarResumen(string? estado)
    {
        var lista = new List<ConversacionResumen>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT c.IdConversacion, c.Telefono, c.IdPaciente, p.Nombre AS NombrePaciente,
                   c.Canal, c.Estado, c.PasoActual, c.FechaUltimaActividad,
                   (SELECT TOP 1 m.Texto FROM MensajeChat m
                    WHERE m.IdConversacion = c.IdConversacion ORDER BY m.IdMensaje DESC) AS UltimoMensaje
            FROM Conversacion c
            LEFT JOIN Paciente p ON p.IdPaciente = c.IdPaciente" +
            (estado != null ? " WHERE c.Estado = @Estado" : "") + @"
            ORDER BY CASE c.Estado WHEN 'Urgente' THEN 0 WHEN 'DerivadaHumano' THEN 1 ELSE 2 END,
                     c.FechaUltimaActividad DESC", con);
        if (estado != null) cmd.Parameters.AddWithValue("@Estado", estado);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            lista.Add(new ConversacionResumen
            {
                IdConversacion       = (int)r["IdConversacion"],
                Telefono             = (string)r["Telefono"],
                IdPaciente           = r["IdPaciente"]     == DBNull.Value ? null : (int)r["IdPaciente"],
                NombrePaciente       = r["NombrePaciente"] == DBNull.Value ? null : (string)r["NombrePaciente"],
                Canal                = (string)r["Canal"],
                Estado               = (string)r["Estado"],
                PasoActual           = (string)r["PasoActual"],
                FechaUltimaActividad = (DateTime)r["FechaUltimaActividad"],
                UltimoMensaje        = r["UltimoMensaje"]  == DBNull.Value ? null : (string)r["UltimoMensaje"]
            });
        return lista;
    }

    private Conversacion Mapear(SqlDataReader r) => new Conversacion
    {
        IdConversacion       = (int)r["IdConversacion"],
        Telefono             = (string)r["Telefono"],
        IdPaciente           = r["IdPaciente"]    == DBNull.Value ? null : (int)r["IdPaciente"],
        Canal                = (string)r["Canal"],
        PasoActual           = (string)r["PasoActual"],
        DatosContexto        = r["DatosContexto"] == DBNull.Value ? null : (string)r["DatosContexto"],
        Estado               = (string)r["Estado"],
        FechaUltimaActividad = (DateTime)r["FechaUltimaActividad"],
        FechaCreacion        = (DateTime)r["FechaCreacion"]
    };
}
