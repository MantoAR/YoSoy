using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

// El consentimiento de WhatsApp con la BASE REAL (la de pruebas, con el esquema de docs/produccion/01-produccion.sql):
//  · ConsentimientoWhatsAppDAO: ida y vuelta de cada campo, reemplazo en un solo paso, atomicidad, BAJA por número y carreras.
//  · El registro de un paciente (POST /pacientes): la casilla sin tildar no registra nada, tildada registra la aceptación a nombre del usuario
//    recién creado, y si algo no sirve (teléfono, edad, texto) no se crea ni la cuenta.
//  · El perfil de punta a punta con la aplicación real, incluido el cambio de teléfono.
//  · La BAJA que llega por el webhook de WhatsApp revoca en la base.
// Cada prueba crea sus filas con DatosDePrueba y las borra al terminar (la cascada sigue las claves foráneas).
[Trait("Categoria", "Integracion")]
[Trait("Tabla", "ConsentimientoWhatsApp")]   // necesita la tabla de la migración 16: se saltea con -Filtro 'Tabla!=ConsentimientoWhatsApp' contra una base sin ella
public class ConsentimientoWhatsAppBaseRealTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string HashDeV0 = "ef9676eb31a07d4f2da1dd8fd2890593dbac39e295a43c74ffb1dba004cf63ef";
    private static readonly DateTime Ahora = new(2031, 3, 4, 10, 0, 0, DateTimeKind.Utc);

    // Un celular de Buenos Aires que no se repite entre pruebas (ni con las filas de otras corridas).
    private static string TelefonoUnico() => "11" + Random.Shared.Next(10_000_000, 99_999_999);

    private static void PonerTelefono(int idPaciente, string? telefono, DateTime? nacimiento = null) =>
        DatosDePrueba.Ejecutar("UPDATE Paciente SET Telefono = @t, FechaNacimiento = @f WHERE IdPaciente = @p",
            ("@t", telefono), ("@f", nacimiento ?? new DateTime(1990, 5, 5)), ("@p", idPaciente));

    private static int FilasDe(int idPaciente) =>
        DatosDePrueba.Contar("SELECT COUNT(*) FROM ConsentimientoWhatsApp WHERE IdPaciente = @p", ("@p", idPaciente));

    // Estas pruebas son del consentimiento ENCENDIDO (con la tabla): la llave Mensajeria:AvisosPorWhatsApp viene apagada por defecto, así que la prenden.
    // Lo que pasa con la llave apagada y sin la tabla está en ConsentimientoWhatsAppSinTablaTests.
    private static WebApplicationFactory<Program> FabricaConLlave() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Mensajeria:AvisosPorWhatsApp"] = "true" })));

    private static int AbiertasDe(int idPaciente) =>
        DatosDePrueba.Contar("SELECT COUNT(*) FROM ConsentimientoWhatsApp WHERE IdPaciente = @p AND RevocadoUtc IS NULL", ("@p", idPaciente));

    // ───────────── ConsentimientoWhatsAppDAO ─────────────

    [Fact]
    public void Aceptar_y_Ultimo_devuelven_cada_campo_tal_cual_y_con_las_fechas_en_UTC()
    {
        using var d = new DatosDePrueba();
        var paciente = d.Paciente();
        var usuario = d.UsuarioDePaciente(paciente);
        var dao = new ConsentimientoWhatsAppDAO();

        var fila = dao.Aceptar(paciente, usuario, "1155550001", "registro", "v0-provisorio", Hash, Ahora, "texto-nuevo");

        Assert.NotNull(fila);
        Assert.True(fila.IdConsentimientoWhatsApp > 0);
        var leida = dao.Ultimo(paciente)!;
        Assert.Equal(fila, leida);
        Assert.Equal((paciente, usuario, "1155550001", "registro", "v0-provisorio", Hash), (leida.IdPaciente, leida.IdUsuario, leida.Telefono, leida.Origen, leida.VersionTexto, leida.HashTexto));
        Assert.Equal(Ahora, leida.AceptadoUtc);
        Assert.Equal(DateTimeKind.Utc, leida.AceptadoUtc.Kind);
        Assert.Null(leida.RevocadoUtc);
        Assert.Null(leida.MotivoRevocacion);
        Assert.True(leida.EstaAbierta);
    }

    [Fact]
    public void Un_paciente_que_nunca_acepto_no_tiene_ultimo()
    {
        using var d = new DatosDePrueba();

        Assert.Null(new ConsentimientoWhatsAppDAO().Ultimo(d.Paciente()));
    }

    [Fact]
    public void Aceptar_con_una_abierta_la_cierra_con_el_motivo_y_abre_la_nueva_en_un_solo_paso()
    {
        using var d = new DatosDePrueba();
        var paciente = d.Paciente();
        var usuario = d.UsuarioDePaciente(paciente);
        var dao = new ConsentimientoWhatsAppDAO();
        var primera = dao.Aceptar(paciente, usuario, "1155550001", "perfil", "v0-provisorio", Hash, Ahora, "texto-nuevo")!;

        var segunda = dao.Aceptar(paciente, usuario, "1155550002", "perfil", "v1", new string('b', 64), Ahora.AddMinutes(5), "telefono-cambiado");

        Assert.NotNull(segunda);
        Assert.Equal(2, FilasDe(paciente));
        Assert.Equal(1, AbiertasDe(paciente));
        Assert.Equal(segunda, dao.Ultimo(paciente));
        var cerrada = DatosDePrueba.Escalar("SELECT MotivoRevocacion FROM ConsentimientoWhatsApp WHERE IdConsentimientoWhatsApp = @i", ("@i", primera.IdConsentimientoWhatsApp));
        Assert.Equal("telefono-cambiado", cerrada);
        var fecha = (DateTime)DatosDePrueba.Escalar("SELECT RevocadoUtc FROM ConsentimientoWhatsApp WHERE IdConsentimientoWhatsApp = @i", ("@i", primera.IdConsentimientoWhatsApp))!;
        Assert.Equal(Ahora.AddMinutes(5), fecha);
    }

    [Fact]
    public void Si_la_aceptacion_nueva_no_entra_la_anterior_sigue_abierta()
    {
        using var d = new DatosDePrueba();
        var paciente = d.Paciente();
        var usuario = d.UsuarioDePaciente(paciente);
        var dao = new ConsentimientoWhatsAppDAO();
        dao.Aceptar(paciente, usuario, "1155550001", "perfil", "v0-provisorio", Hash, Ahora, "texto-nuevo");

        // Un usuario que no existe: la clave foránea rechaza el INSERT y, como va todo en una transacción, el UPDATE que cerraba la
        // anterior se deshace. Nunca queda al paciente sin su aceptación por una que no se pudo registrar.
        Assert.Throws<SqlException>(() => dao.Aceptar(paciente, 2_000_000_000, "1155550002", "perfil", "v1", Hash, Ahora.AddMinutes(5), "telefono-cambiado"));

        var ultima = dao.Ultimo(paciente)!;
        Assert.True(ultima.EstaAbierta);
        Assert.Equal("1155550001", ultima.Telefono);
        Assert.Equal(1, FilasDe(paciente));
    }

    [Fact]
    public void Revocar_cierra_la_abierta_con_el_motivo_y_la_segunda_vez_no_hace_nada()
    {
        using var d = new DatosDePrueba();
        var paciente = d.Paciente();
        var dao = new ConsentimientoWhatsAppDAO();
        dao.Aceptar(paciente, d.UsuarioDePaciente(paciente), "1155550001", "perfil", "v0-provisorio", Hash, Ahora, "texto-nuevo");

        Assert.True(dao.Revocar(paciente, Ahora.AddHours(1), "paciente"));
        Assert.False(dao.Revocar(paciente, Ahora.AddHours(2), "paciente"));

        var fila = dao.Ultimo(paciente)!;
        Assert.False(fila.EstaAbierta);
        Assert.Equal(Ahora.AddHours(1), fila.RevocadoUtc);
        Assert.Equal("paciente", fila.MotivoRevocacion);
        Assert.False(new ConsentimientoWhatsAppDAO().Revocar(d.Paciente(), Ahora, "paciente"));   // otro paciente, que nunca aceptó
    }

    [Fact]
    public void Revocar_con_un_reloj_atrasado_usa_la_hora_de_la_aceptacion_y_no_rompe_la_restriccion()
    {
        using var d = new DatosDePrueba();
        var paciente = d.Paciente();
        var dao = new ConsentimientoWhatsAppDAO();
        dao.Aceptar(paciente, d.UsuarioDePaciente(paciente), "1155550001", "perfil", "v0-provisorio", Hash, Ahora, "texto-nuevo");

        Assert.True(dao.Revocar(paciente, Ahora.AddMinutes(-30), "paciente"));

        Assert.Equal(Ahora, dao.Ultimo(paciente)!.RevocadoUtc);
    }

    [Fact]
    public void La_baja_por_telefono_revoca_a_todos_los_pacientes_con_ese_numero_y_devuelve_cuales()
    {
        using var d = new DatosDePrueba();
        var telefono = TelefonoUnico();
        var uno = d.Paciente();
        var dos = d.Paciente();
        var otro = d.Paciente();
        var dao = new ConsentimientoWhatsAppDAO();
        var filaUno = dao.Aceptar(uno, d.UsuarioDePaciente(uno), telefono, "perfil", "v0-provisorio", Hash, Ahora, "texto-nuevo")!;
        var filaDos = dao.Aceptar(dos, d.UsuarioDePaciente(dos), telefono, "registro", "v0-provisorio", Hash, Ahora, "texto-nuevo")!;
        dao.Aceptar(otro, d.UsuarioDePaciente(otro), TelefonoUnico(), "perfil", "v0-provisorio", Hash, Ahora, "texto-nuevo");

        var revocadas = dao.RevocarVigentesDelTelefono(telefono, Ahora.AddDays(1), "baja-whatsapp");

        Assert.Equal(new[] { (filaUno.IdConsentimientoWhatsApp, uno), (filaDos.IdConsentimientoWhatsApp, dos) }.OrderBy(x => x.Item1),
            revocadas.OrderBy(x => x.IdConsentimientoWhatsApp).Select(x => (x.IdConsentimientoWhatsApp, x.IdPaciente)));
        Assert.Equal("baja-whatsapp", dao.Ultimo(uno)!.MotivoRevocacion);
        Assert.Equal(Ahora.AddDays(1), dao.Ultimo(dos)!.RevocadoUtc);
        Assert.True(dao.Ultimo(otro)!.EstaAbierta);                                       // el de otro número no se toca
        Assert.Empty(dao.RevocarVigentesDelTelefono(telefono, Ahora.AddDays(2), "baja-whatsapp"));   // ya no queda ninguna abierta
    }

    [Fact]
    public void Muchos_pedidos_a_la_vez_nunca_dejan_dos_aceptaciones_abiertas_ni_lanzan_por_la_carrera()
    {
        using var d = new DatosDePrueba();
        var paciente = d.Paciente();
        var usuario = d.UsuarioDePaciente(paciente);
        var dao = new ConsentimientoWhatsAppDAO();
        var resultados = new ConsentimientoWhatsAppFila?[12];

        var excepciones = new List<Exception>();
        Parallel.For(0, resultados.Length, i =>
        {
            try { resultados[i] = dao.Aceptar(paciente, usuario, $"11555500{i:00}", "perfil", "v0-provisorio", Hash, Ahora.AddSeconds(i), "telefono-cambiado"); }
            catch (Exception e) { lock (excepciones) excepciones.Add(e); }
        });

        // Un deadlock (1205) puede pasar con muchos pedidos a la vez; lo que NO puede pasar es una violación del índice sin atrapar.
        Assert.DoesNotContain(excepciones, e => e is SqlException { Number: 2601 or 2627 });
        Assert.Equal(1, AbiertasDe(paciente));
        Assert.Equal(resultados.Count(r => r is not null), FilasDe(paciente));   // cada Aceptar que devolvió una fila dejó exactamente una
    }

    [Fact]
    public void Los_datos_del_paciente_traen_telefono_nacimiento_y_si_tiene_un_tutor()
    {
        using var d = new DatosDePrueba();
        var paciente = d.Paciente();
        PonerTelefono(paciente, "011 15 5555-0001", new DateTime(2015, 6, 7));
        var dao = new ConsentimientoWhatsAppDAO();

        var sinTutor = dao.ObtenerDatosPaciente(paciente)!;
        Assert.Equal(("011 15 5555-0001", new DateTime(2015, 6, 7), false), (sinTutor.Telefono, sinTutor.FechaNacimiento, sinTutor.TieneTutor));

        var tutor = Convert.ToInt32(DatosDePrueba.Escalar(
            "INSERT INTO Tutor (Nombre, Dni, Vinculo, Contacto) VALUES (@n, @d, N'Madre', N'x'); SELECT CAST(SCOPE_IDENTITY() AS INT);",
            ("@n", "Tutor " + d.Marca), ("@d", d.Marca)));
        d.AlLimpiar("Tutor", "IdTutor", tutor);
        DatosDePrueba.Ejecutar("INSERT INTO PacienteTutor (IdPaciente, IdTutor) VALUES (@p, @t)", ("@p", paciente), ("@t", tutor));
        Assert.True(dao.ObtenerDatosPaciente(paciente)!.TieneTutor);

        PonerTelefono(paciente, null);
        Assert.Null(dao.ObtenerDatosPaciente(paciente)!.Telefono);
        Assert.Null(dao.ObtenerDatosPaciente(2_000_000_000));
    }

    // ───────────── El registro de un paciente ─────────────

    private static object Registro(string marca, Action<Dictionary<string, object?>>? ajustes = null)
    {
        var campos = new Dictionary<string, object?>
        {
            ["nombre"] = "Prueba " + marca, ["dni"] = marca, ["mail"] = marca.ToLowerInvariant() + "@example.com", ["usuario"] = marca,
            ["contrasena"] = "ClaveDePrueba123", ["fechaNacimiento"] = "1990-05-05"
        };
        ajustes?.Invoke(campos);
        return campos;
    }

    private static string Marca() => "XTEST-" + Guid.NewGuid().ToString("N")[..10];

    private static async Task<(HttpResponseMessage R, JsonElement J)> Registrar(HttpClient c, object cuerpo)
    {
        var r = await c.PostAsJsonAsync("/pacientes", cuerpo);
        // Un 400 del enlace de parámetros (un tipo que no es el esperado) viene como texto, no como JSON.
        var texto = await r.Content.ReadAsStringAsync();
        JsonElement j = default;
        if (!string.IsNullOrWhiteSpace(texto))
            try { using var doc = JsonDocument.Parse(texto); j = doc.RootElement.Clone(); } catch (JsonException) { }
        return (r, j);
    }

    private static void LimpiarRegistro(DatosDePrueba d, JsonElement creado)
    {
        d.AlLimpiar("Usuario", "IdUsuario", creado.GetProperty("idUsuario").GetInt32());
        d.AlLimpiar("Paciente", "IdPaciente", creado.GetProperty("idPaciente").GetInt32());
    }

    private static bool Existe(string marca) =>
        DatosDePrueba.Contar("SELECT COUNT(*) FROM Paciente WHERE Dni = @m", ("@m", marca)) +
        DatosDePrueba.Contar("SELECT COUNT(*) FROM Usuario WHERE Username = @m", ("@m", marca)) > 0;

    [Theory]
    [InlineData("sin-los-campos")]
    [InlineData("false")]
    [InlineData("null")]
    public async Task Registrarse_sin_tildar_la_casilla_no_registra_ningun_consentimiento(string caso)
    {
        using var fabrica = FabricaConLlave();
        using var cliente = fabrica.CreateClient();
        using var d = new DatosDePrueba();
        var marca = Marca();

        var (r, j) = await Registrar(cliente, Registro(marca, c =>
        {
            c["telefono"] = "11 5555-0001";
            if (caso == "false") { c["aceptaWhatsApp"] = false; c["versionTextoWhatsApp"] = "v0-provisorio"; }
            if (caso == "null") { c["aceptaWhatsApp"] = null; c["versionTextoWhatsApp"] = null; }
        }));
        if (r.IsSuccessStatusCode) LimpiarRegistro(d, j);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.False(j.GetProperty("consentimientoWhatsApp").GetProperty("pedido").GetBoolean());
        Assert.Equal("sin-decision", j.GetProperty("consentimientoWhatsApp").GetProperty("estado").GetString());
        Assert.Equal(0, FilasDe(j.GetProperty("idPaciente").GetInt32()));
    }

    [Fact]
    public async Task Registrarse_con_la_casilla_tildada_registra_la_aceptacion_a_nombre_del_usuario_recien_creado()
    {
        using var fabrica = FabricaConLlave();
        using var cliente = fabrica.CreateClient();
        using var d = new DatosDePrueba();
        var marca = Marca();

        var (r, j) = await Registrar(cliente, Registro(marca, c =>
        {
            c["telefono"] = "011 15 5555-0001";
            c["aceptaWhatsApp"] = true;
            c["versionTextoWhatsApp"] = "v0-provisorio";
        }));
        if (r.IsSuccessStatusCode) LimpiarRegistro(d, j);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.True(j.GetProperty("consentimientoWhatsApp").GetProperty("pedido").GetBoolean());
        Assert.Equal("acepta", j.GetProperty("consentimientoWhatsApp").GetProperty("estado").GetString());

        var idPaciente = j.GetProperty("idPaciente").GetInt32();
        var idUsuario = j.GetProperty("idUsuario").GetInt32();
        var fila = new ConsentimientoWhatsAppDAO().Ultimo(idPaciente)!;
        Assert.Equal((idPaciente, idUsuario, "1155550001", "registro", "v0-provisorio", HashDeV0), (fila.IdPaciente, fila.IdUsuario, fila.Telefono, fila.Origen, fila.VersionTexto, fila.HashTexto));
        Assert.True(fila.EstaAbierta);
        Assert.InRange((DateTime.UtcNow - fila.AceptadoUtc).TotalSeconds, -5, 60);
        Assert.Equal(1, DatosDePrueba.Contar(@"SELECT COUNT(*) FROM LogAuditoria WHERE IdUsuario = @u AND IdEntidad = @p AND Accion = 'Consentimiento de WhatsApp aceptado'",
            ("@u", idUsuario), ("@p", idPaciente)));

        // Y ya con su cuenta ve que está aceptado.
        using var propio = DatosDePrueba.Cliente(fabrica, idUsuario, Roles.Paciente, idPaciente: idPaciente);
        var estado = await VideoApp.Json(await propio.GetAsync("/consentimiento-whatsapp"));
        Assert.Equal("acepta", estado.GetProperty("estado").GetString());
    }

    [Theory]
    [InlineData("5555-0001", 400, "telefono-invalido")]       // sin código de área
    [InlineData("+34 612 345 678", 400, "telefono-invalido")] // de otro país
    [InlineData("", 400, "telefono-invalido")]
    [InlineData(null, 400, "telefono-invalido")]
    public async Task Con_la_casilla_tildada_y_un_telefono_que_no_sirve_no_se_crea_la_cuenta(string? telefono, int status, string codigo)
    {
        using var fabrica = FabricaConLlave();
        using var cliente = fabrica.CreateClient();
        using var d = new DatosDePrueba();
        var marca = Marca();

        var (r, j) = await Registrar(cliente, Registro(marca, c =>
        {
            c["telefono"] = telefono;
            c["aceptaWhatsApp"] = true;
            c["versionTextoWhatsApp"] = "v0-provisorio";
        }));
        if (r.IsSuccessStatusCode) LimpiarRegistro(d, j);

        Assert.Equal(status, (int)r.StatusCode);
        Assert.Equal(codigo, j.GetProperty("codigo").GetString());
        Assert.False(string.IsNullOrWhiteSpace(j.GetProperty("mensaje").GetString()));
        Assert.False(Existe(marca), "no se tendría que haber creado ni el paciente ni el usuario");
    }

    [Theory]
    [InlineData("v-anterior", 409, "version-desactualizada")]
    [InlineData("", 409, "version-desactualizada")]
    [InlineData(null, 409, "version-desactualizada")]
    public async Task Con_la_casilla_tildada_y_una_version_que_no_es_la_vigente_no_se_crea_la_cuenta(string? version, int status, string codigo)
    {
        using var fabrica = FabricaConLlave();
        using var cliente = fabrica.CreateClient();
        using var d = new DatosDePrueba();
        var marca = Marca();

        var (r, j) = await Registrar(cliente, Registro(marca, c =>
        {
            c["telefono"] = "11 5555-0001";
            c["aceptaWhatsApp"] = true;
            c["versionTextoWhatsApp"] = version;
        }));
        if (r.IsSuccessStatusCode) LimpiarRegistro(d, j);

        Assert.Equal(status, (int)r.StatusCode);
        Assert.Equal(codigo, j.GetProperty("codigo").GetString());
        Assert.False(Existe(marca));
    }

    [Fact]
    public async Task Un_menor_que_tilda_la_casilla_no_puede_y_no_se_crea_la_cuenta()
    {
        using var fabrica = FabricaConLlave();
        using var cliente = fabrica.CreateClient();
        using var d = new DatosDePrueba();
        var marca = Marca();

        var (r, j) = await Registrar(cliente, Registro(marca, c =>
        {
            c["telefono"] = "11 5555-0001";
            c["fechaNacimiento"] = DateTime.Today.AddYears(-15).ToString("yyyy-MM-dd");
            c["aceptaWhatsApp"] = true;
            c["versionTextoWhatsApp"] = "v0-provisorio";
        }));
        if (r.IsSuccessStatusCode) LimpiarRegistro(d, j);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("menor-de-edad", j.GetProperty("codigo").GetString());
        Assert.False(Existe(marca));
    }

    [Fact]
    public async Task La_casilla_mandada_como_texto_no_cuenta_como_aceptada_y_no_se_crea_la_cuenta()
    {
        using var fabrica = FabricaConLlave();
        using var cliente = fabrica.CreateClient();
        using var d = new DatosDePrueba();
        var marca = Marca();

        var (r, _) = await Registrar(cliente, Registro(marca, c =>
        {
            c["telefono"] = "11 5555-0001";
            c["aceptaWhatsApp"] = "true";          // un string no es un true explícito
            c["versionTextoWhatsApp"] = "v0-provisorio";
        }));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.False(Existe(marca));
    }

    [Fact]
    public async Task Un_usuario_repetido_con_la_casilla_tildada_da_409_y_no_deja_otra_aceptacion()
    {
        using var fabrica = FabricaConLlave();
        using var cliente = fabrica.CreateClient();
        using var d = new DatosDePrueba();
        var marca = Marca();
        Action<Dictionary<string, object?>> tildada = c =>
        {
            c["telefono"] = "11 5555-0001";
            c["aceptaWhatsApp"] = true;
            c["versionTextoWhatsApp"] = "v0-provisorio";
        };

        var (r1, j1) = await Registrar(cliente, Registro(marca, tildada));
        if (r1.IsSuccessStatusCode) LimpiarRegistro(d, j1);
        var (r2, _) = await Registrar(cliente, Registro(marca, c => { tildada(c); c["dni"] = marca + "-B"; c["mail"] = "b" + marca.ToLowerInvariant() + "@example.com"; }));

        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, r2.StatusCode);
        Assert.Equal(1, DatosDePrueba.Contar("SELECT COUNT(*) FROM ConsentimientoWhatsApp WHERE IdPaciente = @p", ("@p", j1.GetProperty("idPaciente").GetInt32())));
        Assert.Equal(0, DatosDePrueba.Contar("SELECT COUNT(*) FROM Paciente WHERE Dni = @m", ("@m", marca + "-B")));
    }

    // ───────────── El perfil de punta a punta ─────────────

    [Fact]
    public async Task El_perfil_de_punta_a_punta_con_la_aplicacion_real_incluido_el_cambio_de_telefono()
    {
        using var fabrica = FabricaConLlave();
        using var d = new DatosDePrueba();
        var paciente = d.Paciente();
        var usuario = d.UsuarioDePaciente(paciente);
        PonerTelefono(paciente, "11 5555-0001");
        using var yo = DatosDePrueba.Cliente(fabrica, usuario, Roles.Paciente, idPaciente: paciente);

        // Todavía no decidió: nada viene aceptado.
        var inicial = await VideoApp.Json(await yo.GetAsync("/consentimiento-whatsapp"));
        Assert.Equal("sin-decision", inicial.GetProperty("estado").GetString());
        Assert.True(inicial.GetProperty("disponible").GetBoolean());
        Assert.Equal(0, FilasDe(paciente));

        // Acepta.
        var acepta = await yo.PostAsJsonAsync("/consentimiento-whatsapp/aceptar", new { version = "v0-provisorio" });
        Assert.Equal(HttpStatusCode.OK, acepta.StatusCode);
        Assert.Equal("acepta", (await VideoApp.Json(acepta)).GetProperty("estado").GetString());
        var fila = new ConsentimientoWhatsAppDAO().Ultimo(paciente)!;
        Assert.Equal((usuario, "1155550001", "perfil", HashDeV0), (fila.IdUsuario, fila.Telefono, fila.Origen, fila.HashTexto));
        Assert.Equal(PermisoEnvioWhatsApp.ConConsentimiento,
            ((IPoliticaEnvioWhatsApp)fabrica.Services.GetService(typeof(IPoliticaEnvioWhatsApp))!).Evaluar(paciente, "11 5555-0001"));

        // Edita su perfil: otro celular. Nadie toca el consentimiento, pero ya no vale para el número nuevo.
        var ficha = await VideoApp.Json(await yo.GetAsync($"/pacientes/{paciente}"));
        var cambiada = new Dictionary<string, object?>
        {
            ["idPaciente"] = paciente, ["nombre"] = ficha.GetProperty("nombre").GetString(), ["dni"] = ficha.GetProperty("dni").GetString(),
            ["telefono"] = "351 412-3456", ["mail"] = ficha.GetProperty("mail").GetString(), ["estado"] = "Activo",
            ["fechaNacimiento"] = "1990-05-05T00:00:00"
        };
        Assert.Equal(HttpStatusCode.OK, (await yo.PutAsJsonAsync($"/pacientes/{paciente}", cambiada)).StatusCode);
        var tras = await VideoApp.Json(await yo.GetAsync("/consentimiento-whatsapp"));
        Assert.Equal("telefono-cambiado", tras.GetProperty("estado").GetString());
        Assert.Equal(PermisoEnvioWhatsApp.SinDecision,
            ((IPoliticaEnvioWhatsApp)fabrica.Services.GetService(typeof(IPoliticaEnvioWhatsApp))!).Evaluar(paciente, "351 412-3456"));

        // Vuelve a aceptar: la vieja se cierra por el cambio de número y queda una sola abierta, la del número nuevo.
        Assert.Equal(HttpStatusCode.OK, (await yo.PostAsJsonAsync("/consentimiento-whatsapp/aceptar", new { version = "v0-provisorio" })).StatusCode);
        Assert.Equal(2, FilasDe(paciente));
        Assert.Equal(1, AbiertasDe(paciente));
        Assert.Equal("3514123456", new ConsentimientoWhatsAppDAO().Ultimo(paciente)!.Telefono);
        Assert.Equal("telefono-cambiado", DatosDePrueba.Escalar(
            "SELECT TOP 1 MotivoRevocacion FROM ConsentimientoWhatsApp WHERE IdPaciente = @p AND RevocadoUtc IS NOT NULL", ("@p", paciente)));

        // Revoca: queda el historial completo y no se manda más nada.
        var revoca = await yo.PostAsync("/consentimiento-whatsapp/revocar", null);
        Assert.Equal("revocado", (await VideoApp.Json(revoca)).GetProperty("estado").GetString());
        Assert.Equal(0, AbiertasDe(paciente));
        Assert.Equal(PermisoEnvioWhatsApp.Revocado,
            ((IPoliticaEnvioWhatsApp)fabrica.Services.GetService(typeof(IPoliticaEnvioWhatsApp))!).Evaluar(paciente, "351 412-3456"));
        Assert.Equal(3, DatosDePrueba.Contar(@"SELECT COUNT(*) FROM LogAuditoria WHERE IdUsuario = @u AND IdEntidad = @p AND Accion LIKE 'Consentimiento de WhatsApp%'",
            ("@u", usuario), ("@p", paciente)));   // acepta, acepta, revoca
    }

    [Fact]
    public async Task Otro_paciente_no_puede_tocar_el_consentimiento_de_este()
    {
        using var fabrica = FabricaConLlave();
        using var d = new DatosDePrueba();
        var uno = d.Paciente();
        var dos = d.Paciente();
        PonerTelefono(uno, "11 5555-0001");
        PonerTelefono(dos, "351 412-3456");
        using var cliente1 = DatosDePrueba.Cliente(fabrica, d.UsuarioDePaciente(uno), Roles.Paciente, idPaciente: uno);
        using var cliente2 = DatosDePrueba.Cliente(fabrica, d.UsuarioDePaciente(dos), Roles.Paciente, idPaciente: dos);
        await cliente1.PostAsJsonAsync("/consentimiento-whatsapp/aceptar", new { version = "v0-provisorio" });

        // El 2 intenta revocar y aceptar "por" el 1, con todo lo que se le ocurra en el cuerpo: solo toca lo suyo.
        await cliente2.PostAsync("/consentimiento-whatsapp/revocar", null);
        await cliente2.PostAsJsonAsync("/consentimiento-whatsapp/aceptar", new { version = "v0-provisorio", idPaciente = uno, idUsuario = 1 });

        Assert.Equal(1, AbiertasDe(uno));
        Assert.Equal("1155550001", new ConsentimientoWhatsAppDAO().Ultimo(uno)!.Telefono);
        Assert.Equal("3514123456", new ConsentimientoWhatsAppDAO().Ultimo(dos)!.Telefono);
    }

    // ───────────── La BAJA por WhatsApp ─────────────

    private const string SecretoDePrueba = "secreto-de-prueba-123";

    private static WebApplicationFactory<Program> FabricaConWebhook() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Mensajeria:WhatsApp:Secreto"] = SecretoDePrueba, ["Mensajeria:AvisosPorWhatsApp"] = "true" })));

    [Fact]
    public async Task Una_baja_que_llega_por_el_webhook_revoca_la_aceptacion_en_la_base_y_no_se_contesta()
    {
        using var fabrica = FabricaConWebhook();
        using var d = new DatosDePrueba();
        var telefono = TelefonoUnico();
        var paciente = d.Paciente();
        PonerTelefono(paciente, telefono);
        var dao = new ConsentimientoWhatsAppDAO();
        dao.Aceptar(paciente, d.UsuarioDePaciente(paciente), telefono, "perfil", "v0-provisorio", HashDeV0, Ahora, "texto-nuevo");
        d.AlLimpiar("Conversacion", "Telefono", telefono);
        using var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Add("X-Shared-Secret", SecretoDePrueba);

        {
            // Llega como lo manda whatsapp-service: con el 549 adelante.
            var r = await cliente.PostAsJsonAsync("/mensajeria/whatsapp/entrante", new { telefono = "549" + telefono, texto = "BAJA" });

            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal(0, (await VideoApp.Json(r)).GetProperty("respuestas").GetArrayLength());   // el bot no contesta
            var fila = dao.Ultimo(paciente)!;
            Assert.False(fila.EstaAbierta);
            Assert.Equal("baja-whatsapp", fila.MotivoRevocacion);
            Assert.Equal(PermisoEnvioWhatsApp.Revocado,
                ((IPoliticaEnvioWhatsApp)fabrica.Services.GetService(typeof(IPoliticaEnvioWhatsApp))!).Evaluar(paciente, telefono));
            Assert.Equal(1, DatosDePrueba.Contar(@"SELECT COUNT(*) FROM LogAuditoria
                WHERE IdUsuario IS NULL AND Entidad = 'Paciente' AND IdEntidad = @p AND Accion LIKE 'Consentimiento de WhatsApp revocado (BAJA%'", ("@p", paciente)));
            // En el historial queda como cualquier mensaje que llega, y nada saliente.
            Assert.Equal(1, DatosDePrueba.Contar(@"SELECT COUNT(*) FROM MensajeChat m JOIN Conversacion c ON c.IdConversacion = m.IdConversacion
                WHERE c.Telefono = @t AND m.Direccion = 'Entrante'", ("@t", telefono)));
            Assert.Equal(0, DatosDePrueba.Contar(@"SELECT COUNT(*) FROM MensajeChat m JOIN Conversacion c ON c.IdConversacion = m.IdConversacion
                WHERE c.Telefono = @t AND m.Direccion = 'Saliente'", ("@t", telefono)));
        }
        // La auditoría es de solo agregar (migración 19): lo que la API deja en LogAuditoria queda, como en producción.
    }

    [Fact]
    public async Task Un_mensaje_que_no_es_una_baja_por_el_webhook_no_toca_el_consentimiento()
    {
        using var fabrica = FabricaConWebhook();
        using var d = new DatosDePrueba();
        var telefono = TelefonoUnico();
        var paciente = d.Paciente();
        PonerTelefono(paciente, telefono);
        var dao = new ConsentimientoWhatsAppDAO();
        dao.Aceptar(paciente, d.UsuarioDePaciente(paciente), telefono, "perfil", "v0-provisorio", HashDeV0, Ahora, "texto-nuevo");
        d.AlLimpiar("Conversacion", "Telefono", telefono);
        using var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Add("X-Shared-Secret", SecretoDePrueba);

        foreach (var texto in new[] { "hola", "cancelar", "baja por favor", "3" })
            Assert.Equal(HttpStatusCode.OK, (await cliente.PostAsJsonAsync("/mensajeria/whatsapp/entrante", new { telefono = "549" + telefono, texto })).StatusCode);

        Assert.True(dao.Ultimo(paciente)!.EstaAbierta);
    }
}
