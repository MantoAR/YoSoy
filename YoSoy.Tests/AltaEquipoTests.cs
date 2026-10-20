// Alta de usuarios del equipo (18/10): validaciones sin base de datos.
public class AltaEquipoTests
{
    private static readonly PrivilegioCatalogo PrivAdmision = new(Privilegios.Admision, "Admisión", null);
    private static readonly IReadOnlyList<PermisoCatalogo> Catalogo = new[]
    {
        new PermisoCatalogo(Modulos.Profesional, Roles.Profesional, "Profesional", new[] { PrivAdmision }),
        new PermisoCatalogo(Modulos.Profesional, Roles.Auditor, "Profesional auditor", new[] { PrivAdmision }),
        new PermisoCatalogo(Modulos.Crm, Roles.Crm, "CRM y atención al cliente", new[] { new PrivilegioCatalogo(Privilegios.GestionAgendas, "Gestión de agendas", null) }),
        new PermisoCatalogo(Modulos.Configurador, Roles.Configurador, "Configurador", Array.Empty<PrivilegioCatalogo>())
    };

    // CUIL de prueba con dígito verificador correcto: 20-12345678-6.
    private static AltaEquipoRequest Valido(params string[] permisos) => AltaEquipo.Normalizar(new AltaEquipoRequest(
        " Ana ", "Pérez", "20-12345678-6", "351 555-1234", "Ana.Perez@Ejemplo.com", "Ana.Perez",
        permisos.ToList(), new List<PrivilegioPedido>(), "MP 1234", "Córdoba", 3));

    [Theory]
    [InlineData("20123456786", true)]
    [InlineData("27123456780", true)]
    [InlineData("27123456781", false)]   // dígito verificador mal
    [InlineData("30123456781", false)]   // prefijo de empresa, no de persona
    [InlineData("2012345678", false)]    // 10 dígitos
    [InlineData(null, false)]
    public void Cuil_se_valida_con_el_digito_verificador(string? cuil, bool valido) =>
        Assert.Equal(valido, AltaEquipo.CuilValido(cuil));

    [Fact]
    public void Normaliza_cuil_mail_y_usuario()
    {
        var r = Valido(Roles.Profesional);
        Assert.Equal("20123456786", r.Cuil);
        Assert.Equal("ana.perez@ejemplo.com", r.Mail);
        Assert.Equal("ana.perez", r.Username);
        Assert.Equal("Ana", r.Nombre);
        Assert.Equal("12345678", AltaEquipo.DniDeCuil(r.Cuil!));
    }

    [Fact]
    public void Profesional_valido_con_admision()
    {
        var r = Valido(Roles.Profesional) with { Privilegios = new() { new PrivilegioPedido(Roles.Profesional, Privilegios.Admision) } };
        Assert.Null(AltaEquipo.Validar(r, Catalogo));
    }

    [Fact]
    public void Profesional_o_auditor_no_los_dos() =>
        Assert.NotNull(AltaEquipo.Validar(Valido(Roles.Profesional, Roles.Auditor), Catalogo));

    [Fact]
    public void Profesional_exige_matricula_y_especialidad()
    {
        Assert.NotNull(AltaEquipo.Validar(Valido(Roles.Auditor) with { Matricula = null }, Catalogo));
        Assert.NotNull(AltaEquipo.Validar(Valido(Roles.Auditor) with { IdEspecialidad = null }, Catalogo));
        // CRM no necesita matrícula.
        Assert.Null(AltaEquipo.Validar(Valido(Roles.Crm) with { Matricula = null, IdEspecialidad = null }, Catalogo));
    }

    [Fact]
    public void Sin_rol_no_se_registra() =>
        Assert.NotNull(AltaEquipo.Validar(Valido(), Catalogo));

    [Fact]
    public void Admision_no_va_sobre_CRM()
    {
        var r = Valido(Roles.Crm) with { Privilegios = new() { new PrivilegioPedido(Roles.Crm, Privilegios.Admision) } };
        Assert.NotNull(AltaEquipo.Validar(r, Catalogo));
    }

    [Theory]
    [InlineData("corta1", false)]
    [InlineData("sinnumeros", false)]
    [InlineData("12345678", false)]
    [InlineData("clave2026", true)]
    public void Contrasena_de_8_con_letras_y_numeros(string clave, bool valida) =>
        Assert.Equal(valida, AltaEquipo.ValidarContrasena(clave) is null);

    [Fact]
    public void El_token_se_guarda_como_hash_y_no_se_repite()
    {
        var a = AltaEquipo.NuevoToken();
        var b = AltaEquipo.NuevoToken();
        Assert.NotEqual(a, b);
        Assert.Equal(64, AltaEquipo.HashToken(a).Length);
        Assert.DoesNotContain(a, AltaEquipo.HashToken(a));
    }
}
