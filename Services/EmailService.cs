using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

public class EmailService
{
    private readonly string _host   = "smtp.gmail.com";
    private readonly int    _port   = 587;
    private readonly string _from   = "cervantesyosoycor@gmail.com";
    private readonly string _pass   = "cgteljwfxzovygdm";
    private readonly string _appUrl = "http://localhost:5000";

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

    public void EnviarResetPassword(string destinatario, string token)
    {
        var link = $"{_appUrl}/pantallas/reset-password.html?token={token}";

        var mensaje = new MimeMessage();
        mensaje.From.Add(MailboxAddress.Parse(_from));
        mensaje.To.Add(MailboxAddress.Parse(destinatario));
        mensaje.Subject = "Recuperación de contraseña – YoSoy";

        var builder = new BodyBuilder
        {
            HtmlBody = $@"
<div style=""font-family:Arial,sans-serif;max-width:480px;margin:auto"">
  <h2 style=""color:#2c3e50"">Recuperación de contraseña</h2>
  <p>Recibimos una solicitud para restablecer tu contraseña en <strong>YoSoy</strong>.</p>
  <p>Hacé clic en el botón para crear una nueva contraseña. El link expira en <strong>1 hora</strong>.</p>
  <a href=""{link}"" style=""display:inline-block;padding:12px 24px;background:#2980b9;color:#fff;text-decoration:none;border-radius:6px;margin:16px 0"">
    Restablecer contraseña
  </a>
  <p style=""font-size:12px;color:#888"">Si no solicitaste este cambio, ignorá este mail.</p>
</div>"
        };
        mensaje.Body = builder.ToMessageBody();

        using var smtp = new SmtpClient();
        smtp.Connect(_host, _port, SecureSocketOptions.StartTls);
        smtp.Authenticate(_from, _pass);
        smtp.Send(mensaje);
        smtp.Disconnect(true);
    }
}
