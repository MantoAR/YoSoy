using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

// fix-ids-cero-endpoints, Grupo 3 (agenda): /agendas y /agenda (alias sin 's', mismo
// AgendaDAO) devuelven el id en 0 porque Insertar es void y nunca lee el id generado.
// IntervaloMinutos = 9999 es la marca de la fila de prueba (ningún horario real usa eso).
// El profesional y el usuario Configurador son PROPIOS de la prueba (DatosDePrueba): antes dependía
// de que existieran el profesional 1 y el usuario 1 en la base.
[Trait("Categoria", "Integracion")]
public class IdsCeroAgendaTests
{
    private const int Marca = 9999;

    private static async Task ProbarAlta(string ruta)
    {
        using var datos = new DatosDePrueba();
        var idProfesional = datos.Profesional();
        var idConfigurador = datos.Usuario();
        datos.AlLimpiar("Agenda", "IdProfesional", idProfesional);   // solo las filas de ESTE profesional

        using var factory = new WebApplicationFactory<Program>();
        using var client = DatosDePrueba.Cliente(factory, idConfigurador, Roles.Configurador);

        var respuesta = await client.PostAsJsonAsync(ruta, new
        {
            idProfesional, dia = "jueves", horaInicio = "23:58:00", horaFin = "23:59:00",
            disponible = true, intervaloMinutos = Marca
        });

        Assert.True(respuesta.IsSuccessStatusCode, $"esperaba 2xx, llegó {(int)respuesta.StatusCode}");
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        var idDevuelto = cuerpo.GetProperty("idAgenda").GetInt32();

        var idReal = Convert.ToInt32(DatosDePrueba.Escalar(
            "SELECT IdAgenda FROM Agenda WHERE IntervaloMinutos = @m AND IdProfesional = @p", ("@m", Marca), ("@p", idProfesional)));

        Assert.True(idDevuelto > 0, $"idAgenda en la respuesta tendría que ser > 0, llegó {idDevuelto}");
        Assert.Equal(idReal, idDevuelto);
    }

    [Fact]
    public Task Agendas_alta_devuelve_id_real_no_cero() => ProbarAlta("/agendas");

    [Fact]
    public Task Agenda_alias_alta_devuelve_id_real_no_cero() => ProbarAlta("/agenda");
}
