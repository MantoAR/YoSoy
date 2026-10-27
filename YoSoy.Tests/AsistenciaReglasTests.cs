// Migración 37 (rama AAA1): llegada tarde, inasistencia y abandono. Reglas puras de Services/AsistenciaReglas.cs (sin base ni reloj).
// Turno de prueba: jueves 24/09/2026 a las 10:00. Parámetros por defecto: aviso 5 min, tolerancia 15, 24 hs, alerta 2, abandono 3.
public class AsistenciaReglasTests
{
    private static readonly DateTime Inicio = new(2026, 9, 24, 10, 0, 0);
    private static readonly ParametrosAsistencia P = ParametrosAsistencia.PorDefecto;

    [Fact]
    public void Los_parametros_por_defecto_son_los_acordados_y_son_validos()
    {
        Assert.Equal(new ParametrosAsistencia(5, 15, 24, 2, 3), P);
        Assert.Null(P.Validar());
    }

    [Theory]
    [InlineData(15, 15, 24, 2, 3)]   // el aviso tiene que llegar antes de que termine la tolerancia
    [InlineData(5, 15, 24, 3, 3)]    // el abandono pide más ausencias que la alerta
    [InlineData(0, 15, 24, 2, 3)]
    [InlineData(5, 15, -1, 2, 3)]
    public void Valores_sin_sentido_se_rechazan(int aviso, int tolerancia, int horas, int alerta, int abandono) =>
        Assert.NotNull(new ParametrosAsistencia(aviso, tolerancia, horas, alerta, abandono).Validar());

    [Fact]
    public void Desde_filas_toma_lo_guardado_y_completa_lo_que_falta()
    {
        var p = ParametrosAsistencia.Desde(new Dictionary<string, string?> { [ParametrosAsistencia.ClaveTolerancia] = "20", [ParametrosAsistencia.ClaveAlerta] = "x" });
        Assert.Equal(new ParametrosAsistencia(5, 20, 24, 2, 3), p);
        // Una combinación inválida tocada a mano en la base no rompe: rigen los de siempre.
        Assert.Equal(P, ParametrosAsistencia.Desde(new Dictionary<string, string?> { [ParametrosAsistencia.ClaveAviso] = "30" }));
        // Ida y vuelta.
        Assert.Equal(p, ParametrosAsistencia.Desde(p.ComoFilas().ToDictionary(kv => kv.Key, kv => (string?)kv.Value)));
    }

    [Fact]
    public void A_los_15_minutos_no_pasa_solo_a_Ausente_se_habilita_el_boton()
    {
        Assert.False(AsistenciaReglas.PuedeMarcarAusente("Confirmado", Inicio, Inicio.AddMinutes(14), ingreso: false, P));
        Assert.True(AsistenciaReglas.PuedeMarcarAusente("Confirmado", Inicio, Inicio.AddMinutes(15), ingreso: false, P));
        Assert.Contains("10:15", AsistenciaReglas.MotivoParaNoMarcarAusente("Confirmado", Inicio, Inicio.AddMinutes(3), false, P));
        Assert.Contains("Todavía no llegó", AsistenciaReglas.MotivoParaNoMarcarAusente("Confirmado", Inicio, Inicio.AddMinutes(-1), false, P));
    }

    [Fact]
    public void Si_el_paciente_entro_a_la_sala_no_es_inasistencia_aunque_haya_llegado_tarde()
    {
        Assert.False(AsistenciaReglas.PuedeMarcarAusente("Confirmado", Inicio, Inicio.AddMinutes(40), ingreso: true, P));
        Assert.False(AsistenciaReglas.EsperandoConexion(Inicio, Inicio.AddMinutes(10), ingreso: true));
        Assert.Equal(12, AsistenciaReglas.MinutosDemora(Inicio, Inicio.AddMinutes(12).AddSeconds(30)));
        Assert.Equal(0, AsistenciaReglas.MinutosDemora(Inicio, Inicio.AddMinutes(-5)));
    }

    [Theory]
    [InlineData("Pendiente")]
    [InlineData("Cancelado")]
    [InlineData("Ausente")]
    [InlineData("Realizado")]
    public void Solo_un_turno_confirmado_se_marca_Ausente(string estado) =>
        Assert.False(AsistenciaReglas.PuedeMarcarAusente(estado, Inicio, Inicio.AddHours(1), ingreso: false, P));

    [Fact]
    public void El_aviso_al_paciente_va_entre_los_5_y_los_15_minutos()
    {
        Assert.False(AsistenciaReglas.CorrespondeAviso(Inicio, Inicio.AddMinutes(4), false, P));
        Assert.True(AsistenciaReglas.CorrespondeAviso(Inicio, Inicio.AddMinutes(5), false, P));
        Assert.True(AsistenciaReglas.CorrespondeAviso(Inicio, Inicio.AddMinutes(14), false, P));
        Assert.False(AsistenciaReglas.CorrespondeAviso(Inicio, Inicio.AddMinutes(15), false, P));
        Assert.False(AsistenciaReglas.CorrespondeAviso(Inicio, Inicio.AddMinutes(6), true, P));
    }

    [Fact]
    public void Cancelar_con_menos_de_24_horas_queda_cobrable()
    {
        Assert.False(AsistenciaReglas.CancelacionCobrable(Inicio, Inicio.AddHours(-24), P));
        Assert.False(AsistenciaReglas.CancelacionCobrable(Inicio, Inicio.AddHours(-30), P));
        Assert.True(AsistenciaReglas.CancelacionCobrable(Inicio, Inicio.AddHours(-23), P));
        Assert.True(AsistenciaReglas.CancelacionCobrable(Inicio, Inicio.AddMinutes(5), P));
    }

    [Fact]
    public void Las_ausencias_seguidas_cortan_en_el_ultimo_turno_al_que_vino()
    {
        Assert.Equal(2, AsistenciaReglas.AusenciasConsecutivas(new[] { "Ausente", "Ausente", "Realizado", "Ausente" }));
        // Cancelados y pendientes no cuentan ni cortan; un Confirmado ya pasado (nadie lo marcó Ausente) corta.
        Assert.Equal(3, AsistenciaReglas.AusenciasConsecutivas(new[] { "Ausente", "Cancelado", "Ausente", "Pendiente", "Ausente", "Confirmado", "Ausente" }));
        Assert.Equal(0, AsistenciaReglas.AusenciasConsecutivas(new[] { "Realizado", "Ausente", "Ausente" }));
        Assert.Equal(0, AsistenciaReglas.AusenciasConsecutivas(Array.Empty<string>()));
    }

    [Theory]
    [InlineData(0, "ok")]
    [InlineData(1, "ok")]
    [InlineData(2, "alerta")]
    [InlineData(3, "abandono")]
    [InlineData(5, "abandono")]
    public void Dos_ausencias_son_alerta_y_tres_sugieren_abandono(int ausencias, string nivel) =>
        Assert.Equal(nivel, AsistenciaReglas.Nivel(ausencias, P));

    [Theory]
    [InlineData("Presencial", true)]
    [InlineData("presencial ", true)]
    [InlineData("Virtual", false)]
    [InlineData(null, false)]
    public void El_aviso_de_la_sala_no_va_a_los_turnos_presenciales(string? modalidad, bool presencial) =>
        Assert.Equal(presencial, AsistenciaReglas.EsPresencial(modalidad));

    [Fact]
    public void La_recepcionista_marca_Ausente_solo_con_el_privilegio_Gestion_de_pacientes()
    {
        Assert.True(Politicas.Cumple(PrivilegiosAdmisionTests.Usuario(Roles.Crm, Privilegios.GestionPacientes), Quien.GestionPacientes));
        Assert.False(Politicas.Cumple(PrivilegiosAdmisionTests.Usuario(Roles.Crm), Quien.GestionPacientes));
        Assert.False(Politicas.Cumple(PrivilegiosAdmisionTests.Usuario(Roles.Crm, Privilegios.GestionAgendas), Quien.GestionPacientes));
        // El privilegio no le abre nada clínico: ni Admisión ni lo del profesional.
        Assert.False(Politicas.Cumple(PrivilegiosAdmisionTests.Usuario(Roles.Crm, Privilegios.GestionPacientes), Quien.Profesional | Quien.Admision | Quien.Auditor));

        var recepcion = new Identidad(1, Roles.Crm, null, null) { Privilegios = new[] { Privilegios.GestionPacientes } };
        Assert.True(recepcion.GestionaPacientes);
        Assert.False(new Identidad(1, Roles.Crm, null, null).GestionaPacientes);
    }
}
