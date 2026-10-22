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
    void AsignarProfesional(int idConversacion, int? idProfesional);
    bool TomarSiNoTieneDueno(int idConversacion, int idProfesional);

    /// <summary>
    /// Guarda el nombre del perfil de WhatsApp de quien escribe (migración 17), solo si cambió. Si la columna no se puede usar (modo Asistente, o la base sin la migración)
    /// no hace nada. Quien implementa solo lo de arriba (los dobles de prueba) no necesita implementarlo.
    /// </summary>
    void ActualizarNombreContacto(int idConversacion, string nombre) { }
}

public class ConversacionDAO : IConversacionDAO
{
    private readonly IColumnasChatPersonal? _columnas;
    private readonly IMensajeChatDAO _mensajes;
    private readonly VentanaDeConversacion _ventana;

    // Sin la puerta (new ConversacionDAO(), como en algunas pruebas) NUNCA se toca Conversacion.NombreContacto (migración 17): es el comportamiento de siempre.
    public ConversacionDAO(IColumnasChatPersonal? columnas = null, TimeProvider? reloj = null, IMensajeChatDAO? mensajes = null)
    {
        _columnas = columnas;
        _mensajes = mensajes ?? new MensajeChatDAO();
        _ventana = new VentanaDeConversacion(reloj ?? new RelojDeLaClinica(ZonaHorariaDeLaClinica.Elegir(null)));
    }

    // Se pregunta en cada llamada: el modo puede cambiar sin reiniciar la API.
    private bool UsaNombre => _columnas?.Disponibles == true;

    private static string SelectBase(bool conNombre) => $@"
        SELECT IdConversacion, Telefono, IdPaciente, Canal, PasoActual, DatosContexto,
               Estado, FechaUltimaActividad, FechaCreacion, IdProfesionalAsignado{(conNombre ? ", NombreContacto" : "")}
        FROM Conversacion";

    public Conversacion? ObtenerPorId(int id)
    {
        var conNombre = UsaNombre;
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(SelectBase(conNombre) + " WHERE IdConversacion = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Mapear(r, conNombre) : null;
    }

    public Conversacion? ObtenerPorTelefono(string telefono, string canal)
    {
        var conNombre = UsaNombre;
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(SelectBase(conNombre) + " WHERE Telefono = @Tel AND Canal = @Canal", con);
        cmd.Parameters.AddWithValue("@Tel", telefono);
        cmd.Parameters.AddWithValue("@Canal", canal);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Mapear(r, conNombre) : null;
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

    // Escribe la fila con la copia que tiene quien la leyó (el asistente, el modo Personal, los avisos, cerrar y reabrir), que puede ser vieja: una persona pudo vincular o desvincular
    // a mano en el medio (VinculoConversacionDAO). Por eso IdPaciente NO se escribe a ciegas: solo si quien guarda lo CAMBIÓ (el asistente que identifica a la persona), y aun así solo
    // sobre una conversación que sigue sin paciente (ISNULL): nunca pisa un vínculo hecho en el medio, y una copia vieja que no lo tocó no deshace una desvinculación.
    public void Guardar(Conversacion c)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            UPDATE Conversacion
            SET IdPaciente = CASE WHEN @IdPacienteCambio = 1 THEN ISNULL(IdPaciente, @IdPaciente) ELSE IdPaciente END,
                PasoActual = @Paso, DatosContexto = @Ctx,
                Estado = @Estado, FechaUltimaActividad = @FUA
            WHERE IdConversacion = @Id", con);
        cmd.Parameters.AddWithValue("@IdPaciente", (object?)c.IdPaciente    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IdPacienteCambio", c.IdPaciente != c.IdPacienteAlLeer);
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
    // El filtrado por pertenencia NO va acá: esto devuelve todo, VisibilidadConversacion lo
    // filtra después (así una sola consulta sirve para cualquier identidad que la llame).
    public List<ConversacionResumen> ListarResumen(string? estado, int? desdeIdMensaje = null)
    {
        var lista = new List<ConversacionResumen>();
        var conNombre = UsaNombre;
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand($@"
            SELECT c.IdConversacion, c.Telefono, c.IdPaciente, p.Nombre AS NombrePaciente,{(conNombre ? " c.NombreContacto," : "")}
                   c.Canal, c.Estado, c.PasoActual, c.FechaUltimaActividad,
                   um.IdMensaje AS IdUltimoMensaje, um.Texto AS UltimoMensaje,
                   um.FechaHora AS FechaUltimoMensaje, um.Origen AS OrigenUltimoMensaje,
                   (SELECT COUNT(*) FROM MensajeChat nl
                    WHERE nl.IdConversacion = c.IdConversacion AND nl.Direccion = 'Entrante'
                      AND nl.IdMensaje > ISNULL(c.IdUltimoMensajeLeido, 0)) AS NoLeidos,
                   c.IdProfesionalAsignado,
                   LTRIM(RTRIM(prof.Nombre + ' ' + ISNULL(prof.Apellido, ''))) AS NombreProfesionalAsignado
            FROM Conversacion c
            LEFT JOIN Paciente p ON p.IdPaciente = c.IdPaciente
            OUTER APPLY (SELECT TOP 1 m.IdMensaje, m.Texto, m.FechaHora, m.Origen FROM MensajeChat m
                         WHERE m.IdConversacion = c.IdConversacion ORDER BY m.IdMensaje DESC) um
            LEFT JOIN Profesional prof ON prof.IdProfesional = c.IdProfesionalAsignado
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
                TelefonoMostrar      = TelefonoWhatsApp.Formatear((string)r["Telefono"]),
                IdPaciente           = r["IdPaciente"]     == DBNull.Value ? null : (int)r["IdPaciente"],
                NombrePaciente       = r["NombrePaciente"] == DBNull.Value ? null : (string)r["NombrePaciente"],
                NombreContacto       = conNombre && r["NombreContacto"] != DBNull.Value ? (string)r["NombreContacto"] : null,
                Canal                = (string)r["Canal"],
                Estado               = (string)r["Estado"],
                PasoActual           = (string)r["PasoActual"],
                FechaUltimaActividad = (DateTime)r["FechaUltimaActividad"],
                UltimoMensaje        = r["UltimoMensaje"]  == DBNull.Value ? null : (string)r["UltimoMensaje"],
                FechaUltimoMensaje   = r["FechaUltimoMensaje"] == DBNull.Value ? null : (DateTime)r["FechaUltimoMensaje"],
                IdUltimoMensaje      = r["IdUltimoMensaje"] == DBNull.Value ? null : (int)r["IdUltimoMensaje"],
                OrigenUltimoMensaje  = r["OrigenUltimoMensaje"] == DBNull.Value ? null : (string)r["OrigenUltimoMensaje"],
                NoLeidos             = (int)r["NoLeidos"],
                IdProfesionalAsignado      = r["IdProfesionalAsignado"] == DBNull.Value ? null : (int)r["IdProfesionalAsignado"],
                NombreProfesionalAsignado  = r["NombreProfesionalAsignado"] == DBNull.Value ? null : (string)r["NombreProfesionalAsignado"]
            });
        r.Close();
        foreach (var resumen in lista)
            resumen.Ventana = _ventana.Evaluar(_mensajes.UltimoEntrante(resumen.IdConversacion));
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

    // Asignar (Configurador, a cualquiera) o devolver (null = sin asignar). Sin condición:
    // es una decisión deliberada que pisa lo que hubiera antes, a diferencia de Tomar.
    public void AsignarProfesional(int idConversacion, int? idProfesional)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand("UPDATE Conversacion SET IdProfesionalAsignado = @IdProf WHERE IdConversacion = @Id", con);
        cmd.Parameters.AddWithValue("@IdProf", (object?)idProfesional ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Id", idConversacion);
        cmd.ExecuteNonQuery();
    }

    public void ActualizarNombreContacto(int idConversacion, string nombre)
    {
        if (!UsaNombre) return;
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            UPDATE Conversacion SET NombreContacto = @Nombre
            WHERE IdConversacion = @Id AND (NombreContacto IS NULL OR NombreContacto <> @Nombre)", con);
        cmd.Parameters.Add("@Nombre", SqlDbType.NVarChar, 100).Value = nombre;
        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = idConversacion;
        cmd.ExecuteNonQuery();
    }

    // Atómico a propósito (Paso 3): si dos profesionales la toman a la vez, el UPDATE con
    // WHERE IdProfesionalAsignado IS NULL solo afecta una fila para uno de los dos - el otro
    // ve 0 filas afectadas y el endpoint responde 409, sin una transacción explícita ni un
    // SELECT-luego-UPDATE que pueda perder la carrera.
    public bool TomarSiNoTieneDueno(int idConversacion, int idProfesional)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        var cmd = new SqlCommand(@"
            UPDATE Conversacion SET IdProfesionalAsignado = @IdProf
            WHERE IdConversacion = @Id AND IdProfesionalAsignado IS NULL", con);
        cmd.Parameters.AddWithValue("@IdProf", idProfesional);
        cmd.Parameters.AddWithValue("@Id", idConversacion);
        return cmd.ExecuteNonQuery() == 1;
    }

    private Conversacion Mapear(SqlDataReader r, bool conNombre) => new Conversacion
    {
        IdConversacion         = (int)r["IdConversacion"],
        Telefono               = (string)r["Telefono"],
        IdPaciente             = r["IdPaciente"]    == DBNull.Value ? null : (int)r["IdPaciente"],
        Canal                  = (string)r["Canal"],
        PasoActual             = (string)r["PasoActual"],
        DatosContexto          = r["DatosContexto"] == DBNull.Value ? null : (string)r["DatosContexto"],
        Estado                 = (string)r["Estado"],
        FechaUltimaActividad   = (DateTime)r["FechaUltimaActividad"],
        FechaCreacion          = (DateTime)r["FechaCreacion"],
        IdProfesionalAsignado  = r["IdProfesionalAsignado"] == DBNull.Value ? null : (int)r["IdProfesionalAsignado"],
        NombreContacto         = conNombre && r["NombreContacto"] != DBNull.Value ? (string)r["NombreContacto"] : null,
        IdPacienteAlLeer       = r["IdPaciente"]    == DBNull.Value ? null : (int)r["IdPaciente"]
    };
}
