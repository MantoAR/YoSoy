using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

// Envío de mails por SMTP. Las credenciales NO están en el repositorio: appsettings.json trae vacíos Email:From (la
// cuenta, que también es el usuario del SMTP) y Email:Password. Se cargan con dotnet user-secrets en desarrollo y con las
// variables de entorno Email__From y Email__Password en el servidor (docs/DESARROLLO_LOCAL.md y docs/DEPLOY.md).
//
// Si falta algo (Email:Host, Email:From o Email:Password) el correo NO está configurado: la aplicación arranca igual y
// cada intento de envío deja un aviso "Correo no configurado" en el log, sin abrir ninguna conexión. Los Enviar* devuelven
// true si el mail salió y false si no estaba configurado; si SÍ lo estaba y el SMTP falla, la excepción sigue subiendo.
public class EmailService
{
    private const int PuertoSmtpPorDefecto = 587;

    private readonly string _host;
    private readonly int    _port;
    private readonly string _from;
    private readonly string _pass;
    private readonly string _appUrl;
    private readonly ILogger<EmailService> _log;

    // Nunca tira por falta de configuración: este constructor corre cuando se usa el servicio por primera vez, y la
    // aplicación tiene que poder levantar sin ninguna credencial de correo.
    public EmailService(IConfiguration config, ILogger<EmailService>? log = null)
    {
        _log = log ?? NullLogger<EmailService>.Instance;

        var email = config.GetSection("Email");
        _host   = email["Host"]     ?? "";
        _from   = email["From"]     ?? "";
        _pass   = email["Password"] ?? "";
        _appUrl = email["AppUrl"]   ?? "";

        var puerto = email["Port"];
        if (int.TryParse(puerto, out var p) && p > 0)
            _port = p;
        else
        {
            _port = PuertoSmtpPorDefecto;
            if (!string.IsNullOrWhiteSpace(puerto))
                _log.LogWarning("Email:Port no es un puerto válido: se usa el {Puerto}.", PuertoSmtpPorDefecto);
        }
    }

    /// <summary>
    /// ¿Hay con qué mandar un mail? Hacen falta el servidor (Email:Host), la cuenta (Email:From, que también es el
    /// usuario del SMTP) y su contraseña (Email:Password). Un valor en blanco cuenta como vacío.
    /// </summary>
    public bool Configurado => Faltantes().Count == 0;

    // Solo los NOMBRES de las claves que faltan: nunca sus valores.
    private List<string> Faltantes()
    {
        var faltan = new List<string>();
        if (string.IsNullOrWhiteSpace(_host)) faltan.Add("Email:Host");
        if (string.IsNullOrWhiteSpace(_from)) faltan.Add("Email:From");
        if (string.IsNullOrWhiteSpace(_pass)) faltan.Add("Email:Password");
        return faltan;
    }

    // Se llama antes de armar o mandar nada. Sin correo configurado deja el aviso en el log y devuelve false: no se abre
    // ninguna conexión (antes se intentaba autenticar igual y la excepción se perdía en quien llamaba). No registra
    // destinatarios ni valores de configuración.
    private bool PuedeEnviar(string queSeIbaAEnviar)
    {
        var faltan = Faltantes();
        if (faltan.Count == 0) return true;
        _log.LogWarning(
            "Correo no configurado: no se envió {Que}. Falta {Claves}. Se carga con dotnet user-secrets (desarrollo) o con " +
            "las variables de entorno Email__From y Email__Password (servidor); ver docs/DESARROLLO_LOCAL.md.",
            queSeIbaAEnviar, string.Join(", ", faltan));
        return false;
    }

    public bool EnviarAprobacionProfesional(string destinatario, string nombreProfesional, string token)
    {
        if (!PuedeEnviar("el aviso de aprobación del profesional")) return false;

        var link = $"{_appUrl}/pantallas/crear-cuenta-profesional.html?token={token}";

        var mensaje = new MimeMessage();
        mensaje.From.Add(MailboxAddress.Parse(_from));
        mensaje.To.Add(MailboxAddress.Parse(destinatario));
        mensaje.Subject = "Tu cuenta fue aprobada – YoSoy";

        var builder = new BodyBuilder
        {
            HtmlBody = $@"
<div style=""font-family:Arial,sans-serif;max-width:480px;margin:auto"">
  <h2 style=""color:#2c3e50"">¡Tu solicitud fue aprobada!</h2>
  <p>Hola <strong>{nombreProfesional}</strong>,</p>
  <p>Tu solicitud de registro como profesional en <strong>YoSoy</strong> fue aprobada.</p>
  <p>Hacé clic en el botón para crear tu usuario y contraseña:</p>
  <a href=""{link}"" style=""display:inline-block;padding:12px 24px;background:#2980b9;color:#fff;text-decoration:none;border-radius:6px;margin:16px 0"">
    Crear mi cuenta
  </a>
  <p style=""font-size:12px;color:#888"">Si no reconocés este mail, podés ignorarlo.</p>
</div>"
        };
        mensaje.Body = builder.ToMessageBody();

        using var smtp = new SmtpClient();
        smtp.Connect(_host, _port, SecureSocketOptions.StartTls);
        smtp.Authenticate(_from, _pass);
        smtp.Send(mensaje);
        smtp.Disconnect(true);
        return true;
    }

    /// <summary>Base de los links que van en los mails (Email:AppUrl).</summary>
    public string AppUrl => _appUrl.TrimEnd('/');

    // Invitación a un usuario del equipo que dio de alta el Configurador: confirmar la cuenta
    // y elegir la contraseña. Los datos van escapados (los cargó una persona).
    // Devuelve false (sin abrir ninguna conexión) si el correo no está configurado, igual que los demás Enviar*. Es virtual para que las pruebas vean el envío sin SMTP.
    public virtual bool EnviarInvitacionEquipo(string destinatario, string? nombre, string username, string link, DateTime vence)
    {
        if (!PuedeEnviar("la invitación a un usuario del equipo")) return false;
        string E(string? t) => System.Net.WebUtility.HtmlEncode(t ?? "");
        var mensaje = new MimeMessage();
        mensaje.From.Add(MailboxAddress.Parse(_from));
        mensaje.To.Add(MailboxAddress.Parse(destinatario));
        mensaje.Subject = "Confirmá tu cuenta en YoSoy";

        var builder = new BodyBuilder
        {
            TextBody = $"Hola {nombre},\n\nTe dieron de alta en YoSoy. Tu usuario es: {username}\n" +
                       $"Para confirmar tu cuenta y elegir tu contraseña, entrá a:\n{link}\n\n" +
                       $"El link sirve una sola vez y vence el {vence:dd/MM/yyyy} a las {vence:HH:mm}.\n" +
                       "Si no esperabas este mail, ignoralo.",
            HtmlBody = $@"
<div style=""font-family:Arial,sans-serif;max-width:480px;margin:auto"">
  <h2 style=""color:#2c3e50"">Te dieron de alta en YoSoy</h2>
  <p>Hola <strong>{E(nombre)}</strong>,</p>
  <p>Tu usuario es: <strong>{E(username)}</strong></p>
  <p>Para confirmar tu cuenta y elegir tu contraseña, hacé clic en el botón:</p>
  <a href=""{E(link)}"" style=""display:inline-block;padding:12px 24px;background:#2980b9;color:#fff;text-decoration:none;border-radius:6px;margin:16px 0"">
    Confirmar mi cuenta
  </a>
  <p style=""font-size:12px;color:#888"">El link sirve una sola vez y vence el {vence:dd/MM/yyyy} a las {vence:HH:mm}. Si no esperabas este mail, ignoralo.</p>
</div>"
        };
        mensaje.Body = builder.ToMessageBody();

        using var smtp = new SmtpClient();
        smtp.Connect(_host, _port, SecureSocketOptions.StartTls);
        smtp.Authenticate(_from, _pass);
        smtp.Send(mensaje);
        smtp.Disconnect(true);
        return true;
    }

    // Aviso genérico de texto (recordatorios y avisos de turnos del módulo de mensajería).
    // El texto se escapa: puede incluir datos cargados por usuarios. Es virtual para que las pruebas vean QUÉ diría el mail (sin SMTP ni red).
    public virtual bool EnviarAviso(string destinatario, string asunto, string texto)
    {
        if (!PuedeEnviar("un aviso de turno")) return false;

        var html = System.Net.WebUtility.HtmlEncode(texto).Replace("\n", "<br>");

        var mensaje = new MimeMessage();
        mensaje.From.Add(MailboxAddress.Parse(_from));
        mensaje.To.Add(MailboxAddress.Parse(destinatario));
        mensaje.Subject = asunto;

        var builder = new BodyBuilder
        {
            TextBody = texto,
            HtmlBody = $@"<div style=""font-family:Arial,sans-serif;max-width:480px;margin:auto"">{html}</div>"
        };
        mensaje.Body = builder.ToMessageBody();

        using var smtp = new SmtpClient();
        smtp.Connect(_host, _port, SecureSocketOptions.StartTls);
        smtp.Authenticate(_from, _pass);
        smtp.Send(mensaje);
        smtp.Disconnect(true);
        return true;
    }

    public bool EnviarResetPassword(string destinatario, string token, int duracionMinutos)
    {
        if (!PuedeEnviar("el mail de recuperación de contraseña")) return false;

        var link = $"{_appUrl}/pantallas/reset-password.html?token={token}";

        var mensaje = new MimeMessage();
        mensaje.From.Add(MailboxAddress.Parse(_from));
        mensaje.To.Add(MailboxAddress.Parse(destinatario));
        mensaje.Subject = "Recuperación de contraseña – YoSoy";

        var builder = new BodyBuilder { HtmlBody = CuerpoResetPassword(link, duracionMinutos) };
        mensaje.Body = builder.ToMessageBody();

        using var smtp = new SmtpClient();
        smtp.Connect(_host, _port, SecureSocketOptions.StartTls);
        smtp.Authenticate(_from, _pass);
        smtp.Send(mensaje);
        smtp.Disconnect(true);
        return true;
    }

    // Separado del método de arriba para poder probar que el texto coincide con la
    // duración real sin mandar un mail (RecuperacionPasswordTests). El llamador siempre
    // tiene que pasar RecuperacionPasswordDAO.DuracionTokenMinutos, nunca un número suelto.
    public static string CuerpoResetPassword(string link, int duracionMinutos) => $@"
<div style=""font-family:Arial,sans-serif;max-width:480px;margin:auto"">
  <h2 style=""color:#2c3e50"">Recuperación de contraseña</h2>
  <p>Recibimos una solicitud para restablecer tu contraseña en <strong>YoSoy</strong>.</p>
  <p>Hacé clic en el botón para crear una nueva contraseña. El link expira en <strong>{duracionMinutos} minutos</strong>.</p>
  <a href=""{link}"" style=""display:inline-block;padding:12px 24px;background:#2980b9;color:#fff;text-decoration:none;border-radius:6px;margin:16px 0"">
    Restablecer contraseña
  </a>
  <p style=""font-size:12px;color:#888"">Si no solicitaste este cambio, ignorá este mail.</p>
</div>";
}
