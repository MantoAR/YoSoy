using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Configuration;

public class EmailService
{
    private readonly string _host;
    private readonly int    _port;
    private readonly string _from;
    private readonly string _pass;
    private readonly string _appUrl;

    public EmailService(IConfiguration config)
    {
        var email = config.GetSection("Email");
        _host   = email["Host"]!;
        _port   = int.Parse(email["Port"]!);
        _from   = email["From"]!;
        _pass   = email["Password"]!;
        _appUrl = email["AppUrl"]!;
    }

    public void EnviarAprobacionProfesional(string destinatario, string nombreProfesional, string token)
    {
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
    }

    // Aviso genérico de texto (recordatorios y avisos de turnos del módulo de mensajería).
    // El texto se escapa: puede incluir datos cargados por usuarios.
    public void EnviarAviso(string destinatario, string asunto, string texto)
    {
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
    }

    public void EnviarResetPassword(string destinatario, string token, int duracionMinutos)
    {
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
