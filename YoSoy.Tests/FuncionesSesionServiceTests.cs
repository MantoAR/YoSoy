// Interruptores de la sala (RN-10 análisis de expresiones, RN-15 transcripción), sin base de datos.
public class FuncionesSesionServiceTests
{
    private class RepoEnMemoria : IAtencionRepositorio
    {
        public Dictionary<string, SalaInfo> Salas = new();
        public HashSet<(int Pac, string Tipo)> Consentimientos = new();
        public HashSet<(int Prof, int Pac)> Asignaciones = new();
        public List<(string Sala, string Funcion, bool Activa, string Rol)> Cambios = new();

        public SalaInfo? ResolverSala(string codigo) => Salas.GetValueOrDefault(codigo);
        public bool ConsentimientoVigente(int idPaciente, string tipo) => Consentimientos.Contains((idPaciente, tipo));
        public bool EsProfesionalDelPaciente(int idProf, int idPac) => Asignaciones.Contains((idProf, idPac));
        public Dictionary<string, bool> UltimoEstado(string sala)
        {
            var d = new Dictionary<string, bool>();
            foreach (var c in Cambios.Where(c => c.Sala == sala)) d[c.Funcion] = c.Activa;
            return d;
        }
        public void RegistrarCambio(string sala, int idPac, int? idUsu, string rol, string funcion, bool activa) =>
            Cambios.Add((sala, funcion, activa, rol));
    }

    private readonly RepoEnMemoria _repo = new();
    private readonly FuncionesSesionService _s;
    private static readonly Identidad Profesional10 = new(100, Roles.Profesional, null, 10);
    private static readonly Identidad OtroProfesional = new(101, Roles.Profesional, null, 11);
    private static readonly Identidad Paciente1 = new(200, Roles.Paciente, 1, null);
    private static readonly Identidad OtroPaciente = new(201, Roles.Paciente, 2, null);
    private static readonly Identidad Configurador = new(1, Roles.Configurador, null, null);
    // Etapa 3: la entrevista de admisión la hace un profesional con el privilegio Admisión.
    private static readonly Identidad Admision20 = new(102, Roles.Profesional, null, 20)
        { Permisos = new[] { Roles.Profesional }, Privilegios = new[] { Privilegios.Admision } };

    public FuncionesSesionServiceTests()
    {
        _s = new FuncionesSesionService(_repo);
        _repo.Salas["SALA-1"] = new SalaInfo("SALA-1", 1, 10, "Turno", 50, null);
        _repo.Salas["ADM-1"] = new SalaInfo("ADM-1", 1, null, "Admision", null, 7);
        _repo.Asignaciones.Add((10, 1));
    }

    private void ConsentirIa() => _repo.Consentimientos.Add((1, ConsentimientosOpcionales.Ia.Descripcion));
    private void ConsentirTranscripcion() => _repo.Consentimientos.Add((1, ConsentimientosOpcionales.Transcripcion.Descripcion));
    private static bool Activa(ResultadoFunciones r, string f) => r.Estado!.Single(e => e.Funcion == f).Activa;

    [Fact]
    public void RN10_sin_consentimiento_no_se_enciende_la_IA_y_no_queda_registro()
    {
        var r = _s.Cambiar(Profesional10, "SALA-1", FuncionesSesion.IaExpresiones, true);
        Assert.False(r.Ok);
        Assert.Equal(409, r.Status);
        Assert.Equal("RN-10", r.Codigo);
        Assert.Empty(_repo.Cambios);
    }

    [Fact]
    public void RN15_sin_consentimiento_no_se_enciende_la_transcripcion()
    {
        ConsentirIa();   // tener el de IA no habilita la transcripción
        var r = _s.Cambiar(Profesional10, "SALA-1", FuncionesSesion.Transcripcion, true);
        Assert.Equal("RN-15", r.Codigo);
    }

    [Fact]
    public void Con_consentimiento_el_profesional_la_enciende_y_queda_registrada()
    {
        ConsentirIa();
        var r = _s.Cambiar(Profesional10, "SALA-1", FuncionesSesion.IaExpresiones, true);
        Assert.True(r.Ok);
        Assert.True(Activa(r, FuncionesSesion.IaExpresiones));
        Assert.False(Activa(r, FuncionesSesion.Transcripcion));
        Assert.Single(_repo.Cambios);
    }

    [Fact]
    public void El_paciente_puede_apagar_pero_nunca_encender()
    {
        ConsentirIa();
        Assert.Equal(403, _s.Cambiar(Paciente1, "SALA-1", FuncionesSesion.IaExpresiones, true).Status);
        _s.Cambiar(Profesional10, "SALA-1", FuncionesSesion.IaExpresiones, true);
        var r = _s.Cambiar(Paciente1, "SALA-1", FuncionesSesion.IaExpresiones, false);
        Assert.True(r.Ok);
        Assert.False(Activa(r, FuncionesSesion.IaExpresiones));
    }

    [Fact]
    public void Si_el_paciente_revoca_la_funcion_figura_apagada_aunque_el_ultimo_registro_diga_activa()
    {
        ConsentirIa();
        _s.Cambiar(Profesional10, "SALA-1", FuncionesSesion.IaExpresiones, true);
        _repo.Consentimientos.Clear();   // revocó
        var r = _s.Consultar(Paciente1, "SALA-1");
        Assert.False(Activa(r, FuncionesSesion.IaExpresiones));
        Assert.False(r.Estado!.Single(e => e.Funcion == FuncionesSesion.IaExpresiones).Consentida);
    }

    [Fact]
    public void Otro_paciente_u_otro_profesional_no_ven_la_sala()
    {
        Assert.Equal(404, _s.Consultar(OtroPaciente, "SALA-1").Status);
        Assert.Equal(404, _s.Consultar(OtroProfesional, "SALA-1").Status);
        Assert.Equal(404, _s.Cambiar(OtroProfesional, "SALA-1", FuncionesSesion.IaExpresiones, false).Status);
    }

    [Fact]
    public void Admision_maneja_la_sala_de_la_entrevista_de_admision()
    {
        ConsentirTranscripcion();
        var r = _s.Cambiar(Admision20, "ADM-1", FuncionesSesion.Transcripcion, true);
        Assert.True(r.Ok);
        Assert.True(Activa(r, FuncionesSesion.Transcripcion));
    }

    [Fact]
    public void Etapa3_el_configurador_ya_no_entra_a_ninguna_sala()
    {
        Assert.Equal(404, _s.Consultar(Configurador, "ADM-1").Status);
        Assert.Equal(404, _s.Consultar(Configurador, "SALA-1").Status);
    }

    [Fact]
    public void Admision_no_entra_a_las_salas_de_turnos_de_otros()
    {
        Assert.Equal(404, _s.Consultar(Admision20, "SALA-1").Status);
    }

    [Fact]
    public void Funcion_desconocida_o_sala_inexistente()
    {
        Assert.Equal(400, _s.Cambiar(Profesional10, "SALA-1", "Grabacion", true).Status);
        Assert.Equal(404, _s.Cambiar(Profesional10, "NO-EXISTE", FuncionesSesion.IaExpresiones, false).Status);
        Assert.Equal(401, _s.Consultar(null, "SALA-1").Status);
    }
}
