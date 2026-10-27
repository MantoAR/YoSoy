using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// «Sin el consentimiento de tratamiento y telemedicina firmado y vigente no se asigna profesional»: por cada camino por el que se asigna (Admisión, derivar un informe de admisión, el
// Configurador), con datos propios en yosoy_test. Se OMITE si la base no tiene la migración 28.
[Trait("Categoria", "Integracion")]
public class ConsentimientoAsignacionTests : IDisposable
{
    private readonly DatosDePrueba _d = new();
    private readonly EmailServiceEspia _mails = new();
    private readonly WebApplicationFactory<Program> _fabrica;

    public ConsentimientoAsignacionTests()
    {
        _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(s => { s.RemoveAll<EmailService>(); s.AddSingleton<EmailService>(_mails); }));
    }

    public void Dispose() { _fabrica.Dispose(); _d.Dispose(); }

    private HttpClient Admision(int idProfesional)
    {
        var u = _d.UsuarioDeProfesional(idProfesional);
        var token = _fabrica.Services.GetRequiredService<JwtEmisorService>().EmitirModulo(u,
            new SesionModulo(Modulos.Profesional, new[] { Roles.Profesional }, new[] { Privilegios.Admision }, idProfesional));
        return DatosDePrueba.Cliente(_fabrica, token);
    }

    private HttpClient Configurador() => DatosDePrueba.Cliente(_fabrica, _d.Usuario(), Roles.Configurador);

    private string Mail => $"{_d.Marca}@example.test";

    private (int Paciente, int Profesional) Escenario(DateTime? nacimiento = null)
    {
        var pac = _d.Paciente("Ana Gomez " + _d.Marca, mail: Mail);
        if (nacimiento is not null) DatosDePrueba.Ejecutar("UPDATE Paciente SET FechaNacimiento = @f WHERE IdPaciente = @p", ("@f", nacimiento), ("@p", pac));
        return (pac, _d.Profesional());
    }

    private static int Vinculos(int pac) => DatosDePrueba.Contar("SELECT COUNT(*) FROM PacienteProfesional WHERE IdPaciente = @p", ("@p", pac));
    private static int Avisos(int pac) => DatosDePrueba.Contar("SELECT COUNT(*) FROM Notificacion WHERE IdPaciente = @p AND Titulo = @t", ("@p", pac), ("@t", ConsentimientoTratamiento.TituloDelAviso));
    private static async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    [FactConBaseDeConsentimientos]
    public async Task Admision_no_puede_asignar_a_un_paciente_que_no_firmo_y_el_mensaje_dice_que_hacer()
    {
        var (pac, prof) = Escenario();
        var r = await Admision(_d.Profesional()).PostAsJsonAsync($"/pacientes/{pac}/profesionales", new { idPaciente = pac, idProfesional = prof });

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var j = await Json(r);
        Assert.Equal("consentimiento-pendiente", j.GetProperty("codigo").GetString());
        Assert.Equal("No se puede asignar profesional: Ana Gomez " + _d.Marca + " todavía no firmó el consentimiento de tratamiento. Pedile que lo firme y volvé a intentar.", j.GetProperty("mensaje").GetString());
        Assert.Equal(0, Vinculos(pac));
    }

    [FactConBaseDeConsentimientos]
    public async Task El_rechazo_le_avisa_al_paciente_por_campanita_y_mail_una_sola_vez_aunque_se_repita()
    {
        var (pac, prof) = Escenario();
        var admision = Admision(_d.Profesional());
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Conflict, (await admision.PostAsJsonAsync($"/pacientes/{pac}/profesionales", new { idPaciente = pac, idProfesional = prof })).StatusCode);

        Assert.Equal(1, Avisos(pac));                                                // campanita, sin repetir
        var suyos = _mails.Avisos.Where(m => m.Destinatario == Mail).ToList();       // mail, sin repetir
        Assert.Single(suyos);
        Assert.Contains("firmes el consentimiento", suyos[0].Texto);
        // El aviso lleva a la pantalla de firma; no hay camino de WhatsApp en esto.
        Assert.Contains("consentimiento.html", (string)DatosDePrueba.Escalar("SELECT Link FROM Notificacion WHERE IdPaciente = @p AND Titulo = @t", ("@p", pac), ("@t", ConsentimientoTratamiento.TituloDelAviso))!);
    }

    [FactConBaseDeConsentimientos]
    public async Task Cuando_el_paciente_firma_se_puede_asignar()
    {
        var (pac, prof) = Escenario();
        var admision = Admision(_d.Profesional());
        Assert.Equal(HttpStatusCode.Conflict, (await admision.PostAsJsonAsync($"/pacientes/{pac}/profesionales", new { idPaciente = pac, idProfesional = prof })).StatusCode);

        // Firma el propio paciente, desde su sesión.
        var cliente = DatosDePrueba.Cliente(_fabrica, _d.UsuarioDePaciente(pac), Roles.Paciente, idPaciente: pac);
        var firma = await cliente.PostAsJsonAsync($"/pacientes/{pac}/consentimiento-telemedicina", new { acepto = true, version = new ConsentimientoOptions().VersionTexto });
        Assert.Equal(HttpStatusCode.Created, firma.StatusCode);

        var ok = await admision.PostAsJsonAsync($"/pacientes/{pac}/profesionales", new { idPaciente = pac, idProfesional = prof });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(1, Vinculos(pac));
    }

    [FactConBaseDeConsentimientos]
    public async Task Un_profesional_comun_sigue_sin_poder_asignar_y_no_dispara_avisos()
    {
        var (pac, prof) = Escenario();
        var comun = DatosDePrueba.Cliente(_fabrica, _d.UsuarioDeProfesional(prof), Roles.Profesional, idProfesional: prof);
        Assert.Equal(HttpStatusCode.Forbidden, (await comun.PostAsJsonAsync($"/pacientes/{pac}/profesionales", new { idPaciente = pac, idProfesional = prof })).StatusCode);
        Assert.Equal(0, Avisos(pac));
    }

    [FactConBaseDeConsentimientos]
    public async Task El_Configurador_tampoco_asigna_sin_consentimiento_y_si_con_el()
    {
        var (pac, prof) = Escenario();
        var conf = Configurador();
        var r = await conf.PostAsJsonAsync($"/usuarios/pacientes/{pac}/profesionales", new { idProfesionales = new[] { prof } });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("consentimiento-pendiente", (await Json(r)).GetProperty("codigo").GetString());
        Assert.Equal(0, Vinculos(pac));

        _d.ConsentimientoFirmado(pac);
        Assert.Equal(HttpStatusCode.OK, (await conf.PostAsJsonAsync($"/usuarios/pacientes/{pac}/profesionales", new { idProfesionales = new[] { prof } })).StatusCode);
        Assert.Equal(1, Vinculos(pac));
    }

    [FactConBaseDeConsentimientos]
    public async Task Un_paciente_que_no_existe_sigue_dando_404_y_no_409()
    {
        var (_, prof) = Escenario();
        Assert.Equal(HttpStatusCode.NotFound, (await Configurador().PostAsJsonAsync("/usuarios/pacientes/2000000000/profesionales", new { idProfesionales = new[] { prof } })).StatusCode);
    }

    [FactConBaseDeConsentimientos]
    public async Task Derivar_un_informe_de_admision_exige_el_consentimiento_y_no_toca_el_informe()
    {
        var (pac, prof) = Escenario();
        var informe = _d.InformeAdmision(pac, "En entrevista", DateTime.Now, "SALA" + _d.Marca[^6..]);
        _d.Agenda(prof, "Lunes");   // derivar exige que el profesional tenga horarios de atención (rama de Mariana): sin eso responde SIN_AGENDA antes de mirar el consentimiento
        var admision = Admision(_d.Profesional());

        var r = await admision.PutAsJsonAsync($"/informes-admision/{informe}/derivar", new { idProfesional = prof, observaciones = "ficticia" });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("consentimiento-pendiente", (await Json(r)).GetProperty("codigo").GetString());
        Assert.Equal("En entrevista", (string)DatosDePrueba.Escalar("SELECT Estado FROM InformeAdmision WHERE IdInforme = @i", ("@i", informe))!);
        Assert.Equal(0, Vinculos(pac));
        Assert.Equal(1, Avisos(pac));

        // Ya firmado, el consentimiento deja de bloquear. (Antes de la migración 33, lo que sigue de «derivar» fallaba en cualquier base: la consulta que escribía la primera evolución usaba la
        // columna Evolucion.TipoCierre, que ninguna migración creaba. Ya está arreglado.)
        _d.ConsentimientoFirmado(pac);
        var derivada = await admision.PutAsJsonAsync($"/informes-admision/{informe}/derivar", new { idProfesional = prof, observaciones = "ficticia" });
        Assert.Equal(HttpStatusCode.NoContent, derivada.StatusCode);   // desde la migración 33 (y también sin ella: ver Migracion33Tests) deriva sin el 500 de Evolucion.TipoCierre
        Assert.DoesNotContain("consentimiento-pendiente", await derivada.Content.ReadAsStringAsync());
    }

    [FactConBaseDeConsentimientos]
    public async Task CRM_tampoco_confirma_una_derivacion_de_caso_sin_consentimiento()
    {
        var (pac, prof) = Escenario();
        var grupo = Convert.ToInt32(DatosDePrueba.Escalar("INSERT INTO Grupo (Nombre, Activo) VALUES (@n, 1); SELECT CAST(SCOPE_IDENTITY() AS INT);", ("@n", "Grupo " + _d.Marca)));
        _d.AlLimpiar("Grupo", "IdGrupo", grupo);
        var caso = Convert.ToInt32(DatosDePrueba.Escalar("INSERT INTO Caso (IdPaciente, Estado, IdGrupoActual, Origen) VALUES (@p, 'Derivado', @g, 'Admision'); SELECT CAST(SCOPE_IDENTITY() AS INT);", ("@p", pac), ("@g", grupo)));
        _d.AlLimpiar("Caso", "IdCaso", caso);
        var derivacion = Convert.ToInt32(DatosDePrueba.Escalar("INSERT INTO CasoDerivacion (IdCaso, IdGrupo, IdProfesionalSugerido) VALUES (@c, @g, @pr); SELECT CAST(SCOPE_IDENTITY() AS INT);", ("@c", caso), ("@g", grupo), ("@pr", prof)));
        var crm = DatosDePrueba.Cliente(_fabrica, _d.Usuario(), Roles.Crm);

        var r = await crm.PutAsync($"/casosderivaciones/{derivacion}/confirmar", null);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("consentimiento-pendiente", (await Json(r)).GetProperty("codigo").GetString());
        Assert.Equal("Derivado", (string)DatosDePrueba.Escalar("SELECT Estado FROM Caso WHERE IdCaso = @c", ("@c", caso))!);
        Assert.Equal(0, Vinculos(pac));

        // Corregir solo el grupo, sin profesional elegido ni sugerido, no asigna a nadie: no se bloquea.
        DatosDePrueba.Ejecutar("UPDATE CasoDerivacion SET IdProfesionalSugerido = NULL WHERE IdCasoDerivacion = @d", ("@d", derivacion));
        Assert.Equal(HttpStatusCode.NoContent, (await crm.PutAsJsonAsync($"/casosderivaciones/{derivacion}/corregir", new { idGrupo = grupo })).StatusCode);

        // Con el consentimiento firmado, confirmar (con profesional) asigna.
        var derivacion2 = Convert.ToInt32(DatosDePrueba.Escalar("INSERT INTO CasoDerivacion (IdCaso, IdGrupo, IdProfesionalSugerido) VALUES (@c, @g, @pr); SELECT CAST(SCOPE_IDENTITY() AS INT);", ("@c", caso), ("@g", grupo), ("@pr", prof)));
        _d.ConsentimientoFirmado(pac);
        Assert.Equal(HttpStatusCode.NoContent, (await crm.PutAsync($"/casosderivaciones/{derivacion2}/confirmar", null)).StatusCode);
        Assert.Equal(1, Vinculos(pac));
    }

    // ───────────── Cita urgente: la sala no se habilita sin el consentimiento, y el bloqueo queda a la vista ─────────────

    private static int BloqueosDe(int idProfesional, int idTurno) =>
        DatosDePrueba.Contar("SELECT COUNT(*) FROM NotificacionInterna WHERE Destinatario = N'Profesional' AND IdProfesional = @p AND IdTurno = @t AND Tipo = @tipo",
            ("@p", idProfesional), ("@t", idTurno), ("@tipo", BloqueoConsentimiento.Tipo));

    [FactConBaseDeConsentimientos]
    public async Task Generar_la_sala_de_una_cita_urgente_no_saltea_el_consentimiento_y_deja_el_bloqueo()
    {
        var (pac, prof) = Escenario();
        var turno = _d.Turno(pac, prof, "Confirmado", DateTime.Today, TimeSpan.FromHours(10));
        var profesional = DatosDePrueba.Cliente(_fabrica, _d.UsuarioDeProfesional(prof), Roles.Profesional, idProfesional: prof);
        _d.AlLimpiar("NotificacionInterna", "IdTurno", turno);

        var r = await profesional.PutAsync($"/turnos/{turno}/generar-sala", null);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("consentimiento-pendiente", (await Json(r)).GetProperty("codigo").GetString());
        Assert.True(DatosDePrueba.Escalar("SELECT CodigoSala FROM Turno WHERE IdTurno = @t", ("@t", turno)) is null or DBNull);
        Assert.Equal(1, BloqueosDe(prof, turno));      // el profesional lo ve
        Assert.Equal(1, Avisos(pac));                  // y el paciente recibe el aviso para firmar
        Assert.Contains(_mails.Avisos, m => m.Destinatario == Mail);

        // Repetirlo no duplica nada.
        Assert.Equal(HttpStatusCode.Conflict, (await profesional.PutAsync($"/turnos/{turno}/generar-sala", null)).StatusCode);
        Assert.Equal(1, BloqueosDe(prof, turno));
        Assert.Equal(1, Avisos(pac));

        // Con el consentimiento firmado la sala se habilita.
        _d.ConsentimientoFirmado(pac);
        var ok = await profesional.PutAsync($"/turnos/{turno}/generar-sala", null);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.StartsWith("SALA-", (await Json(ok)).GetProperty("codigoSala").GetString());
    }

    [FactConBaseDeConsentimientos]
    public async Task Otro_profesional_no_genera_la_sala_de_un_turno_ajeno()
    {
        var (pac, prof) = Escenario();
        var turno = _d.Turno(pac, prof, "Confirmado", DateTime.Today, TimeSpan.FromHours(10));
        var otro = _d.Profesional();
        var cliente = DatosDePrueba.Cliente(_fabrica, _d.UsuarioDeProfesional(otro), Roles.Profesional, idProfesional: otro);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PutAsync($"/turnos/{turno}/generar-sala", null)).StatusCode);
        Assert.Equal(0, Avisos(pac));
        Assert.Equal(0, BloqueosDe(prof, turno));
    }

    [FactConBaseDeConsentimientos]
    public async Task La_pantalla_de_admision_muestra_firmado_o_pendiente()
    {
        var (pac, _) = Escenario();
        var informe = _d.InformeAdmision(pac, "Pendiente", null, null);
        DatosDePrueba.Ejecutar("UPDATE InformeAdmision SET IdEspecialidad = @e WHERE IdInforme = @i", ("@e", _d.Especialidad()), ("@i", informe));
        var admision = Admision(_d.Profesional());

        async Task<(bool Firmado, string? Motivo)> Leer()
        {
            var lista = await Json(await admision.GetAsync("/informes-admision-detalle"));
            var fila = lista.EnumerateArray().Single(x => x.GetProperty("idInforme").GetInt32() == informe);
            var motivo = fila.GetProperty("consentimientoMotivo");
            return (fila.GetProperty("consentimientoFirmado").GetBoolean(), motivo.ValueKind == JsonValueKind.Null ? null : motivo.GetString());
        }

        Assert.Equal((false, "sin-firma"), await Leer());
        _d.ConsentimientoFirmado(pac);
        Assert.Equal((true, null), await Leer());
    }

    [FactConBaseDeConsentimientos]
    public async Task Para_un_menor_hace_falta_la_firma_del_tutor_y_el_mensaje_lo_dice()
    {
        var (pac, prof) = Escenario(nacimiento: DateTime.Today.AddYears(-12));
        var tutor = Convert.ToInt32(DatosDePrueba.Escalar("INSERT INTO Tutor (Nombre, Dni, Vinculo, Contacto) VALUES (@n, @d, N'Madre', N'x'); SELECT CAST(SCOPE_IDENTITY() AS INT);", ("@n", "Tutor " + _d.Marca), ("@d", _d.Marca + "t")));
        _d.AlLimpiar("Tutor", "IdTutor", tutor);
        DatosDePrueba.Ejecutar("INSERT INTO PacienteTutor (IdPaciente, IdTutor) VALUES (@p, @t)", ("@p", pac), ("@t", tutor));
        var conf = Configurador();

        _d.ConsentimientoFirmado(pac);   // la firma del propio menor no alcanza
        var r = await conf.PostAsJsonAsync($"/usuarios/pacientes/{pac}/profesionales", new { idProfesionales = new[] { prof } });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var j = await Json(r);
        Assert.Equal(ConsentimientoTratamiento.FaltaTutor, j.GetProperty("motivo").GetString());
        Assert.Contains("menor de edad", j.GetProperty("mensaje").GetString());

        // Firma el tutor desde la sesión del menor, eligiendo a quién.
        var cliente = DatosDePrueba.Cliente(_fabrica, _d.UsuarioDePaciente(pac), Roles.Paciente, idPaciente: pac);
        var version = new ConsentimientoOptions().VersionTexto;
        var sinTutor = await cliente.PostAsJsonAsync($"/pacientes/{pac}/consentimiento-telemedicina", new { acepto = true, version });
        Assert.Equal(HttpStatusCode.BadRequest, sinTutor.StatusCode);   // un menor tiene que decir quién firma
        var conTutor = await cliente.PostAsJsonAsync($"/pacientes/{pac}/consentimiento-telemedicina", new { acepto = true, version, idTutor = tutor });
        Assert.Equal(HttpStatusCode.Created, conTutor.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await conf.PostAsJsonAsync($"/usuarios/pacientes/{pac}/profesionales", new { idProfesionales = new[] { prof } })).StatusCode);
    }
}
