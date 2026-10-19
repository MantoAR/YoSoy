using Microsoft.Data.SqlClient;
using System.Collections.Generic;

public interface IMensajeChatDAO
{
    int Insertar(MensajeChat m);
    void ActualizarEstadoEnvio(int idMensaje, string estado);
    List<MensajeChat> ObtenerPorConversacion(int idConversacion, int desdeIdMensaje);
    List<MensajeChat> ObtenerAnteriores(int idConversacion, int? antesDeIdMensaje, int cantidad);

    /// <summary>¿La persona escribió alguna vez en esta conversación? (hay al menos un mensaje entrante)</summary>
    bool TieneMensajeEntrante(int idConversacion);

    /// <summary>
    /// Guarda un mensaje que ENTRA con lo que trae whatsapp-service (id y tipo, de la migración 17). Devuelve el id del mensaje, o null si ya estaba guardado (el mismo id en
    /// la misma conversación). Si las columnas nuevas no se pueden usar (modo Asistente, o la base sin la migración) es un <see cref="Insertar"/> común. Quien implementa
    /// solo <see cref="Insertar"/> (los dobles de prueba) cae en ese.
    /// </summary>
    int? InsertarEntrante(MensajeChat m) => Insertar(m);
}

public class MensajeChatDAO : IMensajeChatDAO
{
    private const string Columnas = "IdMensaje, IdConversacion, Direccion, Origen, Texto, FechaHora, EstadoEnvio";

    private readonly IColumnasChatPersonal? _columnas;

    // Sin la puerta (new MensajeChatDAO(), como en algunas pruebas) NUNCA se tocan las columnas de la migración 17: es el comportamiento de siempre.
    public MensajeChatDAO(IColumnasChatPersonal? columnas = null) => _columnas = columnas;

    // Se pregunta en cada llamada: el modo puede cambiar sin reiniciar la API.
    private bool UsaTipo => _columnas?.Disponibles == true;

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

    public int? InsertarEntrante(MensajeChat m)
    {
        if (!UsaTipo) return Insertar(m);

        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            INSERT INTO MensajeChat (IdConversacion, Direccion, Origen, Texto, FechaHora, EstadoEnvio, IdExterno, Tipo)
            VALUES (@IdConv, @Direccion, @Origen, @Texto, @FechaHora, @EstadoEnvio, @IdExterno, @Tipo);
            SELECT CAST(SCOPE_IDENTITY() AS INT);", con);
        cmd.Parameters.AddWithValue("@IdConv",      m.IdConversacion);
        cmd.Parameters.AddWithValue("@Direccion",   m.Direccion);
        cmd.Parameters.AddWithValue("@Origen",      m.Origen);
        cmd.Parameters.AddWithValue("@Texto",       m.Texto);
        cmd.Parameters.AddWithValue("@FechaHora",   m.FechaHora);
        cmd.Parameters.AddWithValue("@EstadoEnvio", m.EstadoEnvio);
        cmd.Parameters.AddWithValue("@IdExterno",   (object?)m.IdExterno ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Tipo",        m.Tipo ?? TiposDeMensaje.Texto);
        try
        {
            m.IdMensaje = (int)cmd.ExecuteScalar();
            return m.IdMensaje;
        }
        // El índice único (IdConversacion, IdExterno): el mismo mensaje de WhatsApp otra vez (un reintento de whatsapp-service). Lo atómico es la base: no se mira antes.
        catch (SqlException ex) when (ex.Number is 2601 or 2627 && ex.Message.Contains("UQ_MensajeChat_IdExterno", StringComparison.Ordinal))
        {
            return null;
        }
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

    public bool TieneMensajeEntrante(int idConversacion)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            SELECT CASE WHEN EXISTS (SELECT 1 FROM MensajeChat WHERE IdConversacion = @IdConv AND Direccion = 'Entrante') THEN 1 ELSE 0 END", con);
        cmd.Parameters.AddWithValue("@IdConv", idConversacion);
        return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
    }

    // desdeIdMensaje permite que el polling de la pantalla traiga solo lo nuevo.
    public List<MensajeChat> ObtenerPorConversacion(int idConversacion, int desdeIdMensaje)
    {
        var lista = new List<MensajeChat>();
        var conTipo = UsaTipo;
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand($@"
            SELECT {Columnas}{(conTipo ? ", Tipo" : "")}
            FROM MensajeChat
            WHERE IdConversacion = @IdConv AND IdMensaje > @Desde
            ORDER BY IdMensaje", con);
        cmd.Parameters.AddWithValue("@IdConv", idConversacion);
        cmd.Parameters.AddWithValue("@Desde",  desdeIdMensaje);
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(Mapear(r, conTipo));
        return lista;
    }

    // Historial hacia arriba: los 'cantidad' mensajes anteriores a 'antesDeIdMensaje'
    // (sin ese id, los últimos), en orden cronológico. Lo usa la bandeja para no traer
    // conversaciones largas enteras de una vez.
    public List<MensajeChat> ObtenerAnteriores(int idConversacion, int? antesDeIdMensaje, int cantidad)
    {
        var lista = new List<MensajeChat>();
        var conTipo = UsaTipo;
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand($@"
            SELECT * FROM (
                SELECT TOP (@Cantidad) {Columnas}{(conTipo ? ", Tipo" : "")}
                FROM MensajeChat
                WHERE IdConversacion = @IdConv AND (@Antes IS NULL OR IdMensaje < @Antes)
                ORDER BY IdMensaje DESC) x
            ORDER BY IdMensaje", con);
        cmd.Parameters.Add("@Cantidad", System.Data.SqlDbType.Int).Value = cantidad;
        cmd.Parameters.Add("@IdConv", System.Data.SqlDbType.Int).Value = idConversacion;
        cmd.Parameters.Add("@Antes", System.Data.SqlDbType.Int).Value = (object?)antesDeIdMensaje ?? DBNull.Value;
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(Mapear(r, conTipo));
        return lista;
    }

    private static MensajeChat Mapear(SqlDataReader r, bool conTipo) => new()
    {
        IdMensaje      = (int)r["IdMensaje"],
        IdConversacion = (int)r["IdConversacion"],
        Direccion      = (string)r["Direccion"],
        Origen         = (string)r["Origen"],
        Texto          = (string)r["Texto"],
        FechaHora      = (DateTime)r["FechaHora"],
        EstadoEnvio    = (string)r["EstadoEnvio"],
        Tipo           = conTipo ? (string)r["Tipo"] : null
    };
}
