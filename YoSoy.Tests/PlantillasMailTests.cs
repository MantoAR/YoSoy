using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

// Los mails a un paciente por un turno (PlantillasMail: recordatorio, solicitud, turno propuesto, confirmado y cancelado) dicen el día y la hora y datos neutros, y
// nada más: ni el nombre del profesional, ni la especialidad, ni el motivo de la consulta, ni la modalidad, ni el nombre del paciente. La campanita del panel
// (con sesión) NO cambia: sigue con el texto completo.
//  · Por la firma: ninguna plantilla recibe otra cosa que el momento del turno y el link de ingreso (y el asunto también sale de la plantilla).
//  · Por el texto: el asunto y el cuerpo dicen lo justo, y no traen ninguna palabra que revele la atención.
//  · De punta a punta: con un turno cargado de datos "marcados" (profesional, especialidad, modalidad, motivo, paciente), el mail que sale por el NotificadorTurnos
//    REAL no trae ninguna marca y es exactamente la plantilla; la campanita SÍ trae las del profesional y la modalidad, así se sabe que la marca, de colarse, se vería.
//  · Cada aviso del notificador trae su plantilla de mail (aunque hoy solo el recordatorio sale por mail): nadie le puede mandar al mail el texto de la campanita.
public class PlantillasMailTests
{
    private static readonly DateTime Momento = new(2031, 3, 4, 10, 30, 0);
    private const string Url = "https://yosoy.test/ingreso";

    // Palabras que no pueden aparecer en un mail de turno: revelan (o rozan) qué atención recibe la persona. "profesional" está en la lista a propósito: ningún mail
    // lo necesita, y si alguno lo llega a necesitar tiene que ser una decisión consciente.
    private static readonly Regex Prohibidas = new(
        @"profesional|especialidad|motivo|consulta|psic|psiq|terapi|tratamiento|diagn|salud mental|modalidad|virtual|presencial|videollamada|\bsala\b|\bdr\.|\bdra\.|\blic\.",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static IEnumerable<(string Nombre, MailDeTurno Mail)> Todas() => new[]
    {
        (nameof(PlantillasMail.Recordatorio), PlantillasMail.Recordatorio(Momento, Url)),
        (nameof(PlantillasMail.SolicitudRecibida), PlantillasMail.SolicitudRecibida(Momento)),
        (nameof(PlantillasMail.TurnoPropuesto), PlantillasMail.TurnoPropuesto(Momento, Url)),
        (nameof(PlantillasMail.TurnoConfirmado), PlantillasMail.TurnoConfirmado(Momento)),
        (nameof(PlantillasMail.TurnoCancelado), PlantillasMail.TurnoCancelado(Momento, Url))
    };

    // ───────────── La firma es la garantía ─────────────

    [Fact]
    public void Ninguna_plantilla_recibe_otra_cosa_que_el_momento_y_el_link_de_ingreso()
    {
        var metodos = typeof(PlantillasMail).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

        // Una por aviso. Una plantilla nueva obliga a venir acá, y a decidir si lo que recibe puede salir por mail.
        Assert.Equal(new[] { "Recordatorio", "SolicitudRecibida", "TurnoCancelado", "TurnoConfirmado", "TurnoPropuesto" },
            metodos.Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        foreach (var m in metodos)
        {
            Assert.Equal(typeof(MailDeTurno), m.ReturnType);
            var p = m.GetParameters();
            Assert.True(p.Length is 1 or 2, $"{m.Name}: recibe {p.Length} parámetros");
            Assert.True(p[0].ParameterType == typeof(DateTime) && p[0].Name == "momento", $"{m.Name}: el primer parámetro tiene que ser el momento del turno");
            if (p.Length == 2)
                Assert.True(p[1].ParameterType == typeof(string) && p[1].Name == "urlIngreso", $"{m.Name}: el segundo, si hay, es el link de ingreso");
        }
        Assert.Empty(typeof(PlantillasMail).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string) && !f.IsLiteral));   // y no hay textos sueltos que se puedan cambiar desde afuera
    }

    [Fact]
    public void Un_mail_de_turno_es_solo_un_asunto_y_un_cuerpo()
    {
        var propiedades = typeof(MailDeTurno).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract").Select(p => $"{p.Name}:{p.PropertyType.Name}").OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { "Asunto:String", "Cuerpo:String" }, propiedades);
    }

    [Fact]
    public void Hay_un_mail_por_cada_aviso_y_cada_plantilla_de_WhatsApp_tiene_su_par()
    {
        var mail = typeof(PlantillasMail).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Select(m => m.Name).ToHashSet();
        var whatsApp = typeof(PlantillasWhatsApp).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Select(m => m.Name).ToHashSet();

        Assert.True(whatsApp.IsSubsetOf(mail), "cada aviso que sale por WhatsApp tiene que tener su mail mínimo: " + string.Join(", ", whatsApp.Except(mail)));
        Assert.Equal(new[] { "Recordatorio" }, mail.Except(whatsApp).ToArray());                // el recordatorio solo sale por mail (y campanita)
    }

    // ───────────── El texto ─────────────

    [Fact]
    public void Cada_plantilla_dice_el_dia_y_la_hora_en_el_cuerpo()
    {
        foreach (var (nombre, mail) in Todas())
            Assert.True(mail.Cuerpo.Contains(Formato.FechaHora(Momento), StringComparison.Ordinal), $"{nombre} no dice el día y la hora: {mail.Cuerpo}");
        Assert.Equal("martes 04/03 a las 10:30", Formato.FechaHora(Momento));
    }

    [Fact]
    public void Ni_el_asunto_ni_el_cuerpo_traen_una_palabra_que_revele_la_atencion()
    {
        foreach (var (nombre, mail) in Todas())
        {
            var enAsunto = Prohibidas.Match(mail.Asunto);
            var enCuerpo = Prohibidas.Match(mail.Cuerpo);
            Assert.False(enAsunto.Success, $"{nombre}: el asunto dice \"{enAsunto.Value}\": {mail.Asunto}");
            Assert.False(enCuerpo.Success, $"{nombre}: el cuerpo dice \"{enCuerpo.Value}\": {mail.Cuerpo}");
        }
    }

    [Fact]
    public void Los_asuntos_son_cortos_neutros_y_sin_fecha_porque_se_ven_en_la_bandeja_sin_abrir_el_mail()
    {
        foreach (var (nombre, mail) in Todas())
        {
            Assert.StartsWith("YoSoy – ", mail.Asunto);
            Assert.True(mail.Asunto.Length <= 60, $"{nombre}: el asunto tiene {mail.Asunto.Length} caracteres");
            Assert.DoesNotMatch(@"\d", mail.Asunto);               // ni el día ni la hora en el asunto: eso va adentro
            Assert.DoesNotContain("\n", mail.Asunto);               // y un asunto de una línea (nada de inyectar cabeceras)
        }
        Assert.Equal(Todas().Count(), Todas().Select(t => t.Mail.Asunto).Distinct().Count());   // y se distinguen entre sí
    }

    [Fact]
    public void Solo_las_que_lo_necesitan_traen_el_link_y_es_el_que_se_les_paso()
    {
        foreach (var mail in new[] { PlantillasMail.Recordatorio(Momento, Url), PlantillasMail.TurnoPropuesto(Momento, Url), PlantillasMail.TurnoCancelado(Momento, Url) })
            Assert.Contains(Url, mail.Cuerpo);
        foreach (var mail in new[] { PlantillasMail.SolicitudRecibida(Momento), PlantillasMail.TurnoConfirmado(Momento) })
            Assert.DoesNotContain("http", mail.Cuerpo);
        foreach (var (_, mail) in Todas())
            Assert.DoesNotContain("http", mail.Asunto);
    }

    [Fact]
    public void El_recordatorio_dice_como_avisar_que_no_se_va_a_asistir_y_ya_no_habla_de_responder_el_chat()
    {
        var cuerpo = PlantillasMail.Recordatorio(Momento, Url).Cuerpo;

        Assert.Contains("avisanos desde la web", cuerpo);
        Assert.DoesNotContain("chat", cuerpo, StringComparison.OrdinalIgnoreCase);       // el recordatorio ya no sale por WhatsApp
        Assert.DoesNotContain("responder", cuerpo, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ingresá a la web", PlantillasMail.TurnoCancelado(Momento, Url).Cuerpo);   // y la cancelación manda a la web, no a "escribí menu por acá"
        Assert.DoesNotContain("menu", PlantillasMail.TurnoCancelado(Momento, Url).Cuerpo);
    }

    [Fact]
    public void Ninguna_plantilla_es_larga_ni_trae_saltos_de_mas()
    {
        foreach (var (nombre, mail) in Todas())
        {
            Assert.True(mail.Cuerpo.Length <= 400, $"{nombre} tiene {mail.Cuerpo.Length} caracteres");
            Assert.DoesNotContain("\r", mail.Cuerpo);
            Assert.DoesNotContain("\n\n\n", mail.Cuerpo);
        }
    }

    // ───────────── Cada aviso del notificador trae su plantilla ─────────────

    private static string FuenteDelNotificador() =>
        File.ReadAllText(Path.Combine(VideoAccesoEndpointsTests.RaizDelRepositorio(), "Services", "Mensajeria", "NotificadorTurnos.cs")).Replace("\r\n", "\n");

    [Fact]
    public void Cada_aviso_del_notificador_pasa_una_plantilla_de_mail_y_el_mail_sale_solo_de_ella()
    {
        var fuente = FuenteDelNotificador();

        var avisos = Regex.Matches(fuente, @"AvisarPaciente\((idTurno|id\.Value),").Count;
        var conPlantilla = Regex.Matches(fuente, @"\bmail:\s*t\s*=>\s*PlantillasMail\.(Recordatorio|SolicitudRecibida|TurnoPropuesto|TurnoConfirmado|TurnoCancelado)\(t\.Momento").Count;

        Assert.Equal(5, avisos);                  // el recordatorio y los cuatro por evento
        Assert.Equal(avisos, conPlantilla);       // y cada uno con su plantilla, que solo recibe el momento (y el link)
        Assert.Contains("EnviarMail(t.MailPaciente, mail(t))", fuente);   // el mail del paciente sale de la plantilla, nunca del texto de la campanita
        Assert.DoesNotMatch(@"EnviarMail\(t\.MailPaciente,[^)]*(cuerpo|textoApp)", fuente);
    }

    // ───────────── De punta a punta con el notificador ─────────────

    private const string MarcaProfesional = "MarcaProfesional";
    private const string MarcaEspecialidad = "MarcaEspecialidad";
    private const string MarcaModalidad = "MarcaModalidad";
    private const string MarcaMotivo = "MarcaMotivo";
    private const string MarcaPaciente = "MarcaPaciente";
    private static readonly string[] Marcas = { MarcaProfesional, MarcaEspecialidad, MarcaModalidad, MarcaMotivo, MarcaPaciente };

    private sealed class Arnes
    {
        public readonly Escenario E = new();
        public readonly EmailServiceEspia Mails = new();
        public readonly MensajesChatEnMemoria Mensajes = new();
        public readonly CanalEspia Canal = new();
        public readonly DatosConContactoDelPaciente Datos;
        public readonly RecordatoriosEspia Recordatorios = new();
        public readonly NotificadorTurnos Notificador;
        public readonly Turno Turno;

        public Arnes(int idEstado, string? mailDelPaciente = "paciente@example.com")
        {
            // Todo lo del turno que NO es el día y la hora, con una marca que se reconoce si aparece donde no tiene que estar.
            E.Db.Pacientes.Single(p => p.IdPaciente == 1).Nombre = MarcaPaciente + " García";
            var profesional = E.Db.Profesionales.Single(p => p.IdProfesional == 10);
            profesional.Nombre = "Dra. " + MarcaProfesional;
            profesional.Especialidad = MarcaEspecialidad;
            E.Opciones.CurrentValue.UrlIngresoTurno = Url;
            Turno = E.Db.AgregarTurno(1, 10, Escenario.Jueves0900.AddDays(2), idEstado);
            Turno.Modalidad = MarcaModalidad;
            Turno.Motivo = MarcaMotivo;

            Datos = new DatosConContactoDelPaciente(new FakeDatos(E.Db), E.Db) { MailDelPaciente = mailDelPaciente };
            var mensajes = new ServicioMensajes(Mensajes, Canal, E.Reloj, NullLogger<ServicioMensajes>.Instance);
            var servicios = new ServiceCollection().AddSingleton<EmailService>(Mails).BuildServiceProvider();
            Notificador = new NotificadorTurnos(Datos, Recordatorios, new InternasQueNoHacenNada(), E.Conversaciones, mensajes, Canal, E.WhatsApp,
                new FakeTurnoRepositorio(E.Db), servicios, new ConfigurationBuilder().Build(), E.Opciones, E.Reloj, NullLogger<NotificadorTurnos>.Instance);
        }

        public DateTime MomentoDelTurno => Turno.Fecha.Date + Turno.Hora;
    }

    [Fact]
    public void El_mail_del_recordatorio_es_exactamente_la_plantilla_y_no_trae_nada_del_turno_salvo_su_momento()
    {
        var a = new Arnes(BaseEnMemoria.Confirmado);

        a.Notificador.EnviarRecordatorio24h(a.Turno.IdTurno);

        var mail = Assert.Single(a.Mails.Avisos);
        var esperado = PlantillasMail.Recordatorio(a.MomentoDelTurno, Url);
        Assert.Equal("paciente@example.com", mail.Destinatario);
        Assert.Equal(esperado.Asunto, mail.Asunto);
        Assert.Equal(esperado.Cuerpo, mail.Texto);
        foreach (var marca in Marcas)
        {
            Assert.DoesNotContain(marca, mail.Asunto);
            Assert.DoesNotContain(marca, mail.Texto);
        }
        Assert.Contains(Formato.FechaHora(a.MomentoDelTurno), mail.Texto);
        Assert.DoesNotContain("(Virtual)", mail.Texto);
        Assert.Equal(("Email", "Enviado", (string?)null), a.Recordatorios.Resultados.Single(r => r.Canal == "Email"));
    }

    [Fact]
    public void La_campanita_del_recordatorio_no_cambia_trae_el_detalle_completo_y_el_mail_no()
    {
        // Es lo que prueba que las marcas se verían si se colaran en el mail, y que el panel (con sesión) no perdió el detalle.
        var a = new Arnes(BaseEnMemoria.Confirmado);

        a.Notificador.EnviarRecordatorio24h(a.Turno.IdTurno);

        var campanita = Assert.Single(a.Datos.Campanita);
        Assert.Equal("Recordatorio de turno", campanita.Titulo);
        Assert.Equal($"Recordatorio: tenés turno el {Formato.FechaHora(a.MomentoDelTurno)} con Dra. {MarcaProfesional} ({MarcaModalidad}).\nPara ingresar: {Url}" +
                     "\n\nSi no vas a poder asistir, avisanos desde la web.", campanita.Mensaje);
        Assert.Contains(MarcaProfesional, campanita.Mensaje);
        Assert.Contains(MarcaModalidad, campanita.Mensaje);
        Assert.DoesNotContain(MarcaProfesional, Assert.Single(a.Mails.Avisos).Texto);
    }

    [Fact]
    public void Sin_mail_cargado_el_recordatorio_omite_el_mail_y_la_campanita_sale_igual()
    {
        var a = new Arnes(BaseEnMemoria.Confirmado, mailDelPaciente: null);

        a.Notificador.EnviarRecordatorio24h(a.Turno.IdTurno);

        Assert.Empty(a.Mails.Avisos);
        Assert.Equal(("Email", "Omitido", "El paciente no tiene mail cargado."), a.Recordatorios.Resultados.Single(r => r.Canal == "Email"));
        Assert.Single(a.Datos.Campanita);
    }

    [Fact]
    public void Con_el_correo_sin_configurar_el_mail_queda_Omitido_como_siempre()
    {
        var a = new Arnes(BaseEnMemoria.Confirmado);
        var sinConfigurar = new EmailService(new ConfigurationBuilder().Build());          // el de verdad, sin credenciales
        var servicios = new ServiceCollection().AddSingleton(sinConfigurar).BuildServiceProvider();
        var notificador = new NotificadorTurnos(a.Datos, a.Recordatorios, new InternasQueNoHacenNada(), a.E.Conversaciones,
            new ServicioMensajes(a.Mensajes, a.Canal, a.E.Reloj, NullLogger<ServicioMensajes>.Instance), a.Canal, a.E.WhatsApp,
            new FakeTurnoRepositorio(a.E.Db), servicios, new ConfigurationBuilder().Build(), a.E.Opciones, a.E.Reloj, NullLogger<NotificadorTurnos>.Instance);

        notificador.EnviarRecordatorio24h(a.Turno.IdTurno);

        Assert.Equal(("Email", "Omitido", "EmailService no está configurado."), a.Recordatorios.Resultados.Single(r => r.Canal == "Email"));
        Assert.Single(a.Datos.Campanita);
    }

    [Fact]
    public void Los_avisos_por_evento_siguen_sin_salir_por_mail_y_su_campanita_no_cambia()
    {
        // Hoy los cuatro avisos por evento salen por WhatsApp (si la llave está prendida) y por la campanita, no por mail: este cambio solo hizo mínimo el mail que
        // ya existía (el recordatorio). Si algún día se les agrega el mail, esta prueba tiene que cambiar a conciencia, junto con las plantillas de arriba.
        var a = new Arnes(BaseEnMemoria.Pendiente);
        var confirmado = a.E.Db.AgregarTurno(1, 10, Escenario.Jueves0900.AddDays(3), BaseEnMemoria.Confirmado);
        var cancelado = a.E.Db.AgregarTurno(1, 10, Escenario.Jueves0900.AddDays(4), BaseEnMemoria.Cancelado);

        a.Notificador.TurnoSolicitado(a.Turno.IdTurno);
        a.Notificador.TurnoAgendadoPorProfesional(1, 10, a.Turno.Fecha, a.Turno.Hora);
        a.Notificador.TurnoConfirmado(confirmado.IdTurno);
        a.Notificador.TurnoCancelado(cancelado.IdTurno);

        Assert.Empty(a.Mails.Avisos);
        Assert.Equal(4, a.Datos.Campanita.Count);
        Assert.All(a.Recordatorios.Reservados, r => Assert.Contains(r.Canal, new[] { "Chat", "App" }));
        Assert.All(a.Datos.Campanita, c => Assert.Contains(MarcaProfesional, c.Mensaje));       // la campanita sigue con el nombre del profesional
    }
}
