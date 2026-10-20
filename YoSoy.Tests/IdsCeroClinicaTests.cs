using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

// fix-ids-cero-endpoints, Grupo 4 (clínica): Consentimiento, HistoriaClinica, Evolucion,
// Derivacion, EventoCrisis, InteraccionIA y Cobertura devuelven el id en 0 — mismo patrón
// que los grupos anteriores. Cada alta se hace con el rol que hoy corresponde (etapa 2 de roles): lo clínico lo escribe el profesional que
// atiende al paciente (hay un vínculo PacienteProfesional propio de la prueba), consentimientos y derivaciones los hace Admisión (un profesional
// con ese privilegio) y las coberturas siguen siendo del Configurador.
// Todo lo que cada prueba necesita (paciente, historia, profesionales, obra social, tipo de
// consentimiento, usuario Configurador) es PROPIO de la prueba (DatosDePrueba): antes se usaban
// el paciente 1, la historia 1, la obra social 1, el tipo de consentimiento 1 y los profesionales
// 1 y 2 de la base de desarrollo, y fallaban en cualquier otra.
[Trait("Categoria", "Integracion")]
public class IdsCeroClinicaTests
{
    // Pide el alta y comprueba que el id de la respuesta es el real (el de la fila en la base) y no cero.
    private enum Quien { Configurador, Admision, Profesional }

    private static async Task ProbarAlta(DatosDePrueba datos, string ruta, object cuerpo, string propiedadId,
        string tabla, string columnaId, string columnaMarca, object marca, Quien quien = Quien.Configurador, int? idPacienteAtendido = null)
    {
        datos.AlLimpiar(tabla, columnaMarca, marca);

        using var factory = new WebApplicationFactory<Program>();
        var emisor = factory.Services.GetRequiredService<JwtEmisorService>();
        string token;
        if (quien == Quien.Configurador) token = emisor.Emitir(datos.Usuario(), Roles.Configurador);
        else
        {
            var idProfesional = datos.Profesional();
            var idUsuario = datos.UsuarioDeProfesional(idProfesional);
            if (idPacienteAtendido is int idPac) datos.PacienteProfesional(idPac, idProfesional);
            token = quien == Quien.Admision
                ? emisor.EmitirModulo(idUsuario, new SesionModulo(Modulos.Profesional, new[] { Roles.Profesional }, new[] { Privilegios.Admision }, idProfesional))
                : emisor.Emitir(idUsuario, Roles.Profesional, idProfesional: idProfesional);
        }
        using var client = DatosDePrueba.Cliente(factory, token);

        var respuesta = await client.PostAsJsonAsync(ruta, cuerpo);
        Assert.True(respuesta.IsSuccessStatusCode, $"esperaba 2xx, llegó {(int)respuesta.StatusCode}");

        var json = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        var idDevuelto = json.GetProperty(propiedadId).GetInt32();
        var idReal = Convert.ToInt32(DatosDePrueba.Escalar($"SELECT {columnaId} FROM {tabla} WHERE {columnaMarca} = @m", ("@m", marca)));

        Assert.True(idDevuelto > 0, $"{propiedadId} en la respuesta tendría que ser > 0, llegó {idDevuelto}");
        Assert.Equal(idReal, idDevuelto);
    }

    [Fact]
    public async Task Consentimiento_alta_devuelve_id_real_no_cero()
    {
        using var datos = new DatosDePrueba();
        var marca = datos.Marca;
        await ProbarAlta(datos, "/consentimientos",
            new { idPaciente = datos.Paciente(), idTipoConsentimiento = datos.TipoConsentimiento(), version = marca, fechaFirma = DateTime.Now },
            "idConsentimiento", "Consentimiento", "IdConsentimiento", "Version", marca, Quien.Admision);
    }

    [Fact]
    public async Task HistoriaClinica_alta_devuelve_id_real_no_cero()
    {
        // HistoriaClinica tiene UNIQUE en IdPaciente (una sola historia por paciente): el paciente es
        // descartable y propio de la prueba, sin historia previa.
        using var datos = new DatosDePrueba();
        var idPaciente = datos.Paciente();
        await ProbarAlta(datos, "/historiasclinicas", new { idPaciente, fechaApertura = DateTime.Now },
            "idHistoria", "HistoriaClinica", "IdHistoria", "IdPaciente", idPaciente, Quien.Profesional, idPaciente);
    }

    [Fact]
    public async Task Evolucion_alta_devuelve_id_real_no_cero()
    {
        using var datos = new DatosDePrueba();
        var idPaciente = datos.Paciente();
        var idHistoria = datos.HistoriaClinica(idPaciente);
        var idProfesional = datos.Profesional();
        var marca = datos.Marca;
        await ProbarAlta(datos, "/evoluciones", new { idHistoria, idProfesional, fecha = DateTime.Now, diagnostico = marca },
            "idEvolucion", "Evolucion", "IdEvolucion", "Diagnostico", marca, Quien.Profesional, idPaciente);
    }

    [Fact]
    public async Task Derivacion_alta_devuelve_id_real_no_cero()
    {
        using var datos = new DatosDePrueba();
        var marca = datos.Marca;
        await ProbarAlta(datos, "/derivaciones",
            new { idPaciente = datos.Paciente(), idProfesionalOrigen = datos.Profesional(), idProfesionalDestino = datos.Profesional(), motivo = marca },
            "idDerivacion", "Derivacion", "IdDerivacion", "Motivo", marca, Quien.Admision);
    }

    [Fact]
    public async Task EventoCrisis_alta_devuelve_id_real_no_cero()
    {
        using var datos = new DatosDePrueba();
        var marca = datos.Marca;
        var idPaciente = datos.Paciente();
        await ProbarAlta(datos, "/eventoscrisis",
            new { idPaciente, fecha = DateTime.Now, nivelRiesgo = "Bajo", acciones = marca },
            "idEvento", "EventoCrisis", "IdEvento", "Acciones", marca, Quien.Profesional, idPaciente);
    }

    [Fact]
    public async Task InteraccionIA_alta_devuelve_id_real_no_cero()
    {
        using var datos = new DatosDePrueba();
        var marca = datos.Marca;
        var idPaciente = datos.Paciente();
        await ProbarAlta(datos, "/interaccionesia",
            new { idPaciente, fecha = DateTime.Now, consulta = marca, respuesta = "r" },
            "idInteraccion", "InteraccionIA", "IdInteraccion", "Consulta", marca, Quien.Profesional, idPaciente);
    }

    [Fact]
    public async Task Cobertura_alta_devuelve_id_real_no_cero()
    {
        using var datos = new DatosDePrueba();
        var marca = datos.Marca;
        await ProbarAlta(datos, "/coberturas",
            new { idPaciente = datos.Paciente(), idObraSocial = datos.ObraSocial(), nroAfiliado = marca, planCobertura = "Básico" },
            "idCobertura", "Cobertura", "IdCobertura", "NroAfiliado", marca);
    }
}
