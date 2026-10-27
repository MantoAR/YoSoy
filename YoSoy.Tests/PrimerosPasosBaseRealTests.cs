using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>Se OMITE si la base no tiene la migración 31 (primeros pasos) o la 28 (consentimientos del circuito).</summary>
public sealed class FactConBaseDePrimerosPasosAttribute : FactAttribute
{
    public FactConBaseDePrimerosPasosAttribute()
    {
        var motivo = BaseDeConsentimiento.NoDisponible();
        if (motivo is null && !Migracion28.Disponible()) motivo = "La base no tiene la migración migrations/add_consentimientos_circuito.sql.";
        if (motivo is null && Convert.ToInt32(DatosDePrueba.Escalar(
                "SELECT CASE WHEN COL_LENGTH('dbo.InformeAdmision', 'AclaracionRecepcion') IS NOT NULL AND COL_LENGTH('dbo.Consentimiento', 'FechaRevocacion') IS NOT NULL AND COL_LENGTH('dbo.Notificacion', 'Evento') IS NOT NULL THEN 1 ELSE 0 END")) != 1)
            motivo = "La base no tiene la migración migrations/add_primeros_pasos.sql.";
        if (motivo is not null) Skip = motivo;
    }
}

// Revisión del formulario por la recepcionista (aceptar, devolver, otro recurso), reenvío del paciente, preferencias, revocación y vigencia del consentimiento,
// con datos propios en yosoy_test.
[Trait("Categoria", "Integracion")]
public class PrimerosPasosBaseRealTests : IDisposable
{
    private readonly DatosDePrueba _d = new();
    private readonly EmailServiceEspia _mails = new();
    private readonly WebApplicationFactory<Program> _fabrica;

    public PrimerosPasosBaseRealTests()
    {
        _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(s => { s.RemoveAll<EmailService>(); s.AddSingleton<EmailService>(_mails); }));
    }

    public void Dispose() { _fabrica.Dispose(); _d.Dispose(); }

    private HttpClient Recepcion(int idProfesional)
    {
        var u = _d.UsuarioDeProfesional(idProfesional);
        var token = _fabrica.Services.GetRequiredService<JwtEmisorService>().EmitirModulo(u,
            new SesionModulo(Modulos.Profesional, new[] { Roles.Profesional }, new[] { Privilegios.Admision }, idProfesional));
        return DatosDePrueba.Cliente(_fabrica, token);
    }

    // Un paciente con sus datos completos (fecha de nacimiento y teléfono): así el primer paso del recorrido ya está hecho.
    private int NuevoPaciente(string? nombre = null, string? mail = null)
    {
        var pac = _d.Paciente(nombre, mail: mail);
        DatosDePrueba.Ejecutar("UPDATE Paciente SET FechaNacimiento = @f, Telefono = N'11 5555-0001' WHERE IdPaciente = @p", ("@f", new DateTime(1990, 5, 20)), ("@p", pac));
        return pac;
    }

    private HttpClient Paciente(int idPaciente) => DatosDePrueba.Cliente(_fabrica, _d.UsuarioDePaciente(idPaciente), Roles.Paciente, idPaciente: idPaciente);

    private static async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
    private static readonly object Confirmo = new { confirmo = true };

    private (int Paciente, int Informe, HttpClient Recep) Escenario(string estado = "Pendiente")
    {
        var pac = NuevoPaciente("Ana Gomez " + _d.Marca, mail: $"{_d.Marca}@example.test");
        var informe = _d.InformeAdmision(pac, estado, null, null);
        DatosDePrueba.Ejecutar("UPDATE InformeAdmision SET IdEspecialidad = @e WHERE IdInforme = @i", ("@e", _d.Especialidad()), ("@i", informe));
        return (pac, informe, Recepcion(_d.Profesional()));
    }

    private static string EstadoDe(int informe) => (string)DatosDePrueba.Escalar("SELECT Estado FROM InformeAdmision WHERE IdInforme = @i", ("@i", informe))!;
    private static int Avisos(int pac, string titulo) => DatosDePrueba.Contar("SELECT COUNT(*) FROM Notificacion WHERE IdPaciente = @p AND Titulo = @t", ("@p", pac), ("@t", titulo));
    private static int AvisosTotales(int pac) => DatosDePrueba.Contar("SELECT COUNT(*) FROM Notificacion WHERE IdPaciente = @p", ("@p", pac));

    // ───────────── Aceptar ─────────────

    [FactConBaseDePrimerosPasos]
    public async Task Aceptar_pasa_el_informe_a_aprobado_con_quien_y_cuando_y_avisa_para_firmar_sin_duplicar()
    {
        var (pac, informe, recep) = Escenario();

        var r = await recep.PutAsJsonAsync($"/informes-admision/{informe}/aceptar", Confirmo);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("Aprobado", EstadoDe(informe));
        Assert.NotNull(DatosDePrueba.Escalar("SELECT IdUsuarioRevision FROM InformeAdmision WHERE IdInforme = @i", ("@i", informe)) as int?);
        Assert.NotNull(DatosDePrueba.Escalar("SELECT FechaRevision FROM InformeAdmision WHERE IdInforme = @i", ("@i", informe)) as DateTime?);
        Assert.Equal(1, Avisos(pac, "Aceptamos tu formulario"));
        // Sin consentimiento vigente: el aviso para firmar de la etapa 3, una sola vez.
        Assert.Equal(1, Avisos(pac, ConsentimientoTratamiento.TituloDelAviso));
        Assert.Contains(_mails.Avisos, m => m.Destinatario == $"{_d.Marca}@example.test");

        // Repetir no cambia nada ni duplica avisos.
        var otra = await recep.PutAsJsonAsync($"/informes-admision/{informe}/aceptar", Confirmo);
        Assert.Equal(HttpStatusCode.OK, otra.StatusCode);
        Assert.False((await Json(otra)).GetProperty("cambio").GetBoolean());
        Assert.Equal(1, Avisos(pac, "Aceptamos tu formulario"));
        Assert.Equal(1, Avisos(pac, ConsentimientoTratamiento.TituloDelAviso));
    }

    [FactConBaseDePrimerosPasos]
    public async Task Aceptar_con_el_consentimiento_vigente_no_manda_el_aviso_para_firmar()
    {
        var (pac, informe, recep) = Escenario();
        _d.ConsentimientoFirmado(pac);
        Assert.Equal(HttpStatusCode.OK, (await recep.PutAsJsonAsync($"/informes-admision/{informe}/aceptar", Confirmo)).StatusCode);
        Assert.Equal(0, Avisos(pac, ConsentimientoTratamiento.TituloDelAviso));
    }

    [FactConBaseDePrimerosPasos]
    public async Task Sin_confirmacion_explicita_no_se_hace_nada()
    {
        var (pac, informe, recep) = Escenario();
        foreach (var accion in new[] { "aceptar", "devolver", "otro-recurso" })
        {
            var r = await recep.PutAsJsonAsync($"/informes-admision/{informe}/{accion}", new { aclaracion = "x", motivo = "x" });
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }
        Assert.Equal("Pendiente", EstadoDe(informe));
        Assert.Equal(0, AvisosTotales(pac));
    }

    [FactConBaseDePrimerosPasos]
    public async Task Solo_Admision_revisa_el_formulario_no_el_paciente_ni_otro_profesional()
    {
        var (pac, informe, _) = Escenario();
        var otroProfesional = _d.Profesional();
        var comun = DatosDePrueba.Cliente(_fabrica, _d.UsuarioDeProfesional(otroProfesional), Roles.Profesional, idProfesional: otroProfesional);
        foreach (var cliente in new[] { Paciente(pac), comun })
            foreach (var accion in new[] { "aceptar", "devolver", "otro-recurso" })
                Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PutAsJsonAsync($"/informes-admision/{informe}/{accion}", new { confirmo = true, aclaracion = "x", motivo = "x" })).StatusCode);
        Assert.Equal("Pendiente", EstadoDe(informe));
    }

    [FactConBaseDePrimerosPasos]
    public async Task La_recepcionista_del_modulo_CRM_tambien_revisa_los_formularios()
    {
        var (pac, informe, _) = Escenario();
        // Migración 36: sin el privilegio Admisión, la recepcionista no ve los formularios «Sobre mí» (tienen datos sensibles).
        var crmSinPrivilegio = DatosDePrueba.Cliente(_fabrica, _d.Usuario(), Roles.Crm);
        Assert.Equal(HttpStatusCode.Forbidden, (await crmSinPrivilegio.GetAsync("/informes-admision-detalle")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await crmSinPrivilegio.PutAsJsonAsync($"/informes-admision/{informe}/aceptar", Confirmo)).StatusCode);
        // Con el privilegio Admisión (la admisionista), sí.
        var crm = DatosDePrueba.Cliente(_fabrica, _fabrica.Services.GetRequiredService<JwtEmisorService>().EmitirModulo(_d.Usuario(),
            new SesionModulo(Modulos.Crm, new[] { Roles.Crm }, new[] { Privilegios.Admision }, null)));
        var lista = await Json(await crm.GetAsync("/informes-admision-detalle"));
        Assert.Contains(lista.EnumerateArray(), x => x.GetProperty("idInforme").GetInt32() == informe);
        Assert.Equal(HttpStatusCode.OK, (await crm.PutAsJsonAsync($"/informes-admision/{informe}/devolver", new { confirmo = true, aclaracion = "Falta el motivo" })).StatusCode);
        Assert.Equal("Devuelto", EstadoDe(informe));
        // El Configurador solo (sin CRM) sigue sin poder revisar.
        var configurador = DatosDePrueba.Cliente(_fabrica, _d.Usuario(), Roles.Configurador);
        Assert.Equal(HttpStatusCode.Forbidden, (await configurador.PutAsJsonAsync($"/informes-admision/{informe}/aceptar", Confirmo)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await configurador.GetAsync("/informes-admision-detalle")).StatusCode);
    }

    [FactConBaseDePrimerosPasos]
    public async Task Aceptar_un_informe_que_no_existe_da_404()
    {
        var (_, _, recep) = Escenario();
        Assert.Equal(HttpStatusCode.NotFound, (await recep.PutAsJsonAsync("/informes-admision/2147000000/aceptar", Confirmo)).StatusCode);
    }

    // ───────────── Devolver ─────────────

    [FactConBaseDePrimerosPasos]
    public async Task Devolver_exige_la_aclaracion_y_el_paciente_la_ve_en_sus_pasos()
    {
        var (pac, informe, recep) = Escenario();
        Assert.Equal(HttpStatusCode.BadRequest, (await recep.PutAsJsonAsync($"/informes-admision/{informe}/devolver", new { confirmo = true, aclaracion = "   " })).StatusCode);
        Assert.Equal("Pendiente", EstadoDe(informe));

        var r = await recep.PutAsJsonAsync($"/informes-admision/{informe}/devolver", new { confirmo = true, aclaracion = "Contanos hace cuánto sentís esto." });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("Devuelto", EstadoDe(informe));
        Assert.Equal(1, Avisos(pac, "Necesitamos que aclares algo en tu formulario"));

        _d.ConsentimientoFirmado(pac);
        var pasos = await Json(await Paciente(pac).GetAsync($"/pacientes/{pac}/primeros-pasos"));
        Assert.Equal("formulario", pasos.GetProperty("pasoActual").GetString());
        Assert.Equal("completar-formulario", pasos.GetProperty("principal").GetProperty("tipo").GetString());
        Assert.Contains("hace cuánto sentís esto", pasos.GetProperty("pasos").EnumerateArray().Single(p => p.GetProperty("clave").GetString() == "formulario").GetProperty("detalle").GetString());

        // Repetir la devolución no avisa de nuevo.
        var otra = await recep.PutAsJsonAsync($"/informes-admision/{informe}/devolver", new { confirmo = true, aclaracion = "Otra cosa" });
        Assert.False((await Json(otra)).GetProperty("cambio").GetBoolean());
        Assert.Equal(1, Avisos(pac, "Necesitamos que aclares algo en tu formulario"));
    }

    [FactConBaseDePrimerosPasos]
    public async Task El_paciente_completa_de_nuevo_el_formulario_devuelto_y_vuelve_a_revision()
    {
        var (pac, informe, recep) = Escenario();
        var paciente = Paciente(pac);

        // Todavía en revisión: no se puede reenviar.
        var temprano = await paciente.PutAsJsonAsync($"/pacientes/{pac}/informe-admision", new { motivoConsulta = "x" });
        Assert.Equal(HttpStatusCode.Conflict, temprano.StatusCode);

        await recep.PutAsJsonAsync($"/informes-admision/{informe}/devolver", new { confirmo = true, aclaracion = "Falta el motivo" });
        var prof = _d.Profesional();
        var r = await paciente.PutAsJsonAsync($"/pacientes/{pac}/informe-admision", new
        {
            idEspecialidad = _d.Especialidad(), motivoConsulta = "Ansiedad desde hace meses", tratamientoAnterior = false, recibeMedicacion = false,
            horarioPreferido = new { dias = new[] { "lunes", "miercoles" }, franja = "tarde" },
            profesionalesPreferidos = new { cualquiera = false, ids = new[] { prof } }
        });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("Pendiente", EstadoDe(informe));
        Assert.Equal("Ansiedad desde hace meses", (string)DatosDePrueba.Escalar("SELECT MotivoConsulta FROM InformeAdmision WHERE IdInforme = @i", ("@i", informe))!);
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM InformeAdmision WHERE IdPaciente = @p", ("@p", pac)));   // sigue siendo el mismo informe

        // Reenviar dos veces: la segunda ya no corresponde.
        Assert.Equal(HttpStatusCode.Conflict, (await paciente.PutAsJsonAsync($"/pacientes/{pac}/informe-admision", new { motivoConsulta = "otra vez" })).StatusCode);
    }

    [FactConBaseDePrimerosPasos]
    public async Task Otro_paciente_no_completa_el_formulario_ajeno()
    {
        var (pac, informe, recep) = Escenario();
        await recep.PutAsJsonAsync($"/informes-admision/{informe}/devolver", new { confirmo = true, aclaracion = "x" });
        var otro = NuevoPaciente();
        Assert.Equal(HttpStatusCode.Forbidden, (await Paciente(otro).PutAsJsonAsync($"/pacientes/{pac}/informe-admision", new { motivoConsulta = "x" })).StatusCode);
        Assert.Equal("Devuelto", EstadoDe(informe));
    }

    // ───────────── Otro recurso ─────────────

    [FactConBaseDePrimerosPasos]
    public async Task Otro_recurso_cierra_el_ingreso_con_motivo_y_es_el_unico_no_ingresa()
    {
        var (pac, informe, recep) = Escenario();
        var caso = Convert.ToInt32(DatosDePrueba.Escalar("INSERT INTO Caso (IdPaciente, Estado, Origen, IdInforme) VALUES (@p, 'Nuevo', 'Admision', @i); SELECT CAST(SCOPE_IDENTITY() AS INT);", ("@p", pac), ("@i", informe)));
        _d.AlLimpiar("Caso", "IdCaso", caso);

        Assert.Equal(HttpStatusCode.BadRequest, (await recep.PutAsJsonAsync($"/informes-admision/{informe}/otro-recurso", new { confirmo = true, motivo = "" })).StatusCode);
        Assert.Equal("Pendiente", EstadoDe(informe));

        var r = await recep.PutAsJsonAsync($"/informes-admision/{informe}/otro-recurso", new { confirmo = true, motivo = "Necesita una guardia psiquiátrica" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("Otro recurso", EstadoDe(informe));
        Assert.Equal("Necesita una guardia psiquiátrica", (string)DatosDePrueba.Escalar("SELECT MotivoRechazo FROM InformeAdmision WHERE IdInforme = @i", ("@i", informe))!);
        Assert.Equal("Cerrado", (string)DatosDePrueba.Escalar("SELECT Estado FROM Caso WHERE IdCaso = @c", ("@c", caso))!);
        Assert.Equal(1, Avisos(pac, "Sobre tu ingreso a YoSoy"));

        // El paciente ve el cierre con el motivo y ningún botón.
        _d.ConsentimientoFirmado(pac);
        var pasos = await Json(await Paciente(pac).GetAsync($"/pacientes/{pac}/primeros-pasos"));
        Assert.True(pasos.GetProperty("cerrado").GetBoolean());
        Assert.Equal("Necesita una guardia psiquiátrica", pasos.GetProperty("motivoCierre").GetString());
        Assert.Equal(JsonValueKind.Null, pasos.GetProperty("principal").ValueKind);

        // Idempotente, y ya no se puede aceptar ni devolver.
        Assert.False((await Json(await recep.PutAsJsonAsync($"/informes-admision/{informe}/otro-recurso", new { confirmo = true, motivo = "Otro" }))).GetProperty("cambio").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await recep.PutAsJsonAsync($"/informes-admision/{informe}/aceptar", Confirmo)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await recep.PutAsJsonAsync($"/informes-admision/{informe}/devolver", new { confirmo = true, aclaracion = "x" })).StatusCode);
        Assert.Equal(1, Avisos(pac, "Sobre tu ingreso a YoSoy"));
    }

    // ───────────── Formulario: preferencias y un solo informe ─────────────

    private static object Formulario(int especialidad, object? horario = null, object? profesionales = null) => new
    {
        idEspecialidad = especialidad, motivoConsulta = "Quiero empezar terapia", tratamientoAnterior = false, recibeMedicacion = false,
        horarioPreferido = horario, profesionalesPreferidos = profesionales
    };

    [FactConBaseDePrimerosPasos]
    public async Task El_formulario_guarda_el_horario_y_los_profesionales_preferidos_y_la_recepcionista_los_ve()
    {
        var pac = NuevoPaciente();
        var prof = _d.Profesional(nombre: "Marta");
        var esp = _d.Especialidad();
        var r = await Paciente(pac).PostAsJsonAsync("/informes-admision", Formulario(esp,
            new { dias = new[] { "martes", "jueves" }, franja = "manana" }, new { cualquiera = false, ids = new[] { prof } }));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var informe = (await Json(r)).GetProperty("idInforme").GetInt32();
        _d.AlLimpiar("InformeAdmision", "IdInforme", informe);

        var propio = await Json(await Paciente(pac).GetAsync($"/pacientes/{pac}/informe-admision"));
        Assert.Equal("manana", propio.GetProperty("horarioPreferido").GetProperty("franja").GetString());
        Assert.Equal(prof, propio.GetProperty("profesionalesPreferidos").GetProperty("ids")[0].GetInt32());

        var recep = Recepcion(_d.Profesional());
        var lista = await Json(await recep.GetAsync("/informes-admision-detalle"));
        var fila = lista.EnumerateArray().Single(x => x.GetProperty("idInforme").GetInt32() == informe);
        Assert.Equal("jueves", fila.GetProperty("horarioPreferido").GetProperty("dias")[1].GetString());
        Assert.Contains("Marta", fila.GetProperty("profesionalesPreferidosNombres")[0].GetString());
    }

    [FactConBaseDePrimerosPasos]
    public async Task El_me_da_igual_se_guarda_sin_profesionales_puntuales()
    {
        var pac = NuevoPaciente();
        var r = await Paciente(pac).PostAsJsonAsync("/informes-admision", Formulario(_d.Especialidad(), null, new { cualquiera = true, ids = Array.Empty<int>() }));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        _d.AlLimpiar("InformeAdmision", "IdInforme", (await Json(r)).GetProperty("idInforme").GetInt32());
        Assert.Contains("\"cualquiera\":true", (string)DatosDePrueba.Escalar("SELECT ProfesionalesPreferidos FROM InformeAdmision WHERE IdPaciente = @p", ("@p", pac))!);
    }

    [FactConBaseDePrimerosPasos]
    public async Task Preferencias_invalidas_se_rechazan_con_un_mensaje_claro_y_no_crean_el_formulario()
    {
        var pac = NuevoPaciente();
        var paciente = Paciente(pac);
        var esp = _d.Especialidad();

        var dia = await paciente.PostAsJsonAsync("/informes-admision", Formulario(esp, new { dias = new[] { "funday" }, franja = "tarde" }));
        Assert.Equal(HttpStatusCode.BadRequest, dia.StatusCode);
        Assert.Contains("días de la semana", (await Json(dia)).GetProperty("mensaje").GetString());

        var franja = await paciente.PostAsJsonAsync("/informes-admision", Formulario(esp, new { dias = new[] { "lunes" }, franja = "madrugada" }));
        Assert.Equal(HttpStatusCode.BadRequest, franja.StatusCode);

        var inexistente = await paciente.PostAsJsonAsync("/informes-admision", Formulario(esp, null, new { cualquiera = false, ids = new[] { 2147000000 } }));
        Assert.Equal(HttpStatusCode.BadRequest, inexistente.StatusCode);

        var inactivo = _d.Profesional(estado: "Inactivo");
        var baja = await paciente.PostAsJsonAsync("/informes-admision", Formulario(esp, null, new { cualquiera = false, ids = new[] { inactivo } }));
        Assert.Equal(HttpStatusCode.BadRequest, baja.StatusCode);

        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM InformeAdmision WHERE IdPaciente = @p", ("@p", pac)));
    }

    [FactConBaseDePrimerosPasos]
    public async Task Un_paciente_no_puede_mandar_dos_formularios()
    {
        var pac = NuevoPaciente();
        var paciente = Paciente(pac);
        var primero = await paciente.PostAsJsonAsync("/informes-admision", Formulario(_d.Especialidad()));
        Assert.Equal(HttpStatusCode.Created, primero.StatusCode);
        _d.AlLimpiar("InformeAdmision", "IdInforme", (await Json(primero)).GetProperty("idInforme").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await paciente.PostAsJsonAsync("/informes-admision", Formulario(_d.Especialidad()))).StatusCode);
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM InformeAdmision WHERE IdPaciente = @p", ("@p", pac)));
    }

    [FactConBaseDePrimerosPasos]
    public async Task Los_profesionales_elegibles_traen_nombre_y_especialidad_y_no_al_admisionista_interno()
    {
        var pac = NuevoPaciente();
        var prof = _d.Profesional(nombre: "Marta");
        var lista = await Json(await Paciente(pac).GetAsync("/profesionales/elegibles"));
        var fila = lista.EnumerateArray().Single(x => x.GetProperty("idProfesional").GetInt32() == prof);
        Assert.Contains("Marta", fila.GetProperty("nombre").GetString());
        Assert.False(string.IsNullOrWhiteSpace(fila.GetProperty("especialidad").GetString()));
    }

    // ───────────── Asignar después de aceptar ─────────────

    [FactConBaseDePrimerosPasos]
    public async Task Despues_de_aceptar_la_recepcionista_asigna_sin_entrevista_y_sin_tocar_la_historia_clinica()
    {
        var (pac, informe, recep) = Escenario();
        _d.ConsentimientoFirmado(pac);
        var prof = _d.Profesional();
        _d.Agenda(prof, "Lunes");

        // Sin aceptar todavía: no se asigna.
        var antes = await recep.PutAsJsonAsync($"/informes-admision/{informe}/derivar", new { idProfesional = prof });
        Assert.Equal(HttpStatusCode.Conflict, antes.StatusCode);
        Assert.Equal("FORMULARIO_SIN_ACEPTAR", (await Json(antes)).GetProperty("codigo").GetString());

        await recep.PutAsJsonAsync($"/informes-admision/{informe}/aceptar", Confirmo);
        var r = await recep.PutAsJsonAsync($"/informes-admision/{informe}/derivar", new { idProfesional = prof });
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.Equal("Derivado", EstadoDe(informe));
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM PacienteProfesional WHERE IdPaciente = @p AND IdProfesional = @r", ("@p", pac), ("@r", prof)));
        // El formulario no abre ni crea la historia clínica.
        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM HistoriaClinica WHERE IdPaciente = @p", ("@p", pac)));

        var pasos = await Json(await Paciente(pac).GetAsync($"/pacientes/{pac}/primeros-pasos"));
        Assert.Equal(prof, pasos.GetProperty("profesional").GetProperty("idProfesional").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(pasos.GetProperty("profesional").GetProperty("especialidad").GetString()));
    }

    [FactConBaseDePrimerosPasos]
    public async Task Asignar_con_el_consentimiento_revocado_se_bloquea()
    {
        var (pac, informe, recep) = Escenario("Aprobado");
        _d.ConsentimientoFirmado(pac);
        var prof = _d.Profesional();
        _d.Agenda(prof, "Lunes");
        Assert.Equal(HttpStatusCode.OK, (await Paciente(pac).PostAsJsonAsync($"/pacientes/{pac}/consentimiento-telemedicina/revocar", Confirmo)).StatusCode);

        var r = await recep.PutAsJsonAsync($"/informes-admision/{informe}/derivar", new { idProfesional = prof });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("consentimiento-pendiente", (await Json(r)).GetProperty("codigo").GetString());
        Assert.Equal("Aprobado", EstadoDe(informe));
    }

    // ───────────── Revocar el consentimiento ─────────────

    private static int FilasDeConsentimiento(int pac) => DatosDePrueba.Contar("SELECT COUNT(*) FROM Consentimiento WHERE IdPaciente = @p", ("@p", pac));

    [FactConBaseDePrimerosPasos]
    public async Task Revocar_deja_la_constancia_no_borra_la_fila_y_deja_de_ser_vigente()
    {
        var pac = NuevoPaciente("Luis Perez " + _d.Marca);
        _d.ConsentimientoFirmado(pac);
        var paciente = Paciente(pac);
        Assert.True((await Json(await paciente.GetAsync($"/pacientes/{pac}/consentimiento-telemedicina"))).GetProperty("firmado").GetBoolean());

        Assert.Equal(HttpStatusCode.BadRequest, (await paciente.PostAsJsonAsync($"/pacientes/{pac}/consentimiento-telemedicina/revocar", new { })).StatusCode);
        Assert.True((await Json(await paciente.GetAsync($"/pacientes/{pac}/consentimiento-telemedicina"))).GetProperty("firmado").GetBoolean());

        var r = await paciente.PostAsJsonAsync($"/pacientes/{pac}/consentimiento-telemedicina/revocar", Confirmo);
        Assert.True(r.StatusCode == HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        Assert.True((await Json(r)).GetProperty("revocado").GetBoolean());
        Assert.Equal(1, FilasDeConsentimiento(pac));   // no se borró
        Assert.Equal("Inactivo", (string)DatosDePrueba.Escalar("SELECT Estado FROM Consentimiento WHERE IdPaciente = @p", ("@p", pac))!);
        Assert.NotNull(DatosDePrueba.Escalar("SELECT FechaRevocacion FROM Consentimiento WHERE IdPaciente = @p", ("@p", pac)) as DateTime?);
        Assert.NotNull(DatosDePrueba.Escalar("SELECT IdUsuarioRevocacion FROM Consentimiento WHERE IdPaciente = @p", ("@p", pac)) as int?);
        Assert.False((await Json(await paciente.GetAsync($"/pacientes/{pac}/consentimiento-telemedicina"))).GetProperty("firmado").GetBoolean());
        // La misma función única de «vigente» lo dice para todo el sistema.
        Assert.False(ConsentimientoTratamiento.Estado(pac, new ConsentimientoOptions()).Vigente);

        // Revocar de nuevo no hace nada de más.
        var otra = await Json(await paciente.PostAsJsonAsync($"/pacientes/{pac}/consentimiento-telemedicina/revocar", Confirmo));
        Assert.False(otra.GetProperty("revocado").GetBoolean());
        Assert.Equal(1, FilasDeConsentimiento(pac));
    }

    [FactConBaseDePrimerosPasos]
    public async Task Revocar_avisa_a_la_recepcionista_y_al_profesional_de_los_turnos_futuros_sin_cancelarlos()
    {
        var pac = NuevoPaciente("Luis Perez " + _d.Marca);
        var prof = _d.Profesional();
        _d.ConsentimientoFirmado(pac);
        var futuro = _d.Turno(pac, prof, "Confirmado", DateTime.Today.AddDays(5), TimeSpan.FromHours(10));
        var pasado = _d.Turno(pac, prof, "Confirmado", DateTime.Today.AddDays(-5), TimeSpan.FromHours(10));
        var pendiente = _d.Turno(pac, prof, "Pendiente", DateTime.Today.AddDays(6), TimeSpan.FromHours(10));
        _d.AlLimpiar("NotificacionInterna", "IdProfesional", prof);
        var antesAdministrador = DatosDePrueba.Contar("SELECT COUNT(*) FROM NotificacionInterna WHERE Destinatario = N'Administrador' AND Tipo = N'consentimiento-revocado'");

        var r = await Json(await Paciente(pac).PostAsJsonAsync($"/pacientes/{pac}/consentimiento-telemedicina/revocar", Confirmo));
        Assert.Equal(1, r.GetProperty("turnosAvisadosAlProfesional").GetInt32());

        // Recepcionista: aviso en su campanita (Administrador). Profesional: uno solo, por el turno futuro confirmado.
        Assert.Equal(antesAdministrador + 1, DatosDePrueba.Contar("SELECT COUNT(*) FROM NotificacionInterna WHERE Destinatario = N'Administrador' AND Tipo = N'consentimiento-revocado'"));
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM NotificacionInterna WHERE Destinatario = N'Profesional' AND IdProfesional = @p AND Tipo = N'consentimiento-revocado'", ("@p", prof)));
        // Los turnos no se tocaron.
        Assert.Equal("Confirmado", (string)DatosDePrueba.Escalar("SELECT et.Descripcion FROM Turno t JOIN EstadoTurno et ON et.IdEstadoTurno = t.IdEstadoTurno WHERE t.IdTurno = @t", ("@t", futuro))!);
        Assert.Equal("Confirmado", (string)DatosDePrueba.Escalar("SELECT et.Descripcion FROM Turno t JOIN EstadoTurno et ON et.IdEstadoTurno = t.IdEstadoTurno WHERE t.IdTurno = @t", ("@t", pasado))!);
        Assert.Equal("Pendiente", (string)DatosDePrueba.Escalar("SELECT et.Descripcion FROM Turno t JOIN EstadoTurno et ON et.IdEstadoTurno = t.IdEstadoTurno WHERE t.IdTurno = @t", ("@t", pendiente))!);
        // Nada por WhatsApp: ningún aviso al paciente salió por mail por este motivo.
        Assert.Empty(_mails.Avisos);
    }

    [FactConBaseDePrimerosPasos]
    public async Task Solo_el_propio_paciente_revoca_su_consentimiento()
    {
        var pac = NuevoPaciente();
        _d.ConsentimientoFirmado(pac);
        var otro = NuevoPaciente();
        Assert.Equal(HttpStatusCode.Forbidden, (await Paciente(otro).PostAsJsonAsync($"/pacientes/{pac}/consentimiento-telemedicina/revocar", Confirmo)).StatusCode);
        var profesional = _d.Profesional();
        var comun = DatosDePrueba.Cliente(_fabrica, _d.UsuarioDeProfesional(profesional), Roles.Profesional, idProfesional: profesional);
        Assert.Equal(HttpStatusCode.Forbidden, (await comun.PostAsJsonAsync($"/pacientes/{pac}/consentimiento-telemedicina/revocar", Confirmo)).StatusCode);
        Assert.True(ConsentimientoTratamiento.Estado(pac, new ConsentimientoOptions()).Vigente);
    }

    [FactConBaseDePrimerosPasos]
    public async Task Revocado_el_paciente_puede_volver_a_firmar_y_queda_la_historia_de_firmas()
    {
        var pac = NuevoPaciente();
        _d.ConsentimientoFirmado(pac);
        var paciente = Paciente(pac);
        await paciente.PostAsJsonAsync($"/pacientes/{pac}/consentimiento-telemedicina/revocar", Confirmo);

        var op = new ConsentimientoOptions();
        var firma = await paciente.PostAsJsonAsync($"/pacientes/{pac}/consentimiento-telemedicina", new { acepto = true, version = op.VersionTexto });
        Assert.Equal(HttpStatusCode.Created, firma.StatusCode);
        Assert.True(ConsentimientoTratamiento.Estado(pac, op).Vigente);
        Assert.Equal(2, FilasDeConsentimiento(pac));   // la revocada (con constancia) y la nueva
    }

    // ───────────── Vigencia anual ─────────────

    [FactConBaseDePrimerosPasos]
    public async Task Una_firma_de_hace_mas_de_un_ano_vence_se_pide_firmar_y_se_puede_volver_a_firmar()
    {
        var pac = NuevoPaciente();
        var op = new ConsentimientoOptions();
        DatosDePrueba.Ejecutar(@"INSERT INTO Consentimiento (IdPaciente, IdTipoConsentimiento, Version, FechaFirma, Estado)
            SELECT @p, IdTipoConsentimiento, @v, DATEADD(MONTH, -13, GETDATE()), N'Activo' FROM TipoConsentimiento WHERE Descripcion = @t",
            ("@p", pac), ("@v", op.VersionTexto), ("@t", op.TipoRequerido));
        var paciente = Paciente(pac);

        var estado = await Json(await paciente.GetAsync($"/pacientes/{pac}/consentimiento-telemedicina"));
        Assert.False(estado.GetProperty("firmado").GetBoolean());
        Assert.Equal("vencido", estado.GetProperty("motivo").GetString());
        Assert.False(ConsentimientoTratamiento.Estado(pac, op).Vigente);

        var pasos = await Json(await paciente.GetAsync($"/pacientes/{pac}/primeros-pasos"));
        Assert.Equal("firmar-consentimiento", pasos.GetProperty("principal").GetProperty("tipo").GetString());

        var firma = await paciente.PostAsJsonAsync($"/pacientes/{pac}/consentimiento-telemedicina", new { acepto = true, version = op.VersionTexto });
        Assert.Equal(HttpStatusCode.Created, firma.StatusCode);
        Assert.True(ConsentimientoTratamiento.Estado(pac, op).Vigente);
    }

    [FactConBaseDePrimerosPasos]
    public async Task Con_el_consentimiento_vencido_se_bloquea_la_asignacion_como_si_no_hubiera_firmado()
    {
        var (pac, informe, recep) = Escenario("Aprobado");
        var op = new ConsentimientoOptions();
        DatosDePrueba.Ejecutar(@"INSERT INTO Consentimiento (IdPaciente, IdTipoConsentimiento, Version, FechaFirma, Estado)
            SELECT @p, IdTipoConsentimiento, @v, DATEADD(MONTH, -14, GETDATE()), N'Activo' FROM TipoConsentimiento WHERE Descripcion = @t",
            ("@p", pac), ("@v", op.VersionTexto), ("@t", op.TipoRequerido));
        var prof = _d.Profesional();
        _d.Agenda(prof, "Lunes");
        var r = await recep.PutAsJsonAsync($"/informes-admision/{informe}/derivar", new { idProfesional = prof });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("vencido", (await Json(r)).GetProperty("motivo").GetString());
    }

    // ───────────── El endpoint de primeros pasos ─────────────

    [FactConBaseDePrimerosPasos]
    public async Task Cada_paciente_ve_solo_sus_primeros_pasos()
    {
        var pac = NuevoPaciente();
        var otro = NuevoPaciente();
        Assert.Equal(HttpStatusCode.Forbidden, (await Paciente(otro).GetAsync($"/pacientes/{pac}/primeros-pasos")).StatusCode);
        var r = await Json(await Paciente(pac).GetAsync($"/pacientes/{pac}/primeros-pasos"));
        Assert.Equal(7, r.GetProperty("total").GetInt32());
        Assert.Equal("consentimiento", r.GetProperty("pasoActual").GetString());
    }

    [FactConBaseDePrimerosPasos]
    public async Task La_hora_de_la_sesion_la_decide_el_servidor_y_el_boton_se_habilita_diez_minutos_antes()
    {
        var pac = NuevoPaciente();
        var prof = _d.Profesional();
        _d.ConsentimientoFirmado(pac);
        _d.PacienteProfesional(pac, prof);
        DatosDePrueba.Ejecutar("UPDATE PacienteProfesional SET EstadoAsignacion = N'Activa' WHERE IdPaciente = @p", ("@p", pac));
        var informe = _d.InformeAdmision(pac, "Derivado", null, null);
        Assert.True(informe > 0);
        // Dentro de 3 horas: todavía gris. (El reloj de la clínica es el del servidor; el cliente no manda nada.)
        var turno = _d.Turno(pac, prof, "Confirmado", DateTime.Today.AddDays(2), TimeSpan.FromHours(10), "SALA-PP");
        var r = await Json(await Paciente(pac).GetAsync($"/pacientes/{pac}/primeros-pasos"));
        var sesion = r.GetProperty("sesion");
        Assert.Equal(turno, sesion.GetProperty("idTurno").GetInt32());
        Assert.False(sesion.GetProperty("habilitada").GetBoolean());
    }
}
