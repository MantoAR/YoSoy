using System.Security.Claims;

// Etapa 3 de roles y permisos: reparto de funciones entre módulos (sin base de datos).
//   - Politicas.Cumple: quién pasa cada combinación de módulo, permiso y privilegio.
//   - AccesosEdicion.Validar: las reglas del editor de permisos por usuario.
public class RedistribucionEtapa3Tests
{
    private static ClaimsPrincipal Usuario(string[] roles, params string[] privilegios)
    {
        var claims = roles.Select(r => new Claim(ClaimTypes.Role, r))
            .Concat(privilegios.Select(p => new Claim(JwtEmisorService.ClaimPrivilegio, p)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
    }

    private static readonly ClaimsPrincipal Configurador = Usuario(new[] { Roles.Configurador });
    private static readonly ClaimsPrincipal Profesional = Usuario(new[] { Roles.Profesional });
    private static readonly ClaimsPrincipal ProfesionalAdmision = Usuario(new[] { Roles.Profesional }, Privilegios.Admision);
    private static readonly ClaimsPrincipal Auditor = Usuario(new[] { Roles.Auditor, Roles.Profesional });
    private static readonly ClaimsPrincipal Crm = Usuario(new[] { Roles.Crm });
    private static readonly ClaimsPrincipal CrmAgendas = Usuario(new[] { Roles.Crm }, Privilegios.GestionAgendas);
    private static readonly ClaimsPrincipal Paciente = Usuario(new[] { Roles.Paciente });

    [Fact]
    public void El_configurador_no_pasa_nada_clinico_ni_operativo()
    {
        foreach (var q in new[] { Quien.Profesional, Quien.Auditor, Quien.Admision, Quien.Crm, Quien.Agendas })
            Assert.False(Politicas.Cumple(Configurador, q), q.ToString());
        Assert.True(Politicas.Cumple(Configurador, Quien.Configurador));
    }

    [Fact]
    public void Admision_es_un_privilegio_del_profesional_no_un_rol()
    {
        Assert.False(Politicas.Cumple(Profesional, Quien.Admision));
        Assert.True(Politicas.Cumple(ProfesionalAdmision, Quien.Admision));
        // El privilegio solo sirve sobre el permiso que lo admite: un token CRM con el claim no pasa.
        Assert.False(Politicas.Cumple(Usuario(new[] { Roles.Crm }, Privilegios.Admision), Quien.Admision));
    }

    [Fact]
    public void Gestion_de_agendas_es_un_privilegio_de_CRM()
    {
        Assert.False(Politicas.Cumple(Crm, Quien.Agendas));
        Assert.True(Politicas.Cumple(CrmAgendas, Quien.Agendas));
        Assert.False(Politicas.Cumple(Usuario(new[] { Roles.Profesional }, Privilegios.GestionAgendas), Quien.Agendas));
    }

    [Fact]
    public void El_auditor_tiene_todo_lo_del_profesional_y_lo_suyo()
    {
        Assert.True(Politicas.Cumple(Auditor, Quien.Profesional));
        Assert.True(Politicas.Cumple(Auditor, Quien.Auditor));
        Assert.False(Politicas.Cumple(Profesional, Quien.Auditor));
    }

    [Fact]
    public void CRM_nunca_pasa_lo_clinico()
    {
        Assert.False(Politicas.Cumple(CrmAgendas, Quien.Profesional | Quien.Auditor | Quien.Admision));
        Assert.True(Politicas.Cumple(Crm, Quien.Profesional | Quien.Crm));
    }

    [Fact]
    public void El_paciente_solo_pasa_lo_de_paciente()
    {
        Assert.True(Politicas.Cumple(Paciente, Quien.Paciente | Quien.Admision));
        Assert.False(Politicas.Cumple(Paciente, Quien.Configurador | Quien.Auditor | Quien.Crm | Quien.Profesional));
    }

    [Fact]
    public void Identidad_resuelve_los_privilegios_sobre_el_permiso_correcto()
    {
        var admision = new Identidad(1, Roles.Profesional, null, 3) { Permisos = new[] { Roles.Profesional }, Privilegios = new[] { Privilegios.Admision } };
        var agendas = new Identidad(2, Roles.Crm, null, null) { Permisos = new[] { Roles.Crm }, Privilegios = new[] { Privilegios.GestionAgendas } };
        Assert.True(admision.HaceAdmision);
        Assert.False(admision.GestionaAgendas);
        Assert.True(agendas.GestionaAgendas);
        Assert.False(agendas.HaceAdmision);
        Assert.True(agendas.EsCrm);
    }

    // ── Editor de permisos ─────────────────────────────────────
    private static readonly PrivilegioCatalogo PrivAdmision = new(Privilegios.Admision, "Admisión", null);
    private static readonly IReadOnlyList<PermisoCatalogo> Catalogo = new[]
    {
        new PermisoCatalogo(Modulos.Profesional, Roles.Profesional, "Profesional", new[] { PrivAdmision }),
        new PermisoCatalogo(Modulos.Profesional, Roles.Auditor, "Profesional auditor", new[] { PrivAdmision }),
        new PermisoCatalogo(Modulos.Crm, Roles.Crm, "CRM y atención al cliente", new[] { new PrivilegioCatalogo(Privilegios.GestionAgendas, "Gestión de agendas", null) }),
        new PermisoCatalogo(Modulos.Facturacion, Roles.Facturacion, "Administración de facturación y pagos", Array.Empty<PrivilegioCatalogo>()),
        new PermisoCatalogo(Modulos.Configurador, Roles.Configurador, "Configurador", Array.Empty<PrivilegioCatalogo>())
    };
    private static readonly AccesosEdicion.Situacion ConFicha = new(EsPaciente: false, TieneFichaProfesional: true, EsUnoMismo: false, TeniaConfigurador: false);
    private static readonly AccesosEdicion.Situacion SinFicha = ConFicha with { TieneFichaProfesional = false };

    private static AccesosEdicionRequest Pedido(string[] permisos, params (string Permiso, string Privilegio)[] privilegios) =>
        new(permisos.ToList(), privilegios.Select(p => new PrivilegioPedido(p.Permiso, p.Privilegio)).ToList());

    [Fact]
    public void Profesional_y_auditor_exigen_ficha_con_matricula()
    {
        Assert.NotNull(AccesosEdicion.Validar(Catalogo, Pedido(new[] { Roles.Auditor }), SinFicha));
        Assert.Null(AccesosEdicion.Validar(Catalogo, Pedido(new[] { Roles.Profesional, Roles.Auditor }), ConFicha));
        Assert.Null(AccesosEdicion.Validar(Catalogo, Pedido(new[] { Roles.Crm }), SinFicha));
    }

    [Fact]
    public void Un_privilegio_va_sobre_un_permiso_que_lo_admite_y_que_el_usuario_tiene()
    {
        Assert.Null(AccesosEdicion.Validar(Catalogo, Pedido(new[] { Roles.Profesional }, (Roles.Profesional, Privilegios.Admision)), ConFicha));
        Assert.Null(AccesosEdicion.Validar(Catalogo, Pedido(new[] { Roles.Crm }, (Roles.Crm, Privilegios.GestionAgendas)), SinFicha));
        // Admisión sobre CRM: no lo admite.
        Assert.NotNull(AccesosEdicion.Validar(Catalogo, Pedido(new[] { Roles.Crm }, (Roles.Crm, Privilegios.Admision)), SinFicha));
        // Gestión de agendas sin tener CRM.
        Assert.NotNull(AccesosEdicion.Validar(Catalogo, Pedido(new[] { Roles.Configurador }, (Roles.Crm, Privilegios.GestionAgendas)), SinFicha));
    }

    [Fact]
    public void Facturacion_todavia_no_se_asigna_desde_la_pantalla()
    {
        Assert.NotNull(AccesosEdicion.Validar(Catalogo, Pedido(new[] { Roles.Facturacion }), SinFicha));
    }

    [Fact]
    public void Nadie_se_quita_a_si_mismo_el_permiso_de_configurador()
    {
        var yoMismo = SinFicha with { EsUnoMismo = true, TeniaConfigurador = true };
        Assert.NotNull(AccesosEdicion.Validar(Catalogo, Pedido(new[] { Roles.Crm }), yoMismo));
        Assert.Null(AccesosEdicion.Validar(Catalogo, Pedido(new[] { Roles.Configurador, Roles.Crm }), yoMismo));
    }

    [Fact]
    public void Una_cuenta_de_paciente_no_lleva_permisos_del_equipo()
    {
        var paciente = SinFicha with { EsPaciente = true };
        Assert.NotNull(AccesosEdicion.Validar(Catalogo, Pedido(new[] { Roles.Crm }), paciente));
        Assert.Null(AccesosEdicion.Validar(Catalogo, Pedido(Array.Empty<string>()), paciente));
    }

    [Fact]
    public void Sin_ningun_permiso_es_valido_y_el_usuario_no_ve_nada()
    {
        Assert.Null(AccesosEdicion.Validar(Catalogo, Pedido(Array.Empty<string>()), ConFicha));
    }
}
