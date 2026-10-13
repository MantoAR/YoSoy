// Evolución por enfoque: obligatorios al firmar, escalas PHQ-9 y GAD-7 y hash (RN-08, RN-11, RN-16).
public class EvolucionEnfoquesTests
{
    private static Plantilla P(string enfoque) => EvolucionEnfoquesService.PlantillaDe(enfoque)!;
    private static ContenidoEvolucion Campos(params (string, string)[] c) =>
        new(c.ToDictionary(x => x.Item1, x => (string?)x.Item2), null, null);

    [Fact]
    public void Psiquiatria_no_esta_entre_los_enfoques_de_esta_etapa()
    {
        Assert.Equal(new[] { "General", "TCC", "Psicoanalisis", "Sistemica", "InfantoJuvenil" }, EnfoquesEvolucion.Todos);
        Assert.Null(EvolucionEnfoquesService.PlantillaDe("Psiquiatria"));
    }

    [Fact]
    public void TCC_al_firmar_exige_situacion_pensamiento_emocion_y_conducta()
    {
        var (_, errores) = EvolucionEnfoquesService.Normalizar(P("TCC"), Campos(("situacion", "Discusión en el trabajo")), firmar: true);
        Assert.Equal(3, errores.Count);
        Assert.Contains(errores, e => e.Contains("Pensamientos"));
    }

    [Fact]
    public void Un_borrador_se_guarda_aunque_falten_obligatorios()
    {
        var (_, errores) = EvolucionEnfoquesService.Normalizar(P("TCC"), Campos(("situacion", "algo")), firmar: false);
        Assert.Empty(errores);
    }

    [Fact]
    public void Psicoanalisis_es_nota_libre_y_no_sugiere_transcripcion()
    {
        var p = P("Psicoanalisis");
        Assert.False(p.SugiereTranscripcion);
        var (_, errores) = EvolucionEnfoquesService.Normalizar(p, Campos(("notas", "Sueño recurrente con la casa de la infancia.")), firmar: true);
        Assert.Empty(errores);
    }

    [Fact]
    public void Sistemica_exige_al_menos_un_consultante_e_ignora_filas_vacias()
    {
        var p = P("Sistemica");
        var c = new ContenidoEvolucion(new() { ["dinamica"] = "Triangulación" },
            new() { new("", "madre", true), new("  ", null, true) }, null);
        var (_, errores) = EvolucionEnfoquesService.Normalizar(p, c, firmar: true);
        Assert.Contains(errores, e => e.Contains("Consultantes"));

        var c2 = c with { Consultantes = new() { new("Ana", "madre", true), new("Leo", "hijo", false) } };
        var (limpio, errores2) = EvolucionEnfoquesService.Normalizar(p, c2, firmar: true);
        Assert.Empty(errores2);
        Assert.Equal(2, limpio.Consultantes!.Count);
        Assert.Contains("Leo (hijo) - ausente", EvolucionEnfoquesService.TextoPlano(p, limpio));
    }

    [Fact]
    public void Infanto_juvenil_exige_lo_que_se_devuelve_al_tutor_y_valida_las_opciones()
    {
        var p = P("InfantoJuvenil");
        var c = Campos(("relato", "Juego con muñecos"), ("confidencialidad", "Inventado"),
                       ("modalidadDevolucion", "En esta sesión"), ("riesgo", "No"));
        var (_, errores) = EvolucionEnfoquesService.Normalizar(p, c, firmar: true);
        Assert.Contains(errores, e => e.Contains("Valor no válido"));
        Assert.Contains(errores, e => e.Contains("tutor"));
    }

    [Theory]
    [InlineData(new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0 }, 0, "Mínima", false)]
    [InlineData(new[] { 1, 1, 1, 1, 1, 0, 0, 0, 0 }, 5, "Leve", false)]
    [InlineData(new[] { 2, 2, 2, 2, 2, 0, 0, 0, 0 }, 10, "Moderada", false)]
    [InlineData(new[] { 3, 3, 3, 3, 3, 0, 0, 0, 3 }, 18, "Moderadamente severa", true)]
    [InlineData(new[] { 3, 3, 3, 3, 3, 3, 3, 3, 3 }, 27, "Severa", true)]
    public void PHQ9_total_severidad_y_alerta_del_item_9(int[] r, int total, string severidad, bool alerta)
    {
        var res = EvolucionEnfoquesService.Calcular(EvolucionEnfoquesService.Phq9, r);
        Assert.Equal(total, res.Total);
        Assert.Equal(severidad, res.Severidad);
        Assert.Equal(alerta, res.AlertaRiesgo);
    }

    [Theory]
    [InlineData(new[] { 0, 0, 0, 0, 1, 1, 2 }, 4, "Mínima")]
    [InlineData(new[] { 1, 1, 1, 1, 1, 0, 0 }, 5, "Leve")]
    [InlineData(new[] { 2, 2, 2, 2, 2, 0, 0 }, 10, "Moderada")]
    [InlineData(new[] { 3, 3, 3, 3, 3, 0, 0 }, 15, "Severa")]
    public void GAD7_total_y_severidad(int[] r, int total, string severidad)
    {
        var res = EvolucionEnfoquesService.Calcular(EvolucionEnfoquesService.Gad7, r);
        Assert.Equal(total, res.Total);
        Assert.Equal(severidad, res.Severidad);
        Assert.False(res.AlertaRiesgo);
    }

    [Fact]
    public void Escala_incompleta_frena_la_firma_pero_no_el_borrador()
    {
        var c = new ContenidoEvolucion(new() { ["situacion"] = "a", ["pensamientos"] = "b", ["emociones"] = "c", ["conductas"] = "d" },
            null, new() { ["PHQ9"] = new[] { 1, 1, -1, 0, 0, 0, 0, 0, 0 } });
        Assert.NotEmpty(EvolucionEnfoquesService.Normalizar(P("TCC"), c, firmar: true).Errores);
        var (limpio, errores) = EvolucionEnfoquesService.Normalizar(P("TCC"), c, firmar: false);
        Assert.Empty(errores);
        Assert.Null(limpio.Escalas);
    }

    [Fact]
    public void Una_escala_que_no_es_del_enfoque_se_rechaza()
    {
        var c = new ContenidoEvolucion(new() { ["notas"] = "x" }, null, new() { ["GAD7"] = new[] { 0, 0, 0, 0, 0, 0, 0 } });
        var (_, errores) = EvolucionEnfoquesService.Normalizar(P("Psicoanalisis"), c, firmar: true);
        Assert.Contains(errores, e => e.Contains("no corresponde"));
    }

    [Fact]
    public void Campos_que_no_son_de_la_plantilla_se_descartan()
    {
        var (limpio, _) = EvolucionEnfoquesService.Normalizar(P("Psicoanalisis"),
            Campos(("notas", "texto"), ("inyectado", "<script>")), firmar: true);
        Assert.False(limpio.Campos!.ContainsKey("inyectado"));
    }

    [Fact]
    public void RN11_el_hash_cambia_si_cambia_una_coma_de_lo_firmado()
    {
        var e = new Evolucion
        {
            IdHistoria = 1, IdProfesional = 10, Fecha = new DateTime(2026, 10, 12, 18, 0, 0), Enfoque = "TCC",
            ModoRegistro = "Manual", Observaciones = "Situación: discusión", TipoCierre = "Seguimiento",
            ContenidoJson = "{}", Estado = "Firmada", FechaFirma = new DateTime(2026, 10, 12, 18, 50, 0)
        };
        var h1 = EvolucionEnfoquesService.Hash(e);
        Assert.Equal(64, h1.Length);
        Assert.Equal(h1, EvolucionEnfoquesService.Hash(e));
        e.Observaciones = "Situación: discusión.";
        Assert.NotEqual(h1, EvolucionEnfoquesService.Hash(e));
    }

    [Theory]
    [InlineData("Derivacion", "Derivación")]
    [InlineData("Derivación", "Derivación")]
    [InlineData("Alta", "Alta")]
    [InlineData("Cerrado", null)]
    [InlineData(null, null)]
    public void RN08_tipo_de_cierre(string? entrada, string? esperado) =>
        Assert.Equal(esperado, TiposCierre.Normalizar(entrada));
}
