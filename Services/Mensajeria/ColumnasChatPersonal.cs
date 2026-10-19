using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

/// <summary>
/// ¿Se pueden usar las columnas de la migración 17 (MensajeChat.IdExterno, MensajeChat.Tipo y Conversacion.NombreContacto)? Es la ÚNICA puerta por la que los DAOs deciden
/// tocarlas, así que la regla vive en un solo lugar:
///  · modo Asistente (el valor por defecto): NUNCA. La API se puede publicar sin haber aplicado la migración 17 y anda igual: no las lee ni las escribe, ni pregunta por ellas;
///  · modo Personal: solo si la base las tiene. Si faltan, la API sigue guardando los mensajes (sin id, sin tipo y sin nombre: un mensaje repetido no se detecta en la base) y
///    lo dice en el log, como mucho una vez por minuto. Una vez que las encuentra no vuelve a preguntar; mientras no, vuelve a mirar cada minuto, así aplicar la migración con
///    la API andando alcanza (no hace falta reiniciarla).
/// </summary>
public interface IColumnasChatPersonal
{
    bool Disponibles { get; }
}

/// <summary>La consulta a la base, aparte para poder probar la puerta sin una base.</summary>
public interface ISondaDeColumnasChatPersonal
{
    bool Existen();
}

public sealed class SondaSqlDeColumnasChatPersonal : ISondaDeColumnasChatPersonal
{
    public bool Existen()
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(@"
            SELECT CASE WHEN COL_LENGTH('dbo.MensajeChat', 'IdExterno') IS NOT NULL
                         AND COL_LENGTH('dbo.MensajeChat', 'Tipo') IS NOT NULL
                         AND COL_LENGTH('dbo.Conversacion', 'NombreContacto') IS NOT NULL THEN 1 ELSE 0 END", con);
        return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
    }
}

public sealed class ColumnasChatPersonal : IColumnasChatPersonal
{
    public static readonly TimeSpan ConsultarCada = TimeSpan.FromMinutes(1);

    private readonly IOptionsMonitor<MensajeriaOptions> _opciones;
    private readonly ISondaDeColumnasChatPersonal _sonda;
    private readonly TimeProvider _reloj;
    private readonly ILogger<ColumnasChatPersonal> _log;
    private readonly object _candado = new();
    private volatile bool _existen;                       // una vez que existen, no se vuelve a preguntar
    private DateTimeOffset _ultimaConsulta = DateTimeOffset.MinValue;

    public ColumnasChatPersonal(IOptionsMonitor<MensajeriaOptions> opciones, ISondaDeColumnasChatPersonal sonda, TimeProvider reloj, ILogger<ColumnasChatPersonal> log)
    {
        _opciones = opciones;
        _sonda    = sonda;
        _reloj    = reloj;
        _log      = log;
    }

    public bool Disponibles => _opciones.CurrentValue.EsModoPersonal && Existen();

    private bool Existen()
    {
        if (_existen) return true;
        lock (_candado)
        {
            if (_existen) return true;
            var ahora = _reloj.GetUtcNow();
            if (ahora - _ultimaConsulta < ConsultarCada) return false;
            _ultimaConsulta = ahora;

            try
            {
                _existen = _sonda.Existen();
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "No se pudo comprobar si la base tiene las columnas de la migración 17; por ahora se guardan los mensajes sin id, tipo ni nombre.");
                return false;
            }
            if (!_existen)
                _log.LogError("Mensajeria:ModoAtencion es Personal pero a la base le falta la migración 17 (migrations/add_chat_personal_entrantes.sql): los mensajes se guardan " +
                              "sin id, sin tipo y sin el nombre del contacto, y un mensaje repetido no se detecta en la base. Aplicala (no hace falta reiniciar la API).");
            return _existen;
        }
    }
}
