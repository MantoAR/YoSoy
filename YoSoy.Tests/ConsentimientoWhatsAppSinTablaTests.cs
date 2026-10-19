using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

// Con la llave Mensajeria:AvisosPorWhatsApp apagada (por defecto) la API NO toca la tabla ConsentimientoWhatsApp: ni en el registro de pacientes, ni en el
// perfil, ni en los envíos, ni cuando llega una BAJA. Así se puede publicar la API sin haber aplicado la migración 16 y todo anda igual.
//
//  · ConsentimientoWhatsAppSinTablaServicioTests (en memoria): un doble de la tabla que cuenta cada llamada y revienta ("Invalid object name"): con la llave
//    apagada, todo lo público del servicio contesta sin una sola llamada. Una prueba de reflexión obliga a pensar qué hace con la llave cada método nuevo.
//  · SinTablaNotificadorYBotTests (en memoria): el notificador y el asistente con el servicio real.
//  · ConsentimientoWhatsAppSinTablaBaseRealTests (la aplicación entera y la base de pruebas, con los DAOs reales): los mismos recorridos de punta a punta. Corren
//    igual contra una base CON la tabla (yosoy_test) y SIN ella (Scripts\crear-base-de-pruebas.ps1 -SinConsentimientoWhatsApp): ahí, si algo la tocara,
//    el aviso quedaría "Error" y el log traería la queja de la base. Para comprobarlo con todo lo demás de la suite:
//        .\Scripts\correr-tests.ps1 -Base yosoy_test_44 -Filtro 'Tabla!=ConsentimientoWhatsApp'
public class ConsentimientoWhatsAppSinTablaServicioTests
{
    private static EscenarioWhatsApp ApagadoYSinTabla()
    {
        var e = new EscenarioWhatsApp(llave: false);
        e.Repo.Falla = new InvalidOperationException("Invalid object name 'ConsentimientoWhatsApp'.");   // la migración 16 no se aplicó
        return e;
    }

    [Fact]
    public void Todo_lo_publico_del_servicio_contesta_con_la_llave_apagada_sin_una_sola_llamada_a_la_tabla()
    {
        var e = ApagadoYSinTabla();
        var yo = EscenarioWhatsApp.Paciente();

        // El perfil y la pantalla de registro.
        Assert.False(e.Servicio.Texto().Disponible);
        Assert.Equal("avisos-apagados", e.Servicio.Obtener(yo).Estado!.Motivo);
        Assert.Equal("avisos-apagados", e.Servicio.Aceptar(yo, e.Version).Error!.Codigo);
        Assert.Equal("avisos-apagados", e.Servicio.Revocar(yo).Error!.Codigo);
        // El registro de un paciente.
        Assert.Equal("avisos-apagados", e.Servicio.ValidarAceptacionEnRegistro(EscenarioWhatsApp.TelefonoDelPaciente, new DateTime(1990, 5, 5), e.Version)!.Codigo);
        Assert.False(e.Servicio.AceptarEnRegistro(EscenarioWhatsApp.IdPaciente, EscenarioWhatsApp.IdUsuario, EscenarioWhatsApp.TelefonoDelPaciente, e.Version));
        // Los envíos y la BAJA.
        Assert.Equal(PermisoEnvioWhatsApp.Apagado, e.Servicio.Evaluar(EscenarioWhatsApp.IdPaciente, EscenarioWhatsApp.TelefonoDelPaciente));
        Assert.Equal(0, e.Servicio.RegistrarBaja(EscenarioWhatsApp.NumeroDelPaciente));

        Assert.Equal(0, e.Repo.Llamadas);                  // la tabla ni se miró
        Assert.Empty(e.Repo.Filas);
        Assert.Empty(e.Auditoria.Registros);
        Assert.Empty(e.Logs.Lineas);                       // y no hubo ni un aviso de "no se pudo guardar" en el log
    }

    [Fact]
    public void El_doble_de_la_tabla_delata_cualquier_toque_con_la_llave_prendida()
    {
        // Para que el "0 llamadas" de arriba no sea vacío: prendida la llave, el mismo recorrido SÍ llega a la tabla (y ahí revienta).
        var e = new EscenarioWhatsApp(llave: true);
        e.Repo.Falla = new InvalidOperationException("Invalid object name 'ConsentimientoWhatsApp'.");

        Assert.Throws<InvalidOperationException>(() => e.Servicio.Evaluar(EscenarioWhatsApp.IdPaciente, EscenarioWhatsApp.TelefonoDelPaciente));
        Assert.Throws<InvalidOperationException>(() => e.Servicio.Obtener(EscenarioWhatsApp.Paciente()));
        Assert.Equal(2, e.Repo.Llamadas);
        Assert.Equal(0, e.Servicio.RegistrarBaja(EscenarioWhatsApp.NumeroDelPaciente));          // este atrapa el error (el registro no se puede caer por esto)...
        Assert.Equal(3, e.Repo.Llamadas);                                                        // ...pero intentó tocar la tabla
    }

    [Fact]
    public void Cada_metodo_publico_nuevo_del_servicio_obliga_a_decidir_que_hace_con_la_llave_apagada()
    {
        // Si se agrega un método público al servicio, esta prueba falla: hay que hacer que con la llave apagada no toque la tabla y sumarlo al recorrido de arriba.
        var publicos = typeof(ConsentimientoWhatsAppService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[]
        {
            "Aceptar", "AceptarEnRegistro", "Evaluar", "Obtener", "RegistrarBaja", "Revocar", "Texto", "ValidarAceptacionEnRegistro"
        }, publicos);
    }

    [Fact]
    public void La_unica_cosa_publica_que_no_es_un_metodo_es_la_llave()
    {
        var propiedades = typeof(ConsentimientoWhatsAppService)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(p => p.Name).ToArray();

        Assert.Equal(new[] { "Habilitado" }, propiedades);
    }
}

public class SinTablaNotificadorYBotTests
{
    private const string Url = "https://yosoy.test/ingreso";

    private sealed class Arnes
    {
        public readonly Escenario E = new();
        public readonly EscenarioWhatsApp Consentimiento = new(llave: false);
        public readonly MensajesChatEnMemoria Mensajes = new();
        public readonly CanalEspia Canal = new();
        public readonly DatosConContactoDelPaciente Datos;
        public readonly RecordatoriosEspia Recordatorios = new();
        public readonly NotificadorTurnos Notificador;

        public Arnes()
        {
            Consentimiento.Repo.Falla = new InvalidOperationException("Invalid object name 'ConsentimientoWhatsApp'.");
            E.Opciones.CurrentValue.UrlIngresoTurno = Url;
            E.Opciones.CurrentValue.AvisosPorWhatsApp = false;
            Datos = new DatosConContactoDelPaciente(new FakeDatos(E.Db), E.Db);
            var mensajes = new ServicioMensajes(Mensajes, Canal, E.Reloj, NullLogger<ServicioMensajes>.Instance);
            // El servicio REAL del consentimiento, con la tabla que no existe, como política de envío (y la llave apagada, como el notificador).
            Notificador = new NotificadorTurnos(Datos, Recordatorios, new InternasQueNoHacenNada(), E.Conversaciones, mensajes, Canal, Consentimiento.Servicio,
                new FakeTurnoRepositorio(E.Db), new ServiceCollection().BuildServiceProvider(), new ConfigurationBuilder().Build(), E.Opciones, E.Reloj,
                NullLogger<NotificadorTurnos>.Instance);
        }
    }

    [Fact]
    public void Los_cuatro_avisos_con_el_servicio_real_y_sin_tabla_quedan_Omitidos_y_no_tocan_la_tabla()
    {
        var a = new Arnes();
        var pendiente = a.E.Db.AgregarTurno(1, 10, Escenario.Jueves0900.AddDays(2), BaseEnMemoria.Pendiente);
        var confirmado = a.E.Db.AgregarTurno(1, 10, Escenario.Jueves0900.AddDays(3), BaseEnMemoria.Confirmado);
        var cancelado = a.E.Db.AgregarTurno(1, 10, Escenario.Jueves0900.AddDays(4), BaseEnMemoria.Cancelado);

        a.Notificador.TurnoSolicitado(pendiente.IdTurno);
        a.Notificador.TurnoAgendadoPorProfesional(1, 10, pendiente.Fecha, pendiente.Hora);
        a.Notificador.TurnoConfirmado(confirmado.IdTurno);
        a.Notificador.TurnoCancelado(cancelado.IdTurno);

        Assert.Equal(0, a.Consentimiento.Repo.Llamadas);
        Assert.Empty(a.Canal.Envios);
        Assert.Equal(4, a.Recordatorios.Resultados.Count(r => r.Canal == "Chat" && r.Estado == "Omitido" && r.Error!.Contains("apagados")));
        Assert.DoesNotContain(a.Recordatorios.Resultados, r => r.Estado == "Error");
        Assert.Equal(4, a.Datos.Campanita.Count);                                     // la campanita sale igual
    }

    [Fact]
    public async Task Una_BAJA_con_la_llave_apagada_no_toca_la_tabla_y_el_asistente_no_contesta()
    {
        var a = new Arnes();
        var bot = new BotReservasService(a.E.Conversaciones, a.E.Mensajes, new FakeDatos(a.E.Db), new FakeFaq(a.E.Db), a.E.Recordatorios, a.E.Reglas,
            a.E.Notificador, new InterpreteMenu(), new FiltroSeguridad(a.E.Opciones), a.E.Opciones, a.E.Reloj, a.Consentimiento.Servicio);

        var r = await bot.ProcesarAsync(Telefono.Normalizar(Escenario.TelJuan), "BAJA", "Simulado");

        Assert.Empty(r.Respuestas);                                                   // no se contesta una BAJA
        Assert.Equal(0, a.Consentimiento.Repo.Llamadas);                              // y no hay aceptaciones que revocar: la tabla ni se mira
        Assert.Empty(a.Consentimiento.Logs.Lineas);
    }
}

[Trait("Categoria", "Integracion")]
public class ConsentimientoWhatsAppSinTablaBaseRealTests
{
    private const string SecretoDePrueba = "secreto-de-prueba-123";

    // Las quejas que dejaría el código si tocara una tabla que no existe.
    private static readonly string[] QuejasDeLaTabla =
    {
        "Invalid object name", "no se pudo registrar la aceptación", "no se pudo revocar en la base", "ConsentimientoWhatsApp'"
    };

    private static bool HayTabla() => DatosDePrueba.Escalar("SELECT OBJECT_ID(N'dbo.ConsentimientoWhatsApp')") is not null;

    private static int FilasDe(int idPaciente) =>
        HayTabla() ? DatosDePrueba.Contar("SELECT COUNT(*) FROM ConsentimientoWhatsApp WHERE IdPaciente = @p", ("@p", idPaciente)) : 0;

    private static WebApplicationFactory<Program> Fabrica(LogsCapturados logs) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            // La llave de la aplicación real: apagada (la de appsettings.json). Solo se agrega el secreto del webhook y se capturan los logs.
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Mensajeria:WhatsApp:Secreto"] = SecretoDePrueba }));
            b.ConfigureLogging(l => l.AddProvider(logs));
        });

    private static string TelefonoUnico() => "11" + Random.Shared.Next(10_000_000, 99_999_999);

    private static void SinQuejas(LogsCapturados logs)
    {
        foreach (var queja in QuejasDeLaTabla)
            Assert.DoesNotContain(queja, logs.Todo);
    }

    private static string[] FilasDeRecordatorio(int idTurno, string tipo)
    {
        var filas = new List<string>();
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new Microsoft.Data.SqlClient.SqlCommand("SELECT Canal, Estado FROM Recordatorio WHERE IdTurno = @t AND Tipo = @tipo ORDER BY Canal", con);
        cmd.Parameters.AddWithValue("@t", idTurno);
        cmd.Parameters.AddWithValue("@tipo", tipo);
        using var r = cmd.ExecuteReader();
        while (r.Read()) filas.Add($"{r.GetString(0)}:{r.GetString(1)}");
        return filas.ToArray();
    }

    [Fact]
    public async Task Registrarse_aunque_se_mande_aceptaWhatsApp_true_crea_la_cuenta_sin_tocar_la_tabla()
    {
        var logs = new LogsCapturados();
        using var fabrica = Fabrica(logs);
        using var cliente = fabrica.CreateClient();
        using var d = new DatosDePrueba();
        var marca = "XTEST-" + Guid.NewGuid().ToString("N")[..10];

        var r = await cliente.PostAsJsonAsync("/pacientes", new
        {
            nombre = "Prueba " + marca, dni = marca, mail = marca.ToLowerInvariant() + "@example.com", usuario = marca, contrasena = "ClaveDePrueba123",
            fechaNacimiento = "1990-05-05", telefono = "011 15 5555-0001", aceptaWhatsApp = true, versionTextoWhatsApp = "v0-provisorio"
        });
        var j = await VideoApp.Json(r);
        if (r.IsSuccessStatusCode)
        {
            d.AlLimpiar("Usuario", "IdUsuario", j.GetProperty("idUsuario").GetInt32());
            d.AlLimpiar("Paciente", "IdPaciente", j.GetProperty("idPaciente").GetInt32());
        }

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.False(j.GetProperty("consentimientoWhatsApp").GetProperty("pedido").GetBoolean());     // los avisos no existen: no se pidió nada
        Assert.Equal("no-disponible", j.GetProperty("consentimientoWhatsApp").GetProperty("estado").GetString());
        Assert.Equal(0, FilasDe(j.GetProperty("idPaciente").GetInt32()));
        SinQuejas(logs);
    }

    [Fact]
    public async Task El_perfil_de_punta_a_punta_con_la_llave_apagada_no_toca_la_tabla()
    {
        var logs = new LogsCapturados();
        using var fabrica = Fabrica(logs);
        using var d = new DatosDePrueba();
        var paciente = d.Paciente();
        DatosDePrueba.Ejecutar("UPDATE Paciente SET Telefono = @t, FechaNacimiento = @f WHERE IdPaciente = @p", ("@t", TelefonoUnico()), ("@f", new DateTime(1990, 5, 5)), ("@p", paciente));
        using var yo = DatosDePrueba.Cliente(fabrica, d.UsuarioDePaciente(paciente), Roles.Paciente, idPaciente: paciente);

        var estado = await VideoApp.Json(await yo.GetAsync("/consentimiento-whatsapp"));
        var texto = await VideoApp.Json(await yo.GetAsync("/consentimiento-whatsapp/texto"));
        var acepta = await yo.PostAsJsonAsync("/consentimiento-whatsapp/aceptar", new { version = "v0-provisorio" });
        var revoca = await yo.PostAsync("/consentimiento-whatsapp/revocar", null);

        Assert.Equal("avisos-apagados", estado.GetProperty("motivo").GetString());
        Assert.False(texto.GetProperty("disponible").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, acepta.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, revoca.StatusCode);
        Assert.Equal(0, FilasDe(paciente));
        SinQuejas(logs);
    }

    [Fact]
    public async Task Los_avisos_de_turno_con_la_llave_apagada_y_los_DAOs_reales_quedan_Omitidos_y_no_Error()
    {
        // Si el servicio del consentimiento tocara la tabla, en una base sin ella el aviso de WhatsApp quedaría "Error" (la base se queja) y no "Omitido".
        var logs = new LogsCapturados();
        using var fabrica = Fabrica(logs);
        using var d = new DatosDePrueba();
        var telefono = TelefonoUnico();
        var momento = new DateTime(2040, 1, 1, 10, 0, 0).AddDays(Random.Shared.Next(0, 3000));
        var paciente = d.Paciente(mail: d.Marca + "@example.com");
        DatosDePrueba.Ejecutar("UPDATE Paciente SET Telefono = @t WHERE IdPaciente = @p", ("@t", telefono), ("@p", paciente));
        d.AlLimpiar("Conversacion", "Telefono", telefono);
        var turno = d.Turno(paciente, d.Profesional(), "Confirmado", momento.Date, momento.TimeOfDay);

        fabrica.Services.GetRequiredService<NotificadorTurnos>().TurnoConfirmado(turno);

        Assert.Equal(new[] { "App:Enviado", "Chat:Omitido" }, FilasDeRecordatorio(turno, "TurnoConfirmado"));     // la campanita sale; WhatsApp, apagado
        Assert.Contains("apagados", (string)DatosDePrueba.Escalar("SELECT Error FROM Recordatorio WHERE IdTurno = @t AND Tipo = 'TurnoConfirmado' AND Canal = 'Chat'", ("@t", turno))!);
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM Notificacion WHERE IdPaciente = @p", ("@p", paciente)));
        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM Conversacion WHERE Telefono = @t", ("@t", telefono)));    // no se armó ninguna conversación
        SinQuejas(logs);
    }

    [Fact]
    public async Task Una_BAJA_por_el_webhook_con_la_llave_apagada_no_toca_la_tabla_y_el_asistente_no_contesta()
    {
        var logs = new LogsCapturados();
        using var fabrica = Fabrica(logs);
        using var d = new DatosDePrueba();
        var telefono = TelefonoUnico();
        var paciente = d.Paciente();
        DatosDePrueba.Ejecutar("UPDATE Paciente SET Telefono = @t WHERE IdPaciente = @p", ("@t", telefono), ("@p", paciente));
        d.AlLimpiar("Conversacion", "Telefono", telefono);
        // Si la tabla existe, una aceptación de antes (de cuando la llave estaba prendida) tiene que seguir como estaba: apagar no revoca nada.
        var hayTabla = HayTabla();
        if (hayTabla)
            new ConsentimientoWhatsAppDAO().Aceptar(paciente, d.UsuarioDePaciente(paciente), telefono, "perfil", "v0-provisorio",
                "ef9676eb31a07d4f2da1dd8fd2890593dbac39e295a43c74ffb1dba004cf63ef", DateTime.UtcNow, "texto-nuevo");
        using var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Add("X-Shared-Secret", SecretoDePrueba);

        var r = await cliente.PostAsJsonAsync("/mensajeria/whatsapp/entrante", new { telefono = "549" + telefono, texto = "BAJA" });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(0, (await VideoApp.Json(r)).GetProperty("respuestas").GetArrayLength());
        if (hayTabla) Assert.True(new ConsentimientoWhatsAppDAO().Ultimo(paciente)!.EstaAbierta);   // la BAJA no llegó a la tabla: el servicio de WhatsApp es quien la respeta
        SinQuejas(logs);
    }
}
