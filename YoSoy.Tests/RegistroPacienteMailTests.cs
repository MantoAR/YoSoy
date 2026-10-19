using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

// ¿El mail es obligatorio al registrar un paciente? SÍ, en el único camino que crea pacientes: el registro público (POST /pacientes, que arma el paciente, su
// usuario y el vínculo en una sola transacción: PacienteDAO.RegistrarConAcceso). Lo exigen las dos puntas:
//  · el servidor: sin mail, o con uno mal formado, contesta 400 antes de tocar la base;
//  · la pantalla de registro: el campo Email está marcado como obligatorio y se valida (con el mismo criterio de formato) antes de mandar el pedido.
// Lo que NO se exige es que el mail se conserve después: la edición de la ficha (PUT /pacientes/{id}) no lo valida (ver la última prueba, que lo deja anotado).
public class RegistroPacienteMailTests
{
    private const string Mensaje = "El mail es obligatorio y tiene que tener un formato válido.";

    [Theory]
    [InlineData(null)]                                          // el campo ni viene
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sin-arroba")]
    [InlineData("a@")]
    [InlineData("@dominio.com")]
    [InlineData("dos@@dominio.com")]
    [InlineData("con espacio@dominio.com")]
    [InlineData("Nombre Apellido <paciente@dominio.com>")]     // con nombre para mostrar: se compara la dirección con lo escrito, y no coinciden
    public async Task Sin_un_mail_valido_el_registro_responde_400_y_no_llega_a_crear_nada(string? mail)
    {
        using var fabrica = new WebApplicationFactory<Program>();
        using var cliente = fabrica.CreateClient();

        // Todo lo demás en regla: lo único que falla es el mail. El pedido se corta antes de tocar la base.
        var r = await cliente.PostAsJsonAsync("/pacientes", new { nombre = "Prueba", dni = "99999999", mail, usuario = "xtest-mail", contrasena = "ClaveDePrueba123" });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(Mensaje, (await VideoApp.Json(r)).GetProperty("mensaje").GetString());
    }

    [Fact]
    public void La_pantalla_de_registro_marca_el_email_como_obligatorio_y_lo_valida_antes_de_mandar()
    {
        var raiz = VideoAccesoEndpointsTests.RaizDelRepositorio();
        var html = File.ReadAllText(Path.Combine(raiz, "PANTALLAS", "Pantalla_Usuario", "Usuario", "registro.html"));
        var js = File.ReadAllText(Path.Combine(raiz, "PANTALLAS", "Pantalla_Usuario", "Usuario", "js", "app.js"));

        Assert.Contains("<label for=\"email\">Email<span class=\"ui-requerido\">*</span></label><input type=\"email\" id=\"email\"", html);   // con el asterisco de obligatorio
        Assert.Contains("{ id: \"email\", requerido: \"Ingresá tu email.\", patron: /^[^\\s@]+@[^\\s@]+\\.[^\\s@]+$/,", js);                    // y se valida (obligatorio y con formato)
    }
}

// Con la base de pruebas: el mail que se manda queda guardado, y la edición posterior de la ficha NO lo protege.
[Trait("Categoria", "Integracion")]
public class RegistroPacienteMailBaseRealTests
{
    [Fact]
    public async Task Con_un_mail_valido_el_paciente_y_su_usuario_quedan_registrados_con_ese_mail()
    {
        using var fabrica = new WebApplicationFactory<Program>();
        using var cliente = fabrica.CreateClient();
        using var d = new DatosDePrueba();
        var marca = "XTEST-" + Guid.NewGuid().ToString("N")[..10];
        var mail = marca.ToLowerInvariant() + "@example.com";

        var r = await cliente.PostAsJsonAsync("/pacientes", new { nombre = "Prueba " + marca, dni = marca, mail, usuario = marca, contrasena = "ClaveDePrueba123" });
        var j = await VideoApp.Json(r);
        if (r.IsSuccessStatusCode)
        {
            d.AlLimpiar("Usuario", "IdUsuario", j.GetProperty("idUsuario").GetInt32());
            d.AlLimpiar("Paciente", "IdPaciente", j.GetProperty("idPaciente").GetInt32());
        }

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(mail, j.GetProperty("mail").GetString());
        Assert.Equal(mail, (string)DatosDePrueba.Escalar("SELECT Mail FROM Paciente WHERE IdPaciente = @p", ("@p", j.GetProperty("idPaciente").GetInt32()))!);
        Assert.Equal(mail, (string)DatosDePrueba.Escalar("SELECT Mail FROM Usuario WHERE IdUsuario = @u", ("@u", j.GetProperty("idUsuario").GetInt32()))!);
    }

    [Fact]
    public async Task Hallazgo_hoy_la_edicion_de_la_ficha_deja_borrar_el_mail_aunque_el_registro_lo_exija()
    {
        // CARACTERIZACIÓN, no una regla que se quiera: PUT /pacientes/{id} no valida el mail (la pantalla del perfil tampoco lo exige: solo mira el formato si hay algo
        // escrito), y la columna Paciente.Mail admite NULL. Un paciente registrado con mail puede quedarse sin él, y entonces el recordatorio por mail se omite
        // ("El paciente no tiene mail cargado") y solo le queda la campanita. Si algún día se exige el mail en la edición, esta prueba se invierte.
        using var fabrica = new WebApplicationFactory<Program>();
        using var d = new DatosDePrueba();
        var paciente = d.Paciente(mail: d.Marca + "@example.com");
        using var yo = DatosDePrueba.Cliente(fabrica, d.UsuarioDePaciente(paciente), Roles.Paciente, idPaciente: paciente);

        var r = await yo.PutAsJsonAsync($"/pacientes/{paciente}", new { idPaciente = paciente, nombre = "Prueba " + d.Marca, dni = d.Marca, mail = "", estado = "Activo" });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.True(string.IsNullOrWhiteSpace((string?)DatosDePrueba.Escalar("SELECT Mail FROM Paciente WHERE IdPaciente = @p", ("@p", paciente))));
    }
}
