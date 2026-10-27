public class UnionDeTranscripcionTests
{
    private static SegmentoTranscripcion Seg(string pista, double inicio, double fin, string texto) =>
        new(pista, inicio, fin, texto);

    [Fact]
    public void Sin_segmentos_el_texto_queda_vacio()
    {
        var texto = UnionDeTranscripcion.ArmarTexto(null, null, Array.Empty<SegmentoTranscripcion>());

        Assert.Equal("", texto);
    }

    [Fact]
    public void Una_sola_pista_se_formatea_con_su_etiqueta_y_el_tiempo_relativo()
    {
        var texto = UnionDeTranscripcion.ArmarTexto(0, null, new[] { Seg(PistaGrabacion.Profesional, 5, 7, "hola") });

        Assert.Equal("[00:05] Profesional: hola", texto);
    }

    [Fact]
    public void Dos_pistas_se_intercalan_por_el_tiempo_absoluto()
    {
        var segmentos = new[]
        {
            Seg(PistaGrabacion.Profesional, 0, 2, "hola"),
            Seg(PistaGrabacion.Paciente, 3, 5, "buenas")
        };

        var texto = UnionDeTranscripcion.ArmarTexto(0, 0, segmentos);

        Assert.Equal("[00:00] Profesional: hola\n[00:03] Paciente: buenas", texto);
    }

    [Fact]
    public void El_offset_de_inicio_de_pista_se_suma_antes_de_restar_el_cero_compartido()
    {
        var segmentos = new[]
        {
            Seg(PistaGrabacion.Profesional, 0, 1, "primero"),
            Seg(PistaGrabacion.Paciente, 0, 1, "segundo")
        };

        var texto = UnionDeTranscripcion.ArmarTexto(1000, 4000, segmentos);

        Assert.Equal("[00:00] Profesional: primero\n[00:03] Paciente: segundo", texto);
    }

    [Fact]
    public void Segmentos_de_la_misma_pista_separados_por_menos_de_dos_segundos_se_funden_en_una_linea()
    {
        var segmentos = new[]
        {
            Seg(PistaGrabacion.Profesional, 0, 1, "hola"),
            Seg(PistaGrabacion.Profesional, 1.5, 2, "como estas")
        };

        var texto = UnionDeTranscripcion.ArmarTexto(0, null, segmentos);

        Assert.Equal("[00:00] Profesional: hola como estas", texto);
    }

    [Fact]
    public void Segmentos_de_la_misma_pista_separados_por_dos_segundos_o_mas_quedan_en_lineas_distintas()
    {
        var segmentos = new[]
        {
            Seg(PistaGrabacion.Profesional, 0, 1, "hola"),
            Seg(PistaGrabacion.Profesional, 3, 4, "como estas")
        };

        var texto = UnionDeTranscripcion.ArmarTexto(0, null, segmentos);

        Assert.Equal("[00:00] Profesional: hola\n[00:03] Profesional: como estas", texto);
    }

    [Fact]
    public void Un_segmento_con_texto_vacio_se_descarta()
    {
        var segmentos = new[]
        {
            Seg(PistaGrabacion.Profesional, 0, 1, "   "),
            Seg(PistaGrabacion.Paciente, 1, 2, "hola")
        };

        var texto = UnionDeTranscripcion.ArmarTexto(0, 0, segmentos);

        Assert.Equal("[00:01] Paciente: hola", texto);
    }
}
