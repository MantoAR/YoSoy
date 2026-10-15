using System.Security.Cryptography;
using System.Text;

// Crear el usuario de un profesional aprobado desde el Configurador (CrearUsuarioProfesionalService). Sin base:
// el acceso a datos es un doble en memoria. Que el Usuario y el vínculo se creen juntos lo prueban las pruebas de
// integración (CrearUsuarioProfesionalEndpointsTests).
public class CrearUsuarioProfesionalServiceTests
{
    private sealed class RepoFalso : IUsuarioProfesionalAdminRepositorio
    {
        public Profesional? Profesional { get; set; } = new()
        {
            IdProfesional = 7, Nombre = "Ana", Apellido = "Paz", Matricula = "MP-7", Estado = "Activo",
            Mail = "ana.paz@example.com", IdEspecialidad = 1
        };
        public ResultadoCreacionUsuario Resultado { get; set; } = ResultadoCreacionUsuario.Creado;
        public List<(int IdProfesional, string Username, string Hash, string Mail, DateTime Fecha)> Llamadas { get; } = new();

        public Profesional? ObtenerProfesional(int idProfesional) => Profesional;

        public (ResultadoCreacionUsuario Resultado, int IdUsuario) CrearUsuarioYVincular(
            int idProfesional, string username, string passwordHash, string mail, DateTime fechaVinculacion)
        {
            Llamadas.Add((idProfesional, username, passwordHash, mail, fechaVinculacion));
            return (Resultado, Resultado == ResultadoCreacionUsuario.Creado ? 500 : 0);
        }
    }

    private readonly RepoFalso _repo = new();
    private readonly RelojFijo _reloj = new(new DateTime(2026, 10, 15, 9, 0, 0));
    private CrearUsuarioProfesionalService Servicio => new(_repo, _reloj);

    private static string Sha256(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    private static SolicitudCrearUsuarioProfesional Pedido(string? username = "ana.paz", string? mail = null,
        string? contrasena = "ClaveSegura123", bool generar = false) => new(username, mail, contrasena, generar);

    // ── Camino feliz ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Con_contrasena_escrita_crea_el_usuario_y_no_devuelve_ninguna_contrasena()
    {
        var r = Servicio.Crear(7, Pedido(username: "  ana.paz  "));

        Assert.True(r.Ok);
        Assert.Equal(201, r.StatusCode);
        Assert.Equal(500, r.IdUsuario);
        Assert.Equal("ana.paz", r.Username);                    // recortado
        Assert.Equal("ana.paz@example.com", r.Mail);            // el del profesional
        Assert.Null(r.ContrasenaTemporal);                      // la que escribió el Configurador no vuelve

        var llamada = Assert.Single(_repo.Llamadas);
        Assert.Equal(7, llamada.IdProfesional);
        Assert.Equal("ana.paz", llamada.Username);
        Assert.Equal(Sha256("ClaveSegura123"), llamada.Hash);   // el mismo hash que usa /login
        Assert.Equal(new DateTime(2026, 10, 15), llamada.Fecha);
    }

    [Fact]
    public void El_mail_del_pedido_tiene_prioridad_sobre_el_del_profesional()
    {
        var r = Servicio.Crear(7, Pedido(mail: "  otro@example.com "));
        Assert.True(r.Ok);
        Assert.Equal("otro@example.com", r.Mail);
        Assert.Equal("otro@example.com", _repo.Llamadas.Single().Mail);
    }

    [Fact]
    public void Con_generar_contrasena_devuelve_una_temporal_una_sola_vez_y_se_guarda_su_hash()
    {
        var r = Servicio.Crear(7, Pedido(contrasena: "ignorada-123", generar: true));

        Assert.True(r.Ok);
        Assert.NotNull(r.ContrasenaTemporal);
        Assert.Equal(CrearUsuarioProfesionalService.LargoContrasenaTemporal, r.ContrasenaTemporal!.Length);
        Assert.Equal(Sha256(r.ContrasenaTemporal), _repo.Llamadas.Single().Hash);
        Assert.NotEqual(Sha256("ignorada-123"), _repo.Llamadas.Single().Hash);   // la escrita se ignora
    }

    [Fact]
    public void El_resultado_nunca_trae_el_hash_de_la_contrasena()
    {
        var propiedades = typeof(ResultadoCrearUsuarioProfesional).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain(propiedades, n => n.Contains("Hash", StringComparison.OrdinalIgnoreCase));
    }

    // ── Validación de lo que escribe el Configurador ─────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ab")]                       // muy corto
    [InlineData("con espacio")]
    [InlineData("ana@paz")]                  // la arroba no vale: es usuario, no mail
    [InlineData("ñandú")]
    public void Un_usuario_invalido_es_400_y_no_toca_la_base(string? username)
    {
        var r = Servicio.Crear(7, Pedido(username: username));
        Assert.Equal(400, r.StatusCode);
        Assert.Empty(_repo.Llamadas);
    }

    [Fact]
    public void El_usuario_acepta_de_3_a_50_caracteres_con_punto_guion_y_guion_bajo()
    {
        Assert.True(Servicio.Crear(7, Pedido(username: "a.b")).Ok);
        Assert.True(Servicio.Crear(7, Pedido(username: "ana_paz-2")).Ok);
        Assert.True(Servicio.Crear(7, Pedido(username: new string('a', 50))).Ok);
        Assert.Equal(400, Servicio.Crear(7, Pedido(username: new string('a', 51))).StatusCode);
    }

    [Theory]
    [InlineData("1234567")]                  // 7: una menos del mínimo
    [InlineData("")]
    [InlineData(null)]
    public void Una_contrasena_de_menos_de_8_es_400(string? contrasena)
    {
        var r = Servicio.Crear(7, Pedido(contrasena: contrasena));
        Assert.Equal(400, r.StatusCode);
        Assert.Contains("8", r.Mensaje);
        Assert.Empty(_repo.Llamadas);
    }

    [Fact]
    public void Una_contrasena_de_exactamente_8_vale_y_una_de_mas_de_100_no()
    {
        Assert.True(Servicio.Crear(7, Pedido(contrasena: "12345678")).Ok);
        Assert.Equal(400, Servicio.Crear(7, Pedido(contrasena: new string('x', 101))).StatusCode);
    }

    [Theory]
    [InlineData("no-es-un-mail")]
    [InlineData("a b@c.com")]
    [InlineData("@example.com")]
    public void Un_mail_invalido_es_400(string mail)
    {
        var r = Servicio.Crear(7, Pedido(mail: mail));
        Assert.Equal(400, r.StatusCode);
        Assert.Empty(_repo.Llamadas);
    }

    [Fact]
    public void Sin_mail_en_el_pedido_ni_en_la_ficha_es_400()
    {
        _repo.Profesional!.Mail = null!;
        var r = Servicio.Crear(7, Pedido(mail: null));
        Assert.Equal(400, r.StatusCode);
        Assert.Contains("mail", r.Mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_repo.Llamadas);
    }

    // ── El profesional ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Un_profesional_que_no_existe_es_404()
    {
        _repo.Profesional = null;
        Assert.Equal(404, Servicio.Crear(99, Pedido()).StatusCode);
        Assert.Empty(_repo.Llamadas);
    }

    [Theory]
    [InlineData("Pendiente")]
    [InlineData("Rechazado")]
    [InlineData("Inactivo")]
    public void Solo_a_un_profesional_activo_se_le_crea_usuario(string estado)
    {
        _repo.Profesional!.Estado = estado;
        var r = Servicio.Crear(7, Pedido());
        Assert.Equal(409, r.StatusCode);
        Assert.Empty(_repo.Llamadas);
    }

    // ── Lo que decide la base, en la transacción ─────────────────────────────────────────────

    [Theory]
    [InlineData(ResultadoCreacionUsuario.YaTieneUsuario, 409, "ya tiene un usuario")]
    [InlineData(ResultadoCreacionUsuario.UsernameEnUso, 409, "nombre de usuario ya está en uso")]
    [InlineData(ResultadoCreacionUsuario.MailEnUso, 409, "mail ya está asociado")]
    [InlineData(ResultadoCreacionUsuario.ProfesionalNoActivo, 409, "activo")]
    [InlineData(ResultadoCreacionUsuario.ProfesionalInexistente, 404, "No existe")]
    public void Los_resultados_de_la_base_se_traducen_a_un_status_y_un_mensaje_claros(
        ResultadoCreacionUsuario deLaBase, int status, string fragmento)
    {
        _repo.Resultado = deLaBase;
        var r = Servicio.Crear(7, Pedido());

        Assert.False(r.Ok);
        Assert.Equal(status, r.StatusCode);
        Assert.Contains(fragmento, r.Mensaje);
        Assert.Null(r.ContrasenaTemporal);       // si falló, no se devuelve ninguna contraseña
    }

    [Fact]
    public void Si_la_base_rechaza_con_una_contrasena_generada_esta_no_se_devuelve()
    {
        _repo.Resultado = ResultadoCreacionUsuario.UsernameEnUso;
        var r = Servicio.Crear(7, Pedido(generar: true));
        Assert.Equal(409, r.StatusCode);
        Assert.Null(r.ContrasenaTemporal);
    }

    // ── La contraseña temporal ───────────────────────────────────────────────────────────────

    [Fact]
    public void Las_temporales_son_legibles_fuertes_y_distintas_entre_si()
    {
        const string ambiguos = "0O1lI";
        var vistas = new HashSet<string>();
        for (var i = 0; i < 500; i++)
        {
            var c = CrearUsuarioProfesionalService.GenerarContrasenaTemporal();
            Assert.Equal(14, c.Length);
            Assert.Contains(c, ch => char.IsUpper(ch));
            Assert.Contains(c, ch => char.IsLower(ch));
            Assert.Contains(c, ch => char.IsDigit(ch));
            Assert.DoesNotContain(c, ch => ambiguos.Contains(ch));
            Assert.All(c, ch => Assert.True(char.IsAsciiLetterOrDigit(ch)));
            vistas.Add(c);
        }
        Assert.Equal(500, vistas.Count);
    }

    [Fact]
    public void Una_temporal_siempre_cumple_la_politica_de_largo_de_la_pantalla()
    {
        Assert.True(CrearUsuarioProfesionalService.GenerarContrasenaTemporal().Length >= CrearUsuarioProfesionalService.LargoMinimoContrasena);
        Assert.Throws<ArgumentOutOfRangeException>(() => CrearUsuarioProfesionalService.GenerarContrasenaTemporal(7));
    }
}
