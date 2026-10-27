using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

// GET /pacientes/{id}/novedades: la huella barata que consulta el Inicio del paciente cada ~15 s (Bloque B del prompt 08). Con datos propios en yosoy_test.
[Trait("Categoria", "Integracion")]
public class NovedadesPacienteBaseRealTests : IDisposable
{
    private readonly DatosDePrueba _d = new();
    private readonly WebApplicationFactory<Program> _fabrica = new WebApplicationFactory<Program>();

    public void Dispose() { _fabrica.Dispose(); _d.Dispose(); }

    private HttpClient Paciente(int id) => DatosDePrueba.Cliente(_fabrica, _d.UsuarioDePaciente(id), Roles.Paciente, idPaciente: id);
    private static async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    [FactConBaseDeAsignacion]
    public async Task La_huella_es_estable_sin_cambios_y_cambia_cuando_el_estado_del_paciente_cambia()
    {
        var pac = _d.Paciente("Ana " + _d.Marca, mail: _d.Marca + "@example.test");
        var cliente = Paciente(pac);

        var uno = await Json(await cliente.GetAsync($"/pacientes/{pac}/novedades"));
        var dos = await Json(await cliente.GetAsync($"/pacientes/{pac}/novedades"));
        Assert.Equal(uno.GetProperty("huella").GetString(), dos.GetProperty("huella").GetString());   // sin cambios, la misma
        Assert.Equal(0, uno.GetProperty("noLeidas").GetInt32());

        // Lo que hace otra persona: el formulario «Sobre mí» aparece / se acepta.
        var informe = _d.InformeAdmision(pac, "Pendiente", null, null);
        var conInforme = await Json(await cliente.GetAsync($"/pacientes/{pac}/novedades"));
        Assert.NotEqual(uno.GetProperty("huella").GetString(), conInforme.GetProperty("huella").GetString());
        DatosDePrueba.Ejecutar("UPDATE InformeAdmision SET Estado = N'Aprobado' WHERE IdInforme = @i", ("@i", informe));
        var aprobado = await Json(await cliente.GetAsync($"/pacientes/{pac}/novedades"));
        Assert.NotEqual(conInforme.GetProperty("huella").GetString(), aprobado.GetProperty("huella").GetString());

        // Un aviso nuevo (la campanita).
        DatosDePrueba.Ejecutar("INSERT INTO Notificacion (IdPaciente, Titulo, Mensaje, Link) VALUES (@p, N'[TEST] aviso', N'[TEST] aviso', NULL)", ("@p", pac));
        var conAviso = await Json(await cliente.GetAsync($"/pacientes/{pac}/novedades"));
        Assert.Equal(1, conAviso.GetProperty("noLeidas").GetInt32());
        Assert.NotEqual(aprobado.GetProperty("huella").GetString(), conAviso.GetProperty("huella").GetString());

        // Un turno confirmado por el profesional.
        var prof = _d.Profesional();
        var turno = _d.Turno(pac, prof, "Pendiente", DateTime.Today.AddDays(3), new TimeSpan(10, 0, 0));
        var conTurno = await Json(await cliente.GetAsync($"/pacientes/{pac}/novedades"));
        Assert.NotEqual(conAviso.GetProperty("huella").GetString(), conTurno.GetProperty("huella").GetString());
        DatosDePrueba.Ejecutar("UPDATE Turno SET IdEstadoTurno = @e WHERE IdTurno = @t", ("@e", _d.EstadoTurno("Confirmado")), ("@t", turno));
        var confirmado = await Json(await cliente.GetAsync($"/pacientes/{pac}/novedades"));
        Assert.NotEqual(conTurno.GetProperty("huella").GetString(), confirmado.GetProperty("huella").GetString());
        DatosDePrueba.Ejecutar("DELETE FROM Notificacion WHERE IdPaciente = @p", ("@p", pac));
    }

    [FactConBaseDeAsignacion]
    public async Task Solo_el_propio_paciente_y_con_sesion_ve_las_novedades()
    {
        var pac = _d.Paciente("Ana " + _d.Marca, mail: _d.Marca + "@example.test");
        var otro = _d.Paciente("Beto " + _d.Marca, mail: _d.Marca + "b@example.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await Paciente(otro).GetAsync($"/pacientes/{pac}/novedades")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _fabrica.CreateClient().GetAsync($"/pacientes/{pac}/novedades")).StatusCode);
    }

    [FactConBaseDeAsignacion]
    public async Task La_respuesta_es_chica_y_no_trae_datos_de_nadie()
    {
        var pac = _d.Paciente("Ana " + _d.Marca, mail: _d.Marca + "@example.test");
        var cuerpo = await (await Paciente(pac).GetAsync($"/pacientes/{pac}/novedades")).Content.ReadAsStringAsync();
        Assert.True(cuerpo.Length < 80, cuerpo);   // { huella, noLeidas }
        Assert.DoesNotContain(_d.Marca, cuerpo);
    }
}
