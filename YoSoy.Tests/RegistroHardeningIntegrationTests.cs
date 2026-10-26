using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

// Contra una base real (la de Conexion.cs): el alta de paciente responde LO MISMO cuando se repite el mail o el DNI
// (sin decir cuál), y el restablecimiento por mail exige el mínimo de contraseña en el servidor sin gastar el token.
// Cada prueba crea sus propias filas y las borra al final.
[Trait("Categoria", "Integracion")]
public class RegistroHardeningIntegrationTests
{
    private static WebApplicationFactory<Program> Fabrica() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Email:Password"] = " " })));

    private static string Marca() => "xtest" + Guid.NewGuid().ToString("N")[..10];

    private static void Ejecutar(string sql, params (string, object)[] p)
    {
        using var con = Conexion.ObtenerConexion();
        con.Open();
        using var cmd = new SqlCommand(sql, con);
        foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    private static void BorrarPaciente(params string[] usernames)
    {
        foreach (var u in usernames)
        {
            Ejecutar("DELETE FROM UsuarioPaciente WHERE IdUsuario IN (SELECT IdUsuario FROM Usuario WHERE Username = @u)", ("@u", u));
            Ejecutar("DELETE FROM Usuario WHERE Username = @u", ("@u", u));
            Ejecutar("DELETE FROM Paciente WHERE Dni = @u", ("@u", u));
        }
    }

    private static async Task<string?> Mensaje(HttpResponseMessage r)
    {
        var json = await r.Content.ReadFromJsonAsync<JsonElement>();
        return json.TryGetProperty("mensaje", out var m) ? m.GetString() : null;
    }

    [Fact]
    public async Task Mail_repetido_y_dni_repetido_responden_el_mismo_mensaje_y_el_usuario_repetido_uno_propio()
    {
        var marca = Marca();
        var (a, b, c, d) = (marca + "a", marca + "b", marca + "c", marca + "d");
        using var fabrica = Fabrica();
        using var cliente = fabrica.CreateClient();
        try
        {
            var primero = await cliente.PostAsJsonAsync("/pacientes",
                new { nombre = "Prueba", dni = a, mail = a + "@example.com", usuario = a, contrasena = "ClaveDePrueba123", telefono = "11 5555-0001" });
            Assert.Equal(HttpStatusCode.Created, primero.StatusCode);

            // mismo mail, DNI y usuario nuevos
            var porMail = await cliente.PostAsJsonAsync("/pacientes",
                new { nombre = "Prueba", dni = b, mail = a + "@example.com", usuario = b, contrasena = "ClaveDePrueba123", telefono = "11 5555-0001" });
            // mismo DNI, mail y usuario nuevos
            var porDni = await cliente.PostAsJsonAsync("/pacientes",
                new { nombre = "Prueba", dni = a, mail = c + "@example.com", usuario = c, contrasena = "ClaveDePrueba123", telefono = "11 5555-0001" });
            // mismo usuario, DNI y mail nuevos
            var porUsuario = await cliente.PostAsJsonAsync("/pacientes",
                new { nombre = "Prueba", dni = d, mail = d + "@example.com", usuario = a, contrasena = "ClaveDePrueba123", telefono = "11 5555-0001" });

            Assert.Equal(HttpStatusCode.Conflict, porMail.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, porDni.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, porUsuario.StatusCode);

            // cada cuerpo se lee una sola vez
            var mensajePorMail = await Mensaje(porMail);
            var mensajePorDni = await Mensaje(porDni);
            var mensajePorUsuario = await Mensaje(porUsuario);
            Assert.Equal(MensajesConflictoRegistro.DatosYaRegistrados, mensajePorMail);
            Assert.Equal(mensajePorMail, mensajePorDni);
            Assert.Equal(MensajesConflictoRegistro.UsuarioEnUso, mensajePorUsuario);
        }
        finally { BorrarPaciente(a, b, c, d); }
    }

    [Fact]
    public async Task El_restablecimiento_con_una_contrasena_corta_es_400_y_no_gasta_el_token()
    {
        var username = Marca();
        var hashInicial = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("ClaveOriginal123")));
        using var con = Conexion.ObtenerConexion();
        con.Open();
        int idUsuario;
        using (var cmd = new SqlCommand(
            "INSERT INTO Usuario (Username, PasswordHash, Estado, Mail) VALUES (@u, @h, N'Activo', @m); SELECT CAST(SCOPE_IDENTITY() AS INT);", con))
        {
            cmd.Parameters.AddWithValue("@u", username);
            cmd.Parameters.AddWithValue("@h", hashInicial);
            cmd.Parameters.AddWithValue("@m", username + "@example.com");
            idUsuario = (int)cmd.ExecuteScalar()!;
        }

        try
        {
            var tokenCrudo = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tokenCrudo)));
            new RecuperacionPasswordDAO().CrearToken(idUsuario, tokenHash, DateTime.Now.AddMinutes(RecuperacionPasswordDAO.DuracionTokenMinutos));

            using var fabrica = Fabrica();
            using var cliente = fabrica.CreateClient();

            foreach (var mala in new[] { "", "   ", "abc" })
            {
                var r = await cliente.PostAsJsonAsync("/recuperacion-password/reset", new { token = tokenCrudo, nuevaPassword = mala });
                Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
                Assert.Equal(PoliticaContrasena.Validar(mala), await Mensaje(r));
            }

            // La contraseña no se tocó y el token sigue sirviendo: con una buena, funciona.
            using (var cmd = new SqlCommand("SELECT PasswordHash FROM Usuario WHERE IdUsuario = @i", con))
            {
                cmd.Parameters.AddWithValue("@i", idUsuario);
                Assert.Equal(hashInicial, (string)cmd.ExecuteScalar()!);
            }
            var buena = await cliente.PostAsJsonAsync("/recuperacion-password/reset", new { token = tokenCrudo, nuevaPassword = "NuevaClave456" });
            Assert.Equal(HttpStatusCode.OK, buena.StatusCode);
        }
        finally
        {
            Ejecutar("DELETE FROM RecuperacionPassword WHERE IdUsuario = @i", ("@i", idUsuario));
            Ejecutar("DELETE FROM Usuario WHERE IdUsuario = @i", ("@i", idUsuario));
        }
    }
}
