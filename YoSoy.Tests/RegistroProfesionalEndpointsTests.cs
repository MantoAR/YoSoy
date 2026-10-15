using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// POST /profesionales y PUT /profesionales/{id} contra una base real (la de Conexion.cs): matrícula o DNI
// repetidos → 409 con mensaje neutral, especialidad inexistente → 400, datos obligatorios → 400.
// Cada prueba crea sus propias filas (una especialidad, un usuario "admin" para el JWT y los profesionales)
// y las borra al final: no depende de lo que haya cargado en la base. La lógica de fondo está probada sin
// base en RegistroProfesionalServiceTests.
[Trait("Categoria", "Integracion")]
public class RegistroProfesionalEndpointsTests
{
    private sealed class Mundo : IDisposable
    {
        public string Marca { get; } = "xtest" + Guid.NewGuid().ToString("N")[..10];
        public string Dni { get; } = Random.Shared.Next(90_000_000, 99_999_999).ToString();
        public int IdEspecialidad { get; }
        public int IdUsuarioAdmin { get; }
        public WebApplicationFactory<Program> Factory { get; }
        public HttpClient Publico { get; }

        public Mundo()
        {
            IdEspecialidad = Escalar("INSERT INTO Especialidad (Nombre, Estado) VALUES (@a, N'Activo'); SELECT CAST(SCOPE_IDENTITY() AS INT);", ("@a", Marca));
            IdUsuarioAdmin = Escalar("INSERT INTO Usuario (Username, PasswordHash, Estado, Mail) VALUES (@a, N'0', N'Activo', @m); SELECT CAST(SCOPE_IDENTITY() AS INT);",
                ("@a", Marca), ("@m", Marca + "@example.com"));
            Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
                b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Email:Password"] = " " })));
            Publico = Factory.CreateClient();
        }

        public HttpClient Configurador()
        {
            var c = Factory.CreateClient();
            var token = Factory.Services.GetRequiredService<JwtEmisorService>().Emitir(IdUsuarioAdmin, Roles.Configurador);
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return c;
        }

        public object Cuerpo(string matricula, string dni, int? idEspecialidad = null) => new
        {
            nombre = "Prueba", apellido = Marca, dni, sexo = "Otro", fechaNacimiento = "1990-01-01",
            matricula, idEspecialidad = idEspecialidad ?? IdEspecialidad, provincia = "Buenos Aires",
            estado = "Pendiente", mail = Marca + "@example.com", telefono = "1100000000"
        };

        public int FilasConMatricula(string matricula) =>
            Escalar("SELECT COUNT(*) FROM Profesional WHERE Matricula = @m", ("@m", matricula));

        public void Dispose()
        {
            Publico.Dispose();
            Factory.Dispose();
            Ejecutar("DELETE FROM LogAuditoria WHERE Entidad = N'Profesional' AND Detalle LIKE @m", ("@m", "%" + Marca + "%"));
            Ejecutar("DELETE FROM Profesional WHERE Apellido = @m", ("@m", Marca));
            Ejecutar("DELETE FROM Usuario WHERE Username = @m", ("@m", Marca));
            Ejecutar("DELETE FROM Especialidad WHERE IdEspecialidad = @i", ("@i", IdEspecialidad));
        }
    }

    private static int Escalar(string sql, params (string, object)[] parametros)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(sql, con);
        foreach (var (n, v) in parametros) cmd.Parameters.AddWithValue(n, v);
        return (int)cmd.ExecuteScalar()!;
    }

    private static void Ejecutar(string sql, params (string, object)[] parametros)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(sql, con);
        foreach (var (n, v) in parametros) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    private static async Task<string?> Mensaje(HttpResponseMessage r)
    {
        var json = await r.Content.ReadFromJsonAsync<JsonElement>();
        return json.TryGetProperty("mensaje", out var m) ? m.GetString() : null;
    }

    [Fact]
    public async Task Alta_publica_con_matricula_repetida_responde_409_neutral_y_no_crea_otra_fila()
    {
        using var m = new Mundo();
        var matricula = "MP" + m.Marca;

        var primera = await m.Publico.PostAsJsonAsync("/profesionales", m.Cuerpo(matricula, m.Dni));
        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);

        var repetida = await m.Publico.PostAsJsonAsync("/profesionales", m.Cuerpo(matricula, "88" + m.Dni[2..]));

        Assert.Equal(HttpStatusCode.Conflict, repetida.StatusCode);
        Assert.Equal(RegistroProfesionalService.MensajeDuplicadoAlta, await Mensaje(repetida));
        Assert.Equal(1, m.FilasConMatricula(matricula));
    }

    [Fact]
    public async Task Alta_publica_con_dni_repetido_responde_409_aunque_cambie_el_formato()
    {
        using var m = new Mundo();

        var primera = await m.Publico.PostAsJsonAsync("/profesionales", m.Cuerpo("MP1" + m.Marca, m.Dni));
        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);

        var conPuntos = $"{m.Dni[..2]}.{m.Dni[2..5]}.{m.Dni[5..]}";
        var repetida = await m.Publico.PostAsJsonAsync("/profesionales", m.Cuerpo("MP2" + m.Marca, conPuntos));

        Assert.Equal(HttpStatusCode.Conflict, repetida.StatusCode);
        Assert.Equal(RegistroProfesionalService.MensajeDuplicadoAlta, await Mensaje(repetida));
        Assert.Equal(0, m.FilasConMatricula("MP2" + m.Marca));
    }

    [Fact]
    public async Task Alta_publica_con_especialidad_inexistente_responde_400_y_no_crea_nada()
    {
        using var m = new Mundo();
        var matricula = "MP" + m.Marca;

        var r = await m.Publico.PostAsJsonAsync("/profesionales", m.Cuerpo(matricula, m.Dni, idEspecialidad: m.IdEspecialidad + 100_000));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("La especialidad elegida no existe.", await Mensaje(r));
        Assert.Equal(0, m.FilasConMatricula(matricula));
    }

    [Fact]
    public async Task Alta_publica_sin_matricula_responde_400_en_vez_de_500()
    {
        using var m = new Mundo();

        var r = await m.Publico.PostAsJsonAsync("/profesionales", new
        {
            nombre = "Prueba", apellido = m.Marca, dni = m.Dni, idEspecialidad = m.IdEspecialidad, estado = "Pendiente"
        });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(RegistroProfesionalService.MensajeFaltanDatos, await Mensaje(r));
    }

    [Fact]
    public async Task Edicion_a_la_matricula_de_otro_responde_409_y_con_la_propia_200()
    {
        using var m = new Mundo();
        var matriculaA = "MPA" + m.Marca;
        var matriculaB = "MPB" + m.Marca;

        var a = await m.Publico.PostAsJsonAsync("/profesionales", m.Cuerpo(matriculaA, m.Dni));
        var b = await m.Publico.PostAsJsonAsync("/profesionales", m.Cuerpo(matriculaB, "77" + m.Dni[2..]));
        Assert.Equal(HttpStatusCode.Created, a.StatusCode);
        Assert.Equal(HttpStatusCode.Created, b.StatusCode);
        var idB = (await b.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("idProfesional").GetInt32();

        using var admin = m.Configurador();

        // Cambiar la matrícula de B a la de A: 409, y la fila de B queda como estaba.
        var choque = await admin.PutAsJsonAsync($"/profesionales/{idB}", m.Cuerpo(matriculaA, "77" + m.Dni[2..]));
        Assert.Equal(HttpStatusCode.Conflict, choque.StatusCode);
        Assert.Equal(RegistroProfesionalService.MensajeDuplicadoEdicion, await Mensaje(choque));
        Assert.Equal(1, m.FilasConMatricula(matriculaB));

        // Guardar su propio perfil con su misma matrícula y DNI: 200.
        var propio = await admin.PutAsJsonAsync($"/profesionales/{idB}", m.Cuerpo(matriculaB, "77" + m.Dni[2..]));
        Assert.Equal(HttpStatusCode.OK, propio.StatusCode);
    }
}
