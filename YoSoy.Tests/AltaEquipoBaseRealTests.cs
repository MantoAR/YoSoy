using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

// El alta de alguien del equipo por invitación, de punta a punta contra la base de pruebas: el Configurador lo registra, la API crea la cuenta "Pendiente" con su
// permiso, la persona abre el link, elige su contraseña (que cumple la política de la aplicación) y recién entonces puede ingresar, con el módulo que le dieron.
// El mail no sale (en las pruebas el correo no está configurado: EmailService lo deja en el log) y, en Desarrollo, la respuesta trae el link para poder seguir.
[Trait("Categoria", "Integracion")]
public class AltaEquipoBaseRealTests : IDisposable
{
    private readonly DatosDePrueba _datos = new();
    private readonly WebApplicationFactory<Program> _fabrica = new();

    public void Dispose()
    {
        _fabrica.Dispose();
        _datos.Dispose();   // la cuenta creada queda en la auditoría (solo agregar) y se retiene, con su marca
    }

    private HttpClient Cliente(string? token = null)
    {
        var c = _fabrica.CreateClient();
        if (token is not null) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    // Un CUIL de persona con dígito verificador correcto (el mismo cálculo que valida la API).
    private static string CuilNuevo()
    {
        var dni = Random.Shared.Next(10_000_000, 99_999_999);
        var base10 = "20" + dni;
        int[] pesos = { 5, 4, 3, 2, 7, 6, 5, 4, 3, 2 };
        var suma = 0;
        for (var i = 0; i < 10; i++) suma += (base10[i] - '0') * pesos[i];
        var resto = 11 - suma % 11;
        var dv = resto == 11 ? 0 : resto == 10 ? -1 : resto;
        // Con dígito 10 el CUIL cambia de prefijo (23): se vuelve a calcular con ese prefijo.
        if (dv == -1) return CuilNuevo();
        return base10 + dv;
    }

    private HttpClient Configurador() =>
        Cliente(_fabrica.Services.GetRequiredService<JwtEmisorService>().Emitir(_datos.Usuario(), Roles.Configurador));

    private object Pedido(string usuario, string[] permisos) => new
    {
        nombre = "Prueba", apellido = "Equipo " + _datos.Marca, cuil = CuilNuevo(), telefono = "3510000000",
        mail = usuario + "@ejemplo.test", username = usuario, permisos, privilegios = Array.Empty<object>()
    };

    [Fact]
    public async Task El_alta_por_invitacion_deja_la_cuenta_pendiente_y_solo_entra_despues_de_activarla_con_una_contrasena_valida()
    {
        var usuario = ("eq" + Guid.NewGuid().ToString("N")[..10]).ToLowerInvariant();

        var alta = await Configurador().PostAsJsonAsync("/usuarios/equipo", Pedido(usuario, new[] { "crm" }));
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var cuerpo = await alta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(cuerpo.GetProperty("mailEnviado").GetBoolean());   // el correo de las pruebas no está configurado
        var link = cuerpo.GetProperty("linkActivacion").GetString()!;   // en Desarrollo la respuesta lo trae
        var token = Uri.UnescapeDataString(link[(link.IndexOf("token=", StringComparison.Ordinal) + 6)..]);

        // La cuenta nace Pendiente, con el permiso pedido, y en la base solo queda el hash del token.
        Assert.Equal("Pendiente", (string)DatosDePrueba.Escalar("SELECT Estado FROM Usuario WHERE Username = @u", ("@u", usuario))!);
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM UsuarioPermiso up JOIN Permiso p ON p.IdPermiso = up.IdPermiso JOIN Usuario u ON u.IdUsuario = up.IdUsuario WHERE u.Username = @u AND p.Clave = 'crm'", ("@u", usuario)));
        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM InvitacionCuenta WHERE TokenHash = @t", ("@t", token)));   // el token en claro no está
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM InvitacionCuenta WHERE TokenHash = @t", ("@t", AltaEquipo.HashToken(token))));

        // Antes de activar no entra, ni con una contraseña cualquiera.
        var antes = await Cliente().PostAsJsonAsync("/login", new { username = usuario, password = "Prueba12345" });
        Assert.Equal(HttpStatusCode.Unauthorized, antes.StatusCode);

        // El link se puede consultar sin sesión y dice a quién corresponde.
        var info = await Cliente().GetAsync($"/cuentas/invitacion?token={Uri.EscapeDataString(token)}");
        Assert.Equal(HttpStatusCode.OK, info.StatusCode);

        // Una contraseña débil (corta, o sin números) se rechaza y el link sigue sirviendo.
        foreach (var debil in new[] { "abc12", "soloLetrasLargas", "1234567890" })
        {
            var r = await Cliente().PostAsJsonAsync("/cuentas/activar", new { token, password = debil });
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }
        Assert.Equal("Pendiente", (string)DatosDePrueba.Escalar("SELECT Estado FROM Usuario WHERE Username = @u", ("@u", usuario))!);

        // Con una válida queda Activa; el link no se puede reutilizar.
        var ok = await Cliente().PostAsJsonAsync("/cuentas/activar", new { token, password = "Prueba12345" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("Activo", (string)DatosDePrueba.Escalar("SELECT Estado FROM Usuario WHERE Username = @u", ("@u", usuario))!);
        var otraVez = await Cliente().PostAsJsonAsync("/cuentas/activar", new { token, password = "OtraClave12345" });
        Assert.Equal(HttpStatusCode.Gone, otraVez.StatusCode);

        // Ahora ingresa, con su único módulo, y atiende los chats.
        var login = await Cliente().PostAsJsonAsync("/login", new { username = usuario, password = "Prueba12345" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var j = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("crm", j.GetProperty("modulo").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Cliente(j.GetProperty("token").GetString()).GetAsync("/mensajeria/conversaciones")).StatusCode);
    }

    [Fact]
    public async Task Solo_el_Configurador_da_de_alta_y_el_pedido_se_valida()
    {
        var usuario = ("eq" + Guid.NewGuid().ToString("N")[..10]).ToLowerInvariant();
        var emisor = _fabrica.Services.GetRequiredService<JwtEmisorService>();

        // CRM, profesional y sin sesión no pueden.
        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(emisor.Emitir(1, Roles.Crm)).PostAsJsonAsync("/usuarios/equipo", Pedido(usuario, new[] { "crm" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(emisor.Emitir(1, Roles.Profesional, idProfesional: 1)).PostAsJsonAsync("/usuarios/equipo", Pedido(usuario, new[] { "crm" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().PostAsJsonAsync("/usuarios/equipo", Pedido(usuario, new[] { "crm" }))).StatusCode);

        // Sin rol, con un permiso que no existe, o "profesional" sin matrícula: 400 y no se crea nada.
        var configurador = Configurador();
        Assert.Equal(HttpStatusCode.BadRequest, (await configurador.PostAsJsonAsync("/usuarios/equipo", Pedido(usuario, Array.Empty<string>()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await configurador.PostAsJsonAsync("/usuarios/equipo", Pedido(usuario, new[] { "inventado" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await configurador.PostAsJsonAsync("/usuarios/equipo", Pedido(usuario, new[] { "profesional" }))).StatusCode);
        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM Usuario WHERE Username = @u", ("@u", usuario)));
    }
}
