using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;

// Registro público de paciente con acceso: POST /pacientes pasa a crear Paciente + Usuario +
// UsuarioPaciente en una sola transacción (PacienteDAO.RegistrarConAcceso), en vez de solo
// Paciente como antes. Corre contra la base local real; cada prueba borra sus propias filas.
[Trait("Categoria", "Integracion")]
public class RegistroPacienteConAccesoTests
{
    private static object Pedido(string dni, string usuario, string mail, string contrasena = "ClaveDePrueba123") =>
        new { nombre = "Prueba", dni, mail, usuario, contrasena };

    private static async Task BorrarPrueba(params string[] marcas)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        foreach (var m in marcas)
        {
            using (var del = new SqlCommand(
                "DELETE FROM UsuarioPaciente WHERE IdPaciente IN (SELECT IdPaciente FROM Paciente WHERE Dni = @m) OR IdUsuario IN (SELECT IdUsuario FROM Usuario WHERE Username = @m)", con))
            { del.Parameters.AddWithValue("@m", m); await del.ExecuteNonQueryAsync(); }
            using (var del = new SqlCommand("DELETE FROM Usuario WHERE Username = @m", con))
            { del.Parameters.AddWithValue("@m", m); await del.ExecuteNonQueryAsync(); }
            using (var del = new SqlCommand("DELETE FROM Paciente WHERE Dni = @m", con))
            { del.Parameters.AddWithValue("@m", m); await del.ExecuteNonQueryAsync(); }
        }
    }

    [Fact]
    public async Task Registro_exitoso_crea_paciente_usuario_y_vinculo_y_permite_loguearse()
    {
        var marca = "XTEST-" + Guid.NewGuid().ToString("N")[..10];
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var respuesta = await client.PostAsJsonAsync("/pacientes", Pedido(marca, marca, marca.ToLowerInvariant() + "@example.com"));
        try
        {
            Assert.True(respuesta.IsSuccessStatusCode, $"esperaba 2xx, llegó {(int)respuesta.StatusCode}");
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
            var idPaciente = cuerpo.GetProperty("idPaciente").GetInt32();
            var idUsuario = cuerpo.GetProperty("idUsuario").GetInt32();
            Assert.True(idPaciente > 0, $"idPaciente tendría que ser > 0, llegó {idPaciente}");
            Assert.True(idUsuario > 0, $"idUsuario tendría que ser > 0, llegó {idUsuario}");

            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var cmdVinculo = new SqlCommand("SELECT COUNT(*) FROM UsuarioPaciente WHERE IdUsuario = @u AND IdPaciente = @p", con);
            cmdVinculo.Parameters.AddWithValue("@u", idUsuario);
            cmdVinculo.Parameters.AddWithValue("@p", idPaciente);
            Assert.Equal(1, (int)(await cmdVinculo.ExecuteScalarAsync())!);

            var login = await client.PostAsJsonAsync("/login", new { username = marca, password = "ClaveDePrueba123" });
            Assert.True(login.IsSuccessStatusCode, $"el login con el usuario recién creado tendría que funcionar, llegó {(int)login.StatusCode}");
        }
        finally { await BorrarPrueba(marca); }
    }

    [Fact]
    public async Task Usuario_repetido_responde_409_y_no_crea_nada()
    {
        var marca = "XTEST-" + Guid.NewGuid().ToString("N")[..10];
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var primero = await client.PostAsJsonAsync("/pacientes", Pedido(marca + "-A", marca, marca.ToLowerInvariant() + "-a@example.com"));
        Assert.True(primero.IsSuccessStatusCode);
        try
        {
            // Mismo "usuario" que el primero, dni y mail distintos.
            var segundo = await client.PostAsJsonAsync("/pacientes", Pedido(marca + "-B", marca, marca.ToLowerInvariant() + "-b@example.com"));
            Assert.Equal(HttpStatusCode.Conflict, segundo.StatusCode);

            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var cmd = new SqlCommand("SELECT COUNT(*) FROM Paciente WHERE Dni = @d", con);
            cmd.Parameters.AddWithValue("@d", marca + "-B");
            Assert.Equal(0, (int)(await cmd.ExecuteScalarAsync())!);
        }
        finally { await BorrarPrueba(marca, marca + "-A", marca + "-B"); }
    }

    [Fact]
    public async Task Mail_repetido_responde_409_y_no_crea_nada()
    {
        var marca = "XTEST-" + Guid.NewGuid().ToString("N")[..10];
        var mailCompartido = marca.ToLowerInvariant() + "@example.com";
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var primero = await client.PostAsJsonAsync("/pacientes", Pedido(marca + "-A", marca + "-A", mailCompartido));
        Assert.True(primero.IsSuccessStatusCode);
        try
        {
            var segundo = await client.PostAsJsonAsync("/pacientes", Pedido(marca + "-B", marca + "-B", mailCompartido));
            Assert.Equal(HttpStatusCode.Conflict, segundo.StatusCode);

            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var cmd = new SqlCommand("SELECT COUNT(*) FROM Usuario WHERE Username = @u", con);
            cmd.Parameters.AddWithValue("@u", marca + "-B");
            Assert.Equal(0, (int)(await cmd.ExecuteScalarAsync())!);
        }
        finally { await BorrarPrueba(marca + "-A", marca + "-B"); }
    }

    [Fact]
    public async Task Dni_repetido_responde_409_y_no_deja_un_usuario_huerfano()
    {
        var marca = "XTEST-" + Guid.NewGuid().ToString("N")[..10];
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var primero = await client.PostAsJsonAsync("/pacientes", Pedido(marca, marca + "-A", marca.ToLowerInvariant() + "-a@example.com"));
        Assert.True(primero.IsSuccessStatusCode);
        try
        {
            // Mismo DNI que el primero: el pre-check de Dni corta antes de crear el Usuario nuevo.
            var segundo = await client.PostAsJsonAsync("/pacientes", Pedido(marca, marca + "-B", marca.ToLowerInvariant() + "-b@example.com"));
            Assert.Equal(HttpStatusCode.Conflict, segundo.StatusCode);

            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var cmd = new SqlCommand("SELECT COUNT(*) FROM Usuario WHERE Username = @u", con);
            cmd.Parameters.AddWithValue("@u", marca + "-B");
            Assert.Equal(0, (int)(await cmd.ExecuteScalarAsync())!);
        }
        finally { await BorrarPrueba(marca, marca + "-A", marca + "-B"); }
    }

    [Fact]
    public async Task Contrasena_corta_responde_400_y_no_crea_nada()
    {
        var marca = "XTEST-" + Guid.NewGuid().ToString("N")[..10];
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var respuesta = await client.PostAsJsonAsync("/pacientes", Pedido(marca, marca, marca.ToLowerInvariant() + "@example.com", contrasena: "abc"));
        try
        {
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            using var con = Conexion.ObtenerConexion();
            con.Open();
            using var cmd = new SqlCommand("SELECT COUNT(*) FROM Paciente WHERE Dni = @d", con);
            cmd.Parameters.AddWithValue("@d", marca);
            Assert.Equal(0, (int)(await cmd.ExecuteScalarAsync())!);
        }
        finally { await BorrarPrueba(marca); }
    }

    [Fact]
    public async Task Mail_con_formato_invalido_responde_400()
    {
        var marca = "XTEST-" + Guid.NewGuid().ToString("N")[..10];
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var respuesta = await client.PostAsJsonAsync("/pacientes", Pedido(marca, marca, "no-es-un-mail"));
        try { Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode); }
        finally { await BorrarPrueba(marca); }
    }

    [Fact]
    public async Task Limite_por_ip_bloquea_despues_de_diez_pero_no_a_otra_ip()
    {
        var marca = "XTEST-" + Guid.NewGuid().ToString("N")[..10];
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        try
        {
            // Mismo pedido 10 veces: solo el primero crea la fila, los otros 9 chocan por
            // usuario repetido (409) — lo que importa acá es que el rate limiter los deje
            // pasar al handler (nunca 429) hasta el límite.
            for (int i = 0; i < 10; i++)
            {
                client.DefaultRequestHeaders.Remove("X-Forwarded-For");
                client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.31");
                var r = await client.PostAsJsonAsync("/pacientes", Pedido(marca, marca, marca.ToLowerInvariant() + "@example.com"));
                Assert.NotEqual(HttpStatusCode.TooManyRequests, r.StatusCode);
            }

            client.DefaultRequestHeaders.Remove("X-Forwarded-For");
            client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.31");
            var undecimo = await client.PostAsJsonAsync("/pacientes", Pedido(marca, marca, marca.ToLowerInvariant() + "@example.com"));
            Assert.Equal(HttpStatusCode.TooManyRequests, undecimo.StatusCode);

            client.DefaultRequestHeaders.Remove("X-Forwarded-For");
            client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.32");
            var otraIp = await client.PostAsJsonAsync("/pacientes", Pedido(marca + "-otraip", marca + "-otraip", marca.ToLowerInvariant() + "-otraip@example.com"));
            Assert.NotEqual(HttpStatusCode.TooManyRequests, otraIp.StatusCode);
        }
        finally { await BorrarPrueba(marca, marca + "-otraip"); }
    }
}
