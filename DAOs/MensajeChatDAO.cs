using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public interface IMensajeChatDAO
{
    int Insertar(MensajeChat m);
    void ActualizarEstadoEnvio(int idMensaje, string estado);
    List<MensajeChat> ObtenerPorConversacion(int idConversacion, int desdeIdMensaje);
}

public class MensajeChatDAO : IMensajeChatDAO
{
    public int Insertar(MensajeChat m)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            INSERT INTO MensajeChat (IdConversacion, Direccion, Origen, Texto, FechaHora, EstadoEnvio)
            VALUES (@IdConv, @Direccion, @Origen, @Texto, @FechaHora, @EstadoEnvio);
            SELECT CAST(SCOPE_IDENTITY() AS INT);", con);
        cmd.Parameters.AddWithValue("@IdConv",      m.IdConversacion);
        cmd.Parameters.AddWithValue("@Direccion",   m.Direccion);
        cmd.Parameters.AddWithValue("@Origen",      m.Origen);
        cmd.Parameters.AddWithValue("@Texto",       m.Texto);
        cmd.Parameters.AddWithValue("@FechaHora",   m.FechaHora);
        cmd.Parameters.AddWithValue("@EstadoEnvio", m.EstadoEnvio);
        m.IdMensaje = (int)cmd.ExecuteScalar();
        return m.IdMensaje;
    }

    public void ActualizarEstadoEnvio(int idMensaje, string estado)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(
            "UPDATE MensajeChat SET EstadoEnvio = @Estado WHERE IdMensaje = @Id", con);
        cmd.Parameters.AddWithValue("@Estado", estado);
        cmd.Parameters.AddWithValue("@Id",     idMensaje);
        cmd.ExecuteNonQuery();
    }

    // desdeIdMensaje permite que el polling de la pantalla traiga solo lo nuevo.
    public List<MensajeChat> ObtenerPorConversacion(int idConversacion, int desdeIdMensaje)
    {
        var lista = new List<MensajeChat>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT IdMensaje, IdConversacion, Direccion, Origen, Texto, FechaHora, EstadoEnvio
            FROM MensajeChat
            WHERE IdConversacion = @IdConv AND IdMensaje > @Desde
            ORDER BY IdMensaje", con);
        cmd.Parameters.AddWithValue("@IdConv", idConversacion);
        cmd.Parameters.AddWithValue("@Desde",  desdeIdMensaje);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            lista.Add(new MensajeChat
            {
                IdMensaje      = (int)r["IdMensaje"],
                IdConversacion = (int)r["IdConversacion"],
                Direccion      = (string)r["Direccion"],
                Origen         = (string)r["Origen"],
                Texto          = (string)r["Texto"],
                FechaHora      = (DateTime)r["FechaHora"],
                EstadoEnvio    = (string)r["EstadoEnvio"]
            });
        return lista;
    }
}
