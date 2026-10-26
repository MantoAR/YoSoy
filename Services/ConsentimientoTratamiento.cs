using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

// El consentimiento de TRATAMIENTO Y TELEMEDICINA: es de la institución (no de un profesional), se firma una vez y se exige en dos momentos del circuito:
//   · ANTES DE ASIGNAR un profesional ("sin firma no se asigna"): BloquearSiNoFirmo, en cada camino por el que Admisión/CRM/el Configurador asigna a un paciente.
//   · AL CONFIRMAR un turno (RN-02): TurnoReglasService, como defensa.
// Los dos usan LA MISMA decisión: ConsentimientoTratamiento.Estado / ConsentimientoDAO.EstadoDeTratamiento. No hay otro criterio de «vigente» en el sistema.
//
// Vigente = hay una firma de ese tipo que no fue dada de baja, de la VERSIÓN del texto que está vigente (las firmas sin versión, anteriores, valen) y, si la persona es
// MENOR de edad (menos de 18 años o con un tutor cargado), firmada por su tutor (Consentimiento.IdTutorFirmante, migración 28). Sin la migración 28 la regla del tutor
// no se aplica (queda como antes). El texto legal NO está validado: ver Consentimiento:MarcaProvisoria.
public record FirmaTratamiento(DateTime FechaFirma, string? Version, int? IdTutorFirmante);

public record EstadoTratamiento(bool Vigente, string Motivo, bool EsMenor, DateTime? FechaFirma, string? Version, int? IdTutorFirmante);

public static class ConsentimientoTratamiento
{
    public const string Ok = "ok";
    public const string SinFirma = "sin-firma";
    public const string VersionAnterior = "version-anterior";
    public const string FaltaTutor = "falta-tutor";
    public const string Vencido = "vencido";

    // Vigencia anual: una firma de hace más de este tiempo deja de valer y se pide firmar de nuevo (0 = no vence). Se configura en
    // Consentimiento:VigenciaMeses. Los caminos que no reciben las opciones (RN-02 en TurnoRepositorioDAO) leen este valor, que la API fija al arrancar.
    public const int VigenciaMesesPorDefecto = 12;
    public static int VigenciaMeses { get; set; } = VigenciaMesesPorDefecto;

    public const string UrlFirmaPorDefecto = "/pantallas/Pantalla_Paciente/consentimiento.html";
    public const string TituloDelAviso = "Para continuar tenés que firmar el consentimiento";

    /// <summary>La decisión, sin base de datos (para probarla): las firmas no dadas de baja, de la más nueva a la más vieja.</summary>
    public static EstadoTratamiento Decidir(IReadOnlyList<FirmaTratamiento> firmas, string? versionRequerida, bool esMenor, bool reglaDelTutor, DateTime? hoy = null, int vigenciaMeses = 0)
    {
        if (firmas.Count == 0) return new(false, SinFirma, esMenor, null, null, null);

        var deLaVersion = firmas.Where(f => versionRequerida is null || string.IsNullOrEmpty(f.Version) || f.Version == versionRequerida).ToList();
        if (deLaVersion.Count == 0) return new(false, VersionAnterior, esMenor, firmas[0].FechaFirma, firmas[0].Version, firmas[0].IdTutorFirmante);

        // Vigencia anual: de las firmas de esta versión solo valen las de los últimos N meses (la fecha de firma más reciente manda).
        if (hoy is DateTime ahora && vigenciaMeses > 0)
        {
            var recientes = deLaVersion.Where(f => f.FechaFirma.AddMonths(vigenciaMeses) > ahora).ToList();
            if (recientes.Count == 0) return new(false, Vencido, esMenor, deLaVersion[0].FechaFirma, deLaVersion[0].Version, deLaVersion[0].IdTutorFirmante);
            deLaVersion = recientes;
        }

        if (esMenor && reglaDelTutor)
        {
            var delTutor = deLaVersion.FirstOrDefault(f => f.IdTutorFirmante is not null);
            return delTutor is null
                ? new(false, FaltaTutor, true, deLaVersion[0].FechaFirma, deLaVersion[0].Version, null)
                : new(true, Ok, true, delTutor.FechaFirma, delTutor.Version, delTutor.IdTutorFirmante);
        }
        var f0 = deLaVersion[0];
        return new(true, Ok, esMenor, f0.FechaFirma, f0.Version, f0.IdTutorFirmante);
    }

    public static EstadoTratamiento Estado(int idPaciente, string tipo, string? version, DateTime? hoy = null, int? vigenciaMeses = null) =>
        new ConsentimientoDAO().EstadoDeTratamiento(idPaciente, tipo, version, hoy ?? DateTime.Now, vigenciaMeses ?? VigenciaMeses);

    public static EstadoTratamiento Estado(int idPaciente, ConsentimientoOptions op, DateTime? hoy = null) => Estado(idPaciente, op.TipoRequerido, op.VersionTexto, hoy, op.VigenciaMeses);

    public static string MensajeParaAsignar(string nombrePaciente, EstadoTratamiento e) => e.Motivo switch
    {
        Vencido => $"No se puede asignar profesional: el consentimiento de tratamiento de {nombrePaciente} venció (pasó más de un año desde que lo firmó) y tiene que volver a firmarlo. Pedile que lo firme y volvé a intentar.",
        VersionAnterior => $"No se puede asignar profesional: el texto del consentimiento de tratamiento cambió y {nombrePaciente} tiene que volver a firmarlo. Pedile que lo firme y volvé a intentar.",
        FaltaTutor => $"No se puede asignar profesional: {nombrePaciente} es menor de edad y todavía no tiene el consentimiento de tratamiento firmado por su tutor. Pedile al tutor que lo firme y volvé a intentar.",
        _ => $"No se puede asignar profesional: {nombrePaciente} todavía no firmó el consentimiento de tratamiento. Pedile que lo firme y volvé a intentar."
    };

    /// <summary>
    /// null si el paciente puede ser asignado a un profesional. Si no, la respuesta 409 con un mensaje claro (y el link para firmar) y, de paso, el aviso al paciente para que firme
    /// (campanita y mail), una sola vez mientras siga pendiente. Es lo que se llama antes de asignar.
    /// </summary>
    public static IResult? BloquearSiNoFirmo(HttpContext ctx, int idPaciente)
    {
        var servicios = ctx.RequestServices;
        // Un paciente que no existe no es asunto del consentimiento: lo responde (404) el endpoint que llama.
        var paciente = new PacienteDAO().ObtenerPorId(idPaciente);
        if (paciente is null) return null;
        var op = servicios.GetRequiredService<IOptionsMonitor<ConsentimientoOptions>>().CurrentValue;
        var estado = Estado(idPaciente, op);
        if (estado.Vigente) return null;

        var nombre = string.IsNullOrWhiteSpace(paciente.Nombre) ? $"el paciente #{idPaciente}" : paciente.Nombre;
        // La campanita lleva a la pantalla de firma por su ruta propia; el mail, a la dirección completa si está configurada (Consentimiento:UrlFirma).
        var link = UrlFirmaPorDefecto;
        try { AvisoParaFirmar.Pedir(idPaciente, link, string.IsNullOrWhiteSpace(op.UrlFirma) ? null : op.UrlFirma, servicios.GetService<EmailService>(), servicios.GetService<ILoggerFactory>()?.CreateLogger("Consentimiento")); }
        catch (Exception) { /* el aviso no puede tapar el rechazo */ }
        return Results.Json(new { codigo = "consentimiento-pendiente", mensaje = MensajeParaAsignar(nombre, estado), motivo = estado.Motivo, urlConsentimiento = link }, statusCode: 409);
    }
}

/// <summary>
/// El aviso al paciente «Para continuar tenés que firmar el consentimiento» (campanita y mail; nada por WhatsApp), con acceso directo a la pantalla de firma. Idempotente: no se repite
/// mientras el anterior siga sin leer ni hayan pasado 24 horas.
/// </summary>
public static class AvisoParaFirmar
{
    public const int HorasSinRepetir = 24;

    public static bool Pedir(int idPaciente, string link, string? linkParaElMail, EmailService? email, ILogger? log)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using (var existe = new SqlCommand(@"SELECT COUNT(*) FROM Notificacion WHERE IdPaciente = @P AND Titulo = @T AND (Leida = 0 OR FechaCreacion > DATEADD(HOUR, -@H, GETDATE()))", con))
        {
            existe.Parameters.AddWithValue("@P", idPaciente);
            existe.Parameters.AddWithValue("@T", ConsentimientoTratamiento.TituloDelAviso);
            existe.Parameters.AddWithValue("@H", HorasSinRepetir);
            if ((int)existe.ExecuteScalar()! > 0) return false;
        }
        string? mail;
        using (var ins = new SqlCommand("INSERT INTO Notificacion (IdPaciente, Titulo, Mensaje, Link) VALUES (@P, @T, @M, @L); SELECT Mail FROM Paciente WHERE IdPaciente = @P", con))
        {
            ins.Parameters.AddWithValue("@P", idPaciente);
            ins.Parameters.AddWithValue("@T", ConsentimientoTratamiento.TituloDelAviso);
            ins.Parameters.AddWithValue("@M", "Para seguir con tu atención en YoSoy necesitamos que firmes el consentimiento informado de tratamiento y telemedicina. Ingresá a tu panel y abrí «Consentimiento»: lleva un minuto.");
            ins.Parameters.AddWithValue("@L", link);
            mail = ins.ExecuteScalar() as string;
        }
        if (!string.IsNullOrWhiteSpace(mail) && email is not null)
        {
            try
            {
                email.EnviarAviso(mail, "YoSoy: falta tu firma del consentimiento",
                    "Para seguir con tu atención en YoSoy necesitamos que firmes el consentimiento informado de tratamiento y telemedicina.\n\n"
                    + (linkParaElMail is null ? "Ingresá a tu panel de YoSoy y abrí «Consentimiento»." : $"Firmalo acá: {linkParaElMail}"));
            }
            catch (Exception) { log?.LogWarning("No se pudo enviar el mail para pedir la firma del consentimiento."); }
        }
        return true;
    }
}

public static class Migracion28
{
    private static bool _existe;

    /// <summary>¿La base tiene la migración 28 (IdTutorFirmante y AceptacionVideollamada)? Una vez que la tiene no se vuelve a preguntar.</summary>
    public static bool Disponible()
    {
        if (_existe) return true;
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID('dbo.AceptacionVideollamada', 'U') IS NOT NULL AND COL_LENGTH('dbo.Consentimiento', 'IdTutorFirmante') IS NOT NULL THEN 1 ELSE 0 END", con);
        return _existe = (int)cmd.ExecuteScalar()! == 1;
    }
}
