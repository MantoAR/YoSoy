using Microsoft.Data.SqlClient;
using System.Collections.Generic;
using System.Data;

public interface IConversacionDAO
{
    Conversacion? ObtenerPorId(int id);
    Conversacion? ObtenerPorTelefono(string telefono, string canal);
    Conversacion ObtenerOCrear(string telefono, string canal);
    void Guardar(Conversacion c);
    List<ConversacionResumen> ListarResumen(string? estado, int? desdeIdMensaje = null);
    void MarcarLeida(int idConversacion, int hastaIdMensaje);
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

    // Para la bandeja: urgentes siempre arriba; el resto por última actividad.
    // desdeIdMensaje: solo las conversaciones con mensajes posteriores a ese id (el polling
    // de la bandeja pide solo lo que cambió). NoLeidos cuenta los mensajes del paciente
    // posteriores a la marca de lectura del equipo (migrations/add_lectura_conversacion.sql).
    public List<ConversacionResumen> ListarResumen(string? estado, int? desdeIdMensaje = null)
    {
        var lista = new List<ConversacionResumen>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT c.IdConversacion, c.Telefono, c.IdPaciente, p.Nombre AS NombrePaciente,
                   c.Canal, c.Estado, c.PasoActual, c.FechaUltimaActividad,
                   um.IdMensaje AS IdUltimoMensaje, um.Texto AS UltimoMensaje,
                   um.FechaHora AS FechaUltimoMensaje, um.Origen AS OrigenUltimoMensaje,
                   (SELECT COUNT(*) FROM MensajeChat nl
                    WHERE nl.IdConversacion = c.IdConversacion AND nl.Direccion = 'Entrante'
                      AND nl.IdMensaje > ISNULL(c.IdUltimoMensajeLeido, 0)) AS NoLeidos
            FROM Conversacion c
            LEFT JOIN Paciente p ON p.IdPaciente = c.IdPaciente
            OUTER APPLY (SELECT TOP 1 m.IdMensaje, m.Texto, m.FechaHora, m.Origen FROM MensajeChat m
                         WHERE m.IdConversacion = c.IdConversacion ORDER BY m.IdMensaje DESC) um
            WHERE (@Estado IS NULL OR c.Estado = @Estado)
              AND (@Desde IS NULL OR EXISTS (SELECT 1 FROM MensajeChat d
                                             WHERE d.IdConversacion = c.IdConversacion AND d.IdMensaje > @Desde))
            ORDER BY CASE WHEN c.Estado = 'Urgente' THEN 0 ELSE 1 END,
                     c.FechaUltimaActividad DESC", con);
        cmd.Parameters.Add("@Estado", SqlDbType.NVarChar, 20).Value = (object?)estado ?? DBNull.Value;
        cmd.Parameters.Add("@Desde", SqlDbType.Int).Value = (object?)desdeIdMensaje ?? DBNull.Value;
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
                UltimoMensaje        = r["UltimoMensaje"]  == DBNull.Value ? null : (string)r["UltimoMensaje"],
                FechaUltimoMensaje   = r["FechaUltimoMensaje"] == DBNull.Value ? null : (DateTime)r["FechaUltimoMensaje"],
                IdUltimoMensaje      = r["IdUltimoMensaje"] == DBNull.Value ? null : (int)r["IdUltimoMensaje"],
                OrigenUltimoMensaje  = r["OrigenUltimoMensaje"] == DBNull.Value ? null : (string)r["OrigenUltimoMensaje"],
                NoLeidos             = (int)r["NoLeidos"]
            });
        return lista;
    }

    // Marca la conversación como leída por el equipo hasta 'hastaIdMensaje' (inclusive).
    // Nunca retrocede la marca y nunca la pone más allá del último mensaje existente.
    public void MarcarLeida(int idConversacion, int hastaIdMensaje)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            UPDATE Conversacion
            SET IdUltimoMensajeLeido = x.Hasta
            FROM Conversacion
            CROSS APPLY (SELECT MAX(m.IdMensaje) AS Hasta FROM MensajeChat m
                         WHERE m.IdConversacion = @Id AND m.IdMensaje <= @Hasta) x
            WHERE IdConversacion = @Id AND x.Hasta IS NOT NULL
              AND (IdUltimoMensajeLeido IS NULL OR IdUltimoMensajeLeido < x.Hasta)", con);
        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = idConversacion;
        cmd.Parameters.Add("@Hasta", SqlDbType.Int).Value = hastaIdMensaje;
        cmd.ExecuteNonQuery();
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
