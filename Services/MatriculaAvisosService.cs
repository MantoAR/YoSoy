// Avisos diarios de vencimiento de matrícula (bloque 2 de rama AAA1, migración 33).
//
// Desde 30 días antes del vencimiento, y todos los días hasta que se renueve, avisa:
//   - al profesional: campanita (NotificacionInterna) y mail;
//   - a cada Profesional auditor activo: campanita y mail.
// Un aviso por matrícula y por día (tabla AvisoMatricula, con UNIQUE): si la API se reinicia o hay dos instancias, no se repite.
// Corre una vez por hora y solo manda desde la hora configurada (Matriculas:HoraAviso, por defecto 8), en la zona de la clínica.
// El mail no lleva ningún dato de pacientes: solo la matrícula, la especialidad y la fecha.
// La suspensión de la agenda NO la hace este servicio: es un cálculo (Services/AgendaRestricciones.cs) y vale aunque este
// servicio esté apagado. El día del vencimiento queda además en la auditoría administrativa.
//
// Configuración: Matriculas:AvisosHabilitados (true por defecto), Matriculas:HoraAviso (8).
public record ResultadoAvisosMatricula(bool Ejecutado, int Matriculas, int Avisos, int Correos, string? Motivo = null);

public class MatriculaAvisosService : BackgroundService
{
    private readonly TimeProvider _reloj;
    private readonly EmailService _email;
    private readonly IConfiguration _config;
    private readonly ILogger<MatriculaAvisosService> _log;

    public MatriculaAvisosService(TimeProvider reloj, EmailService email, IConfiguration config, ILogger<MatriculaAvisosService> log)
    {
        _reloj = reloj; _email = email; _config = config; _log = log;
    }

    private bool Habilitado => _config.GetValue("Matriculas:AvisosHabilitados", true);
    private int HoraAviso => Math.Clamp(_config.GetValue("Matriculas:HoraAviso", 8), 0, 23);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Un rato después del arranque, para no competir con el resto de lo que la API hace al levantar.
        try { await Task.Delay(TimeSpan.FromMinutes(3), _reloj, ct); }
        catch (TaskCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try { EjecutarUnaVez(); }
            catch (Exception ex)
            {
                _log.LogError("Matrículas: falló la pasada de avisos ({Tipo}); se reintenta en una hora.", ex.GetType().Name);
            }
            try { await Task.Delay(TimeSpan.FromHours(1), _reloj, ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    /// <summary>Una pasada. <paramref name="forzar"/> la corre aunque esté apagada o sea antes de la hora (botón de prueba).</summary>
    public ResultadoAvisosMatricula EjecutarUnaVez(bool forzar = false)
    {
        if (!forzar && !Habilitado) return new(false, 0, 0, 0, "Avisos apagados (Matriculas:AvisosHabilitados = false).");
        var ahora = _reloj.GetLocalNow().DateTime;
        if (!forzar && ahora.Hour < HoraAviso) return new(false, 0, 0, 0, $"Todavía no son las {HoraAviso}:00.");

        var hoy = ahora.Date;
        var dao = new AgendaRestriccionesDAO();
        var pendientes = dao.AvisosPendientes(hoy, EstadoMatricula.DiasDeAviso);
        if (pendientes.Count == 0) return new(true, 0, 0, 0);
        var auditores = dao.Auditores();

        int avisos = 0, correos = 0;
        foreach (var m in pendientes)
        {
            var vencida = hoy >= m.Vencimiento.Date;
            if (!dao.RegistrarAviso(m.IdMatricula, hoy, vencida ? "Vencida" : "PorVencer")) continue;

            var dias = (int)(m.Vencimiento.Date - hoy).TotalDays;
            var cuando = vencida ? $"venció el {m.Vencimiento:dd/MM/yyyy}" : $"vence el {m.Vencimiento:dd/MM/yyyy} ({(dias == 1 ? "falta 1 día" : $"faltan {dias} días")})";
            var efecto = m.Principal
                ? (vencida ? "La agenda no ofrece turnos hasta que se cargue la renovación." : "Desde esa fecha la agenda deja de ofrecer turnos.")
                : "Es una matrícula secundaria: no corta la agenda.";

            var tituloProf = vencida ? "Tu matrícula venció" : "Tu matrícula está por vencer";
            var textoProf = $"Tu matrícula de {m.Especialidad} N° {m.Numero} {cuando}. {efecto} Para renovarla, enviá la constancia al Configurador.";
            dao.CrearAvisoProfesional(m.IdProfesional, "Matricula", tituloProf, textoProf);
            avisos++;
            if (Enviar(m.MailProfesional, $"YoSoy · {tituloProf}", textoProf)) correos++;

            var tituloAud = vencida ? $"Matrícula vencida: {m.Profesional}" : $"Matrícula por vencer: {m.Profesional}";
            var textoAud = $"La matrícula de {m.Especialidad} N° {m.Numero} de {m.Profesional} {cuando}. {efecto}";
            foreach (var a in auditores.Where(a => a.IdProfesional != m.IdProfesional))
            {
                dao.CrearAvisoProfesional(a.IdProfesional, "Matricula", tituloAud, textoAud);
                avisos++;
                if (Enviar(a.Mail, $"YoSoy · {tituloAud}", textoAud)) correos++;
            }

            if (vencida && dias == 0 && m.Principal)
                AuditoriaAgenda.Registrar(null, "Agenda suspendida por matrícula vencida", "Matricula", m.IdMatricula,
                    $"Profesional #{m.IdProfesional} · venció el {m.Vencimiento:dd/MM/yyyy}", "sistema");
        }

        _log.LogInformation("Matrículas: {Matriculas} matrícula(s) por vencer o vencidas; {Avisos} aviso(s) y {Correos} mail(s).",
            pendientes.Count, avisos, correos);
        return new(true, pendientes.Count, avisos, correos);
    }

    private bool Enviar(string? destinatario, string asunto, string texto)
    {
        if (string.IsNullOrWhiteSpace(destinatario)) return false;
        try { return _email.EnviarAviso(destinatario, asunto, texto); }
        catch (Exception ex)
        {
            _log.LogWarning("Matrículas: no se pudo mandar un aviso por mail ({Tipo}).", ex.GetType().Name);
            return false;
        }
    }
}
